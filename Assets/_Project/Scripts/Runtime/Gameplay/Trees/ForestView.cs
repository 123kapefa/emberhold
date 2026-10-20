using System.Collections.Generic;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Meshing;
using Emberhold.World.Trees;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Trees
{
    /// <summary>
    /// Лес в сцене: при старте рассаживает деревья по профилям местности мира (фон и зоны, <see cref="ForestScatter"/>;
    /// у каждого огня сцены роща на краю круга света),
    /// показывает каждое гранёной моделью вида (<see cref="TreeMeshBuilder"/>) и убирает упавшие. Упавшее дерево кладёт поленья
    /// в те же ресурсы на земле, что и камень (<see cref="DroppedItemsView"/>). На месте срубленного остаётся пень. Рубят деревья рабочие (WorkerCrew),
    /// приказ рубить ставит игрок инструментом 5 (BlockEditController).
    /// </summary>
    public sealed class ForestView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private DroppedItemsView droppedItems;
        [Tooltip("Вокруг огней в этом радиусе (в блоках) деревья не растут: место для лагеря.")]
        [Min(0f)]
        [SerializeField] private float clearAroundFire = 3f;

        [Header("Вид")]
        [Tooltip("Материал деревьев на шейдере Emberhold/World/Tree: цвет из вершин сетки, _BaseColor красит крону.")]
        [SerializeField] private Material material;
        [Tooltip("Наклон дерева в градусах, до: у каждого свой, чтобы лес не стоял по линейке.")]
        [Range(0f, 10f)]
        [SerializeField] private float maxLean = 4f;
        [Tooltip("Разброс размера дерева: от 1 − x до 1 + x/2.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float sizeSpread = 0.1f;

        private readonly Dictionary<TreeStand, GameObject> visuals = new();
        // Сетки строятся один раз на форму вида и общие у всех деревьев этой формы.
        private readonly Dictionary<(TreeSpecies species, int variant), Mesh> meshes = new();
        private readonly Dictionary<TreeStump, GameObject> stumps = new();
        private readonly List<TreeStump> stumpsGone = new();
        private readonly Dictionary<TreeSpecies, Mesh> stumpMeshes = new();
        private MaterialPropertyBlock properties;
        private Forest forest;
        private int shownVersion = -1;

        /// <summary>
        /// Деревья мира. Рассаживаются при первом обращении: мир и ресурсы на земле создаются в Awake,
        /// поэтому из любого Start лес уже готов. Null, если сцена собрана не до конца.
        /// </summary>
        public Forest Forest
        {
            get
            {
                if (forest == null && worldView != null && worldView.World != null && droppedItems != null && droppedItems.Items != null)
                    Plant();
                return forest;
            }
        }

        private void Start()
        {
            if (Forest == null)
            {
                Debug.LogError("[Forest] Не назначены VoxelWorldView или DroppedItemsView.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (forest != null)
                forest.Felled -= OnFelled;
            foreach (var visual in visuals.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
            foreach (var stump in stumps.Values)
            {
                if (stump != null)
                    Destroy(stump);
            }
            foreach (var mesh in meshes.Values)
                Destroy(mesh);
            foreach (var mesh in stumpMeshes.Values)
                Destroy(mesh);
        }

        private void LateUpdate()
        {
            if (forest == null)
                return;
            // Под деревом могли выкопать землю или поставить блок в его клетку.
            forest.Settle(worldView.World);
            if (shownVersion == forest.Version)
                return;
            shownVersion = forest.Version;
            foreach (var tree in forest.Trees)
            {
                if (!visuals.ContainsKey(tree))
                    visuals[tree] = CreateVisual(tree);
            }
            ShowStumps();
        }

        /// <summary>
        /// Дерево, в которое попадает луч из сцены, ближе maxDistance; distance — в единицах сцены.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out TreeStand tree, out float distance)
        {
            tree = null;
            distance = maxDistance;
            if (Forest == null)
                return false;
            var space = worldView.transform;
            // Мир не масштабируется, поэтому расстояние в блоках совпадает с расстоянием в сцене.
            return forest.Raycast(worldView.World, space.InverseTransformPoint(ray.origin),
                space.InverseTransformDirection(ray.direction), maxDistance, out tree, out distance);
        }

        /// <summary>Пень, в который попадает луч из сцены, ближе maxDistance; distance — в единицах сцены.</summary>
        public bool RaycastStump(Ray ray, float maxDistance, out TreeStump stump, out float distance)
        {
            stump = null;
            distance = maxDistance;
            if (Forest == null)
                return false;
            var space = worldView.transform;
            return forest.RaycastStump(worldView.World, space.InverseTransformPoint(ray.origin),
                space.InverseTransformDirection(ray.direction), maxDistance, out stump, out distance);
        }

        /// <summary>Коробка пня для рамки, в долях клетки пня.</summary>
        public (Vector3 min, Vector3 max) LocalBounds(TreeStump stump)
        {
            var (min, max) = Forest.StumpBounds(worldView.World, stump);
            return (min - stump.Cell, max - stump.Cell);
        }

        /// <summary>Коробка дерева для рамки: от основания до макушки, шириной с крону; в долях клетки дерева.</summary>
        public (Vector3 min, Vector3 max) LocalBounds(TreeStand tree)
        {
            var half = Mathf.Max(tree.Species.CrownWidth, tree.Species.TrunkWidth) * 0.5f;
            var bottom = Forest.BaseHeight(worldView.World, tree) - tree.Cell.y;
            var center = new Vector2(0.5f, 0.5f) + tree.Offset;
            return (new Vector3(center.x - half, bottom, center.y - half), new Vector3(center.x + half, bottom + tree.Species.Height, center.y + half));
        }

        private void Plant()
        {
            forest = new Forest(droppedItems.Items);
            forest.Felled += OnFelled;
            var terrain = worldView.Terrain;
            if (terrain == null)
                return;
            var fires = new List<Vector3>();
            // У каждого огня сцены роща стоит на краю круга света: первые дрова рядом с лагерем.
            var starts = new List<ForestStart>();
            foreach (var source in FindObjectsByType<LightSource>())
            {
                var fire = worldView.transform.InverseTransformPoint(source.transform.position);
                fires.Add(fire);
                if (source.FullRadius > 0f)
                    starts.Add(new ForestStart(new Vector2(fire.x, fire.z), source.FullRadius));
            }
            var clear = clearAroundFire * clearAroundFire;
            var planted = ForestScatter.Scatter(worldView.World, terrain.Seed, worldView.ForestAt, forest, column =>
            {
                // Выше границы леса на горах деревьев нет.
                if (worldView.AboveTreeLine(column))
                    return true;
                foreach (var fire in fires)
                {
                    var d = new Vector2(column.x + 0.5f - fire.x, column.y + 0.5f - fire.z);
                    if (d.sqrMagnitude <= clear)
                        return true;
                }
                return false;
            }, starts);
            Debug.Log($"[Forest] Посажено деревьев: {planted}.", this);
        }

        private void OnFelled(TreeStand tree)
        {
            if (!visuals.Remove(tree, out var visual))
                return;
            Destroy(visual);
        }

        /// <summary>
        /// Модель дерева: общая сетка формы вида, свои поворот, наклон и размер по клетке, оттенок кроны
        /// с разбросом и оттенком травы локации (MaterialPropertyBlock).
        /// </summary>
        private GameObject CreateVisual(TreeStand tree)
        {
            properties ??= new MaterialPropertyBlock();
            var species = tree.Species;
            var seed = CellSeed(tree.Cell);
            var random = new System.Random(seed);
            var root = new GameObject($"Tree {species.DisplayName}");
            root.transform.SetParent(transform, false);
            var space = worldView.transform;
            var bottom = Forest.BaseHeight(worldView.World, tree);
            var lean = Quaternion.Euler(Range(random, -maxLean, maxLean), 0f, Range(random, -maxLean, maxLean));
            var position = tree.Position;
            root.transform.SetPositionAndRotation(space.TransformPoint(new Vector3(position.x, bottom, position.y)),
                space.rotation * lean * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f));
            root.transform.localScale = Vector3.one * Range(random, 1f - sizeSpread, 1f + sizeSpread * 0.5f);

            root.AddComponent<MeshFilter>().sharedMesh = MeshFor(species, random.Next(species.Variants));
            var renderer = root.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            var grass = worldView.World.GrassTint?.Sample(position.x, position.y) ?? Color.white;
            properties.SetColor(BaseColorId, TreeMeshBuilder.CrownTint(species.CrownColor, grass, seed ^ 0x5bd1e995));
            renderer.SetPropertyBlock(properties);
            // Без коллайдера: дерево выбирается лучом по коробке (Forest.Raycast) и не мешает лучу выбора блока.
            return root;
        }

        /// <summary>Пни: новые ставим, исчезнувшие (выкорчевали, под ними копали, в клетку поставили блок) убираем.</summary>
        private void ShowStumps()
        {
            foreach (var stump in forest.Stumps)
            {
                if (!stumps.ContainsKey(stump))
                    stumps[stump] = CreateStump(stump);
            }
            stumpsGone.Clear();
            foreach (var stump in stumps.Keys)
            {
                if (!stump.IsPresent)
                    stumpsGone.Add(stump);
            }
            foreach (var stump in stumpsGone)
            {
                Destroy(stumps[stump]);
                stumps.Remove(stump);
            }
        }

        private GameObject CreateStump(TreeStump stump)
        {
            var go = new GameObject($"Stump {stump.Species.DisplayName}");
            go.transform.SetParent(transform, false);
            var space = worldView.transform;
            var position = stump.Position;
            go.transform.SetPositionAndRotation(
                space.TransformPoint(new Vector3(position.x, Forest.BaseHeight(worldView.World, stump), position.y)),
                space.rotation * Quaternion.Euler(0f, CellSeed(stump.Cell) % 360, 0f));
            if (!stumpMeshes.TryGetValue(stump.Species, out var mesh))
                stumpMeshes[stump.Species] = mesh = BuildStump(stump.Species);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>Пень-заглушка: короткий гранёный ствол чуть шире дерева со светлым срезом сверху (вид — #120).</summary>
        private static Mesh BuildStump(TreeSpecies species)
        {
            var mesh = new FacetedMesh();
            var radius = Mathf.Max(0.06f, species.TrunkWidth * 0.5f);
            var random = new System.Random(species.DisplayName.GetHashCode());
            // a = 0: шейдер деревьев оставляет цвет вершин как есть (не красит его оттенком кроны).
            var bark = Untinted(species.TrunkColor.linear);
            var cut = Untinted(Color.Lerp(species.TrunkColor, Color.white, 0.45f).linear);
            // Низ уходит в землю, чтобы на склоне не было щели.
            mesh.AddPrism(7, Vector3.down * 0.15f, radius * 1.18f, radius * 1.02f, TreeStump.Height + 0.15f, radius * 0.06f, random, _ => bark);
            mesh.AddPrism(7, Vector3.up * (TreeStump.Height - 0.01f), radius * 0.95f, radius * 0.92f, 0.03f, 0f, random, _ => cut);
            return mesh.ToMesh($"Stump {species.DisplayName}");
        }

        private static Color Untinted(Color color) => new(color.r, color.g, color.b, 0f);

        private Mesh MeshFor(TreeSpecies species, int variant)
        {
            if (!meshes.TryGetValue((species, variant), out var mesh))
            {
                mesh = TreeMeshBuilder.Build(species, variant);
                meshes[(species, variant)] = mesh;
            }
            return mesh;
        }

        private static int CellSeed(Vector3Int cell)
        {
            unchecked
            {
                return cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791;
            }
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
