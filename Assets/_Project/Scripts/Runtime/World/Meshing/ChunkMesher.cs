using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Строит меш чанка только из видимых граней. Блок занимает ячейку от пола до своих четырёх верхних углов:
    /// плоский — ровной крышкой, склон — двумя треугольниками. Боковая грань рисуется только там,
    /// где край блока выше края соседа на общем ребре. Вершины — в координатах чанка, в единицах блока.
    /// </summary>
    public static class ChunkMesher
    {
        private const float Sixteenth = 1f / BlockShape.MaxHeight;

        private enum Side
        {
            Right,
            Left,
            Forward,
            Back,
        }

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

                var s = block.Shape;
                var material = block.Material;
                var index = ChunkData.Index(x, y, z);
                var p = new Vector3(x, y, z);

                // Верх закрыт, только если блок полный и сверху стоит другой блок (он всегда опирается на пол ячейки).
                if (!s.IsFull || Neighbor(world, data, origin, x, y + 1, z).IsAir)
                    AddTop(output, material, index, p, s);

                // Низ закрыт только полным блоком снизу; у воздуха форма пустая.
                if (!Neighbor(world, data, origin, x, y - 1, z).Shape.IsFull)
                    AddBottom(output, material, index, p);

                // Общее ребро с соседом: наши два угла и его два угла на той же стороне.
                var n = Neighbor(world, data, origin, x + 1, y, z).Shape;
                AddSide(output, material, index, p, Side.Right, s.C10, s.C11, n.C00, n.C01);
                n = Neighbor(world, data, origin, x - 1, y, z).Shape;
                AddSide(output, material, index, p, Side.Left, s.C00, s.C01, n.C10, n.C11);
                n = Neighbor(world, data, origin, x, y, z + 1).Shape;
                AddSide(output, material, index, p, Side.Forward, s.C01, s.C11, n.C00, n.C10);
                n = Neighbor(world, data, origin, x, y, z - 1).Shape;
                AddSide(output, material, index, p, Side.Back, s.C00, s.C10, n.C01, n.C11);
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

        /// <summary>
        /// Верх по четырём углам. Если углы лежат в одной плоскости — один четырёхугольник, иначе два треугольника.
        /// Диагональ проводим между углами с меньшей разницей высот: тогда у склона «три угла вверху,
        /// один внизу» получается ровная половина и наклонная половина, а не излом поперёк склона.
        /// </summary>
        private static void AddTop(ChunkMeshData output, byte material, int index, Vector3 p, BlockShape s)
        {
            var c00 = p + new Vector3(0, s.C00 * Sixteenth, 0);
            var c10 = p + new Vector3(1, s.C10 * Sixteenth, 0);
            var c01 = p + new Vector3(0, s.C01 * Sixteenth, 1);
            var c11 = p + new Vector3(1, s.C11 * Sixteenth, 1);
            var uv00 = new Vector2(p.x, p.z);
            var uv10 = new Vector2(p.x + 1, p.z);
            var uv01 = new Vector2(p.x, p.z + 1);
            var uv11 = new Vector2(p.x + 1, p.z + 1);

            if (s.C00 + s.C11 == s.C10 + s.C01)
            {
                output.AddQuad(material, index, c00, c01, c11, c10, Normal(c00, c01, c11), uv00, uv01, uv11, uv10);
            }
            else if (Mathf.Abs(s.C00 - s.C11) <= Mathf.Abs(s.C10 - s.C01))
            {
                output.AddTriangle(material, index, c00, c01, c11, Normal(c00, c01, c11), uv00, uv01, uv11);
                output.AddTriangle(material, index, c00, c11, c10, Normal(c00, c11, c10), uv00, uv11, uv10);
            }
            else
            {
                output.AddTriangle(material, index, c01, c11, c10, Normal(c01, c11, c10), uv01, uv11, uv10);
                output.AddTriangle(material, index, c01, c10, c00, Normal(c01, c10, c00), uv01, uv10, uv00);
            }
        }

        private static void AddBottom(ChunkMeshData output, byte material, int index, Vector3 p)
        {
            output.AddQuad(material, index,
                p, p + new Vector3(1, 0, 0), p + new Vector3(1, 0, 1), p + new Vector3(0, 0, 1),
                Vector3.down,
                new Vector2(p.x, p.z), new Vector2(p.x + 1, p.z), new Vector2(p.x + 1, p.z + 1), new Vector2(p.x, p.z + 1));
        }

        /// <summary>
        /// Боковая грань на ребре, где наш край идёт от высоты a0 до a1, а край соседа — от b0 до b1
        /// (t от 0 до 1 вдоль ребра). Видна только часть, где мы выше соседа: четырёхугольник,
        /// треугольник, если края сходятся в одном конце, или треугольник до точки пересечения краёв.
        /// </summary>
        private static void AddSide(ChunkMeshData output, byte material, int index, Vector3 p, Side side,
            int a0, int a1, int b0, int b1)
        {
            var d0 = a0 - b0;
            var d1 = a1 - b1;
            if (d0 <= 0 && d1 <= 0)
                return;

            if (d0 >= 0 && d1 >= 0)
            {
                if (d0 == 0)
                    AddSideTriangle(output, material, index, p, side, (0f, a0), (1f, a1), (1f, b1));
                else if (d1 == 0)
                    AddSideTriangle(output, material, index, p, side, (0f, b0), (0f, a0), (1f, a1));
                else
                    AddSideQuad(output, material, index, p, side, (0f, b0), (0f, a0), (1f, a1), (1f, b1));
                return;
            }

            // Края пересекаются: видна часть до точки пересечения с той стороны, где мы выше.
            var t = (float)d0 / (d0 - d1);
            var h = b0 + (b1 - b0) * t;
            if (d0 > 0)
                AddSideTriangle(output, material, index, p, side, (0f, b0), (0f, a0), (t, h));
            else
                AddSideTriangle(output, material, index, p, side, (t, h), (1f, a1), (1f, b1));
        }

        private static void AddSideQuad(ChunkMeshData output, byte material, int index, Vector3 p, Side side,
            (float t, float h) a, (float t, float h) b, (float t, float h) c, (float t, float h) d)
        {
            var normal = SideNormal(side);
            Vector3 va = SidePoint(p, side, a), vb = SidePoint(p, side, b), vc = SidePoint(p, side, c), vd = SidePoint(p, side, d);
            Vector2 ua = SideUv(p, side, a), ub = SideUv(p, side, b), uc = SideUv(p, side, c), ud = SideUv(p, side, d);
            if (Vector3.Dot(Vector3.Cross(vb - va, vc - va), normal) >= 0f)
                output.AddQuad(material, index, va, vb, vc, vd, normal, ua, ub, uc, ud);
            else
                output.AddQuad(material, index, va, vd, vc, vb, normal, ua, ud, uc, ub);
        }

        private static void AddSideTriangle(ChunkMeshData output, byte material, int index, Vector3 p, Side side,
            (float t, float h) a, (float t, float h) b, (float t, float h) c)
        {
            var normal = SideNormal(side);
            Vector3 va = SidePoint(p, side, a), vb = SidePoint(p, side, b), vc = SidePoint(p, side, c);
            Vector2 ua = SideUv(p, side, a), ub = SideUv(p, side, b), uc = SideUv(p, side, c);
            if (Vector3.Dot(Vector3.Cross(vb - va, vc - va), normal) >= 0f)
                output.AddTriangle(material, index, va, vb, vc, normal, ua, ub, uc);
            else
                output.AddTriangle(material, index, va, vc, vb, normal, ua, uc, ub);
        }

        /// <summary>Точка боковой грани: t — вдоль ребра (по Z для сторон X, по X для сторон Z), h — высота в шестнадцатых.</summary>
        private static Vector3 SidePoint(Vector3 p, Side side, (float t, float h) point)
        {
            var y = point.h * Sixteenth;
            return p + side switch
            {
                Side.Right => new Vector3(1f, y, point.t),
                Side.Left => new Vector3(0f, y, point.t),
                Side.Forward => new Vector3(point.t, y, 1f),
                _ => new Vector3(point.t, y, 0f),
            };
        }

        private static Vector2 SideUv(Vector3 p, Side side, (float t, float h) point)
        {
            var along = side is Side.Right or Side.Left ? p.z : p.x;
            return new Vector2(along + point.t, p.y + point.h * Sixteenth);
        }

        private static Vector3 SideNormal(Side side) => side switch
        {
            Side.Right => Vector3.right,
            Side.Left => Vector3.left,
            Side.Forward => Vector3.forward,
            _ => Vector3.back,
        };

        private static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).normalized;
    }
}
