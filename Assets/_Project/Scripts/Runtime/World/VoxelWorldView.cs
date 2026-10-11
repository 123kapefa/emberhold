using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.World
{
    /// <summary>
    /// Показывает воксельный мир в сцене: на каждый чанк один GameObject с мешем и коллайдером на слое Ground.
    /// В Awake генерирует долину; каждый кадр перестраивает только изменённые чанки.
    /// </summary>
    public sealed class VoxelWorldView : MonoBehaviour
    {
        [SerializeField] private ValleySettings valley;
        [Tooltip("Материал для каждого номера материала блока: 0 — воздух (не используется), 1 — трава, 2 — земля, 3 — камень.")]
        [SerializeField] private Material[] blockMaterials;
        [Tooltip("Сдвинуть мир так, чтобы центр долины оказался в начале координат сцены.")]
        [SerializeField] private bool centerOnOrigin = true;

        private readonly Dictionary<Vector3Int, ChunkView> chunkViews = new();
        private readonly List<Vector3Int> dirtyBuffer = new();
        private readonly List<Material> materialBuffer = new();
        private ChunkMeshData meshData;
        private bool missingMaterialReported;

        public VoxelWorld World { get; private set; }

        private void Awake()
        {
            if (valley == null)
            {
                Debug.LogError("[VoxelWorld] Не назначен ValleySettings.", this);
                enabled = false;
                return;
            }

            World = ValleyGenerator.Generate(valley.Parameters);
            if (centerOnOrigin)
            {
                var size = World.SizeInBlocks;
                transform.position = new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f);
            }

            meshData = new ChunkMeshData();
            World.MarkAllDirty();
            RebuildDirtyChunks();
        }

        private void LateUpdate()
        {
            if (World != null && World.DirtyCount > 0)
                RebuildDirtyChunks();
        }

        private void OnDestroy()
        {
            foreach (var view in chunkViews.Values)
                Destroy(view.Mesh);
            chunkViews.Clear();
        }

        private void RebuildDirtyChunks()
        {
            dirtyBuffer.Clear();
            World.TakeDirtyChunks(dirtyBuffer);
            foreach (var chunk in dirtyBuffer)
                RebuildChunk(chunk);
        }

        private void RebuildChunk(Vector3Int chunk)
        {
            ChunkMesher.Build(World, chunk, meshData);
            if (!chunkViews.TryGetValue(chunk, out var view))
            {
                if (meshData.IsEmpty)
                    return;
                view = CreateChunkView(chunk);
                chunkViews.Add(chunk, view);
            }

            ApplyMesh(view);
        }

        private ChunkView CreateChunkView(Vector3Int chunk)
        {
            var go = new GameObject($"Chunk {chunk.x},{chunk.y},{chunk.z}")
            {
                layer = Layers.Ground,
            };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = chunk * ChunkData.Size;

            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return new ChunkView(mesh, go.AddComponent<MeshRenderer>(), go.AddComponent<MeshCollider>());
        }

        private void ApplyMesh(ChunkView view)
        {
            var mesh = view.Mesh;
            mesh.Clear();
            mesh.indexFormat = meshData.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(meshData.Vertices);
            mesh.SetNormals(meshData.Normals);
            mesh.SetUVs(0, meshData.Uvs);

            // Подмеш только для материалов, которые есть в чанке.
            materialBuffer.Clear();
            var subMesh = 0;
            mesh.subMeshCount = CountUsedMaterials();
            for (var material = 0; material < meshData.Triangles.Length; material++)
            {
                var triangles = meshData.Triangles[material];
                if (triangles.Count == 0)
                    continue;
                mesh.SetTriangles(triangles, subMesh++, false);
                materialBuffer.Add(MaterialFor(material));
            }
            mesh.RecalculateBounds();

            view.Renderer.SetSharedMaterials(materialBuffer);
            view.Renderer.enabled = !meshData.IsEmpty;
            // Коллайдер берёт меш при назначении, поэтому переназначаем после каждой перестройки.
            view.Collider.sharedMesh = null;
            if (!meshData.IsEmpty)
                view.Collider.sharedMesh = mesh;
        }

        private int CountUsedMaterials()
        {
            var count = 0;
            foreach (var triangles in meshData.Triangles)
            {
                if (triangles.Count > 0)
                    count++;
            }
            return count;
        }

        private Material MaterialFor(int material)
        {
            if (blockMaterials != null && material < blockMaterials.Length && blockMaterials[material] != null)
                return blockMaterials[material];
            if (!missingMaterialReported)
            {
                missingMaterialReported = true;
                Debug.LogWarning($"[VoxelWorld] Нет материала для блока {material}.", this);
            }
            return null;
        }

        private readonly struct ChunkView
        {
            public readonly Mesh Mesh;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;

            public ChunkView(Mesh mesh, MeshRenderer renderer, MeshCollider collider)
            {
                Mesh = mesh;
                Renderer = renderer;
                Collider = collider;
            }
        }
    }
}
