using System.Collections.Generic;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Анимация падения блоков без опоры. Мир меняется сразу (<see cref="BlockStability"/>), а здесь упавшие
    /// блоки видимо летят вниз от старой клетки до новой и слегка подскакивают при ударе. Пока блок летит,
    /// чанк не рисует его в новой клетке (<see cref="VoxelWorldView.HideBlock"/>), но луч мыши и логика его уже видят.
    /// Блоки, падающие на одно и то же расстояние, летят одинаково, поэтому каждая такая группа — один меш:
    /// обрушение из сотни блоков даёт несколько объектов, а не сотню.
    /// Если клетку, куда летит блок, изменили или блок упал дальше, его вырезают из полёта группы:
    /// изменённую клетку сразу рисует чанк, а упавший дальше блок продолжает лететь с той же высоты и скорости.
    /// </summary>
    public sealed class BlockFallView : MonoBehaviour
    {
        [SerializeField] private BlockEditController editor;
        [SerializeField] private VoxelWorldView worldView;
        [Tooltip("Ускорение падения, блоков в секунду за секунду.")]
        [Range(1f, 200f)]
        [SerializeField] private float gravity = 30f;
        [Tooltip("На сколько блоков упавший блок подскакивает при ударе.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float landingBounce = 0.06f;
        [Tooltip("Сколько секунд длится подскок после удара.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float landingBounceSeconds = 0.12f;

        private readonly List<Flight> flights = new();
        private readonly Dictionary<int, List<BlockFall>> byDistance = new();
        private readonly List<(BlockFall fall, float lift, float speed)> refalls = new();
        private readonly List<BlockFall> single = new(1);
        private readonly List<Vector3Int> groupCells = new();
        private readonly List<Block> groupBlocks = new();
        private readonly List<Material> groupMaterials = new();
        private readonly List<Color> groupColors = new();
        private readonly ChunkMeshData meshData = new();
        private readonly ChunkMeshData scratch = new();
        private VoxelWorld flightsWorld;

        /// <summary>Ускорение падения; с ним же падают ресурсы на земле.</summary>
        public float Gravity => gravity;

        /// <summary>Сколько групп блоков сейчас в полёте (для тестов).</summary>
        public int FlightCount => flights.Count;

        /// <summary>Сколько длится падение на distance блоков вместе с подскоком.</summary>
        public float Duration(float distance) => FallMotion.FallSeconds(distance, gravity) + landingBounceSeconds;

        private void OnEnable()
        {
            if (editor == null || worldView == null)
            {
                Debug.LogError("[BlockFall] Не назначены BlockEditController или VoxelWorldView.", this);
                enabled = false;
                return;
            }
            editor.BlockChanged += OnBlockChanged;
            editor.BlocksFell += OnBlocksFell;
        }

        private void OnDisable()
        {
            if (editor != null)
            {
                editor.BlockChanged -= OnBlockChanged;
                editor.BlocksFell -= OnBlocksFell;
            }
            LandAll();
        }

        /// <summary>Клетку, куда летит блок, изменили: её сразу рисует чанк, а блок из полёта убираем.</summary>
        private void OnBlockChanged(Vector3Int cell)
        {
            if (worldView.World == flightsWorld && worldView.IsHidden(cell) && TryFindFlight(cell, out var flight, out var index))
                Release(flight, index);
        }

        private void OnBlocksFell(IReadOnlyList<BlockFall> falls)
        {
            if (worldView.World != flightsWorld)
                LandAll();
            flightsWorld = worldView.World;

            foreach (var list in byDistance.Values)
                list.Clear();
            refalls.Clear();
            foreach (var fall in falls)
            {
                // Блок ещё летел и упал дальше: продолжаем его полёт с той высоты и скоростью, что видны сейчас.
                if (worldView.IsHidden(fall.From) && TryFindFlight(fall.From, out var flight, out var index))
                {
                    var elapsed = Time.time - flight.Start;
                    var height = flight.Distance + FallMotion.Offset(flight.Distance, elapsed, gravity,
                        landingBounceSeconds, landingBounce);
                    var speed = elapsed < FallMotion.FallSeconds(flight.Distance, gravity) ? gravity * elapsed : 0f;
                    refalls.Add((fall, Mathf.Max(0f, height), speed));
                    Release(flight, index);
                    continue;
                }

                var distance = fall.From.y - fall.To.y;
                if (!byDistance.TryGetValue(distance, out var list))
                    byDistance[distance] = list = new List<BlockFall>();
                list.Add(fall);
            }

            foreach (var list in byDistance.Values)
            {
                if (list.Count > 0)
                    StartFlight(list, 0f, 0f);
            }
            foreach (var (fall, height, speed) in refalls)
            {
                // Падение со скоростью speed с высоты height — то же, что свободное падение, начатое
                // speed / g секунд назад на speed² / 2g выше.
                single.Clear();
                single.Add(fall);
                StartFlight(single, height + speed * speed / (2f * gravity), speed / gravity);
            }
        }

        /// <param name="lift">На сколько блоков над клеткой From начинается полёт.</param>
        /// <param name="startedAgo">Сколько секунд назад полёт начался бы с нулевой скоростью.</param>
        private void StartFlight(List<BlockFall> falls, float lift, float startedAgo)
        {
            var go = new GameObject($"Falling Blocks ×{falls.Count}");
            var mesh = new Mesh { name = go.name };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var flight = new Flight(falls[0].From.y - falls[0].To.y + lift, lift, Time.time - startedAgo, go, mesh, renderer);
            foreach (var fall in falls)
            {
                flight.Falls.Add(fall);
                worldView.HideBlock(fall.To);
            }
            BuildMesh(flight);
            flights.Add(flight);
            Place(flight, Time.time - flight.Start);
        }

        /// <summary>Меш группы по её блокам в исходных клетках.</summary>
        private void BuildMesh(Flight flight)
        {
            groupCells.Clear();
            groupBlocks.Clear();
            foreach (var fall in flight.Falls)
            {
                groupCells.Add(fall.From);
                groupBlocks.Add(fall.Block);
            }
            flight.Origin = BlockGroupMesher.Build(groupCells, groupBlocks, meshData, scratch, worldView.World.GrassTint)
                + Vector3.up * flight.Lift;

            var mesh = flight.Mesh;
            mesh.Clear();
            mesh.indexFormat = meshData.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(meshData.Vertices);
            mesh.SetNormals(meshData.Normals);
            mesh.SetUVs(0, meshData.Uvs);
            // Трава в полёте — того же оттенка и с той же бахромой, что и в мире.
            VoxelWorldView.SetGroundBlend(mesh, meshData, groupColors);
            groupMaterials.Clear();
            var subMesh = 0;
            mesh.subMeshCount = CountMaterials(meshData);
            for (var material = 1; material < meshData.Triangles.Length; material++)
            {
                if (meshData.Triangles[material].Count == 0)
                    continue;
                mesh.SetTriangles(meshData.Triangles[material], subMesh++, false);
                groupMaterials.Add(worldView.BlockMaterial((byte)material));
            }
            mesh.RecalculateBounds();
            flight.Renderer.SetSharedMaterials(groupMaterials);
        }

        private static int CountMaterials(ChunkMeshData data)
        {
            var count = 0;
            for (var material = 1; material < data.Triangles.Length; material++)
            {
                if (data.Triangles[material].Count > 0)
                    count++;
            }
            return count;
        }

        // В Update, а не в LateUpdate: вернувшийся в чанк блок перестроится в LateUpdate вида мира
        // в этом же кадре, и между исчезновением летящего меша и появлением блока не будет пустого кадра.
        private void Update()
        {
            if (flights.Count > 0 && worldView.World != flightsWorld)
                LandAll();
            for (var i = flights.Count - 1; i >= 0; i--)
            {
                var flight = flights[i];
                var elapsed = Time.time - flight.Start;
                if (elapsed >= Duration(flight.Distance))
                    Land(i);
                else
                    Place(flight, elapsed);
            }
        }

        private void Place(Flight flight, float elapsed)
        {
            var offset = FallMotion.Offset(flight.Distance, elapsed, gravity, landingBounceSeconds, landingBounce);
            var space = worldView.transform;
            flight.Object.transform.SetPositionAndRotation(
                space.TransformPoint(flight.Origin + Vector3.up * offset), space.rotation);
            flight.Object.transform.localScale = space.lossyScale;
        }

        private bool TryFindFlight(Vector3Int to, out Flight flight, out int index)
        {
            foreach (var candidate in flights)
            {
                for (var i = 0; i < candidate.Falls.Count; i++)
                {
                    if (candidate.Falls[i].To != to)
                        continue;
                    flight = candidate;
                    index = i;
                    return true;
                }
            }
            flight = null;
            index = -1;
            return false;
        }

        /// <summary>Убрать один блок из полёта группы: чанк снова рисует его клетку, меш группы — без него.</summary>
        private void Release(Flight flight, int index)
        {
            worldView.ShowBlock(flight.Falls[index].To);
            flight.Falls.RemoveAt(index);
            if (flight.Falls.Count == 0)
            {
                flights.Remove(flight);
                DestroyFlight(flight);
                return;
            }
            BuildMesh(flight);
            Place(flight, Time.time - flight.Start);
        }

        private void Land(int index)
        {
            var flight = flights[index];
            flights.RemoveAt(index);
            if (worldView != null && worldView.World == flightsWorld)
            {
                foreach (var fall in flight.Falls)
                    worldView.ShowBlock(fall.To);
            }
            DestroyFlight(flight);
        }

        private static void DestroyFlight(Flight flight)
        {
            // При выгрузке сцены объект полёта может быть уже уничтожен.
            if (flight.Object != null)
                Object.Destroy(flight.Object);
            Object.Destroy(flight.Mesh);
        }

        private void LandAll()
        {
            for (var i = flights.Count - 1; i >= 0; i--)
                Land(i);
        }

        private sealed class Flight
        {
            /// <summary>С какой высоты над клеткой назначения начинается полёт, в блоках.</summary>
            public readonly float Distance;
            /// <summary>На сколько блоков полёт начинается выше исходной клетки (продолжение прерванного полёта).</summary>
            public readonly float Lift;
            public readonly float Start;
            public readonly GameObject Object;
            public readonly Mesh Mesh;
            public readonly MeshRenderer Renderer;
            /// <summary>Блоки группы; в клетках To чанк их прячет до приземления.</summary>
            public readonly List<BlockFall> Falls = new();
            /// <summary>Где в координатах мира блоков начало меша в момент старта.</summary>
            public Vector3 Origin;

            public Flight(float distance, float lift, float start, GameObject gameObject, Mesh mesh, MeshRenderer renderer)
            {
                Distance = distance;
                Lift = lift;
                Start = start;
                Object = gameObject;
                Mesh = mesh;
                Renderer = renderer;
            }
        }
    }
}
