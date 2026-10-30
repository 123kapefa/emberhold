using System.Collections.Generic;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Trees;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Trees;
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
        /// <summary>Чертёж: блок ставят рабочие, принеся ресурс.</summary>
        Blueprint,
        /// <summary>Приказ копать: блок выкапывают рабочие. По дереву — приказ рубить.</summary>
        DigOrder,
        /// <summary>Поддон 2×2 для ресурсов: ставится сразу и бесплатно, повторный клик убирает.</summary>
        Pallet,
        /// <summary>Чертёж костра: рабочие приносят поленья и ставят новый костёр на блок под курсором.</summary>
        Campfire,
    }

    /// <summary>
    /// Правка мира мышью: подсвечивает блок под курсором, ЛКМ применяет текущий инструмент.
    /// 1 — «Копать»: каждый клик снимает с блока ХП (прочность — в BlockDurability), появляются трещины,
    /// а когда ХП кончились, блок исчезает целиком;
    /// 2 — «Убрать» сразу; 3 — «Строить» сразу: блок встаёт за гранью под курсором, рядом виден его предпросмотр.
    /// 4 — «Чертёж» и 5 — «Поручить копать»: то же самое делают рабочие (<see cref="WorkerCrew"/>);
    /// клик инструментом 5 по дереву поручает его срубить; повторный клик по тому же месту снимает чертёж или приказ.
    /// 6 — «Поддон»: ставит поддон 2×2 на землю под курсором (блок под курсором — его ближний угол) или убирает стоящий там.
    /// 7 — «Костёр»: чертёж костра на блоке под курсором (клетка над ним должна быть в свету); строят его рабочие.
    /// При стройке и чертеже G — следующая форма, T — материал, щелчок ПКМ — поворот, [ и ] — высота полублока.
    /// ПКМ с перетаскиванием по-прежнему вращает камеру: поворот блока срабатывает, только если мышь
    /// между нажатием и отпусканием почти не сдвинулась. Камера занимает ПКМ (удержание), СКМ, колесо
    /// и WASD/QE/RF. Ввод берётся из карты «Building» в EmberholdControls.
    /// Править можно только внутри света (BuildZone); снаружи рамка другого цвета и клик даёт подсказку.
    /// После каждой правки блоки, потерявшие опору, падают (<see cref="BlockStability"/>);
    /// видимое падение рисует <see cref="BlockFallView"/> по событию <see cref="BlocksFell"/>.
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
        [Tooltip("Зона застройки из источников света. Если пусто — править можно везде (песочницы).")]
        [SerializeField] private BuildZone buildZone;
        [Tooltip("Рабочие для чертежей и приказов копать. Если пусто — инструменты 4 и 5 недоступны (песочницы).")]
        [SerializeField] private WorkerCrew workers;
        [Tooltip("Лес: инструмент 5 по дереву поручает его срубить. Если пусто — деревья не выбираются.")]
        [SerializeField] private ForestView forest;

        private InputActionMap map;
        private InputAction point;
        private InputAction use;
        private InputAction toolDig;
        private InputAction toolRemove;
        private InputAction toolBuild;
        private InputAction toolBlueprint;
        private InputAction toolDigOrder;
        private InputAction toolPallet;
        private InputAction toolCampfire;
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
        private (FormBox extent, Vector3Int face)? outlineShape;

        private readonly List<Vector3> crackVertices = new();
        private readonly List<int> crackIndices = new();
        private MaterialPropertyBlock crackProperties;
        private GameObject cracks;
        private MeshRenderer cracksRenderer;
        private Mesh cracksMesh;
        private int cracksVersion = -1;
        private int cracksHiddenVersion = -1;
        private float blockedMessageUntil = float.NegativeInfinity;
        private string blockedMessage = DarkDigMessage;
        private const string DarkDigMessage = "Темно, сюда не копать";
        private const string DarkBuildMessage = "Темно, здесь не построить";
        private const string UnbreakableMessage = "Не копается";
        private const string PalletBlockedMessage = "Поддону нужна ровная свободная площадка 2 на 2 в свете костра";

        private BlockGhost ghost;
        private Vector2 rotatePressPosition;
        private BlockPlacement.Plan? plan;
        private readonly List<BlockFall> falls = new();

        public BlockTool Tool { get; set; } = BlockTool.Dig;

        /// <summary>Блок под курсором в этом кадре, если он есть.</summary>
        public BlockHit? Hovered { get; private set; }

        /// <summary>Дерево под курсором при инструменте 5, если оно ближе блока.</summary>
        public TreeStand HoveredTree { get; private set; }

        /// <summary>Что ставит инструмент «Строить».</summary>
        public BuildSelection Selection { get; } = new();

        /// <summary>Куда и что встанет при клике инструментом «Строить», если ставить есть куда.</summary>
        public BlockPlacement.Plan? Plan => plan;

        /// <summary>Урон начатых блоков. Рабочие потом будут копать через него же.</summary>
        public BlockDamage Damage { get; private set; }

        /// <summary>Проверка опоры после правок; разрушения от врагов потом пойдут через неё же.</summary>
        public BlockStability Stability { get; } = new();

        /// <summary>Блоки, упавшие после последней правки.</summary>
        public IReadOnlyList<BlockFall> LastFalls => falls;

        /// <summary>После правки упали блоки без опоры (тот же список, что <see cref="LastFalls"/>). Мир уже изменён.</summary>
        public event System.Action<IReadOnlyList<BlockFall>> BlocksFell;

        /// <summary>Правка изменила блок в этой клетке (до проверки опоры и <see cref="BlocksFell"/>).</summary>
        public event System.Action<Vector3Int> BlockChanged;

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
            toolBlueprint = map.FindAction("ToolBlueprint", true);
            toolDigOrder = map.FindAction("ToolDigOrder", true);
            toolPallet = map.FindAction("ToolPallet", true);
            toolCampfire = map.FindAction("ToolCampfire");
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
            if (workers != null && toolBlueprint.WasPressedThisFrame())
                Tool = BlockTool.Blueprint;
            if (workers != null && toolDigOrder.WasPressedThisFrame())
                Tool = BlockTool.DigOrder;
            if (workers != null && toolPallet.WasPressedThisFrame())
                Tool = BlockTool.Pallet;
            if (workers != null && workers.BuildsCampfires && toolCampfire != null && toolCampfire.WasPressedThisFrame())
                Tool = BlockTool.Campfire;
            if (PlacesBlocks)
                ReadBuildInput();

            HoveredTree = PickTree(point.ReadValue<Vector2>());
            Hovered = HoveredTree == null ? Pick(point.ReadValue<Vector2>()) : null;
            UpdatePlan();
            if (HoveredTree is { } tree && use.WasPressedThisFrame() && !IsCameraDragging() && !IsStrikingCrawler())
            {
                if (!ApplyToTree(tree))
                    ShowBlockedMessage(DarkDigMessage);
            }
            else if (Hovered is { } palletHit && Tool == BlockTool.Pallet && use.WasPressedThisFrame() && !IsCameraDragging()
                     && !IsStrikingCrawler())
            {
                if (!ApplyPallet(palletHit))
                    ShowBlockedMessage(PalletBlockedMessage);
            }
            else if (Hovered is { } hit && use.WasPressedThisFrame() && !IsCameraDragging() && !IsStrikingCrawler())
            {
                if (IsUnbreakable(hit))
                    ShowBlockedMessage(UnbreakableMessage);
                else if (!CanApply(hit))
                    ShowBlockedMessage(DarkMessage);
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
            if (PlacesBlocks && Hovered is { } hit
                && BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var p))
                plan = p;
        }

        /// <summary>Инструмент ставит блок по плану постановки: сразу или чертежом.</summary>
        private bool PlacesBlocks => Tool is BlockTool.Build or BlockTool.Blueprint;

        private bool IsCameraDragging() =>
            (cameraOrbitHold != null && cameraOrbitHold.IsPressed()) || (cameraPanHold != null && cameraPanHold.IsPressed());

        /// <summary>
        /// Щелчок пришёлся по ползуну (его бьёт CrawlerStrike) или по кнопкам спаунера: блок за ними не трогаем.
        /// </summary>
        private bool IsStrikingCrawler()
        {
            var pointer = point.ReadValue<Vector2>();
            return CrawlerSpawner.IsOverButtons(pointer)
                || Crawler.TryPick(targetCamera.ScreenPointToRay(pointer), settings.MaxRayDistance, out _);
        }

        /// <summary>
        /// Применить текущий инструмент к блоку; вне света ничего не делает, кроме снятия чертежа или приказа. Возвращает true, если мир изменился
        /// (блок убран или поставлен) или поставлен либо снят чертёж или приказ копать.
        /// Меш перестроит VoxelWorldView в LateUpdate этого кадра.
        /// </summary>
        public bool Apply(BlockHit hit)
        {
            falls.Clear();
            if (IsUnbreakable(hit) || !CanApply(hit))
                return false;

            Vector3Int changed;
            switch (Tool)
            {
                case BlockTool.Dig:
                    if (!Damage.Hit(worldView.World, hit.Block, settings.HitDamage))
                        return false;
                    changed = hit.Block;
                    break;
                case BlockTool.Remove:
                    if (!BlockEdits.Remove(worldView.World, hit.Block))
                        return false;
                    changed = hit.Block;
                    break;
                case BlockTool.Build:
                    if (!BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var p))
                        return false;
                    BlockPlacement.Apply(worldView.World, p);
                    // Досыпанный блок начинает с целой прочностью.
                    Damage.Forget(p.Cell);
                    changed = p.Cell;
                    break;
                case BlockTool.Blueprint:
                    return workers != null && BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var blueprint)
                        && workers.ToggleBlueprint(blueprint);
                case BlockTool.DigOrder:
                    return workers != null && workers.ToggleDig(hit.Block);
                case BlockTool.Pallet:
                    return ApplyPallet(hit);
                case BlockTool.Campfire:
                    return workers != null && workers.ToggleCampfire(hit.Block + Vector3Int.up);
                default:
                    return false;
            }

            OnWorldChanged(changed);
            return true;
        }

        /// <summary>
        /// Инструмент 6 по земле: убрать поддон, стоящий над блоком под курсором, или поставить новый, у которого
        /// этот блок — пол ближнего угла. Ставить можно, только если вся площадка в свете; убрать — везде.
        /// true — поддон поставлен или убран.
        /// </summary>
        public bool ApplyPallet(BlockHit hit)
        {
            if (workers == null || workers.Pallets == null)
                return false;
            var origin = hit.Block + Vector3Int.up;
            if (workers.Pallets.At(origin) is { } existing)
                return workers.RemovePallet(existing);
            return PalletInLight(origin) && workers.TogglePallet(origin);
        }

        /// <summary>Все клетки площадки поддона с углом origin освещены.</summary>
        private bool PalletInLight(Vector3Int origin)
        {
            for (var dz = 0; dz < Pallet.Size; dz++)
            for (var dx = 0; dx < Pallet.Size; dx++)
            {
                if (!CanEdit(origin + new Vector3Int(dx, 0, dz)))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Инструмент 5 по дереву: поручить срубить или снять приказ. Поручить можно только в свете, снять — везде.
        /// true — приказ поставлен или снят.
        /// </summary>
        public bool ApplyToTree(TreeStand tree)
        {
            if (workers == null || tree == null || Tool != BlockTool.DigOrder)
                return false;
            var order = workers.Orders.At(tree.Cell);
            if (!CanEdit(tree.Cell) && order is not { Kind: WorkKind.Chop })
                return false;
            return workers.ToggleChop(tree);
        }

        /// <summary>
        /// Блок в клетке поставили или убрали (игрок или рабочий): сообщить виду, уронить блоки без опоры
        /// вместе с их уроном и показать падение. Результат — в <see cref="LastFalls"/>.
        /// </summary>
        public void OnWorldChanged(Vector3Int changed)
        {
            falls.Clear();
            BlockChanged?.Invoke(changed);
            // Блоки без опоры падают, их урон падает вместе с ними.
            if (Stability.Settle(worldView.World, changed, falls) > 0)
            {
                Damage.Move(falls);
                BlocksFell?.Invoke(falls);
            }
        }

        private void ShowBlockedMessage(string message)
        {
            blockedMessage = message;
            blockedMessageUntil = Time.unscaledTime + settings.BlockedMessageSeconds;
        }

        /// <summary>Инструмент убирает блоки (копать, убрать, поручить копать), а блок под курсором — скала или коренная порода.</summary>
        private bool IsUnbreakable(BlockHit hit) =>
            Tool is BlockTool.Dig or BlockTool.Remove or BlockTool.DigOrder
            && !BlockMaterials.IsBreakable(worldView.World.GetBlock(hit.Block).Material);

        /// <summary>
        /// Ячейка, которую меняет текущий инструмент: при стройке — куда встанет блок, у костра — клетка над блоком
        /// под курсором, иначе — блок под курсором.
        /// </summary>
        private Vector3Int TargetCell(BlockHit hit)
        {
            if (Tool == BlockTool.Campfire)
                return hit.Block + Vector3Int.up;
            return PlacesBlocks && BlockPlacement.TryPlan(worldView.World, hit, Selection.Block, out var p) ? p.Cell : hit.Block;
        }

        /// <summary>
        /// Инструмент можно применить: клетка в свете. Чертёж или приказ, оставшийся в темноте (костёр погас
        /// или сжался), снять можно и там.
        /// </summary>
        private bool CanApply(BlockHit hit)
        {
            var cell = TargetCell(hit);
            if (CanEdit(cell))
                return true;
            var order = workers != null ? workers.Orders.At(cell) : null;
            return order != null && (Tool == BlockTool.Blueprint && order.Kind == WorkKind.Build
                                     || Tool == BlockTool.DigOrder && order.Kind == WorkKind.Dig
                                     || Tool == BlockTool.Campfire && order.Kind == WorkKind.Campfire);
        }

        /// <summary>
        /// Блок освещён (свет доходит до центра блока; глубоко под огнём темно) или зона застройки не задана.
        /// </summary>
        public bool CanEdit(Vector3Int block) =>
            buildZone == null || buildZone.Reaches(worldView.transform.TransformPoint(block + new Vector3(0.5f, 0.5f, 0.5f)));

        private string DarkMessage => Tool is BlockTool.Dig or BlockTool.Remove or BlockTool.DigOrder ? DarkDigMessage : DarkBuildMessage;

        /// <summary>Подсказка у курсора, пока он наведён туда, где текущий инструмент не сработает; иначе null.</summary>
        private string HoverMessage()
        {
            if (HoveredTree is { } tree)
                return CanEdit(tree.Cell) || workers != null && workers.Orders.At(tree.Cell) is { Kind: WorkKind.Chop } ? null : DarkDigMessage;
            if (Hovered is not { } hit)
                return null;
            if (IsUnbreakable(hit))
                return UnbreakableMessage;
            return CanApply(hit) ? null : DarkMessage;
        }

        /// <summary>Дерево под курсором при инструменте 5, если луч попадает в него раньше, чем в землю.</summary>
        private TreeStand PickTree(Vector2 screenPosition)
        {
            if (forest == null || workers == null || Tool != BlockTool.DigOrder)
                return null;
            var ray = targetCamera.ScreenPointToRay(screenPosition);
            if (!forest.Raycast(ray, settings.MaxRayDistance, out var tree, out var distance))
                return null;
            if (Physics.Raycast(ray, out var ground, settings.MaxRayDistance, settings.GroundMask, QueryTriggerInteraction.Ignore)
                && ground.distance < distance)
                return null;
            return tree;
        }

        /// <summary>Блок под точкой экрана: по треугольнику меша, с поправкой на скос над ним (<see cref="BlockPicking.PreferSlopeAbove"/>).</summary>
        public BlockHit? Pick(Vector2 screenPosition)
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
                return worldView.World.GetBlock(block).IsAir ? null
                    : BlockPicking.PreferSlopeAbove(worldView.World, new BlockHit(block, BlockPicking.DominantAxis(localNormal)));
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
            if (HoveredTree is { } tree)
            {
                ShowTreeOutline(tree);
                return;
            }
            if (Tool == BlockTool.Pallet && Hovered is { } palletHit)
            {
                ShowPalletOutline(palletHit);
                return;
            }
            if (Hovered is not { } hit || PlacesBlocks && plan == null)
            {
                outline.SetActive(false);
                return;
            }

            // При стройке рамка — вокруг ячейки, куда встанет блок, без метки грани.
            var cell = PlacesBlocks ? plan.Value.Cell : hit.Block;
            var block = PlacesBlocks ? plan.Value.Block : worldView.World.GetBlock(hit.Block);
            var face = PlacesBlocks ? Vector3Int.zero : hit.Face;
            var extent = block.Shape.Extent;
            if (outlineShape is not { } shown || !shown.extent.Equals(extent) || shown.face != face)
            {
                outlineShape = (extent, face);
                var (min, max) = ExtentBounds(extent);
                BlockOutlineMesh.Build(min, max, face, settings.OutlinePadding, outlineVertices, outlineIndices);
                outlineMesh.Clear();
                outlineMesh.SetVertices(outlineVertices);
                outlineMesh.SetIndices(outlineIndices, MeshTopology.Lines, 0);
                outlineMesh.RecalculateBounds();
            }

            PlaceInWorld(outline.transform, cell);
            var color = !CanEdit(TargetCell(hit)) || IsUnbreakable(hit) ? settings.BlockedColor : Tool switch
            {
                BlockTool.Dig => settings.DigColor,
                BlockTool.Build => settings.BuildColor,
                BlockTool.Blueprint => settings.BlueprintColor,
                BlockTool.DigOrder => settings.DigOrderColor,
                BlockTool.Campfire => settings.BlueprintColor,
                _ => settings.RemoveColor,
            };
            outlineProperties.SetColor(BaseColorId, color);
            outlineRenderer.SetPropertyBlock(outlineProperties);
            outline.SetActive(true);
        }

        /// <summary>Рамка вокруг дерева под курсором: цвет приказа, вне света — запрета.</summary>
        private void ShowTreeOutline(TreeStand tree)
        {
            var (min, max) = forest.LocalBounds(tree);
            // Рамку дерева строим каждый кадр: деревьев под курсором немного, а форма блока потом перестроится заново.
            outlineShape = null;
            BlockOutlineMesh.Build(min, max, Vector3Int.zero, settings.OutlinePadding, outlineVertices, outlineIndices);
            outlineMesh.Clear();
            outlineMesh.SetVertices(outlineVertices);
            outlineMesh.SetIndices(outlineIndices, MeshTopology.Lines, 0);
            outlineMesh.RecalculateBounds();
            PlaceInWorld(outline.transform, tree.Cell);
            outlineProperties.SetColor(BaseColorId, CanEdit(tree.Cell) ? settings.DigOrderColor : settings.BlockedColor);
            outlineRenderer.SetPropertyBlock(outlineProperties);
            outline.SetActive(true);
        }

        /// <summary>
        /// Рамка поддона под курсором: стоящего — цветом «убрать», нового — цветом чертежа, если его можно поставить,
        /// иначе цветом запрета.
        /// </summary>
        private void ShowPalletOutline(BlockHit hit)
        {
            var origin = hit.Block + Vector3Int.up;
            var existing = workers != null && workers.Pallets != null ? workers.Pallets.At(origin) : null;
            if (existing != null)
                origin = existing.Origin;
            outlineShape = null;
            BlockOutlineMesh.Build(Vector3.zero, new Vector3(Pallet.Size, Pallet.Height, Pallet.Size), Vector3Int.zero, settings.OutlinePadding,
                outlineVertices, outlineIndices);
            outlineMesh.Clear();
            outlineMesh.SetVertices(outlineVertices);
            outlineMesh.SetIndices(outlineIndices, MeshTopology.Lines, 0);
            outlineMesh.RecalculateBounds();
            PlaceInWorld(outline.transform, origin);
            var canPlace = workers != null && workers.Pallets != null && PalletInLight(origin)
                           && workers.Pallets.CanPlace(worldView.World, origin);
            var color = existing != null ? settings.RemoveColor : canPlace ? settings.BlueprintColor : settings.BlockedColor;
            outlineProperties.SetColor(BaseColorId, color);
            outlineRenderer.SetPropertyBlock(outlineProperties);
            outline.SetActive(true);
        }

        /// <summary>Коробка формы в долях ячейки: по ней строятся рамка и трещины.</summary>
        private static (Vector3 min, Vector3 max) ExtentBounds(FormBox e)
        {
            const float n = BlockShape.MaxHeight;
            return (new Vector3(e.X0, e.Y0, e.Z0) / n, new Vector3(e.X1, e.Y1, e.Z1) / n);
        }

        private void UpdateGhost()
        {
            // Досыпание видно по рамке, отдельный предпросмотр поверх блока не нужен; вне света тоже не показываем.
            if (plan is not { FillsExisting: false } p || !CanEdit(p.Cell))
            {
                ghost.Hide();
                return;
            }
            ghost.Show(p.Block, worldView.BlockMaterial(p.Block.Material));
            PlaceInWorld(ghost.Transform, p.Cell);
        }

        /// <summary>
        /// Трещины всех повреждённых блоков одним мешем; перестраиваем, только когда изменился урон
        /// или набор блоков, которые чанк прячет на время падения.
        /// </summary>
        private void UpdateCracks()
        {
            if (cracksVersion == Damage.Version && cracksHiddenVersion == worldView.HiddenVersion)
                return;
            cracksVersion = Damage.Version;
            cracksHiddenVersion = worldView.HiddenVersion;

            crackVertices.Clear();
            crackIndices.Clear();
            foreach (var (block, _) in Damage.Entries)
            {
                // Блок ещё летит в эту клетку: трещины появятся, когда он долетит.
                if (worldView.IsHidden(block))
                    continue;
                var (min, max) = ExtentBounds(worldView.World.GetBlock(block).Shape.Extent);
                var stage = BlockDamage.Stage(Damage.Fraction(worldView.World, block), settings.CrackStages);
                BlockCrackMesh.Append(block, min, max, stage, block.GetHashCode(), settings.OutlinePadding * 0.5f,
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
            var name = Tool switch
            {
                BlockTool.Dig => "Копать",
                BlockTool.Remove => "Убрать",
                BlockTool.Build => "Строить",
                BlockTool.Blueprint => "Чертёж",
                BlockTool.Pallet => "Поддон",
                BlockTool.Campfire => "Костёр",
                _ => "Поручить копать и рубить",
            };
            var keys = workers != null
                ? "1 — копать, 2 — убрать сразу, 3 — строить сразу, 4 — чертёж, 5 — поручить копать или рубить дерево, 6 — поддон" +
                  (workers.BuildsCampfires ? ", 7 — костёр" : "") + ", ЛКМ — применить"
                : "1 — копать, 2 — убрать сразу, 3 — строить, ЛКМ — применить";
            GUI.Label(new Rect(10, 10, 900, 22), $"Инструмент: {name}   ({keys})");
            var message = HoverMessage() ?? (Time.unscaledTime < blockedMessageUntil ? blockedMessage : null);
            if (message != null)
            {
                // Подсказка у курсора: GUI считает y сверху, экран — снизу.
                var mouse = point.ReadValue<Vector2>();
                var rect = new Rect(mouse.x + 16f, Screen.height - mouse.y - 8f, 360f, 24f);
                GUI.Label(rect, message);
            }
            if (!PlacesBlocks)
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
