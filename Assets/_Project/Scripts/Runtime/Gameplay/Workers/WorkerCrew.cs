using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Trees;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Отряд рабочих в сцене: при старте ставит их у первого источника света, каждый кадр обновляет задачи
    /// и рабочих, рисует рабочих (гранёный человек из частей WorkerMeshBuilder, анимация по состоянию из WorkerAnimation,
    /// ноша на плече из WorkerCarryBuilder), рамки чертежей и приказов копать и рубить.
    /// Чертежи и приказы ставит игрок инструментами BlockEditController (4 и 5). Топливо для огней отряд заводит сам:
    /// у каждого огня, в котором есть место хотя бы под одно полено, висит задача принести полено.
    /// Поддоны (<see cref="Pallets"/>) ставит игрок инструментом 6; пока у них нет своего вида, отряд рисует их рамкой 2×2×2
    /// и подписью над ней: что лежит, сколько и сколько влезает («бревно 2-20»).
    /// Урон блоков и устойчивость общие с BlockEditController, ресурсы на земле — из DroppedItemsView.
    /// </summary>
    public sealed class WorkerCrew : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Как быстро рабочий разворачивается, градусов в секунду.</summary>
        private const float TurnDegreesPerSecond = 420f;

        /// <summary>Ближе скольких блоков от огня ночью свободный рабочий греется.</summary>
        private const float WarmDistance = 4.5f;

        /// <summary>Через сколько секунд у огня рабочий садится.</summary>
        private const float SitAfterSeconds = 8f;

        [SerializeField] private WorkerSettings settings;
        [Tooltip("Сколько стоят блоки по чертежу.")]
        [SerializeField] private BuildCostTable costs;
        [SerializeField] private BlockEditController editor;
        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private DroppedItemsView droppedItems;
        [Tooltip("Где светло. Если пусто — рабочие ходят везде (песочницы).")]
        [SerializeField] private BuildZone buildZone;
        [Tooltip("Деревья для приказа рубить. Если пусто — рубить нечего.")]
        [SerializeField] private ForestView forest;
        [Tooltip("Смена суток: ночью свободные рабочие греются и сидят у огня. Если пусто — ищется в сцене.")]
        [SerializeField] private DayNightCycle dayNight;

        [Header("Вид")]
        [SerializeField] private Material workerMaterial;
        [Tooltip("Материал рамок чертежей и приказов (Unlit, цвет задаётся ниже).")]
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private Color blueprintColor = new(0.35f, 0.75f, 1f);
        [SerializeField] private Color digOrderColor = new(1f, 0.45f, 0.15f);
        [SerializeField] private Color palletColor = new(0.95f, 0.8f, 0.45f);
        [Range(0f, 0.1f)]
        [SerializeField] private float outlinePadding = 0.03f;

        private readonly List<Worker> workers = new();
        private readonly List<WorkerVisual> visuals = new();
        private readonly Dictionary<LightSource, FuelTarget> fuelTargets = new();
        private readonly List<LightSource> goneFires = new();
        private readonly LightZone litZone = new();
        private readonly Matrix4x4[] partMatrices = new Matrix4x4[WorkerPose.PartCount];
        private WorkerContext context;

        private readonly List<Vector3> lineVertices = new();
        private readonly List<int> lineIndices = new();
        private GameObject blueprintLines;
        private GameObject digLines;
        private GameObject palletLines;
        private int shownOrdersVersion = -1;
        private int shownPalletsVersion = -1;
        private GUIStyle palletLabelStyle;

        /// <summary>Чертежи и приказы копать.</summary>
        public WorkOrders Orders { get; } = new();

        public IReadOnlyList<Worker> Workers => workers;

        /// <summary>Поддоны для ресурсов; создаются в Start, до этого null.</summary>
        public Pallets Pallets { get; private set; }

        private void Start()
        {
            // Урон блоков и ресурсы на земле создаются в Awake других компонентов.
            if (settings == null || editor == null || worldView == null || droppedItems == null
                || worldView.World == null || editor.Damage == null || droppedItems.Items == null)
            {
                Debug.LogError("[Workers] Не назначены WorkerSettings, BlockEditController, VoxelWorldView или DroppedItemsView.", this);
                enabled = false;
                return;
            }

            Pallets = new Pallets(settings.PalletCapacity);
            context = new WorkerContext(worldView.World, Orders, droppedItems.Items, editor.Damage, editor.Stability, settings, IsLit,
                () => buildZone == null || litZone.Count > 0, editor.OnWorldChanged, forest != null ? forest.Forest : null, Pallets);
            Orders.Cancelled += OnOrderCancelled;
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            blueprintLines = CreateLines("Blueprints", blueprintColor);
            digLines = CreateLines("Dig Orders", digOrderColor);
            palletLines = CreateLines("Pallets", palletColor);
            UpdateLight();
            Spawn();
        }

        private void OnDestroy()
        {
            Orders.Cancelled -= OnOrderCancelled;
            foreach (var visual in visuals)
            {
                if (visual.Body != null)
                    Destroy(visual.Body.gameObject);
                foreach (var mesh in visual.Meshes)
                    Destroy(mesh);
                if (visual.CarryShape != null)
                    Destroy(visual.CarryShape.Mesh);
            }
            DestroyLines(blueprintLines);
            DestroyLines(digLines);
            DestroyLines(palletLines);
        }

        private void Update()
        {
            if (context == null)
                return;
            UpdateLight();
            // Клетку чертежа могли занять, блок под приказом — выкопать вручную, дерево — уронить.
            Orders.Prune(worldView.World);
            PrunePallets();
            UpdateFuelOrders();
            var deltaTime = Time.deltaTime;
            foreach (var worker in workers)
                worker.Tick(deltaTime);
        }

        private void LateUpdate()
        {
            if (context == null)
                return;
            var space = worldView.transform;
            var deltaTime = Time.deltaTime;
            var night = dayNight != null && dayNight.IsNight;
            for (var i = 0; i < workers.Count; i++)
                Animate(workers[i], visuals[i], space, deltaTime, night);
            UpdateOrderLines();
        }

        /// <summary>Выбрать анимацию по состоянию рабочего, повернуть его и расставить части по позе.</summary>
        private void Animate(Worker worker, WorkerVisual visual, Transform space, float deltaTime, bool night)
        {
            var move = worker.Position - visual.LastPosition;
            move.y = 0f;
            visual.LastPosition = worker.Position;
            var speed = deltaTime > 0f ? move.magnitude / deltaTime : 0f;
            visual.Speed = Mathf.Lerp(visual.Speed, speed, 1f - Mathf.Exp(-10f * deltaTime));
            if (worker.State != visual.LastState)
            {
                visual.LastState = worker.State;
                visual.StateTime = 0f;
            }
            visual.StateTime += deltaTime;

            // Куда смотреть: за работой — на задачу, в пути — по ходу, ночью без дела — на огонь.
            var fire = NearestFire(worker.Position);
            var warming = worker.State == WorkerState.Idle && night && fire.HasValue && visual.Speed < 0.3f;
            Vector3? look = null;
            if (worker.State == WorkerState.Putting && worker.HaulTarget != null)
                look = worker.HaulTarget.Center - worker.Position;
            else if (worker.State is WorkerState.Working or WorkerState.Putting && worker.Order != null)
                look = worker.Order.Cell + new Vector3(0.5f, 0f, 0.5f) - worker.Position;
            else if (move.sqrMagnitude > 1e-6f)
                look = move;
            else if (warming)
                look = fire.Value - worker.Position;
            var turning = false;
            if (look is { } direction && new Vector2(direction.x, direction.z).sqrMagnitude > 1e-6f)
            {
                var target = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                visual.Yaw = Mathf.MoveTowardsAngle(visual.Yaw, target, TurnDegreesPerSecond * deltaTime);
                turning = Mathf.Abs(Mathf.DeltaAngle(visual.Yaw, target)) > 10f;
            }

            visual.NightIdleTime = warming ? visual.NightIdleTime + deltaTime : 0f;
            var animator = visual.Animator;
            switch (worker.State)
            {
                case WorkerState.Fleeing:
                    animator.Play(visual.StateTime < WorkerAnimation.DefaultDuration(WorkerClip.Startle) ? WorkerClip.Startle : WorkerClip.Run);
                    break;
                case WorkerState.PickingUp:
                    animator.Play(WorkerClip.PickUp, settings.PickUpSeconds);
                    break;
                case WorkerState.Putting:
                    animator.Play(WorkerClip.Put, settings.PutSeconds);
                    break;
                case WorkerState.Working when worker.Order != null && worker.Order.Kind == WorkKind.Build:
                    animator.Play(WorkerClip.Place, settings.BuildSeconds);
                    break;
                case WorkerState.Working:
                    // Удары ролей (рубить, копать) — отдельной задачей; пока каждый удар — наклон «поставить».
                    animator.Play(WorkerClip.Place, settings.DigHitSeconds);
                    if (animator.Progress >= 1f)
                        animator.Restart();
                    break;
                default:
                    if (visual.Speed > 0.3f)
                        animator.Play(worker.Carrying != null ? WorkerClip.Carry : WorkerClip.Walk);
                    else if (turning)
                        animator.Play(WorkerClip.Turn);
                    else if (warming)
                        animator.Play(visual.NightIdleTime > SitAfterSeconds ? WorkerClip.Sit : WorkerClip.Warm);
                    else
                        animator.Play(worker.Carrying != null ? WorkerClip.Carry : WorkerClip.Idle);
                    break;
            }
            // Ноша — по тому, что несут; пока поднимает — то, за чем пришёл.
            var itemId = worker.Carrying?.ItemId ?? (worker.State == WorkerState.PickingUp ? worker.WantedItemId : null);
            var carry = WorkerCarryBuilder.CarryFor(itemId, visual.Look.Role);
            var pose = animator.Update(deltaTime, WorkerMotion.For(visual.Look, settings.Height, visual.Speed, visual.Seed, carry));
            WorkerSkeleton.Evaluate(pose, settings.Height, partMatrices);

            // Сетка в блоках, ноги в начале координат: стоит на верхе столбца.
            var body = visual.Body;
            body.SetPositionAndRotation(space.TransformPoint(worker.Position), space.rotation * Quaternion.Euler(0f, visual.Yaw, 0f));
            body.localScale = space.lossyScale;
            for (var k = 0; k < visual.Parts.Length; k++)
            {
                // Мягкие части (подол, пелерина) гнутся по костям, их вершины уже в системе рабочего.
                if (visual.Skins[k] != null)
                    visual.Skins[k].Apply(partMatrices);
                else
                    visual.Parts[k].SetLocalPositionAndRotation(partMatrices[k].GetColumn(3), partMatrices[k].rotation);
            }

            if (carry != visual.CarryKind)
            {
                visual.CarryKind = carry;
                if (visual.CarryShape != null)
                    Destroy(visual.CarryShape.Mesh);
                // Сетка ноши у каждого своя: лямки корзины меняются по позе.
                visual.CarryShape = carry == WorkerCarry.None
                    ? null
                    : new WorkerCarryBuilder.Shape(WorkerCarryBuilder.Build(carry, settings.Height, "Carry " + carry), carry, settings.Height);
                visual.Carry.sharedMesh = visual.CarryShape?.Mesh;
            }
            visual.Carry.transform.SetLocalPositionAndRotation(pose.LoadPosition, pose.LoadRotation);
            // Лямки корзины на плечах, пока она на спине, и втягиваются, когда её снимают.
            visual.CarryShape?.Apply(pose.LoadOnBack);
        }

        /// <summary>Ближайший горящий огонь в WarmDistance блоках от точки (координаты мира блоков) или null.</summary>
        private Vector3? NearestFire(Vector3 position)
        {
            if (buildZone == null)
                return null;
            Vector3? best = null;
            var bestDistance = WarmDistance * WarmDistance;
            foreach (var source in buildZone.Sources)
            {
                if (source == null || !source.IsLit)
                    continue;
                var local = worldView.transform.InverseTransformPoint(source.transform.position);
                var offset = local - position;
                offset.y = 0f;
                if (offset.sqrMagnitude > bestDistance)
                    continue;
                bestDistance = offset.sqrMagnitude;
                best = local;
            }
            return best;
        }

        /// <summary>Поставить чертёж по плану постановки или убрать уже стоящий там. true — что-то изменилось.</summary>
        public bool ToggleBlueprint(BlockPlacement.Plan plan)
        {
            if (Orders.At(plan.Cell) is { } existing)
                return existing.Kind == WorkKind.Build && Orders.Cancel(existing);
            var cost = costs != null ? costs.CostOf(plan.Block.Material) : null;
            return Orders.AddBuild(plan.Cell, plan.Block, plan.FillsExisting, cost) != null;
        }

        /// <summary>Поручить выкопать блок или снять приказ. true — что-то изменилось.</summary>
        public bool ToggleDig(Vector3Int block)
        {
            if (Orders.At(block) is { } existing)
                return existing.Kind == WorkKind.Dig && Orders.Cancel(existing);
            var current = worldView.World.GetBlock(block);
            return !current.IsAir && BlockMaterials.IsBreakable(current.Material) && Orders.AddDig(block) != null;
        }

        /// <summary>Поручить срубить дерево или снять приказ. true — что-то изменилось.</summary>
        public bool ToggleChop(TreeStand tree)
        {
            if (tree == null)
                return false;
            if (Orders.At(tree.Cell) is { } existing)
                return existing.Kind == WorkKind.Chop && Orders.Cancel(existing);
            return Orders.AddChop(tree) != null;
        }

        /// <summary>
        /// Поставить поддон с угловой клеткой origin (над землёй) или убрать тот, что уже стоит в этой клетке.
        /// Ставить можно на ровный твёрдый пол 2×2 с пустым местом на 2 блока вверх. true — что-то изменилось.
        /// </summary>
        public bool TogglePallet(Vector3Int origin)
        {
            if (Pallets == null)
                return false;
            if (Pallets.At(origin) is { } existing)
                return RemovePallet(existing);
            return Pallets.Add(worldView.World, origin) != null;
        }

        /// <summary>Убрать поддон; что на нём лежало, высыпается кучкой на его место.</summary>
        public bool RemovePallet(Pallet pallet)
        {
            var itemId = pallet.ItemId;
            var count = pallet.Count;
            if (!Pallets.Remove(pallet))
                return false;
            if (count > 0)
                droppedItems.Items.Drop(worldView.World, pallet.Origin, new ItemDrop(itemId, count));
            return true;
        }

        /// <summary>Поддон, под которым выкопали пол или в клетки которого поставили блок, разваливается: ресурс высыпается.</summary>
        private void PrunePallets()
        {
            for (var i = Pallets.All.Count - 1; i >= 0; i--)
            {
                var pallet = Pallets.All[i];
                if (!Pallets.IsValid(worldView.World, pallet))
                    RemovePallet(pallet);
            }
        }

        /// <summary>
        /// Задачи топлива: у огня, в котором помещается хотя бы одно полено, — задача принести одно полено (одна ходка).
        /// Ещё никем не взятая задача снимается, если огонь успели накормить. Задача огня, который погасили насовсем
        /// (выключен или удалён: тогда он и из зоны уходит) или сдвинули, снимается, даже если её уже несут: ноша остаётся на земле.
        /// </summary>
        private void UpdateFuelOrders()
        {
            if (buildZone == null)
                return;
            goneFires.Clear();
            foreach (var source in fuelTargets.Keys)
            {
                if (source == null || !source.isActiveAndEnabled)
                    goneFires.Add(source);
            }
            foreach (var source in goneFires)
            {
                CancelFuelOrder(fuelTargets[source]);
                fuelTargets.Remove(source);
            }

            foreach (var source in buildZone.Sources)
            {
                if (source.Fuel == null)
                    continue;
                if (!fuelTargets.TryGetValue(source, out var target))
                    fuelTargets[source] = target = new FuelTarget(source);
                // Огонь стоит на земле; его клетка — пустая над ней, даже если под огнём скат или полублок.
                var local = worldView.transform.InverseTransformPoint(source.transform.position);
                var cell = new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y + 0.5f), Mathf.FloorToInt(local.z));
                var space = Mathf.FloorToInt(source.Fuel.Space + 1e-4f);
                if (target.Order != null && Orders.Contains(target.Order))
                {
                    if (target.Order.Cell != cell || target.Order.Worker == null && space < 1)
                        CancelFuelOrder(target);
                    continue;
                }
                target.Order = null;
                if (space >= 1 && worldView.World.IsInsideBlocks(cell) && Orders.At(cell) == null)
                    target.Order = Orders.AddFuel(cell, target, settings.FuelItemId, 1);
            }
        }

        private void CancelFuelOrder(FuelTarget target)
        {
            if (target.Order != null)
                Orders.Cancel(target.Order);
            target.Order = null;
        }

        private bool IsLit(Vector2Int column) =>
            buildZone == null || litZone.Contains(worldView.transform.TransformPoint(new Vector3(column.x + 0.5f, 0f, column.y + 0.5f)));

        private void UpdateLight()
        {
            if (buildZone != null)
                buildZone.CollectCircles(litZone);
        }

        /// <summary>Принесённый к отменённому чертежу ресурс остаётся лежать на его месте.</summary>
        private void OnOrderCancelled(WorkOrder order)
        {
            if (order.ItemId != null && order.Delivered > 0)
                droppedItems.Items.Drop(worldView.World, order.Cell, new ItemDrop(order.ItemId, order.Delivered));
        }

        /// <summary>Рабочие встают кольцом в двух блоках от первого источника света (или от центра мира).</summary>
        private void Spawn()
        {
            var world = worldView.World;
            var center = buildZone != null && buildZone.Sources.Count > 0
                ? worldView.transform.InverseTransformPoint(buildZone.Sources[0].transform.position)
                : new Vector3(world.SizeInBlocks.x * 0.5f, 0f, world.SizeInBlocks.z * 0.5f);
            for (var i = 0; i < settings.Count; i++)
            {
                var angle = (i + 0.5f) * Mathf.PI * 2f / Mathf.Max(1, settings.Count);
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f;
                var column = WalkSurface.ColumnOf(point);
                column.x = Mathf.Clamp(column.x, 0, world.SizeInBlocks.x - 1);
                column.y = Mathf.Clamp(column.y, 0, world.SizeInBlocks.z - 1);
                workers.Add(new Worker(context, context.Surface.StandPoint(column)));
                // Сначала лицом к огню.
                var yaw = Mathf.Atan2(center.x - point.x, center.z - point.z) * Mathf.Rad2Deg;
                visuals.Add(CreateVisual(i, workers[i].Position, yaw));
            }
        }

        private WorkerVisual CreateVisual(int index, Vector3 position, float yaw)
        {
            var body = new GameObject($"Worker {index + 1}");
            body.transform.SetParent(transform, false);
            // Облик по номеру рабочего: у каждого свои кожа, волосы и рубаха. Части двигает анимация.
            // Коллайдеров нет: рабочий не мешает лучу выбора блока.
            var look = settings.LookFor(index);
            var meshes = WorkerMeshBuilder.BuildParts(look, settings.Height, body.name);
            var parts = new Transform[meshes.Length];
            var skins = new WorkerSkin[meshes.Length];
            for (var k = 0; k < meshes.Length; k++)
            {
                parts[k] = CreatePart(body.transform, ((WorkerPart)k).ToString(), meshes[k]);
                if (WorkerSkin.IsSoft((WorkerPart)k))
                    skins[k] = new WorkerSkin(meshes[k], (WorkerPart)k, settings.Height, look.Role);
            }
            var carry = CreatePart(body.transform, "Carried Item", null);
            // Фаза анимаций своя у каждого, чтобы отряд не дышал и не шагал в такт.
            var seed = Mathf.Repeat(index * 0.618034f, 1f);
            return new WorkerVisual(body.transform, parts, skins, meshes, carry.GetComponent<MeshFilter>(), look, seed, position) { Yaw = yaw };
        }

        private Transform CreatePart(Transform parent, string name, Mesh mesh)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            if (workerMaterial != null)
                renderer.sharedMaterial = workerMaterial;
            return part.transform;
        }

        private GameObject CreateLines(string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = outlineMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(properties);
            go.SetActive(false);
            return go;
        }

        private static void DestroyLines(GameObject lines)
        {
            if (lines == null)
                return;
            Destroy(lines.GetComponent<MeshFilter>().sharedMesh);
            Destroy(lines);
        }

        /// <summary>Рамки всех чертежей и приказов; перестраиваем, только когда задачи изменились.</summary>
        private void UpdateOrderLines()
        {
            PlaceInWorld(blueprintLines.transform);
            PlaceInWorld(digLines.transform);
            PlaceInWorld(palletLines.transform);
            if (shownPalletsVersion != Pallets.Version)
            {
                shownPalletsVersion = Pallets.Version;
                FillPalletLines();
            }
            if (shownOrdersVersion == Orders.Version)
                return;
            shownOrdersVersion = Orders.Version;
            FillLines(blueprintLines, WorkKind.Build);
            FillLines(digLines, WorkKind.Dig);
        }

        private void PlaceInWorld(Transform target)
        {
            var space = worldView.transform;
            target.SetPositionAndRotation(space.position, space.rotation);
            target.localScale = space.lossyScale;
        }

        private void FillLines(GameObject lines, WorkKind kind)
        {
            lineVertices.Clear();
            lineIndices.Clear();
            foreach (var order in Orders.Orders)
            {
                // Приказы рубить рисуются вместе с приказами копать, рамкой вокруг дерева.
                if (kind == WorkKind.Dig && order.Kind == WorkKind.Chop && forest != null)
                {
                    var (min, max) = forest.LocalBounds(order.Tree);
                    BlockOutlineMesh.AppendBox(order.Cell + min, order.Cell + max, outlinePadding, lineVertices, lineIndices);
                    continue;
                }
                if (order.Kind != kind)
                    continue;
                // Чертёж — по форме будущего блока, приказ копать — по нынешнему блоку и чуть шире, чтобы не сливаться с рамкой курсора.
                var block = kind == WorkKind.Build ? order.Block : worldView.World.GetBlock(order.Cell);
                var height = (float)block.Shape.Top / BlockShape.MaxHeight;
                BlockOutlineMesh.AppendBox(order.Cell, height, outlinePadding, lineVertices, lineIndices);
            }
            var mesh = lines.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            mesh.SetVertices(lineVertices);
            mesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            lines.SetActive(lineIndices.Count > 0);
        }

        private void FillPalletLines()
        {
            lineVertices.Clear();
            lineIndices.Clear();
            foreach (var pallet in Pallets.All)
                BlockOutlineMesh.AppendBox(pallet.Origin, pallet.Origin + new Vector3(Pallet.Size, Pallet.Height, Pallet.Size), outlinePadding,
                    lineVertices, lineIndices);
            var mesh = palletLines.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            mesh.SetVertices(lineVertices);
            mesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            palletLines.SetActive(lineIndices.Count > 0);
        }

        /// <summary>Подписи поддонов над их рамками: что лежит и сколько из скольких.</summary>
        private void OnGUI()
        {
            if (context == null || Pallets.All.Count == 0)
                return;
            var camera = UnityEngine.Camera.main;
            if (camera == null)
                return;
            palletLabelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
            foreach (var pallet in Pallets.All)
            {
                var top = worldView.transform.TransformPoint(pallet.Center + Vector3.up * (Pallet.Height + 0.3f));
                var screen = camera.WorldToScreenPoint(top);
                if (screen.z <= 0f)
                    continue;
                var text = PalletLabel.Text(pallet, Pallets.Capacity(pallet.ItemId));
                var size = palletLabelStyle.CalcSize(new GUIContent(text));
                // GUI считает y сверху, экран — снизу.
                GUI.Label(new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y), text, palletLabelStyle);
            }
        }

        /// <summary>Огонь как цель для топлива рабочих.</summary>
        private sealed class FuelTarget : IFuelTarget
        {
            private readonly LightSource source;

            public FuelTarget(LightSource source) => this.source = source;

            /// <summary>Задача топлива этого огня, если она сейчас есть.</summary>
            public WorkOrder Order { get; set; }

            public float FuelSpace => source.Fuel?.Space ?? 0f;

            public float AddFuel(float amount) => source.AddFuel(amount);
        }

        private sealed class WorkerVisual
        {
            public WorkerVisual(Transform body, Transform[] parts, WorkerSkin[] skins, Mesh[] meshes, MeshFilter carry, WorkerLook look, float seed,
                Vector3 position)
            {
                Body = body;
                Parts = parts;
                Skins = skins;
                Meshes = meshes;
                Carry = carry;
                Look = look;
                Seed = seed;
                LastPosition = position;
            }

            public Transform Body { get; }

            /// <summary>Части тела по индексу <see cref="WorkerPart"/>.</summary>
            public Transform[] Parts { get; }

            /// <summary>Мягкие части по индексу <see cref="WorkerPart"/>; у жёстких — null.</summary>
            public WorkerSkin[] Skins { get; }

            public Mesh[] Meshes { get; }
            public MeshFilter Carry { get; }
            public WorkerCarry CarryKind { get; set; }
            public WorkerCarryBuilder.Shape CarryShape { get; set; }
            public WorkerLook Look { get; }
            public float Seed { get; }
            public WorkerAnimator Animator { get; } = new();

            /// <summary>Куда рабочий смотрит, градусов вокруг оси Y мира блоков; держится, пока он стоит.</summary>
            public float Yaw { get; set; }

            public Vector3 LastPosition { get; set; }

            /// <summary>Скорость по земле, блоков в секунду, сглаженная.</summary>
            public float Speed { get; set; }

            public WorkerState LastState { get; set; }

            /// <summary>Сколько секунд рабочий в нынешнем состоянии.</summary>
            public float StateTime { get; set; }

            /// <summary>Сколько секунд стоит без дела у огня ночью.</summary>
            public float NightIdleTime { get; set; }
        }
    }
}
