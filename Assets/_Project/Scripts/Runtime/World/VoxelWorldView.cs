using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Emberhold.World
{
    /// <summary>
    /// Показывает воксельный мир в сцене: на каждый чанк один GameObject с мешем и коллайдером на слое Ground.
    /// В Awake генерирует рельеф по настройкам и зонам местности сцены; каждый кадр перестраивает только изменённые чанки.
    /// </summary>
    public sealed class VoxelWorldView : MonoBehaviour
    {
        [Tooltip("Рельеф, который сгенерировать при старте. Пусто — мир передаст другой компонент через Show (например, песочница).")]
        [FormerlySerializedAs("valley")]
        [SerializeField] private TerrainSettings terrain;
        [Tooltip("Материал для каждого номера материала блока: 0 — воздух (не используется), 1 — трава, 2 — земля, 3 — камень, 4 — дерево.")]
        [SerializeField] private Material[] blockMaterials;
        [Tooltip("Сдвинуть мир так, чтобы центр мира оказался в начале координат сцены.")]
        [SerializeField] private bool centerOnOrigin = true;
        [Tooltip("Сглаженное освещение склонов: нормали верха блоков усредняются по соседям.")]
        [SerializeField] private bool smoothShading;

        private readonly Dictionary<Vector3Int, ChunkView> chunkViews = new();
        private readonly Dictionary<Collider, ChunkView> viewsByCollider = new();
        private readonly List<Vector3Int> dirtyBuffer = new();
        private readonly List<Material> sharedMaterials = new();
        private ChunkMeshData meshData;
        private readonly SurfaceNormals surfaceNormals = new();
        private bool missingMaterialReported;

        public VoxelWorld World { get; private set; }

        /// <summary>Сглаженное освещение склонов; при переключении весь мир перестраивается.</summary>
        public bool SmoothShading
        {
            get => smoothShading;
            set
            {
                if (smoothShading == value)
                    return;
                smoothShading = value;
                World?.MarkAllDirty();
            }
        }

        private void Awake()
        {
            if (terrain != null)
                Show(terrain.Generate(CollectZones(terrain.SizeInChunks * ChunkData.Size)));
        }

        /// <summary>Зоны местности сцены в координатах сетки мира, который встанет на место этого вида.</summary>
        private List<TerrainZoneArea> CollectZones(Vector3Int sizeInBlocks)
        {
            var zones = new List<TerrainZoneArea>();
            var origin = centerOnOrigin ? WorldOrigin(sizeInBlocks) : transform.position;
            foreach (var zone in FindObjectsByType<TerrainZone>())
                if (zone.TryGetArea(origin, out var area))
                    zones.Add(area);
            return zones;
        }

        /// <summary>Где окажется угол (0, 0) сетки мира, если центр мира в начале координат.</summary>
        private static Vector3 WorldOrigin(Vector3Int sizeInBlocks) =>
            new(-sizeInBlocks.x * 0.5f, 0f, -sizeInBlocks.z * 0.5f);

        /// <summary>Показать мир: старые чанки убираются, все чанки нового мира строятся сразу.</summary>
        public void Show(VoxelWorld world)
        {
            ClearChunkViews();
            World = world;
            if (centerOnOrigin)
            {
                transform.position = WorldOrigin(World.SizeInBlocks);
            }

            meshData ??= new ChunkMeshData();
            if (sharedMaterials.Count == 0)
                BuildSharedMaterials();
            World.MarkAllDirty();
            RebuildDirtyChunks();
        }

        private void LateUpdate()
        {
            if (World != null && World.DirtyCount > 0)
                RebuildDirtyChunks();
        }

        private void OnDestroy() => ClearChunkViews();

        private void ClearChunkViews()
        {
            foreach (var view in chunkViews.Values)
            {
                Destroy(view.Mesh);
                Destroy(view.Collider.gameObject);
            }
            chunkViews.Clear();
            viewsByCollider.Clear();
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
            ChunkMesher.Build(World, chunk, meshData, smoothShading ? surfaceNormals : null);
            if (!chunkViews.TryGetValue(chunk, out var view))
            {
                if (meshData.IsEmpty)
                    return;
                view = CreateChunkView(chunk);
                chunkViews.Add(chunk, view);
                viewsByCollider.Add(view.Collider, view);
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
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.SetSharedMaterials(sharedMaterials);
            return new ChunkView(chunk, mesh, renderer, go.AddComponent<MeshCollider>());
        }

        private void ApplyMesh(ChunkView view)
        {
            var mesh = view.Mesh;
            mesh.Clear();
            mesh.indexFormat = meshData.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(meshData.Vertices);
            mesh.SetNormals(meshData.Normals);
            mesh.SetUVs(0, meshData.Uvs);

            // Подмеш на каждый материал, даже пустой: у рендерера всегда один и тот же список материалов.
            // Если список менять при каждой перестройке, GPU Resident Drawer может остаться со старым
            // сопоставлением подмешей и материалов и нарисовать землю камнем до следующей перестройки.
            mesh.subMeshCount = sharedMaterials.Count;
            view.TriangleBlocks.Clear();
            for (var subMesh = 0; subMesh < sharedMaterials.Count; subMesh++)
            {
                mesh.SetTriangles(meshData.Triangles[subMesh + 1], subMesh, false);
                // Коллайдер нумерует треугольники подряд по подмешам, в том же порядке.
                view.TriangleBlocks.AddRange(meshData.TriangleBlocks[subMesh + 1]);
            }
            ReportUnmappedMaterials();
            mesh.RecalculateBounds();

            view.Renderer.enabled = !meshData.IsEmpty;
            // Коллайдер берёт меш при назначении, поэтому переназначаем после каждой перестройки.
            view.Collider.sharedMesh = null;
            if (!meshData.IsEmpty)
                view.Collider.sharedMesh = mesh;
        }

        /// <summary>Материалы для подмешей: подмеш i — материал блока i + 1 (воздух не рисуется).</summary>
        private void BuildSharedMaterials()
        {
            sharedMaterials.Clear();
            var count = Mathf.Max(BlockMaterials.Count, blockMaterials != null ? blockMaterials.Length : 0);
            for (var material = 1; material < count; material++)
                sharedMaterials.Add(MaterialFor(material));
        }

        private void ReportUnmappedMaterials()
        {
            for (var material = sharedMaterials.Count + 1; material < meshData.Triangles.Length; material++)
            {
                if (meshData.Triangles[material].Count > 0)
                    MaterialFor(material);
            }
        }

        /// <summary>Материал, которым рисуется блок с этим номером материала (для предпросмотра постановки).</summary>
        public Material BlockMaterial(byte material) => MaterialFor(material);

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

        /// <summary>
        /// Блок, которому принадлежит треугольник меша чанка, по попаданию луча (RaycastHit.collider и
        /// RaycastHit.triangleIndex). Точнее, чем считать по точке: на склоне точка у низкого края лежит
        /// прямо на полу ячейки.
        /// </summary>
        public bool TryGetBlock(Collider collider, int triangleIndex, out Vector3Int block)
        {
            block = default;
            if (collider == null || !viewsByCollider.TryGetValue(collider, out var view)
                || (uint)triangleIndex >= (uint)view.TriangleBlocks.Count)
                return false;

            block = view.Chunk * ChunkData.Size + ChunkData.Position(view.TriangleBlocks[triangleIndex]);
            return true;
        }

        private sealed class ChunkView
        {
            public readonly Vector3Int Chunk;
            public readonly Mesh Mesh;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;
            /// <summary>Индекс блока в чанке для каждого треугольника меша по порядку.</summary>
            public readonly List<int> TriangleBlocks = new();

            public ChunkView(Vector3Int chunk, Mesh mesh, MeshRenderer renderer, MeshCollider collider)
            {
                Chunk = chunk;
                Mesh = mesh;
                Renderer = renderer;
                Collider = collider;
            }
        }
    }
}
