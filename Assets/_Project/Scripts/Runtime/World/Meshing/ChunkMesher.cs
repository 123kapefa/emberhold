using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Строит меш чанка только из видимых граней. Блок занимает ячейку от пола до высоты формы,
    /// поэтому боковая грань рисуется лишь там, где блок выше соседа: от верха соседа до своего верха.
    /// Вершины — в координатах чанка, в единицах блока.
    /// Склоны пока рисуются плоскими по самому высокому углу; настоящие склоны — карточка #14.
    /// </summary>
    public static class ChunkMesher
    {
        private const float Sixteenth = 1f / BlockShape.MaxHeight;

        public static void Build(VoxelWorld world, Vector3Int chunk, ChunkMeshData output)
        {
            output.Clear();
            var data = world.GetChunk(chunk);
            var origin = chunk * ChunkData.Size;

            for (var y = 0; y < ChunkData.Size; y++)
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < ChunkData.Size; x++)
            {
                var block = data.Get(x, y, z);
                if (block.IsAir)
                    continue;

                var top = block.Shape.Top;
                var material = block.Material;
                var p = new Vector3(x, y, z);
                var t = top * Sixteenth;

                // Верх: закрыт, только если блок полный и сверху стоит другой блок (он всегда опирается на пол ячейки).
                if (top < BlockShape.MaxHeight || Neighbor(world, data, origin, x, y + 1, z).IsAir)
                    AddTop(output, material, p, t);

                // Низ: закрыт полным блоком снизу.
                var below = Neighbor(world, data, origin, x, y - 1, z);
                if (below.IsAir || below.Shape.Top < BlockShape.MaxHeight)
                    AddBottom(output, material, p);

                AddSide(output, material, p, top, Neighbor(world, data, origin, x + 1, y, z), Vector3.right);
                AddSide(output, material, p, top, Neighbor(world, data, origin, x - 1, y, z), Vector3.left);
                AddSide(output, material, p, top, Neighbor(world, data, origin, x, y, z + 1), Vector3.forward);
                AddSide(output, material, p, top, Neighbor(world, data, origin, x, y, z - 1), Vector3.back);
            }
        }

        /// <summary>
        /// Сосед по локальной координате: внутри чанка — напрямую, снаружи — через мир.
        /// Ниже дна мира считаем сплошной камень, чтобы не рисовать невидимое дно.
        /// </summary>
        private static Block Neighbor(VoxelWorld world, ChunkData data, Vector3Int origin, int x, int y, int z)
        {
            if (ChunkData.IsInside(x, y, z))
                return data.Get(x, y, z);
            var global = origin + new Vector3Int(x, y, z);
            if (global.y < 0)
                return Block.Full(BlockMaterials.Stone);
            return world.GetBlock(global);
        }

        private static void AddTop(ChunkMeshData output, byte material, Vector3 p, float t)
        {
            output.AddQuad(material,
                p + new Vector3(0, t, 0), p + new Vector3(0, t, 1), p + new Vector3(1, t, 1), p + new Vector3(1, t, 0),
                Vector3.up,
                new Vector2(p.x, p.z), new Vector2(p.x, p.z + 1), new Vector2(p.x + 1, p.z + 1), new Vector2(p.x + 1, p.z));
        }

        private static void AddBottom(ChunkMeshData output, byte material, Vector3 p)
        {
            output.AddQuad(material,
                p, p + new Vector3(1, 0, 0), p + new Vector3(1, 0, 1), p + new Vector3(0, 0, 1),
                Vector3.down,
                new Vector2(p.x, p.z), new Vector2(p.x + 1, p.z), new Vector2(p.x + 1, p.z + 1), new Vector2(p.x, p.z + 1));
        }

        private static void AddSide(ChunkMeshData output, byte material, Vector3 p, int top, Block neighbor, Vector3 normal)
        {
            var neighborTop = neighbor.IsAir ? 0 : neighbor.Shape.Top;
            if (neighborTop >= top)
                return;

            var a = neighborTop * Sixteenth;
            var b = top * Sixteenth;
            Vector3 v0, v1, v2, v3;
            float u0, u1;
            if (normal == Vector3.right)
            {
                v0 = new Vector3(1, a, 0); v1 = new Vector3(1, b, 0); v2 = new Vector3(1, b, 1); v3 = new Vector3(1, a, 1);
                u0 = p.z; u1 = p.z + 1;
            }
            else if (normal == Vector3.left)
            {
                v0 = new Vector3(0, a, 1); v1 = new Vector3(0, b, 1); v2 = new Vector3(0, b, 0); v3 = new Vector3(0, a, 0);
                u0 = p.z + 1; u1 = p.z;
            }
            else if (normal == Vector3.forward)
            {
                v0 = new Vector3(1, a, 1); v1 = new Vector3(1, b, 1); v2 = new Vector3(0, b, 1); v3 = new Vector3(0, a, 1);
                u0 = p.x + 1; u1 = p.x;
            }
            else
            {
                v0 = new Vector3(0, a, 0); v1 = new Vector3(0, b, 0); v2 = new Vector3(1, b, 0); v3 = new Vector3(1, a, 0);
                u0 = p.x; u1 = p.x + 1;
            }

            var ya = p.y + a;
            var yb = p.y + b;
            output.AddQuad(material, p + v0, p + v1, p + v2, p + v3, normal,
                new Vector2(u0, ya), new Vector2(u0, yb), new Vector2(u1, yb), new Vector2(u1, ya));
        }
    }
}
