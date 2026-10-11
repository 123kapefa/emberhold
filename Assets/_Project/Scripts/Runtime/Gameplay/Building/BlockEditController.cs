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
        Build,
    }

    /// <summary>
    /// Правка мира мышью: подсвечивает блок под курсором, ЛКМ применяет текущий инструмент.
    /// 1 — «Копать»: каждый клик снимает с блока ХП (прочность — в BlockDurability), появляются трещины,
    /// а когда ХП кончились, блок исчезает целиком;
    /// 2 — «Убрать» сразу; 3 — «Строить»: блок встаёт за гранью под курсором, рядом виден его предпросмотр.
    /// При стройке G — следующая форма, T — материал, щелчок ПКМ — поворот, [ и ] — высота полублока.
    /// ПКМ с перетаскиванием по-прежнему вращает камеру: поворот блока срабатывает, только если мышь
    /// между нажатием и отпусканием почти не сдвинулась. Камера занимает ПКМ (удержание), СКМ, колесо
    /// и WASD/QE/RF. Ввод берётся из карты «Building» в EmberholdControls.
    /// </summary>
    public sealed class BlockEditController : MonoBehaviour
    {
        private const string ActionMapName = "Building";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private BlockEditSettings settings;
        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private BlockDurabilityTable durability;
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
        private InputAction toolBuild;
        private InputAction nextForm;
        private InputAction nextMaterial;
        private InputAction rotate;
        private InputAction slabHigher;
        private InputAction slabLower;
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

        private BlockGhost ghost;
        private Vector2 rotatePressPosition;
        private BlockPlacement.Plan? plan;

        public BlockTool Tool { get; set; } = BlockTool.Dig;

        /// <summary>Блок под курсором в этом кадре, если он есть.</summary>
        public BlockHit? Hovered { get; private set; }

        /// <summary>Что ставит инструмент «Строить».</summary>
        public BuildSelection Selection { get; } = new();

        /// <summary>Куда и что встанет при клике инструментом «Строить», если ставить есть куда.</summary>
        public BlockPlacement.Plan? Plan => plan;

        /// <summary>Урон начатых блоков. Рабочие потом будут копать через него же.</summary>
        public BlockDamage Damage { get; private set; }

        private void Awake()
        {
            if (settings == null || worldView == null || durability == null)
            {
                Debug.LogError("[BlockEdit] Не назначены BlockEditSettings, VoxelWorldView или BlockDurability.", this);
                enabled = false;
                return;
            }

            Damage = new BlockDamage(durability);

            if (targetCamera == null)
                targetCamera = UnityEngine.Camera.main;
            (outline, outlineMesh, outlineRenderer) = CreateLineObject("Block Outline");
            outlineProperties = new MaterialPropertyBlock();
            (cracks, cracksMesh, cracksRenderer) = CreateLineObject("Block Cracks");
            crackProperties = new MaterialPropertyBlock();
            ghost = new BlockGhost();
        }

        private void OnEnable()
        {
            if (Damage == null)
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
            toolBuild = map.FindAction("ToolBuild", true);
            nextForm = map.FindAction("NextForm", true);
            nextMaterial = map.FindAction("NextMaterial", true);
            rotate = map.FindAction("Rotate", true);
            slabHigher = map.FindAction("SlabHigher", true);
            slabLower = map.FindAction("SlabLower", true);
            cameraOrbitHold = asset.FindAction("Camera/OrbitHold");
            cameraPanHold = asset.FindAction("Camera/PanHold");
            map.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
            Hovered = null;
            plan = null;
            if (outline != null)
                outline.SetActive(false);
            ghost?.Hide();
        }

        private void OnDestroy()
        {
            Destroy(outlineMesh);
            Destroy(outline);
            Destroy(cracksMesh);
            Destroy(cracks);
            ghost?.Destroy();
        }

        private void Update()
        {
            if (map == null || targetCamera == null || worldView.World == null)
                return;

            if (toolDig.WasPressedThisFrame())
                Tool = BlockTool.Dig;
            if (toolRemove.WasPressedThisFrame())
                Tool = BlockTool.Remove;
            if (toolBuild.WasPressedThisFrame())
                Tool = BlockTool.Build;
            if (Tool == BlockTool.Build)
                ReadBuildInput();

            Hovered = Pick(point.ReadValue<Vector2>());
            UpdatePlan();
            if (Hovered is { } hit && use.WasPressedThisFrame() && !IsCameraDragging())
            {
                Apply(hit);
                // Блок мог исчезнуть или появиться: рамка и предпросмотр должны сразу показать новое состояние.
                Hovered = worldView.World.GetBlock(hit.Block).IsAir ? null : hit;
                UpdatePlan();
            }

            // Блок могли убрать не копанием (инструмент «Убрать», потом рабочие): его урон больше не нужен.
            Damage.ForgetMissing(worldView.World);
            UpdateOutline();
            UpdateGhost();
            UpdateCracks();
        }

        private void ReadBuildInput()
        {
            if (nextForm.WasPressedThisFrame())
                Selection.NextForm();
            if (nextMaterial.WasPressedThisFrame())
                Selection.NextMaterial();
            // Щелчок ПКМ — поворот; если мышь заметно сдвинулась, это было вращение камеры.
            var pointer = point.ReadValue<Vector2>();
            if (rotate.WasPressedThisFrame())
                rotatePressPosition = pointer;
            if (rotate.WasReleasedThisFrame()
                && (pointer - rotatePressPosition).sqrMagnitude <= settings.RotateClickMaxDrag * settings.RotateClickMaxDrag)
                Selection.Rotate();
            if (slabHigher.WasPressedThisFrame())
                Selection.SlabHeight++;
            if (slabLower.WasPressedThisFrame())
                Selection.SlabHeight--;
        }

        private void UpdatePlan()
        {
            plan = null;
            if (Tool == BlockTool.Build && Hovered is { } hit
                && BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var p))
                plan = p;
        }

        private bool IsCameraDragging() =>
            (cameraOrbitHold != null && cameraOrbitHold.IsPressed()) || (cameraPanHold != null && cameraPanHold.IsPressed());

        /// <summary>
        /// Применить текущий инструмент к блоку. Возвращает true, если мир изменился (блок убран или поставлен).
        /// Меш перестроит VoxelWorldView в LateUpdate этого кадра.
        /// </summary>
        public bool Apply(BlockHit hit)
        {
            switch (Tool)
            {
                case BlockTool.Dig:
                    return Damage.Hit(worldView.World, hit.Block, settings.HitDamage);
                case BlockTool.Remove:
                    return BlockEdits.Remove(worldView.World, hit.Block);
                case BlockTool.Build:
                    if (!BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var p))
                        return false;
                    BlockPlacement.Apply(worldView.World, p);
                    // Досыпанный блок начинает с целой прочностью.
                    Damage.Forget(p.Cell);
                    return true;
                default:
                    return false;
            }
        }

        private BlockHit? Pick(Vector2 screenPosition)
        {
            var ray = targetCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var raycast, settings.MaxRayDistance, settings.GroundMask, QueryTriggerInteraction.Ignore))
                return null;

            // Мир блоков — локальные координаты VoxelWorldView: блок (x, y, z) занимает куб от (x, y, z).
            var space = worldView.transform;
            var localPoint = space.InverseTransformPoint(raycast.point);
            var localNormal = space.InverseTransformDirection(raycast.normal);
            // Точно — по треугольнику меша; по точке — запасной путь, если попали не в чанк.
            // Коллайдер перестраивается в LateUpdate, поэтому проверяем, что блок ещё на месте.
            if (worldView.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                return worldView.World.GetBlock(block).IsAir ? null : new BlockHit(block, BlockPicking.DominantAxis(localNormal));
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
            if (Hovered is not { } hit || Tool == BlockTool.Build && plan == null)
            {
                outline.SetActive(false);
                return;
            }

            // При стройке рамка — вокруг ячейки, куда встанет блок, без метки грани.
            var cell = Tool == BlockTool.Build ? plan.Value.Cell : hit.Block;
            var block = Tool == BlockTool.Build ? plan.Value.Block : worldView.World.GetBlock(hit.Block);
            var face = Tool == BlockTool.Build ? Vector3Int.zero : hit.Face;
            var shape = (height: (float)block.Shape.Top / BlockShape.MaxHeight, face);
            if (outlineShape != shape)
            {
                outlineShape = shape;
                BlockOutlineMesh.Build(shape.height, shape.face, settings.OutlinePadding, outlineVertices, outlineIndices);
                outlineMesh.Clear();
                outlineMesh.SetVertices(outlineVertices);
                outlineMesh.SetIndices(outlineIndices, MeshTopology.Lines, 0);
                outlineMesh.RecalculateBounds();
            }

            PlaceInWorld(outline.transform, cell);
            outlineProperties.SetColor(BaseColorId, Tool switch
            {
                BlockTool.Dig => settings.DigColor,
                BlockTool.Build => settings.BuildColor,
                _ => settings.RemoveColor,
            });
            outlineRenderer.SetPropertyBlock(outlineProperties);
            outline.SetActive(true);
        }

        private void UpdateGhost()
        {
            // Досыпание видно по рамке, отдельный предпросмотр поверх блока не нужен.
            if (plan is not { FillsExisting: false } p)
            {
                ghost.Hide();
                return;
            }
            ghost.Show(p.Block, worldView.BlockMaterial(p.Block.Material));
            PlaceInWorld(ghost.Transform, p.Cell);
        }

        /// <summary>Трещины всех повреждённых блоков одним мешем; перестраиваем, только когда урон изменился.</summary>
        private void UpdateCracks()
        {
            if (cracksVersion == Damage.Version)
                return;
            cracksVersion = Damage.Version;

            crackVertices.Clear();
            crackIndices.Clear();
            foreach (var (block, _) in Damage.Entries)
            {
                var height = (float)worldView.World.GetBlock(block).Shape.Top / BlockShape.MaxHeight;
                var stage = BlockDamage.Stage(Damage.Fraction(worldView.World, block), settings.CrackStages);
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
            var name = Tool switch { BlockTool.Dig => "Копать", BlockTool.Remove => "Убрать", _ => "Строить" };
            GUI.Label(new Rect(10, 10, 600, 22), $"Инструмент: {name}   (1 — копать, 2 — убрать сразу, 3 — строить, ЛКМ — применить)");
            if (Tool != BlockTool.Build)
                return;
            var form = BuildSelection.FormName(Selection.Form);
            if (Selection.Form == BuildForm.Slab)
                form += $" {Selection.SlabHeight}/16";
            GUI.Label(new Rect(10, 30, 700, 22),
                $"{form}, {BuildSelection.MaterialName(Selection.Material)}, поворот {Selection.Rotation * 90}°   " +
                "(G — форма, T — материал, щелчок ПКМ — повернуть, [ ] — высота полублока)");
        }
    }
}
