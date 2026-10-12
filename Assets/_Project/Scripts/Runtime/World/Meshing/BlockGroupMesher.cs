using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Меш группы блоков отдельно от мира (например, блоков, которые падают вместе). Блоки кладутся
    /// в маленький временный мир и мешируются тем же <see cref="ChunkMesher"/>: грани между блоками группы
    /// не рисуются, а все наружные, включая низ, рисуются. Под группой оставлен пустой слой: ниже дна
    /// временного мира мешер считает камень и низ бы не нарисовал.
    /// </summary>
    public static class BlockGroupMesher
    {
        /// <summary>
        /// Построить меш блоков blocks[i] в клетках cells[i] (координаты мира блоков) в output.
        /// Возвращает начало координат меша: вершина + начало = точка в координатах мира блоков.
        /// </summary>
        public static Vector3Int Build(IReadOnlyList<Vector3Int> cells, IReadOnlyList<Block> blocks, ChunkMeshData output,
            ChunkMeshData scratch = null)
        {
            output.Clear();
            if (cells.Count == 0)
                return Vector3Int.zero;

            var min = cells[0];
            var max = cells[0];
            foreach (var cell in cells)
            {
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);
            }
            var origin = min + Vector3Int.down;
            var size = max - origin + Vector3Int.one;
            var chunks = new Vector3Int(ChunksFor(size.x), ChunksFor(size.y), ChunksFor(size.z));

            var group = new VoxelWorld(chunks);
            for (var i = 0; i < cells.Count; i++)
                group.SetBlock(cells[i] - origin, blocks[i]);

            scratch ??= new ChunkMeshData();
            for (var y = 0; y < chunks.y; y++)
            for (var z = 0; z < chunks.z; z++)
            for (var x = 0; x < chunks.x; x++)
            {
                var chunk = new Vector3Int(x, y, z);
                ChunkMesher.Build(group, chunk, scratch);
                Append(scratch, chunk * ChunkData.Size, output);
            }
            return origin;
        }

        private static int ChunksFor(int blocks) => (blocks + ChunkData.Size - 1) / ChunkData.Size;

        private static void Append(ChunkMeshData from, Vector3 offset, ChunkMeshData to)
        {
            var first = to.Vertices.Count;
            foreach (var vertex in from.Vertices)
                to.Vertices.Add(vertex + offset);
            to.Normals.AddRange(from.Normals);
            to.Uvs.AddRange(from.Uvs);
            for (var material = 0; material < from.Triangles.Length; material++)
            {
                foreach (var index in from.Triangles[material])
                    to.Triangles[material].Add(first + index);
                to.TriangleBlocks[material].AddRange(from.TriangleBlocks[material]);
            }
        }
    }
}
