using System;
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

        /// <param name="smoothNormals">Если задано — верх блоков получает сглаженные нормали (плавное освещение склонов).</param>
        public static void Build(VoxelWorld world, Vector3Int chunk, ChunkMeshData output, SurfaceNormals smoothNormals = null)
        {
            output.Clear();
            var data = world.GetChunk(chunk);
            var origin = chunk * ChunkData.Size;
            smoothNormals?.Prepare(world, chunk);

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

                if (s.IsCatalog)
                {
                    AddCatalogForm(output, material, index, p, s, world, data, origin, x, y, z);
                    continue;
                }

                // Верх закрыт, только если блок полный, а блок сверху закрывает весь пол своей ячейки
                // (у внешнего угла ската половина пола открыта — там видна земля снизу).
                if (!s.IsFull || !Neighbor(world, data, origin, x, y + 1, z).Shape.CoversFloor)
                    AddTop(output, material, index, p, s, smoothNormals, (origin.y + y) * BlockShape.MaxHeight);

                // Низ закрыт только полным блоком снизу; у воздуха форма пустая.
                if (!Neighbor(world, data, origin, x, y - 1, z).Shape.IsFull)
                    AddBottom(output, material, index, p, s);

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
        private static void AddTop(ChunkMeshData output, byte material, int index, Vector3 p, BlockShape s,
            SurfaceNormals smooth, int floorHeight)
        {
            if (smooth != null)
            {
                AddSmoothTop(output, material, index, p, s, smooth, floorHeight);
                return;
            }

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
                // Половина с нулевыми углами лежит на полу ячейки: у неё нет объёма, и рисовать её
                // нельзя — она закрыла бы собой землю под блоком (внешний угол ската).
                if (s.C00 + s.C01 + s.C11 > 0)
                    output.AddTriangle(material, index, c00, c01, c11, Normal(c00, c01, c11), uv00, uv01, uv11);
                if (s.C00 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, c00, c11, c10, Normal(c00, c11, c10), uv00, uv11, uv10);
            }
            else
            {
                if (s.C01 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, c01, c11, c10, Normal(c01, c11, c10), uv01, uv11, uv10);
                if (s.C01 + s.C10 + s.C00 > 0)
                    output.AddTriangle(material, index, c01, c10, c00, Normal(c01, c10, c00), uv01, uv10, uv00);
            }
        }

        /// <summary>То же разбиение, что у <see cref="AddTop"/>, но нормали в вершинах сглажены.</summary>
        private static void AddSmoothTop(ChunkMeshData output, byte material, int index, Vector3 p, BlockShape s,
            SurfaceNormals smooth, int floorHeight)
        {
            var x = (int)p.x;
            var z = (int)p.z;
            var c00 = p + new Vector3(0, s.C00 * Sixteenth, 0);
            var c10 = p + new Vector3(1, s.C10 * Sixteenth, 0);
            var c01 = p + new Vector3(0, s.C01 * Sixteenth, 1);
            var c11 = p + new Vector3(1, s.C11 * Sixteenth, 1);
            var uv00 = new Vector2(p.x, p.z);
            var uv10 = new Vector2(p.x + 1, p.z);
            var uv01 = new Vector2(p.x, p.z + 1);
            var uv11 = new Vector2(p.x + 1, p.z + 1);

            // Нормаль граней на случай, когда вершина не на поверхности (её сгладить не по чему).
            var flat = s.C00 + s.C11 == s.C10 + s.C01;
            var alongMain = Mathf.Abs(s.C00 - s.C11) <= Mathf.Abs(s.C10 - s.C01);
            Vector3 fa, fb;
            if (alongMain)
            {
                fa = Normal(c00, c01, c11);
                fb = flat ? fa : Normal(c00, c11, c10);
            }
            else
            {
                fa = Normal(c01, c11, c10);
                fb = flat ? fa : Normal(c01, c10, c00);
            }

            Vector3 N(int cx, int cz, int corner, Vector3 fallback) =>
                smooth.At(x + cx, z + cz, floorHeight + corner, fallback);

            if (alongMain)
            {
                if (s.C00 + s.C01 + s.C11 > 0)
                    output.AddTriangle(material, index, c00, c01, c11,
                        N(0, 0, s.C00, fa), N(0, 1, s.C01, fa), N(1, 1, s.C11, fa), uv00, uv01, uv11);
                if (s.C00 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, c00, c11, c10,
                        N(0, 0, s.C00, fb), N(1, 1, s.C11, fb), N(1, 0, s.C10, fb), uv00, uv11, uv10);
            }
            else
            {
                if (s.C01 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, c01, c11, c10,
                        N(0, 1, s.C01, fa), N(1, 1, s.C11, fa), N(1, 0, s.C10, fa), uv01, uv11, uv10);
                if (s.C01 + s.C10 + s.C00 > 0)
                    output.AddTriangle(material, index, c01, c10, c00,
                        N(0, 1, s.C01, fb), N(1, 0, s.C10, fb), N(0, 0, s.C00, fb), uv01, uv10, uv00);
            }
        }

        /// <summary>
        /// Фигурная форма из каталога: грани всех её коробок. Грань не рисуется, если её целиком закрывает
        /// другая коробка той же формы или (на стороне ячейки) полный блок соседа.
        /// </summary>
        private static void AddCatalogForm(ChunkMeshData output, byte material, int index, Vector3 p, BlockShape s,
            VoxelWorld world, ChunkData data, Vector3Int origin, int x, int y, int z)
        {
            const int n = BlockShape.MaxHeight;
            var source = BlockForms.Boxes(s.Form);
            Span<FormBox> boxes = stackalloc FormBox[source.Length];
            for (var i = 0; i < source.Length; i++)
                boxes[i] = source[i].Rotated(s.Rotation);

            // Закрыта ли сторона ячейки полным блоком соседа: +X, −X, +Y, −Y, +Z, −Z.
            Span<bool> sideClosed = stackalloc bool[6];
            sideClosed[0] = Neighbor(world, data, origin, x + 1, y, z).Shape.IsFull;
            sideClosed[1] = Neighbor(world, data, origin, x - 1, y, z).Shape.IsFull;
            sideClosed[2] = Neighbor(world, data, origin, x, y + 1, z).Shape.IsFull;
            sideClosed[3] = Neighbor(world, data, origin, x, y - 1, z).Shape.IsFull;
            sideClosed[4] = Neighbor(world, data, origin, x, y, z + 1).Shape.IsFull;
            sideClosed[5] = Neighbor(world, data, origin, x, y, z - 1).Shape.IsFull;

            for (var i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                for (var face = 0; face < 6; face++)
                {
                    var onCellSide = face switch
                    {
                        0 => b.X1 == n, 1 => b.X0 == 0, 2 => b.Y1 == n, 3 => b.Y0 == 0, 4 => b.Z1 == n, _ => b.Z0 == 0,
                    };
                    if (onCellSide && sideClosed[face] || CoveredByOtherBox(boxes, i, face))
                        continue;
                    AddBoxFace(output, material, index, p, b, face);
                }
            }
        }

        /// <summary>Закрыта ли грань face коробки i вплотную прилегающей другой коробкой целиком.</summary>
        private static bool CoveredByOtherBox(ReadOnlySpan<FormBox> boxes, int i, int face)
        {
            var b = boxes[i];
            for (var j = 0; j < boxes.Length; j++)
            {
                if (j == i)
                    continue;
                var o = boxes[j];
                var covered = face switch
                {
                    0 => o.X0 == b.X1 && o.Y0 <= b.Y0 && o.Y1 >= b.Y1 && o.Z0 <= b.Z0 && o.Z1 >= b.Z1,
                    1 => o.X1 == b.X0 && o.Y0 <= b.Y0 && o.Y1 >= b.Y1 && o.Z0 <= b.Z0 && o.Z1 >= b.Z1,
                    2 => o.Y0 == b.Y1 && o.X0 <= b.X0 && o.X1 >= b.X1 && o.Z0 <= b.Z0 && o.Z1 >= b.Z1,
                    3 => o.Y1 == b.Y0 && o.X0 <= b.X0 && o.X1 >= b.X1 && o.Z0 <= b.Z0 && o.Z1 >= b.Z1,
                    4 => o.Z0 == b.Z1 && o.X0 <= b.X0 && o.X1 >= b.X1 && o.Y0 <= b.Y0 && o.Y1 >= b.Y1,
                    _ => o.Z1 == b.Z0 && o.X0 <= b.X0 && o.X1 >= b.X1 && o.Y0 <= b.Y0 && o.Y1 >= b.Y1,
                };
                if (covered)
                    return true;
            }
            return false;
        }

        private static void AddBoxFace(ChunkMeshData output, byte material, int index, Vector3 p, FormBox b, int face)
        {
            float x0 = b.X0 * Sixteenth, x1 = b.X1 * Sixteenth;
            float y0 = b.Y0 * Sixteenth, y1 = b.Y1 * Sixteenth;
            float z0 = b.Z0 * Sixteenth, z1 = b.Z1 * Sixteenth;
            Vector3 a, c, d, e, normal;
            switch (face)
            {
                case 0: a = new(x1, y0, z0); c = new(x1, y1, z0); d = new(x1, y1, z1); e = new(x1, y0, z1); normal = Vector3.right; break;
                case 1: a = new(x0, y0, z1); c = new(x0, y1, z1); d = new(x0, y1, z0); e = new(x0, y0, z0); normal = Vector3.left; break;
                case 2: a = new(x0, y1, z0); c = new(x0, y1, z1); d = new(x1, y1, z1); e = new(x1, y1, z0); normal = Vector3.up; break;
                case 3: a = new(x0, y0, z0); c = new(x1, y0, z0); d = new(x1, y0, z1); e = new(x0, y0, z1); normal = Vector3.down; break;
                case 4: a = new(x1, y0, z1); c = new(x1, y1, z1); d = new(x0, y1, z1); e = new(x0, y0, z1); normal = Vector3.forward; break;
                default: a = new(x0, y0, z0); c = new(x0, y1, z0); d = new(x1, y1, z0); e = new(x1, y0, z0); normal = Vector3.back; break;
            }
            output.AddQuad(material, index, p + a, p + c, p + d, p + e, normal,
                FaceUv(p, a, face), FaceUv(p, c, face), FaceUv(p, d, face), FaceUv(p, e, face));
        }

        /// <summary>UV в мировых единицах, как у остальных граней: верх/низ — XZ, стороны X — ZY, стороны Z — XY.</summary>
        private static Vector2 FaceUv(Vector3 p, Vector3 v, int face) => face switch
        {
            0 or 1 => new Vector2(p.z + v.z, p.y + v.y),
            2 or 3 => new Vector2(p.x + v.x, p.z + v.z),
            _ => new Vector2(p.x + v.x, p.y + v.y),
        };

        /// <summary>
        /// Низ блока — только под той частью верха, у которой есть объём: у внешнего угла ската половина
        /// верха лежит на полу ячейки, и низ под ней лёг бы на землю внизу своей текстурой.
        /// </summary>
        private static void AddBottom(ChunkMeshData output, byte material, int index, Vector3 p, BlockShape s)
        {
            var b00 = p;
            var b10 = p + new Vector3(1, 0, 0);
            var b01 = p + new Vector3(0, 0, 1);
            var b11 = p + new Vector3(1, 0, 1);
            var uv00 = new Vector2(p.x, p.z);
            var uv10 = new Vector2(p.x + 1, p.z);
            var uv01 = new Vector2(p.x, p.z + 1);
            var uv11 = new Vector2(p.x + 1, p.z + 1);

            if (s.C00 + s.C11 == s.C10 + s.C01)
            {
                output.AddQuad(material, index, b00, b10, b11, b01, Vector3.down, uv00, uv10, uv11, uv01);
            }
            else if (Mathf.Abs(s.C00 - s.C11) <= Mathf.Abs(s.C10 - s.C01))
            {
                if (s.C00 + s.C01 + s.C11 > 0)
                    output.AddTriangle(material, index, b00, b11, b01, Vector3.down, uv00, uv11, uv01);
                if (s.C00 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, b00, b10, b11, Vector3.down, uv00, uv10, uv11);
            }
            else
            {
                if (s.C01 + s.C11 + s.C10 > 0)
                    output.AddTriangle(material, index, b01, b10, b11, Vector3.down, uv01, uv10, uv11);
                if (s.C01 + s.C10 + s.C00 > 0)
                    output.AddTriangle(material, index, b01, b00, b10, Vector3.down, uv01, uv00, uv10);
            }
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
