using System.Collections.Generic;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Meshing;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Trees
{
    /// <summary>
    /// Лес в сцене: при старте рассаживает деревья по профилям местности мира (фон и зоны, <see cref="ForestScatter"/>;
    /// у каждого огня сцены роща на краю круга света),
    /// показывает каждое гранёной моделью вида (<see cref="TreeMeshBuilder"/>). От удара крона вздрагивает, летят щепки,
    /// с кроны сыплются листья или хвоя. Срубленное дерево валится от рубящего: наклоняется всё быстрее, бьётся о землю,
    /// подскакивает и распадается на поленья — они вылетают из ствола в кучку у основания, в те же ресурсы на земле,
    /// что и камень (<see cref="DroppedItemsView"/>). На месте срубленного остаётся пень. Рубят деревья рабочие (WorkerCrew),
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

        [Header("Рубка")]
        [Tooltip("Сколько секунд валится дерево высотой 4 блока; выше — дольше (как корень из высоты).")]
        [Range(0.3f, 3f)]
        [SerializeField] private float fallSeconds = 1.3f;
        [Tooltip("Сколько упавшее дерево лежит, прежде чем распасться на поленья, секунды.")]
        [Range(0f, 2f)]
        [SerializeField] private float lieSeconds = 0.4f;

        // Насколько дерево ложится (градусы) и как подскакивает, ударившись о землю.
        private const float FallAngle = 86f;
        private const float BounceAngle = 7f;
        private const float BounceSeconds = 0.28f;
        private const float ShakeSeconds = 0.45f;
        private const float ShakeAngle = 2.2f;

        private readonly Dictionary<TreeStand, TreeVisual> visuals = new();
        private readonly List<Falling> falls = new();
        private readonly Dictionary<TreeStump, GameObject> stumps = new();
        private readonly List<TreeStump> stumpsGone = new();
        private readonly Dictionary<TreeSpecies, Mesh> stumpMeshes = new();
        // Сетки строятся один раз на форму вида и общие у всех деревьев этой формы.
        private readonly Dictionary<(TreeSpecies species, int variant), Mesh> meshes = new();
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
            {
                forest.Felled -= OnFelled;
                forest.Struck -= OnStruck;
                forest.StumpStruck -= OnStumpStruck;
                forest.Uprooted -= OnUprooted;
            }
            foreach (var visual in visuals.Values)
            {
                if (visual.Root != null)
                    Destroy(visual.Root);
            }
            foreach (var fall in falls)
            {
                if (fall.Visual.Root != null)
                    Destroy(fall.Visual.Root);
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
            Animate();
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
            forest.Struck += OnStruck;
            forest.StumpStruck += OnStumpStruck;
            forest.Uprooted += OnUprooted;
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

        /// <summary>Удар: крона вздрагивает от рубящего, из ствола летят щепки, с кроны — листья или хвоя.</summary>
        private void OnStruck(TreeStand tree)
        {
            if (!visuals.TryGetValue(tree, out var visual))
                return;
            visual.ShakeStart = Time.time;
            var away = Away(tree);
            visual.ShakeAxis = ParentAxis(away);
            var trunk = new Vector3(tree.Position.x, Forest.BaseHeight(worldView.World, tree) + 0.5f, tree.Position.y)
                        - new Vector3(away.x, 0f, away.y) * (tree.Species.TrunkWidth * 0.5f);
            var debris = droppedItems.Debris;
            debris.Burst(trunk, Vector3.one * 0.05f, 4, DebrisShape.Chip, DebrisColors.WoodChip, 0.06f, 1.2f, 2.4f, 1.2f,
                -new Vector3(away.x, 0f, away.y) * 1.5f);
            debris.Burst(CrownCenter(tree), CrownHalf(tree), 3, DebrisShape.Leaf, visual.Crown, LeafSize(tree), 0.3f, 0.2f, 2.6f);
        }

        /// <summary>Удар по пню: летят щепки и комья земли.</summary>
        private void OnStumpStruck(TreeStump stump)
        {
            var top = StumpTop(stump);
            var debris = droppedItems.Debris;
            debris.Burst(top, Vector3.one * 0.08f, 4, DebrisShape.Chip, DebrisColors.WoodChip, 0.06f, 1.2f, 2.4f, 1.2f);
            debris.Burst(top + Vector3.down * TreeStump.Height, new Vector3(0.25f, 0.02f, 0.25f), 3, DebrisShape.Chip,
                DebrisColors.Of(BlockMaterials.Dirt, 0), 0.07f, 1f, 2.2f, 1.2f);
        }

        /// <summary>Пень выкорчеван: брызги земли и щепок, полено вылетает из ямки.</summary>
        private void OnUprooted(TreeStump stump)
        {
            var top = StumpTop(stump);
            var ground = top + Vector3.down * TreeStump.Height;
            var debris = droppedItems.Debris;
            debris.Burst(ground, new Vector3(0.3f, 0.05f, 0.3f), 10, DebrisShape.Chip, DebrisColors.Of(BlockMaterials.Dirt, 0),
                0.09f, 1.6f, 3f, 1.6f);
            debris.Burst(top, Vector3.one * 0.12f, 5, DebrisShape.Shard, stump.Species.TrunkColor, 0.12f, 1.4f, 2.8f, 1.6f);
            droppedItems.ExpectArrivals(stump.Cell, stump.Species.ItemId, new[] { top }, 0f);
        }

        private Vector3 StumpTop(TreeStump stump) =>
            new(stump.Position.x, Forest.BaseHeight(worldView.World, stump) + TreeStump.Height, stump.Position.y);

        /// <summary>
        /// Дерево упало: модель валится от рубящего (или в случайную сторону, если не рубили), а поленья, уже лежащие
        /// в данных, появятся в кучке, когда дерево ударится о землю и распадётся — вылетят из ствола.
        /// </summary>
        private void OnFelled(TreeStand tree)
        {
            if (!visuals.Remove(tree, out var visual))
                return;
            var away = Away(tree);
            var duration = fallSeconds * Mathf.Sqrt(Mathf.Max(1f, tree.Species.Height) / 4f);
            var fall = new Falling(visual, tree, ParentAxis(away), new Vector3(away.x, 0f, away.y), Time.time, duration);
            falls.Add(fall);

            var origins = new Vector3[tree.Species.WoodYield];
            for (var i = 0; i < origins.Length; i++)
                origins[i] = LyingPoint(fall, (i + 0.5f) / origins.Length * 0.85f) + Vector3.up * 0.15f;
            droppedItems.ExpectArrivals(tree.Cell, tree.Species.ItemId, origins, duration + BounceSeconds + lieSeconds);
        }

        /// <summary>Анимации деревьев: дрожь от удара и падение срубленных.</summary>
        private void Animate()
        {
            var now = Time.time;
            foreach (var visual in visuals.Values)
            {
                var t = now - visual.ShakeStart;
                if (t < 0f || t > ShakeSeconds || visual.Root == null)
                {
                    if (visual.Root != null && visual.Shaking)
                    {
                        visual.Root.transform.localRotation = visual.Rest;
                        visual.Shaking = false;
                    }
                    continue;
                }
                visual.Shaking = true;
                var angle = ShakeAngle * Mathf.Exp(-t * 7f) * Mathf.Sin(t * 38f);
                visual.Root.transform.localRotation = Quaternion.AngleAxis(angle, visual.ShakeAxis) * visual.Rest;
            }

            for (var i = falls.Count - 1; i >= 0; i--)
            {
                var fall = falls[i];
                var t = now - fall.Start;
                if (fall.Visual.Root == null)
                {
                    falls.RemoveAt(i);
                    continue;
                }
                if (t >= fall.Duration + BounceSeconds + lieSeconds)
                {
                    Break(fall);
                    falls.RemoveAt(i);
                    continue;
                }
                fall.Visual.Root.transform.localRotation = Quaternion.AngleAxis(FallAngleAt(fall, t), fall.Axis) * fall.Visual.Rest;
            }
        }

        /// <summary>Угол падения в момент t: сначала медленно, потом всё быстрее; ударившись, дерево подскакивает.</summary>
        private static float FallAngleAt(Falling fall, float t)
        {
            if (t < fall.Duration)
            {
                var x = t / fall.Duration;
                return FallAngle * x * x * x;
            }
            var after = (t - fall.Duration) / BounceSeconds;
            return after < 1f ? FallAngle - BounceAngle * Mathf.Sin(after * Mathf.PI) : FallAngle;
        }

        /// <summary>Упавшее дерево распадается: модель исчезает, по стволу брызги щепок, на месте кроны — листья.</summary>
        private void Break(Falling fall)
        {
            var debris = droppedItems.Debris;
            var tree = fall.Tree;
            for (var i = 0; i < 4; i++)
                debris.Burst(LyingPoint(fall, 0.15f + i * 0.2f), new Vector3(0.2f, 0.1f, 0.2f), 3, DebrisShape.Chip,
                    i % 2 == 0 ? DebrisColors.WoodChip : tree.Species.TrunkColor, 0.08f, 1.4f, 2.6f, 1.4f);
            var crown = LyingPoint(fall, Mathf.Lerp(tree.Species.TrunkHeight / Mathf.Max(0.01f, tree.Species.Height), 1f, 0.5f));
            debris.Burst(crown, Vector3.one * (tree.Species.CrownWidth * 0.35f), 14, DebrisShape.Leaf, fall.Visual.Crown,
                LeafSize(tree), 1.2f, 1.6f, 2.4f);
            Destroy(fall.Visual.Root);
        }

        /// <summary>Точка лежащего ствола на доле along от основания до макушки, координаты мира блоков.</summary>
        private Vector3 LyingPoint(Falling fall, float along)
        {
            var tree = fall.Tree;
            var bottom = Forest.BaseHeight(worldView.World, tree);
            var tilt = Quaternion.AngleAxis(FallAngle, fall.AxisLocal);
            var offset = tilt * (Vector3.up * (tree.Species.Height * along * fall.Visual.Scale));
            return new Vector3(tree.Position.x, bottom, tree.Position.y) + offset;
        }

        /// <summary>Пни: новые ставим, исчезнувшие (под ними копали, в клетку поставили блок) убираем.</summary>
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
                var standing = false;
                foreach (var other in forest.Stumps)
                {
                    if (other == stump)
                    {
                        standing = true;
                        break;
                    }
                }
                if (!standing)
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
            var seed = CellSeed(stump.Cell);
            go.transform.SetPositionAndRotation(
                space.TransformPoint(new Vector3(position.x, Forest.BaseHeight(worldView.World, stump), position.y)),
                space.rotation * Quaternion.Euler(0f, seed % 360, 0f));
            if (!stumpMeshes.TryGetValue(stump.Species, out var mesh))
                stumpMeshes[stump.Species] = mesh = BuildStump(stump.Species);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>Пень: короткий гранёный ствол чуть шире дерева, сверху светлый косой срез.</summary>
        private static Mesh BuildStump(TreeSpecies species)
        {
            var mesh = new FacetedMesh();
            var radius = Mathf.Max(0.06f, species.TrunkWidth * 0.5f);
            var random = new System.Random(species.DisplayName.GetHashCode());
            var bark = WorkerMeshBuilder.Paint(species.TrunkColor);
            var cut = WorkerMeshBuilder.Paint(DebrisColors.WoodChip);
            // Низ уходит в землю, чтобы на склоне не было щели.
            mesh.AddPrism(7, Vector3.down * 0.15f, radius * 1.18f, radius * 1.02f, TreeStump.Height + 0.15f, radius * 0.06f, random, bark);
            mesh.Transform = Matrix4x4.TRS(Vector3.up * TreeStump.Height, Quaternion.Euler(8f, 0f, 0f), Vector3.one);
            mesh.AddPrism(7, Vector3.down * 0.02f, radius * 0.95f, radius * 0.92f, 0.03f, 0f, random, cut);
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh($"Stump {species.DisplayName}");
        }

        /// <summary>Куда валится дерево: от рубящего, а если не рубили — в сторону по клетке.</summary>
        private static Vector2 Away(TreeStand tree)
        {
            if (tree.FallDirection.sqrMagnitude > 0.5f)
                return tree.FallDirection;
            var angle = CellSeed(tree.Cell) % 360 * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private Vector3 CrownCenter(TreeStand tree)
        {
            var species = tree.Species;
            var bottom = Forest.BaseHeight(worldView.World, tree);
            return new Vector3(tree.Position.x, bottom + (species.TrunkHeight + species.Height) * 0.5f, tree.Position.y);
        }

        private static Vector3 CrownHalf(TreeStand tree)
        {
            var species = tree.Species;
            return new Vector3(species.CrownWidth * 0.35f, (species.Height - species.TrunkHeight) * 0.3f, species.CrownWidth * 0.35f);
        }

        /// <summary>Размер листа или хвоинки: хвоя мельче листьев.</summary>
        private static float LeafSize(TreeStand tree) => tree.Species.Shape == TreeShape.Leafy ? 0.09f : 0.06f;

        /// <summary>
        /// Модель дерева: общая сетка формы вида, свои поворот, наклон и размер по клетке, оттенок кроны
        /// с разбросом и оттенком травы локации (MaterialPropertyBlock).
        /// </summary>
        private TreeVisual CreateVisual(TreeStand tree)
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
            var scale = Range(random, 1f - sizeSpread, 1f + sizeSpread * 0.5f);
            root.transform.localScale = Vector3.one * scale;

            root.AddComponent<MeshFilter>().sharedMesh = MeshFor(species, random.Next(species.Variants));
            var renderer = root.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            var grass = worldView.World.GrassTint?.Sample(position.x, position.y) ?? Color.white;
            var tint = TreeMeshBuilder.CrownTint(species.CrownColor, grass, seed ^ 0x5bd1e995);
            properties.SetColor(BaseColorId, tint);
            renderer.SetPropertyBlock(properties);
            // Без коллайдера: дерево выбирается лучом по коробке (Forest.Raycast) и не мешает лучу выбора блока.
            var crown = species.CrownColor * tint;
            crown.a = 1f;
            return new TreeVisual(root, root.transform.localRotation, scale, crown, ParentAxis(Away(tree)));
        }

        /// <summary>
        /// Ось, вокруг которой модель наклоняется в сторону away (X и Z мира блоков), в системе родителя моделей:
        /// поворот на положительный угол клонит верх дерева в эту сторону.
        /// </summary>
        private Vector3 ParentAxis(Vector2 away)
        {
            var space = worldView.transform;
            var up = transform.InverseTransformDirection(space.up);
            var direction = transform.InverseTransformDirection(space.TransformDirection(new Vector3(away.x, 0f, away.y)));
            return Vector3.Cross(up, direction).normalized;
        }

        /// <summary>Модель дерева и как её трясти и ронять.</summary>
        private sealed class TreeVisual
        {
            public readonly GameObject Root;
            /// <summary>Поворот модели стоя (наклон и поворот вокруг оси), в системе родителя.</summary>
            public readonly Quaternion Rest;
            public readonly float Scale;
            /// <summary>Цвет кроны с оттенком дерева: им же окрашены листья, летящие с неё.</summary>
            public readonly Color Crown;
            public Vector3 ShakeAxis;
            public float ShakeStart = float.NegativeInfinity;
            public bool Shaking;

            public TreeVisual(GameObject root, Quaternion rest, float scale, Color crown, Vector3 shakeAxis)
            {
                Root = root;
                Rest = rest;
                Scale = scale;
                Crown = crown;
                ShakeAxis = shakeAxis;
            }
        }

        /// <summary>Падающее срубленное дерево.</summary>
        private sealed class Falling
        {
            public readonly TreeVisual Visual;
            public readonly TreeStand Tree;
            /// <summary>Ось, вокруг которой валится модель, в системе родителя модели.</summary>
            public readonly Vector3 Axis;
            /// <summary>Та же ось в координатах мира блоков (для точек лежащего ствола).</summary>
            public readonly Vector3 AxisLocal;
            public readonly float Start;
            public readonly float Duration;

            public Falling(TreeVisual visual, TreeStand tree, Vector3 axis, Vector3 away, float start, float duration)
            {
                Visual = visual;
                Tree = tree;
                AxisLocal = Vector3.Cross(Vector3.up, away).normalized;
                Axis = axis;
                Start = start;
                Duration = duration;
            }
        }

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
