using System.Collections.Generic;
using Emberhold.World;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Предпросмотр блока, который встанет при клике: меш одного блока в его настоящем материале.
    /// Меш строится тем же <see cref="ChunkMesher"/>, что и чанки, на мире из одной ячейки,
    /// и перестраивается только когда меняется блок.
    /// </summary>
    public sealed class BlockGhost
    {
        private readonly VoxelWorld cell = new(Vector3Int.one);
        private readonly ChunkMeshData meshData = new();
        private readonly List<Material> materials = new(1);
        private readonly GameObject gameObject;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private Block? shown;

        public BlockGhost()
        {
            gameObject = new GameObject("Block Ghost");
            gameObject.SetActive(false);
            mesh = new Mesh { name = "Block Ghost" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public Transform Transform => gameObject.transform;

        public void Show(Block block, Material material)
        {
            if (shown != block)
            {
                shown = block;
                cell.SetBlock(Vector3Int.zero, block);
                ChunkMesher.Build(cell, Vector3Int.zero, meshData);
                mesh.Clear();
                mesh.SetVertices(meshData.Vertices);
                mesh.SetNormals(meshData.Normals);
                mesh.SetUVs(0, meshData.Uvs);
                mesh.SetTriangles(meshData.Triangles[block.Material], 0, false);
                mesh.RecalculateBounds();
            }
            materials.Clear();
            materials.Add(material);
            renderer.SetSharedMaterials(materials);
            gameObject.SetActive(true);
        }

        // Объект лежит в корне сцены и при выгрузке сцены может быть уничтожен раньше владельца.
        public void Hide()
        {
            if (gameObject != null)
                gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (mesh != null)
                Object.Destroy(mesh);
            if (gameObject != null)
                Object.Destroy(gameObject);
        }
    }
}
