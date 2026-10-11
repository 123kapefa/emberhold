using System.Collections.Generic;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Building
{
    public enum BlockTool
    {
        Dig,
        Remove,
    }

    /// <summary>
    /// Правка мира мышью: подсвечивает блок под курсором, ЛКМ применяет текущий инструмент.
    /// 1 — «Копать»: каждый клик добавляет блоку урон и трещины, после нескольких кликов блок исчезает целиком;
    /// 2 — «Убрать» сразу. Камера занимает ПКМ, СКМ и колесо,
    /// поэтому здесь только ЛКМ и цифры. Ввод берётся из карты «Building» в EmberholdControls.
    /// </summary>
    public sealed class BlockEditController : MonoBehaviour
    {
        private const string ActionMapName = "Building";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private BlockEditSettings settings;
        [SerializeField] private VoxelWorldView worldView;
        [Tooltip("Камера, из которой пускаем луч. Если пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;
        [Tooltip("Материал рамки подсветки (Unlit, цвет задаётся из настроек).")]
        [SerializeField] private Material outlineMaterial;

        private InputActionMap map;
        private InputAction point;
        private InputAction use;
        private InputAction toolDig;
        private InputAction toolRemove;
        // Камера: пока игрок крутит или тащит её, ЛКМ не должна копать то, что проносится под курсором.
        private InputAction cameraOrbitHold;
        private InputAction cameraPanHold;

        private readonly List<Vector3> outlineVertices = new();
        private readonly List<int> outlineIndices = new();
        private MaterialPropertyBlock outlineProperties;
        private GameObject outline;
        private MeshRenderer outlineRenderer;
        private Mesh outlineMesh;
        private (float height, Vector3Int face)? outlineShape;

        private readonly List<Vector3> crackVertices = new();
        private readonly List<int> crackIndices = new();
        private MaterialPropertyBlock crackProperties;
        private GameObject cracks;
        private MeshRenderer cracksRenderer;
        private Mesh cracksMesh;
        private int cracksVersion = -1;

        public BlockTool Tool { get; private set; } = BlockTool.Dig;

        /// <summary>Блок под курсором в этом кадре, если он есть.</summary>
        public BlockHit? Hovered { get; private set; }

        /// <summary>Урон начатых блоков. Рабочие потом будут копать через него же.</summary>
        public BlockDamage Damage { get; } = new();

        private void Awake()
        {
            if (settings == null || worldView == null)
            {
                Debug.LogError("[BlockEdit] Не назначены BlockEditSettings или VoxelWorldView.", this);
                enabled = false;
                return;
            }

            if (targetCamera == null)
                targetCamera = UnityEngine.Camera.main;
            (outline, outlineMesh, outlineRenderer) = CreateLineObject("Block Outline");
            outlineProperties = new MaterialPropertyBlock();
            (cracks, cracksMesh, cracksRenderer) = CreateLineObject("Block Cracks");
            crackProperties = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (settings == null || worldView == null)
            {
                enabled = false;
                return;
            }

            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            if (map == null)
            {
                Debug.LogError($"[BlockEdit] Нет карты ввода «{ActionMapName}».", this);
                enabled = false;
                return;
            }

            point = map.FindAction("Point", true);
            use = map.FindAction("Use", true);
            toolDig = map.FindAction("ToolDig", true);
            toolRemove = map.FindAction("ToolRemove", true);
            cameraOrbitHold = asset.FindAction("Camera/OrbitHold");
            cameraPanHold = asset.FindAction("Camera/PanHold");
            map.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
            Hovered = null;
            if (outline != null)
                outline.SetActive(false);
        }

        private void OnDestroy()
        {
            Destroy(outlineMesh);
            Destroy(outline);
            Destroy(cracksMesh);
            Destroy(cracks);
        }

        private void Update()
        {
            if (map == null || targetCamera == null || worldView.World == null)
                return;

            if (toolDig.WasPressedThisFrame())
                Tool = BlockTool.Dig;
            if (toolRemove.WasPressedThisFrame())
                Tool = BlockTool.Remove;

            Hovered = Pick(point.ReadValue<Vector2>());
            if (Hovered is { } hit && use.WasPressedThisFrame() && !IsCameraDragging())
            {
                Apply(hit);
                // Блок мог исчезнуть: рамка должна сразу показать новое состояние.
                Hovered = worldView.World.GetBlock(hit.Block).IsAir ? null : hit;
            }

            // Блок могли убрать не копанием (инструмент «Убрать», потом рабочие): его урон больше не нужен.
            Damage.ForgetMissing(worldView.World);
            UpdateOutline();
            UpdateCracks();
        }

        private bool IsCameraDragging() =>
            (cameraOrbitHold != null && cameraOrbitHold.IsPressed()) || (cameraPanHold != null && cameraPanHold.IsPressed());

        /// <summary>Применить текущий инструмент к блоку. Возвращает true, если блок убран. Меш перестроит VoxelWorldView в LateUpdate этого кадра.</summary>
        public bool Apply(BlockHit hit) => Tool switch
        {
            BlockTool.Dig => Damage.Add(worldView.World, hit.Block, 1f / settings.HitsToBreak),
            BlockTool.Remove => BlockEdits.Remove(worldView.World, hit.Block),
            _ => false,
        };

        private BlockHit? Pick(Vector2 screenPosition)
        {
            var ray = targetCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var raycast, settings.MaxRayDistance, settings.GroundMask, QueryTriggerInteraction.Ignore))
                return null;

            // Мир блоков — локальные координаты VoxelWorldView: блок (x, y, z) занимает куб от (x, y, z).
            var space = worldView.transform;
            var localPoint = space.InverseTransformPoint(raycast.point);
            var localNormal = space.InverseTransformDirection(raycast.normal);
            return BlockPicking.TryPick(worldView.World, localPoint, localNormal, out var hit) ? hit : null;
        }

        /// <summary>
        /// Объект с мешем из линий в корне сцены: у мира в детях только чанки,
        /// а масштаб и поворот мира копируем при каждом показе.
        /// </summary>
        private (GameObject, Mesh, MeshRenderer) CreateLineObject(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = outlineMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return (go, mesh, renderer);
        }

        private void PlaceInWorld(Transform target, Vector3 localPosition)
        {
            var space = worldView.transform;
            target.SetPositionAndRotation(space.TransformPoint(localPosition), space.rotation);
            target.localScale = space.lossyScale;
        }

        private void UpdateOutline()
        {
            if (Hovered is not { } hit)
            {
                outline.SetActive(false);
                return;
            }

            var block = worldView.World.GetBlock(hit.Block);
            var shape = (height: (float)block.Shape.Top / BlockShape.MaxHeight, face: hit.Face);
            if (outlineShape != shape)
            {
                outlineShape = shape;
                BlockOutlineMesh.Build(shape.height, shape.face, settings.OutlinePadding, outlineVertices, outlineIndices);
                outlineMesh.Clear();
                outlineMesh.SetVertices(outlineVertices);
                outlineMesh.SetIndices(outlineIndices, MeshTopology.Lines, 0);
                outlineMesh.RecalculateBounds();
            }

            PlaceInWorld(outline.transform, hit.Block);
            outlineProperties.SetColor(BaseColorId, Tool == BlockTool.Dig ? settings.DigColor : settings.RemoveColor);
            outlineRenderer.SetPropertyBlock(outlineProperties);
            outline.SetActive(true);
        }

        /// <summary>Трещины всех повреждённых блоков одним мешем; перестраиваем, только когда урон изменился.</summary>
        private void UpdateCracks()
        {
            if (cracksVersion == Damage.Version)
                return;
            cracksVersion = Damage.Version;

            crackVertices.Clear();
            crackIndices.Clear();
            foreach (var (block, value) in Damage.Entries)
            {
                var height = (float)worldView.World.GetBlock(block).Shape.Top / BlockShape.MaxHeight;
                var stage = BlockDamage.Stage(value, settings.CrackStages);
                BlockCrackMesh.Append(block, height, stage, block.GetHashCode(), settings.OutlinePadding * 0.5f,
                    crackVertices, crackIndices);
            }

            cracksMesh.Clear();
            cracksMesh.SetVertices(crackVertices);
            cracksMesh.SetTriangles(crackIndices, 0, false);
            cracksMesh.RecalculateBounds();
            PlaceInWorld(cracks.transform, Vector3.zero);
            crackProperties.SetColor(BaseColorId, settings.CrackColor);
            cracksRenderer.SetPropertyBlock(crackProperties);
            cracks.SetActive(crackIndices.Count > 0);
        }

        private void OnGUI()
        {
            if (map == null)
                return;
            var name = Tool == BlockTool.Dig ? "Копать" : "Убрать";
            GUI.Label(new Rect(10, 10, 400, 22), $"Инструмент: {name}   (1 — копать, 2 — убрать сразу, ЛКМ — применить)");
        }
    }
}
