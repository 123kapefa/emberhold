using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Макет #79: «пиксельный» блок. Верх простой формы (по высотам углов) делится на сетку N × N
    /// (<see cref="PixelSlopes.Steps"/>), в каждой клетке — высота формы в её середине, округлённая до шага 16 / N.
    /// Рисуются верхи клеток (одинаковые соседние сливаются в прямоугольники) и стенки-ступени между клетками
    /// и между блоками по сторонам. Если над клеткой лежит блок, клетка под ним считается полной, чтобы в стопке
    /// слоёв на крутом склоне не было щелей.
    /// </summary>
    public static class StepMesher
    {
        private const int H = BlockShape.MaxHeight;

        public static void AddBlock(ChunkMeshData output, VoxelWorld world, Vector3Int global, Vector3 p, Block block, int index,
            Action<int> afterTop)
        {
            var n = PixelSlopes.Steps;
            var material = block.Material;
            var above = Get(world, global + Vector3Int.up);

            // Полный блок: верх — одним квадратом, если сверху воздух; под полным блоком верха нет.
            if (block.Shape.IsFull && (above.IsAir || IsFullSimple(above)))
            {
                if (above.IsAir)
                {
                    var start0 = output.Vertices.Count;
                    Span<int> full = stackalloc int[1] { H };
                    AddTops(output, material, index, p, full, 1);
                    afterTop?.Invoke(start0);
                }
                AddSide(output, world, global, p, block, above, index, n, Vector3Int.right);
                AddSide(output, world, global, p, block, above, index, n, Vector3Int.left);
                AddSide(output, world, global, p, block, above, index, n, Vector3Int.forward);
                AddSide(output, world, global, p, block, above, index, n, Vector3Int.back);
                return;
            }

            // Верх: клетки, которые не закрыты блоком сверху.
            Span<int> tops = stackalloc int[n * n];
            for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var m = Micro(block, above, i, j, n);
                tops[i + j * n] = m > 0 && Raw(above, i, j, n) == 0 ? m : -1;
            }
            var start = output.Vertices.Count;
            AddTops(output, material, index, p, tops, n);

            // Ступени внутри блока: между соседними клетками разной высоты (видна часть выше соседа).
            for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var a = Micro(block, above, i, j, n);
                if (i + 1 < n)
                {
                    var b = Micro(block, above, i + 1, j, n);
                    if (a != b)
                        AddRiserX(output, material, index, p, (i + 1) / (float)n, j / (float)n, (j + 1) / (float)n,
                            Mathf.Min(a, b), Mathf.Max(a, b), a > b);
                }
                if (j + 1 < n)
                {
                    var b = Micro(block, above, i, j + 1, n);
                    if (a != b)
                        AddRiserZ(output, material, index, p, (j + 1) / (float)n, i / (float)n, (i + 1) / (float)n,
                            Mathf.Min(a, b), Mathf.Max(a, b), a > b);
                }
            }
            afterTop?.Invoke(start);

            // Стороны блока: по клеткам края, от высоты соседа до своей.
            AddSide(output, world, global, p, block, above, index, n, Vector3Int.right);
            AddSide(output, world, global, p, block, above, index, n, Vector3Int.left);
            AddSide(output, world, global, p, block, above, index, n, Vector3Int.forward);
            AddSide(output, world, global, p, block, above, index, n, Vector3Int.back);
        }

        private static bool IsFullSimple(Block block) => !block.IsAir && block.Shape.IsFull;

        private static Block Get(VoxelWorld world, Vector3Int pos)
        {
            if (pos.y < 0)
                return Block.Full(BlockMaterials.Stone);
            return world.IsInsideBlocks(pos) ? world.GetBlock(pos) : Block.Air;
        }

        /// <summary>Высота формы в середине клетки (i, j), округлённая до шага, в шестнадцатых; фигурные формы и воздух — 0.</summary>
        private static int Raw(Block block, int i, int j, int n)
        {
            if (block.IsAir || block.Shape.IsCatalog)
                return 0;
            var s = block.Shape;
            var u = (i + 0.5f) / n;
            var v = (j + 0.5f) / n;
            var h = Mathf.Lerp(Mathf.Lerp(s.C00, s.C10, u), Mathf.Lerp(s.C01, s.C11, u), v);
            var q = H / n;
            return Mathf.Clamp(Mathf.RoundToInt(h / q) * q, 0, H);
        }

        /// <summary>Высота клетки с учётом блока сверху: под непустой клеткой верхнего блока клетка полная.</summary>
        private static int Micro(Block block, Block above, int i, int j, int n)
        {
            if (block.IsAir)
                return 0;
            if (block.Shape.IsCatalog)
                return 0;
            var raw = Raw(block, i, j, n);
            return raw < H && Raw(above, i, j, n) > 0 ? H : raw;
        }

        private static void AddSide(ChunkMeshData output, VoxelWorld world, Vector3Int global, Vector3 p, Block block, Block above,
            int index, int n, Vector3Int dir)
        {
            var neighbourPos = global + dir;
            var neighbour = Get(world, neighbourPos);
            if (IsFullSimple(neighbour))
                return;
            var neighbourAbove = Get(world, neighbourPos + Vector3Int.up);
            var runStart = 0;
            int runA = -1, runB = -1;
            for (var k = 0; k <= n; k++)
            {
                int a = 0, b = 0;
                if (k < n)
                {
                    // Своя клетка у края и клетка соседа напротив.
                    var (si, sj, ni, nj) = dir.x > 0 ? (n - 1, k, 0, k)
                        : dir.x < 0 ? (0, k, n - 1, k)
                        : dir.z > 0 ? (k, n - 1, k, 0)
                        : (k, 0, k, n - 1);
                    a = Micro(block, above, si, sj, n);
                    b = neighbour.Shape.IsCatalog && !neighbour.IsAir ? 0 : Micro(neighbour, neighbourAbove, ni, nj, n);
                    if (a <= b)
                        a = b = 0;
                }
                if (k < n && a == runA && b == runB)
                    continue;
                if (runA > runB && k > runStart)
                    AddSideQuad(output, block.Material, index, p, dir, runStart / (float)n, k / (float)n, runB, runA);
                runStart = k;
                runA = a;
                runB = b;
            }
        }

        private static void AddSideQuad(ChunkMeshData output, byte material, int index, Vector3 p, Vector3Int dir,
            float t0, float t1, int low, int high)
        {
            float y0 = low / (float)H, y1 = high / (float)H;
            Vector3 a, b, c, d;
            if (dir.x != 0)
            {
                var x = dir.x > 0 ? 1f : 0f;
                a = new Vector3(x, y0, t0); b = new Vector3(x, y1, t0); c = new Vector3(x, y1, t1); d = new Vector3(x, y0, t1);
            }
            else
            {
                var z = dir.z > 0 ? 1f : 0f;
                a = new Vector3(t0, y0, z); b = new Vector3(t0, y1, z); c = new Vector3(t1, y1, z); d = new Vector3(t1, y0, z);
            }
            AddFacingQuad(output, material, index, p, a, b, c, d, dir);
        }

        private static void AddRiserX(ChunkMeshData output, byte material, int index, Vector3 p, float x, float z0, float z1,
            int low, int high, bool facesPositive)
        {
            float y0 = low / (float)H, y1 = high / (float)H;
            AddFacingQuad(output, material, index, p, new Vector3(x, y0, z0), new Vector3(x, y1, z0), new Vector3(x, y1, z1),
                new Vector3(x, y0, z1), facesPositive ? Vector3Int.right : Vector3Int.left);
        }

        private static void AddRiserZ(ChunkMeshData output, byte material, int index, Vector3 p, float z, float x0, float x1,
            int low, int high, bool facesPositive)
        {
            float y0 = low / (float)H, y1 = high / (float)H;
            AddFacingQuad(output, material, index, p, new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z),
                new Vector3(x1, y0, z), facesPositive ? Vector3Int.forward : Vector3Int.back);
        }

        private static void AddFacingQuad(ChunkMeshData output, byte material, int index, Vector3 p,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3Int dir)
        {
            var normal = (Vector3)dir;
            Vector3 va = p + a, vb = p + b, vc = p + c, vd = p + d;
            if (Vector3.Dot(Vector3.Cross(vb - va, vc - va), normal) < 0f)
                (vb, vd) = (vd, vb);
            output.AddQuad(material, index, va, vb, vc, vd, normal, Uv(va, dir), Uv(vb, dir), Uv(vc, dir), Uv(vd, dir));
        }

        private static Vector2 Uv(Vector3 v, Vector3Int dir) => dir.x != 0 ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y);

        /// <summary>Верхи клеток: одинаковые по высоте соседние клетки сливаются в прямоугольники (жадно).</summary>
        private static void AddTops(ChunkMeshData output, byte material, int index, Vector3 p, Span<int> tops, int n)
        {
            Span<bool> done = stackalloc bool[n * n];
            for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var h = tops[i + j * n];
                if (done[i + j * n] || h < 0)
                    continue;
                var w = 1;
                while (i + w < n && !done[i + w + j * n] && tops[i + w + j * n] == h)
                    w++;
                var d = 1;
                while (j + d < n)
                {
                    var ok = true;
                    for (var k = 0; k < w && ok; k++)
                        ok = !done[i + k + (j + d) * n] && tops[i + k + (j + d) * n] == h;
                    if (!ok)
                        break;
                    d++;
                }
                for (var dj = 0; dj < d; dj++)
                for (var di = 0; di < w; di++)
                    done[i + di + (j + dj) * n] = true;

                float x0 = i / (float)n, x1 = (i + w) / (float)n, z0 = j / (float)n, z1 = (j + d) / (float)n, y = h / (float)H;
                var c00 = p + new Vector3(x0, y, z0);
                var c10 = p + new Vector3(x1, y, z0);
                var c01 = p + new Vector3(x0, y, z1);
                var c11 = p + new Vector3(x1, y, z1);
                output.AddQuad(material, index, c00, c01, c11, c10, Vector3.up,
                    new Vector2(c00.x, c00.z), new Vector2(c01.x, c01.z), new Vector2(c11.x, c11.z), new Vector2(c10.x, c10.z));
            }
        }
    }
}
