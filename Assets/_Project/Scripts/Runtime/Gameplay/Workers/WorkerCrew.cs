using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Отряд рабочих в сцене: при старте ставит их у первого источника света, каждый кадр обновляет задачи
    /// и рабочих, рисует рабочих (капсула, над головой — кубик ноши), рамки чертежей и приказов копать.
    /// Чертежи и приказы ставит игрок инструментами BlockEditController (4 и 5).
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
        private readonly List<(Transform body, GameObject carry)> visuals = new();
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
                () => buildZone == null || litZone.Count > 0);
            Orders.Cancelled += OnOrderCancelled;
            blueprintLines = CreateLines("Blueprints", blueprintColor);
            digLines = CreateLines("Dig Orders", digOrderColor);
            UpdateLight();
            Spawn();
        }

        private void OnDestroy()
        {
            Orders.Cancelled -= OnOrderCancelled;
            foreach (var (body, _) in visuals)
            {
                if (body != null)
                    Destroy(body.gameObject);
            }
            DestroyLines(blueprintLines);
            DestroyLines(digLines);
        }

        private void Update()
        {
            if (context == null)
                return;
            UpdateLight();
            // Клетку чертежа могли занять, блок под приказом — выкопать вручную.
            Orders.Prune(worldView.World);
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
                var (body, carry) = visuals[i];
                // Капсула 0.9 блока ростом стоит ногами на верхе столбца.
                body.SetPositionAndRotation(space.TransformPoint(worker.Position + Vector3.up * 0.45f), space.rotation);
                body.localScale = space.lossyScale.x * new Vector3(0.4f, 0.45f, 0.4f);
                carry.SetActive(worker.Carrying != null);
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
                visuals.Add(CreateVisual(i));
            }
        }

        private (Transform, GameObject) CreateVisual(int index)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = $"Worker {index + 1}";
            // Без коллайдеров: рабочий не должен мешать лучу выбора блока.
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            if (workerMaterial != null)
                body.GetComponent<MeshRenderer>().sharedMaterial = workerMaterial;

            var carry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carry.name = "Carried Item";
            Destroy(carry.GetComponent<Collider>());
            carry.transform.SetParent(body.transform, false);
            // Над головой: капсула в своих координатах от -1 до 1 по высоте.
            carry.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            carry.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);
            if (carryMaterial != null)
                carry.GetComponent<MeshRenderer>().sharedMaterial = carryMaterial;
            carry.SetActive(false);
            return (body.transform, carry);
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
    }
}
