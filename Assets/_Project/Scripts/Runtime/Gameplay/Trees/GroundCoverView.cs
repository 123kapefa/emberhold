using System.Collections.Generic;
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
        private GroundCover cover;

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

        // Построить участки у камеры (ближние первыми, не больше budget новых), перестроить изменившиеся, убрать далёкие.
        private void Refresh(int budget)
        {
            var camera = Camera.main;
            if (camera == null)
                return;
            var space = worldView.transform;
            var eye = space.InverseTransformPoint(camera.transform.position);
            var center = new Vector2(eye.x, eye.z);
            var keep = drawDistance + RegionSize;

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

            foreach (var key in dirty)
            {
                if (shown.TryGetValue(key, out var region))
                    Fill(key, region.Mesh);
            }
            dirty.Clear();

            wanted.Clear();
            var reach = Mathf.CeilToInt(drawDistance / RegionSize) + 1;
            var middle = new Vector2Int(Mathf.FloorToInt(center.x / RegionSize), Mathf.FloorToInt(center.y / RegionSize));
            for (var dz = -reach; dz <= reach; dz++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                var key = middle + new Vector2Int(dx, dz);
                if (!shown.ContainsKey(key) && regions.ContainsKey(key) && Distance(key, center) <= drawDistance)
                    wanted.Add(key);
            }
            wanted.Sort((a, b) => Distance(a, center).CompareTo(Distance(b, center)));
            for (var i = 0; i < wanted.Count && i < budget; i++)
                shown[wanted[i]] = Create(wanted[i]);
        }

        private Region Create(Vector2Int key)
        {
            var root = new GameObject($"Ground cover {key.x},{key.y}");
            root.transform.SetParent(worldView.transform, false);
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
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var world = worldView.World;
            if (regions.TryGetValue(key, out var list))
            {
                foreach (var patch in list)
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
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            var triangles = new int[vertices.Count];
            for (var i = 0; i < triangles.Length; i++)
                triangles[i] = i;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
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
