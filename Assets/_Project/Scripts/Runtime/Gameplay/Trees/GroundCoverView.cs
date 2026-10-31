using System;
using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Vegetation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Trees
{
    /// <summary>
    /// Мелочь на земле в сцене: после леса рассаживает траву, цветы, папоротник и прочее по профилям местности
    /// (<see cref="GroundCoverScatter"/>) и рисует их. Пучков много, поэтому они собраны в общие сетки по участкам
    /// <see cref="RegionSize"/>×<see cref="RegionSize"/> столбцов, и сетки строятся только у камеры (в пределах
    /// drawDistance): дальше мелочь всё равно не разглядеть. Пучок, под которым копали или строили, пропадает,
    /// и его участок перестраивается. Цвет травы подстраивается под оттенок травы локации, как кроны деревьев.
    /// Когда камера отъезжает от земли дальше thinFrom, мелочь редеет ступенями, дальше hideBeyond скрыта целиком
    /// (<see cref="GroundCoverThinning"/>). Участок собирается один раз, разложенным на части по ступеням
    /// (<see cref="GroundCoverThinning.FirstLevel"/>), и смена ступени только включает и выключает готовые части:
    /// пересборка всех участков на каждой ступени давала рывки при зуме и переполняла буфер видеокарты (#151).
    /// </summary>
    public sealed class GroundCoverView : MonoBehaviour
    {
        /// <summary>Сторона участка, который рисуется одной сеткой, в столбцах.</summary>
        public const int RegionSize = 16;

        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private ForestView forestView;
        [Tooltip("Вокруг огней в этом радиусе (в блоках) мелочь не растёт: место самого костра.")]
        [Min(0f)]
        [SerializeField] private float clearAroundFire = 1.5f;

        [Header("Вид")]
        [Tooltip("Материал мелочи на шейдере Emberhold/World/Tree (тот же, что у деревьев): цвет из вершин сетки.")]
        [SerializeField] private Material material;
        [Tooltip("Мелочь рисуется на таком расстоянии от камеры по горизонтали, в блоках; дальше участки не строятся.")]
        [Min(16f)]
        [SerializeField] private float drawDistance = 96f;
        [Tooltip("С такого расстояния от камеры до земли (в блоках) мелочь начинает редеть: мелкая пропадает первой.")]
        [Min(0f)]
        [SerializeField] private float thinFrom = 60f;
        [Tooltip("Дальше этого расстояния от камеры до земли (в блоках) мелочь скрыта целиком и участки не строятся.")]
        [Min(0f)]
        [SerializeField] private float hideBeyond = 110f;
        [Tooltip("Сколько участков строить за кадр, когда камера сдвинулась (сначала ближние).")]
        [Range(1, 64)]
        [SerializeField] private int regionsPerFrame = 8;
        [Tooltip("Мелочь отбрасывает тени. Красивее, но дороже.")]
        [SerializeField] private bool castShadows = true;

        private readonly Dictionary<Vector2Int, List<GroundCoverPatch>> regions = new();
        private readonly Dictionary<Vector2Int, Region> shown = new();
        // Пучки участка по частям: в части i — те, что видны со ступени i + 1. Общий буфер на время сборки.
        private readonly List<GroundCoverPatch>[] byLevel = NewByLevel();
        private readonly HashSet<Vector2Int> dirty = new();
        private readonly Dictionary<(GroundCoverKind kind, int variant), GroundCoverMeshBuilder.Shape> shapes = new();
        private readonly List<Vector2Int> wanted = new();
        private readonly List<Vector2Int> far = new();
        // Буферы сетки участка: общие для всех, чтобы перестройка не мусорила.
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Color> colors = new();
        private readonly List<int> indices = new();
        private Comparison<Vector2Int> byDistance;
        private Vector2 sortCenter;
        private Vector2Int? lastMiddle;
        // Ступень редения (GroundCoverThinning): Steps — вся мелочь, 0 — скрыта.
        private int level = GroundCoverThinning.Steps;
        private GroundCover cover;
        // Свой корень для участков с позой мира: детьми вида мира могут быть только чанки.
        private Transform container;

        private sealed class Region
        {
            public GameObject Root;
            // Части по ступеням: Parts[i] видна со ступени i + 1. Пустая часть не рисуется.
            public readonly MeshRenderer[] Parts = new MeshRenderer[GroundCoverThinning.Steps];
            public readonly Mesh[] Meshes = new Mesh[GroundCoverThinning.Steps];
        }

        /// <summary>Мелочь мира; рассаживается при первом обращении после леса. Null, если сцена собрана не до конца.</summary>
        public GroundCover Cover
        {
            get
            {
                if (cover == null && worldView != null && worldView.World != null && forestView != null && forestView.Forest != null)
                    Plant();
                return cover;
            }
        }

        /// <summary>Сколько участков собрано у камеры (видны они или скрыты ступенью редения).</summary>
        public int ShownRegions => shown.Count;

        /// <summary>Сколько собранных участков сейчас рисует хоть одну часть.</summary>
        public int VisibleRegions
        {
            get
            {
                var count = 0;
                foreach (var region in shown.Values)
                {
                    foreach (var part in region.Parts)
                    {
                        if (part.enabled)
                        {
                            count++;
                            break;
                        }
                    }
                }
                return count;
            }
        }

        /// <summary>Сколько раз собиралась сетка участка с начала игры: смена ступени редения её не трогает.</summary>
        public int Builds { get; private set; }

        /// <summary>Ступень редения: <see cref="GroundCoverThinning.Steps"/> — видна вся мелочь, 0 — скрыта.</summary>
        public int ThinningLevel => level;

        private void Start()
        {
            if (Cover == null)
            {
                Debug.LogError("[GroundCover] Не назначены VoxelWorldView или ForestView.", this);
                enabled = false;
                return;
            }
            // Первый кадр — всё в пределах видимости сразу, без постепенной подгрузки.
            Refresh(int.MaxValue);
        }

        private void OnDestroy()
        {
            cover?.Watch(null);
            foreach (var region in shown.Values)
                Release(region);
            shown.Clear();
            if (container != null)
                Destroy(container.gameObject);
        }

        private void LateUpdate()
        {
            if (cover != null)
                Refresh(regionsPerFrame);
        }

        private void Plant()
        {
            cover = new GroundCover();
            var terrain = worldView.Terrain;
            if (terrain == null)
                return;
            var fires = new List<Vector3>();
            foreach (var source in FindObjectsByType<LightSource>())
                fires.Add(worldView.transform.InverseTransformPoint(source.transform.position));
            var clear = clearAroundFire * clearAroundFire;
            var planted = GroundCoverScatter.Scatter(worldView.World, terrain.Seed, worldView.ForestAt, forestView.Forest, cover, column =>
            {
                // На снегу мелочи на земле нет.
                if (worldView.AboveSnowLine(column))
                    return true;
                foreach (var fire in fires)
                {
                    var d = new Vector2(column.x + 0.5f - fire.x, column.y + 0.5f - fire.z);
                    if (d.sqrMagnitude <= clear)
                        return true;
                }
                return false;
            });
            foreach (var patch in cover.Patches)
                RegionList(RegionOf(patch)).Add(patch);
            cover.Removed += OnRemoved;
            cover.Changed += OnChanged;
            cover.Watch(worldView.World);
            Debug.Log($"[GroundCover] Посажено пучков: {planted}.", this);
        }

        private void OnRemoved(GroundCoverPatch patch)
        {
            var region = RegionOf(patch);
            if (regions.TryGetValue(region, out var list))
                list.Remove(patch);
            if (shown.ContainsKey(region))
                dirty.Add(region);
        }

        // Пучок остался, но земля под ним сменилась: его участок перестраивается.
        private void OnChanged(GroundCoverPatch patch)
        {
            var region = RegionOf(patch);
            if (shown.ContainsKey(region))
                dirty.Add(region);
        }

        // Перестроить изменившиеся участки и построить новые у камеры (ближние первыми, вместе не больше budget), убрать далёкие.
        private void Refresh(int budget)
        {
            var camera = Camera.main;
            if (camera == null)
                return;
            var space = worldView.transform;
            var eye = space.InverseTransformPoint(camera.transform.position);
            var center = new Vector2(eye.x, eye.z);
            var newLevel = GroundCoverThinning.Level(GroundDistance(camera, eye), thinFrom, hideBeyond, level);
            if (newLevel != level)
            {
                level = newLevel;
                // Другая ступень — только включить и выключить готовые части, ничего не пересобирая.
                foreach (var region in shown.Values)
                    ApplyLevel(region);
                lastMiddle = null;
            }
            var keep = drawDistance + RegionSize;
            var middle = new Vector2Int(Mathf.FloorToInt(center.x / RegionSize), Mathf.FloorToInt(center.y / RegionSize));
            // Камера в том же участке, всё нужное построено и ничего не менялось — делать нечего.
            if (middle == lastMiddle && dirty.Count == 0 && (wanted.Count == 0 || level == 0))
                return;

            far.Clear();
            foreach (var key in shown.Keys)
            {
                if (Distance(key, center) > keep)
                    far.Add(key);
            }
            foreach (var key in far)
            {
                Release(shown[key]);
                shown.Remove(key);
                dirty.Remove(key);
            }

            // Изменившиеся участки (копали, строили) перестраиваются в тот же счёт budget, ближние первыми.
            sortCenter = center;
            byDistance ??= (a, b) => Distance(a, sortCenter).CompareTo(Distance(b, sortCenter));
            far.Clear();
            far.AddRange(dirty);
            far.Sort(byDistance);
            var refilled = Mathf.Min(far.Count, budget);
            for (var i = 0; i < refilled; i++)
            {
                if (shown.TryGetValue(far[i], out var region))
                    Fill(far[i], region);
                dirty.Remove(far[i]);
            }
            budget -= refilled;

            // Мелочь скрыта целиком — новых участков не строим; собранные остаются, чтобы при возвращении
            // камеры просто включиться.
            if (level == 0)
            {
                wanted.Clear();
                lastMiddle = middle;
                return;
            }

            wanted.Clear();
            var reach = Mathf.CeilToInt(drawDistance / RegionSize) + 1;
            for (var dz = -reach; dz <= reach; dz++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                var key = middle + new Vector2Int(dx, dz);
                if (!shown.ContainsKey(key) && regions.ContainsKey(key) && Distance(key, center) <= drawDistance)
                    wanted.Add(key);
            }
            wanted.Sort(byDistance);
            var built = Mathf.Min(wanted.Count, budget);
            for (var i = 0; i < built; i++)
                shown[wanted[i]] = Create(wanted[i]);
            // Построенные убираем из очереди: пустая очередь — всё у камеры уже нарисовано.
            wanted.RemoveRange(0, built);
            lastMiddle = middle;
        }

        private Region Create(Vector2Int key)
        {
            if (container == null)
            {
                container = new GameObject("Ground cover").transform;
                var space = worldView.transform;
                container.SetPositionAndRotation(space.position, space.rotation);
                container.localScale = space.lossyScale;
            }
            var root = new GameObject($"Ground cover {key.x},{key.y}");
            root.transform.SetParent(container, false);
            var region = new Region { Root = root };
            for (var i = 0; i < GroundCoverThinning.Steps; i++)
            {
                var part = new GameObject($"Level {i + 1}");
                part.transform.SetParent(root.transform, false);
                var mesh = new Mesh { name = $"{root.name} level {i + 1}", indexFormat = IndexFormat.UInt32 };
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                if (material != null)
                    renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                region.Parts[i] = renderer;
                region.Meshes[i] = mesh;
            }
            Fill(key, region);
            return region;
        }

        // Сетки участка по частям: каждая часть — формы её пучков, поставленные на землю, повёрнутые и подкрашенные
        // оттенком травы. Собираются все части сразу, видимость по ступени ставит ApplyLevel.
        private void Fill(Vector2Int key, Region region)
        {
            Builds++;
            foreach (var part in byLevel)
                part.Clear();
            if (regions.TryGetValue(key, out var list))
            {
                foreach (var patch in list)
                    byLevel[GroundCoverThinning.FirstLevel(patch) - 1].Add(patch);
            }
            for (var i = 0; i < byLevel.Length; i++)
                FillPart(byLevel[i], region.Meshes[i]);
            ApplyLevel(region);
        }

        // Часть видна, если ступень дошла до неё и в ней есть что рисовать.
        private void ApplyLevel(Region region)
        {
            for (var i = 0; i < region.Parts.Length; i++)
                region.Parts[i].enabled = level >= i + 1 && region.Meshes[i].vertexCount > 0;
        }

        private void FillPart(List<GroundCoverPatch> patches, Mesh mesh)
        {
            vertices.Clear();
            normals.Clear();
            colors.Clear();
            var world = worldView.World;
            foreach (var patch in patches)
            {
                var shape = ShapeFor(patch.Kind, patch.Variant);
                var position = patch.Position;
                var rotation = Quaternion.Euler(0f, patch.Rotation, 0f);
                var origin = new Vector3(position.x, GroundCover.BaseHeight(world, patch), position.y);
                var grass = world.GrassTint?.Sample(position.x, position.y) ?? Color.white;
                for (var i = 0; i < shape.Vertices.Count; i++)
                {
                    vertices.Add(origin + rotation * (shape.Vertices[i] * patch.Scale));
                    normals.Add(rotation * shape.Normals[i]);
                    var color = shape.Colors[i];
                    if (color.a > 0.5f)
                        color = GroundCoverMeshBuilder.Tint(color, grass);
                    // Альфа 0: шейдер деревьев не красит эти вершины своим _BaseColor.
                    color.a = 0f;
                    colors.Add(color);
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            for (var i = indices.Count; i < vertices.Count; i++)
                indices.Add(i);
            mesh.SetTriangles(indices, 0, vertices.Count, 0);
            mesh.RecalculateBounds();
        }

        // Расстояние от камеры до земли по взгляду, в блоках; луч только по земле, рабочие и предметы не в счёт.
        // Если луч не попал в землю — высота камеры над миром.
        private float GroundDistance(Camera camera, Vector3 eye)
        {
            var view = camera.transform;
            var scale = Mathf.Max(worldView.transform.lossyScale.y, 1e-4f);
            if (Physics.Raycast(view.position, view.forward, out var hit, hideBeyond * 4f * scale, Layers.GroundMask,
                    QueryTriggerInteraction.Ignore))
                return hit.distance / scale;
            return eye.y;
        }

        private GroundCoverMeshBuilder.Shape ShapeFor(GroundCoverKind kind, int variant)
        {
            if (!shapes.TryGetValue((kind, variant), out var shape))
            {
                shape = GroundCoverMeshBuilder.Build(kind, variant);
                shapes[(kind, variant)] = shape;
            }
            return shape;
        }

        private List<GroundCoverPatch> RegionList(Vector2Int key)
        {
            if (!regions.TryGetValue(key, out var list))
                regions[key] = list = new List<GroundCoverPatch>();
            return list;
        }

        private static List<GroundCoverPatch>[] NewByLevel()
        {
            var parts = new List<GroundCoverPatch>[GroundCoverThinning.Steps];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = new List<GroundCoverPatch>();
            return parts;
        }

        private static Vector2Int RegionOf(GroundCoverPatch patch) =>
            new(Mathf.FloorToInt((float)patch.Cell.x / RegionSize), Mathf.FloorToInt((float)patch.Cell.z / RegionSize));

        // Расстояние от точки до ближайшей точки участка по горизонтали.
        private static float Distance(Vector2Int key, Vector2 point)
        {
            var min = (Vector2)key * RegionSize;
            var closest = Vector2.Max(min, Vector2.Min(point, min + Vector2.one * RegionSize));
            return (closest - point).magnitude;
        }

        private static void Release(Region region)
        {
            if (region.Root != null)
                Destroy(region.Root);
            foreach (var mesh in region.Meshes)
            {
                if (mesh != null)
                    Destroy(mesh);
            }
        }
    }
}
