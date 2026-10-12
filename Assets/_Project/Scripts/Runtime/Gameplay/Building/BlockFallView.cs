using System.Collections.Generic;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Анимация падения блоков без опоры. Мир меняется сразу (<see cref="BlockStability"/>), а здесь упавшие
    /// блоки видимо летят вниз от старой клетки до новой и слегка подскакивают при ударе. Пока блок летит,
    /// чанк не рисует его в новой клетке (<see cref="VoxelWorldView.HideBlock"/>), но луч мыши и логика его уже видят.
    /// Блоки, падающие на одно и то же расстояние, летят одинаково, поэтому каждая такая группа — один меш:
    /// обрушение из сотни блоков даёт несколько объектов, а не сотню.
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
        private readonly List<Vector3Int> groupCells = new();
        private readonly List<Block> groupBlocks = new();
        private readonly List<Material> groupMaterials = new();
        private readonly List<int> groupMaterialIds = new();
        private readonly ChunkMeshData meshData = new();
        private readonly ChunkMeshData scratch = new();
        private VoxelWorld flightsWorld;

        /// <summary>Ускорение падения; с ним же падают ресурсы на земле.</summary>
        public float Gravity => gravity;

        /// <summary>Сколько групп блоков сейчас в полёте (для тестов).</summary>
        public int FlightCount => flights.Count;

        /// <summary>Сколько длится падение на distance блоков вместе с подскоком.</summary>
        public float Duration(int distance) => FallMotion.FallSeconds(distance, gravity) + landingBounceSeconds;

        private void OnEnable()
        {
            if (editor == null || worldView == null)
            {
                Debug.LogError("[BlockFall] Не назначены BlockEditController или VoxelWorldView.", this);
                enabled = false;
                return;
            }
            editor.BlocksFell += OnBlocksFell;
        }

        private void OnDisable()
        {
            if (editor != null)
                editor.BlocksFell -= OnBlocksFell;
            LandAll();
        }

        private void OnBlocksFell(IReadOnlyList<BlockFall> falls)
        {
            if (worldView.World != flightsWorld)
                LandAll();
            flightsWorld = worldView.World;

            // Блок, который ещё летел, упал дальше: прошлый полёт его группы заканчиваем сразу.
            foreach (var fall in falls)
            {
                if (!worldView.IsHidden(fall.From))
                    continue;
                for (var i = flights.Count - 1; i >= 0; i--)
                {
                    if (flights[i].Cells.Contains(fall.From))
                        Land(i);
                }
            }

            foreach (var list in byDistance.Values)
                list.Clear();
            foreach (var fall in falls)
            {
                var distance = fall.From.y - fall.To.y;
                if (!byDistance.TryGetValue(distance, out var list))
                    byDistance[distance] = list = new List<BlockFall>();
                list.Add(fall);
            }
            foreach (var (distance, list) in byDistance)
            {
                if (list.Count > 0)
                    StartFlight(distance, list);
            }
        }

        private void StartFlight(int distance, List<BlockFall> falls)
        {
            groupCells.Clear();
            groupBlocks.Clear();
            foreach (var fall in falls)
            {
                groupCells.Add(fall.From);
                groupBlocks.Add(fall.Block);
            }
            var origin = BlockGroupMesher.Build(groupCells, groupBlocks, meshData, scratch);

            var go = new GameObject($"Falling Blocks ×{falls.Count}");
            var mesh = new Mesh { name = go.name };
            mesh.indexFormat = meshData.Vertices.Count > ushort.MaxValue
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(meshData.Vertices);
            mesh.SetNormals(meshData.Normals);
            mesh.SetUVs(0, meshData.Uvs);
            groupMaterialIds.Clear();
            for (var material = 1; material < meshData.Triangles.Length; material++)
            {
                if (meshData.Triangles[material].Count > 0)
                    groupMaterialIds.Add(material);
            }
            mesh.subMeshCount = groupMaterialIds.Count;
            groupMaterials.Clear();
            for (var i = 0; i < groupMaterialIds.Count; i++)
            {
                mesh.SetTriangles(meshData.Triangles[groupMaterialIds[i]], i, false);
                groupMaterials.Add(worldView.BlockMaterial((byte)groupMaterialIds[i]));
            }
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().SetSharedMaterials(groupMaterials);

            var flight = new Flight(distance, Time.time, origin, go, mesh);
            foreach (var fall in falls)
            {
                flight.Cells.Add(fall.To);
                worldView.HideBlock(fall.To);
            }
            flights.Add(flight);
            Place(flight, 0f);
        }

        // В Update, а не в LateUpdate: вернувшийся в чанк блок перестроится в LateUpdate вида мира
        // в этом же кадре, и между исчезновением летящего меша и появлением блока не будет пустого кадра.
        private void Update()
        {
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

        private void Land(int index)
        {
            var flight = flights[index];
            flights.RemoveAt(index);
            if (worldView != null && worldView.World == flightsWorld)
            {
                foreach (var cell in flight.Cells)
                    worldView.ShowBlock(cell);
            }
            // При выгрузке сцены объект полёта может быть уже уничтожен.
            if (flight.Object != null)
                Destroy(flight.Object);
            Destroy(flight.Mesh);
        }

        private void LandAll()
        {
            for (var i = flights.Count - 1; i >= 0; i--)
                Land(i);
        }

        private sealed class Flight
        {
            public readonly int Distance;
            public readonly float Start;
            public readonly Vector3 Origin;
            public readonly GameObject Object;
            public readonly Mesh Mesh;
            /// <summary>Куда легли блоки группы: там чанк их прячет до приземления.</summary>
            public readonly HashSet<Vector3Int> Cells = new();

            public Flight(int distance, float start, Vector3 origin, GameObject gameObject, Mesh mesh)
            {
                Distance = distance;
                Start = start;
                Origin = origin;
                Object = gameObject;
                Mesh = mesh;
            }
        }
    }
}
