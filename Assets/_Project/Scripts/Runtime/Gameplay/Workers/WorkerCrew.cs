using System.Collections.Generic;
using Emberhold.Gameplay.Building;
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
    /// и рабочих, рисует рабочих (гранёный человек из WorkerMeshBuilder, над головой — кубик ноши), рамки чертежей и приказов копать и рубить.
    /// Чертежи и приказы ставит игрок инструментами BlockEditController (4 и 5). Топливо для огней отряд заводит сам:
    /// у каждого огня, в котором есть место хотя бы под одно полено, висит задача принести поленья.
    /// Урон блоков и устойчивость общие с BlockEditController, ресурсы на земле — из DroppedItemsView.
    /// </summary>
    public sealed class WorkerCrew : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

        [Header("Вид")]
        [SerializeField] private Material workerMaterial;
        [Tooltip("Материал кубика ноши над головой.")]
        [SerializeField] private Material carryMaterial;
        [Tooltip("Материал рамок чертежей и приказов (Unlit, цвет задаётся ниже).")]
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private Color blueprintColor = new(0.35f, 0.75f, 1f);
        [SerializeField] private Color digOrderColor = new(1f, 0.45f, 0.15f);
        [Range(0f, 0.1f)]
        [SerializeField] private float outlinePadding = 0.03f;

        private readonly List<Worker> workers = new();
        private readonly List<WorkerVisual> visuals = new();
        private readonly Dictionary<LightSource, FuelTarget> fuelTargets = new();
        private readonly List<LightSource> goneFires = new();
        private readonly LightZone litZone = new();
        private WorkerContext context;

        private readonly List<Vector3> lineVertices = new();
        private readonly List<int> lineIndices = new();
        private GameObject blueprintLines;
        private GameObject digLines;
        private int shownOrdersVersion = -1;

        /// <summary>Чертежи и приказы копать.</summary>
        public WorkOrders Orders { get; } = new();

        public IReadOnlyList<Worker> Workers => workers;

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

            context = new WorkerContext(worldView.World, Orders, droppedItems.Items, editor.Damage, editor.Stability, settings, IsLit,
                () => buildZone == null || litZone.Count > 0, editor.OnWorldChanged, forest != null ? forest.Forest : null);
            Orders.Cancelled += OnOrderCancelled;
            blueprintLines = CreateLines("Blueprints", blueprintColor);
            digLines = CreateLines("Dig Orders", digOrderColor);
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
                Destroy(visual.Mesh);
            }
            DestroyLines(blueprintLines);
            DestroyLines(digLines);
        }

        private void Update()
        {
            if (context == null)
                return;
            UpdateLight();
            // Клетку чертежа могли занять, блок под приказом — выкопать вручную, дерево — уронить.
            Orders.Prune(worldView.World);
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
            for (var i = 0; i < workers.Count; i++)
            {
                var worker = workers[i];
                var visual = visuals[i];
                var body = visual.Body;
                var carry = visual.Carry;
                // Лицом туда, куда идёт, а за работой — к задаче.
                var look = worker.State == WorkerState.Working && worker.Order != null
                    ? worker.Order.Cell + new Vector3(0.5f, 0f, 0.5f) - worker.Position
                    : worker.Position - visual.LastPosition;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-6f)
                    visual.Yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                visual.LastPosition = worker.Position;
                // Сетка в блоках, ноги в начале координат: стоит на верхе столбца.
                body.SetPositionAndRotation(space.TransformPoint(worker.Position), space.rotation * Quaternion.Euler(0f, visual.Yaw, 0f));
                body.localScale = space.lossyScale;
                carry.gameObject.SetActive(worker.Carrying != null);
                // Ноша того же цвета, что кучка ресурса: поленья не спутать с камнем.
                if (worker.Carrying is { } item && droppedItems.MaterialFor(item.ItemId) is { } material && carry.sharedMaterial != material)
                    carry.sharedMaterial = material;
            }
            UpdateOrderLines();
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
            return !worldView.World.GetBlock(block).IsAir && Orders.AddDig(block) != null;
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
        /// Задачи топлива: у огня, в котором помещается хотя бы одно полено, — задача принести до FuelPerTrip поленьев.
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
                    target.Order = Orders.AddFuel(cell, target, settings.FuelItemId, Mathf.Min(space, settings.FuelPerTrip));
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
                visuals.Add(CreateVisual(i, workers[i].Position));
            }
        }

        private WorkerVisual CreateVisual(int index, Vector3 position)
        {
            var body = new GameObject($"Worker {index + 1}");
            body.transform.SetParent(transform, false);
            // Облик по номеру рабочего: у каждого свои кожа, волосы и рубаха. Коллайдера нет: рабочий не мешает лучу выбора блока.
            var mesh = WorkerMeshBuilder.Build(settings.LookFor(index), settings.Height, body.name);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>();
            if (workerMaterial != null)
                renderer.sharedMaterial = workerMaterial;

            var carry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carry.name = "Carried Item";
            Destroy(carry.GetComponent<Collider>());
            carry.transform.SetParent(body.transform, false);
            // Над головой.
            carry.transform.localPosition = new Vector3(0f, settings.Height + 0.25f, 0f);
            carry.transform.localScale = new Vector3(0.32f, 0.3f, 0.32f);
            if (carryMaterial != null)
                carry.GetComponent<MeshRenderer>().sharedMaterial = carryMaterial;
            carry.SetActive(false);
            return new WorkerVisual(body.transform, carry.GetComponent<MeshRenderer>(), mesh, position);
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
            public WorkerVisual(Transform body, MeshRenderer carry, Mesh mesh, Vector3 position)
            {
                Body = body;
                Carry = carry;
                Mesh = mesh;
                LastPosition = position;
            }

            public Transform Body { get; }
            public MeshRenderer Carry { get; }
            public Mesh Mesh { get; }

            /// <summary>Куда рабочий смотрит, градусов вокруг оси Y мира блоков; держится, пока он стоит.</summary>
            public float Yaw { get; set; }

            public Vector3 LastPosition { get; set; }
        }
    }
}
