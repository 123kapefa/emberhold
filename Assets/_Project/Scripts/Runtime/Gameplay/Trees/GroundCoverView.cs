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
    /// (<see cref="GroundCoverThinning"/>).
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
            public Mesh Mesh;
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

        /// <summary>Сколько участков сейчас нарисовано.</summary>
        public int ShownRegions => shown.Count;

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
                // Другая ступень — перестроить всё нарисованное; скрытая мелочь не держит ни сеток, ни памяти.
                foreach (var key in shown.Keys)
                    dirty.Add(key);
                lastMiddle = null;
            }
            if (level == 0)
            {
                foreach (var region in shown.Values)
                    Release(region);
                shown.Clear();
                dirty.Clear();
                wanted.Clear();
                return;
            }
            var keep = drawDistance + RegionSize;
            var middle = new Vector2Int(Mathf.FloorToInt(center.x / RegionSize), Mathf.FloorToInt(center.y / RegionSize));
            // Камера в том же участке, всё нужное построено и ничего не менялось — делать нечего.
            if (middle == lastMiddle && dirty.Count == 0 && wanted.Count == 0)
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

            // Изменившиеся участки перестраиваются в тот же счёт budget, ближние первыми: смена ступени редения
            // трогает все нарисованные участки, и разом это был бы рывок кадра.
            sortCenter = center;
            byDistance ??= (a, b) => Distance(a, sortCenter).CompareTo(Distance(b, sortCenter));
            far.Clear();
            far.AddRange(dirty);
            far.Sort(byDistance);
            var refilled = Mathf.Min(far.Count, budget);
            for (var i = 0; i < refilled; i++)
            {
                if (shown.TryGetValue(far[i], out var region))
                    Fill(far[i], region.Mesh);
                dirty.Remove(far[i]);
            }
            budget -= refilled;

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
            var mesh = new Mesh { name = root.name, indexFormat = IndexFormat.UInt32 };
            Fill(key, mesh);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return new Region { Root = root, Mesh = mesh };
        }

        // Сетка участка: формы всех его пучков, поставленные на землю, повёрнутые и подкрашенные оттенком травы.
        private void Fill(Vector2Int key, Mesh mesh)
        {
            vertices.Clear();
            normals.Clear();
            colors.Clear();
            var world = worldView.World;
            if (regions.TryGetValue(key, out var list))
            {
                foreach (var patch in list)
                {
                    if (!GroundCoverThinning.Keeps(patch, level))
                        continue;
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
            if (region.Mesh != null)
                Destroy(region.Mesh);
        }
    }
}
