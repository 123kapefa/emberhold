using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.World
{
    /// <summary>
    /// Горы за краем мира (#102): хребет продолжается наружу и уходит в море тумана, так что у карты не видно
    /// ровного обреза. Только картинка — ни блоков, ни коллайдера: дойти туда и копать нельзя.
    /// Строится заново, когда у мира появляется новая земля (генерация), по высотам края этой земли.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WorldSkirt : MonoBehaviour
    {
        [Tooltip("Мир, вокруг которого стоят горы. Если пусто — первый в сцене.")]
        [SerializeField] private VoxelWorldView view;

        private readonly ChunkMeshData data = new();
        private readonly List<Color> colors = new();
        private Mesh mesh;
        private TerrainSurface builtFor;

        /// <summary>Рельеф гор, по которому построен меш; null, пока его нет.</summary>
        public WorldSkirtShape Shape { get; private set; }

        /// <summary>Меш гор; null, пока его нет.</summary>
        public Mesh Mesh => mesh;

        private void OnEnable()
        {
            if (view == null)
                view = FindAnyObjectByType<VoxelWorldView>();
            Rebuild();
        }

        private void LateUpdate()
        {
            var surface = view != null && view.World != null ? view.World.Surface : null;
            if (surface != builtFor)
                Rebuild();
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }

        private void Rebuild()
        {
            var world = view != null ? view.World : null;
            var surface = world?.Surface;
            builtFor = surface;
            var filter = GetComponent<MeshFilter>();
            if (surface == null)
            {
                Shape = null;
                filter.sharedMesh = null;
                return;
            }

            var seed = view.Terrain != null ? view.Terrain.Seed : 0;
            Shape = new WorldSkirtShape((x, z) => surface.Corner(x, z) / (float)BlockShape.MaxHeight,
                surface.SizeX, surface.SizeZ, seed);
            WorldSkirtMesh.Build(Shape, surface, world.GrassTint, data);

            if (mesh == null)
                mesh = new Mesh { name = "World Skirt" };
            mesh.Clear();
            mesh.indexFormat = data.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uvs);
            VoxelWorldView.SetGroundBlend(mesh, data, colors);
            mesh.SetTriangles(data.Triangles[BlockMaterials.Grass], 0);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;

            // Горы стоят там же, где угол мира (0, 0); земля рисуется тем же материалом, что гладкая трава мира.
            transform.SetPositionAndRotation(view.transform.position, Quaternion.identity);
            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = view.BlockMaterial(BlockMaterials.Grass);
            // Тени от гор за краем падали бы на землю мира непредсказуемо и стоили бы дальних каскадов.
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
