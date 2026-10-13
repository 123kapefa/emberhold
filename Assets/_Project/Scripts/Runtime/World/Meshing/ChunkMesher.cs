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
            var surface = world.Surface;

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

                // Земля нетронутого столбца рисуется гладкой поверхностью, а не гранями блоков: каждый блок —
                // своим слоем поверхности. Стенки к соседям ниже (тронутым или с выкопанным верхом) — тоже по слою на блок,
                // чтобы клик по стенке ямы попадал в свой блок.
                if (surface != null && surface.IsNatural(origin.x + x, origin.z + z))
                {
                    var global = origin + new Vector3Int(x, y, z);
                    if (global.y <= surface.TopY(global.x, global.z))
                    {
                        AddSurfaceLayer(output, surface, material, index, p, global);
                        AddSurfaceWalls(output, surface, material == BlockMaterials.Grass ? BlockMaterials.Dirt : material,
                            index, p, global);
                        continue;
                    }
                }

                if (s.IsCatalog)
                {
                    AddCatalogForm(output, material, index, p, s, world, data, origin, x, y, z);
                    continue;
                }

                // Верх закрыт, только если блок полный, а блок сверху закрывает весь пол своей ячейки
                // (у внешнего угла ската половина пола открыта — там видна земля снизу).
                if (!s.IsFull || !Neighbor(world, data, origin, x, y + 1, z).Shape.CoversFloor)
                {
                    var start = output.Vertices.Count;
                    AddTop(output, material, index, p, s, smoothNormals, (origin.y + y) * BlockShape.MaxHeight);
                    if (GrassEdge.TakesGrass(material))
                        AddGrassEdge(output, world, start, p, s, origin + new Vector3Int(x, y, z));
                }

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

            output.ApplyGrassTint(world.GrassTint, origin);
        }

        /// <summary>Вершина гладкой поверхности: место в ячейке (0..1), высота в шестнадцатых от дна мира, нормаль и каменность.</summary>
        private struct SurfacePoint
        {
            public float X, Z, Height, Rock;
            public Vector3 Normal;

            public static SurfacePoint Lerp(SurfacePoint a, SurfacePoint b, float t) => new()
            {
                X = Mathf.Lerp(a.X, b.X, t),
                Z = Mathf.Lerp(a.Z, b.Z, t),
                Height = Mathf.Lerp(a.Height, b.Height, t),
                Rock = Mathf.Lerp(a.Rock, b.Rock, t),
                Normal = Vector3.Lerp(a.Normal, b.Normal, t),
            };
        }

        /// <summary>
        /// Слой гладкой поверхности нетронутого столбца (<see cref="TerrainSurface"/>), который принадлежит блоку:
        /// часть двух треугольников поверхности с высотой в его слое (выше пола, до потолка включительно),
        /// с нормалями по соседним углам и весом камня в вершинах. Если верх столбца выкопан и поверхность
        /// выше него, верхний блок закрыт сверху ровной крышкой на своём потолке — своим материалом.
        /// </summary>
        private static void AddSurfaceLayer(ChunkMeshData output, TerrainSurface surface, byte material, int index,
            Vector3 p, Vector3Int block)
        {
            int gx = block.x, gz = block.z;
            SurfacePoint Corner(int dx, int dz) => new()
            {
                X = dx,
                Z = dz,
                Height = surface.Corner(gx + dx, gz + dz),
                Rock = surface.Rock(gx + dx, gz + dz),
                Normal = surface.Normal(gx + dx, gz + dz),
            };

            SurfacePoint c00 = Corner(0, 0), c10 = Corner(1, 0), c01 = Corner(0, 1), c11 = Corner(1, 1);
            var floor = block.y * BlockShape.MaxHeight;
            var isTop = block.y == surface.TopY(gx, gz);
            if (Mathf.Abs(c00.Height - c11.Height) <= Mathf.Abs(c10.Height - c01.Height))
            {
                AddSurfaceSlice(output, material, index, p, floor, isTop, c00, c01, c11);
                AddSurfaceSlice(output, material, index, p, floor, isTop, c00, c11, c10);
            }
            else
            {
                AddSurfaceSlice(output, material, index, p, floor, isTop, c01, c11, c10);
                AddSurfaceSlice(output, material, index, p, floor, isTop, c01, c10, c00);
            }
        }

        /// <summary>
        /// Часть треугольника поверхности в слое блока от пола floor (шестнадцатые) на блок вверх, а у верхнего
        /// блока столбца — ещё и ровная крышка там, где треугольник выше потолка.
        /// </summary>
        private static void AddSurfaceSlice(ChunkMeshData output, byte material, int index, Vector3 p, int floor, bool isTop,
            SurfacePoint a, SurfacePoint b, SurfacePoint c)
        {
            var ceiling = floor + BlockShape.MaxHeight;
            var face = Normal(SurfacePosition(p, floor, a), SurfacePosition(p, floor, b), SurfacePosition(p, floor, c));
            Span<SurfacePoint> triangle = stackalloc SurfacePoint[] { a, b, c };
            Span<SurfacePoint> clipped = stackalloc SurfacePoint[8];
            Span<SurfacePoint> layer = stackalloc SurfacePoint[8];

            var count = Clip(triangle, clipped, floor, true);
            count = Clip(clipped[..count], layer, ceiling, false);
            // Часть ровно на полу слоя — верх блока ниже, не наша.
            var above = false;
            for (var i = 0; i < count; i++)
                above |= layer[i].Height > floor + 1e-3f;
            if (above)
            {
                for (var i = 1; i + 1 < count; i++)
                    AddSurfaceTriangle(output, index, p, floor, face, layer[0], layer[i], layer[i + 1]);
            }

            if (!isTop)
                return;
            count = Clip(triangle, layer, ceiling, true);
            var higher = false;
            for (var i = 0; i < count; i++)
                higher |= layer[i].Height > ceiling + 1e-3f;
            if (!higher)
                return;
            var top = p + Vector3.up;
            for (var i = 1; i + 1 < count; i++)
            {
                SurfacePoint u = layer[0], v = layer[i], w = layer[i + 1];
                output.AddTriangle(material, index,
                    top + new Vector3(u.X, 0f, u.Z), top + new Vector3(v.X, 0f, v.Z), top + new Vector3(w.X, 0f, w.Z), Vector3.up,
                    new Vector2(p.x + u.X, p.z + u.Z), new Vector2(p.x + v.X, p.z + v.Z), new Vector2(p.x + w.X, p.z + w.Z));
            }
        }

        /// <summary>
        /// Отрезать от выпуклого многоугольника часть ниже (keepAbove) или выше высоты level (шестнадцатые).
        /// Обход точек сохраняется. Возвращает число точек в result (меньше трёх — ничего не осталось).
        /// </summary>
        private static int Clip(ReadOnlySpan<SurfacePoint> polygon, Span<SurfacePoint> result, float level, bool keepAbove)
        {
            var count = 0;
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                var da = keepAbove ? a.Height - level : level - a.Height;
                var db = keepAbove ? b.Height - level : level - b.Height;
                if (da >= 0f)
                    result[count++] = a;
                if (da > 0f && db < 0f || da < 0f && db > 0f)
                    result[count++] = SurfacePoint.Lerp(a, b, da / (da - db));
            }
            return count < 3 ? 0 : count;
        }

        private static Vector3 SurfacePosition(Vector3 p, int floor, SurfacePoint point) =>
            p + new Vector3(point.X, (point.Height - floor) * Sixteenth, point.Z);

        /// <summary>
        /// Стенки блока нетронутого столбца к соседям ниже него (стенки ям, подкопов и выкопанного верха соседа):
        /// в слое блока от верха соседа до края поверхности. У тронутого соседа верх — дно мира, у нетронутого —
        /// срезанный верх его земли. Край поверхности на ребре прямой, а стенка идёт по нему, обрезанному
        /// по слою и с изломом там, где он выходит за слой. Каждый слой принадлежит своему блоку.
        /// </summary>
        private static void AddSurfaceWalls(ChunkMeshData output, TerrainSurface surface, byte material, int index,
            Vector3 p, Vector3Int block)
        {
            int gx = block.x, gz = block.z;
            var floor = block.y * BlockShape.MaxHeight;

            void Wall(Side side, int nx, int nz, int ax, int az, int bx, int bz)
            {
                if (!surface.Contains(nx, nz))
                    return;
                // Ниже верха соседа стенку закрывает он сам.
                var below = surface.IsNatural(nx, nz) ? surface.Ceiling(nx, nz) : 0;
                if (below >= surface.Ceiling(gx, gz))
                    return;
                AddSurfaceWall(output, material, index, p, side, surface.Corner(ax, az) - floor, surface.Corner(bx, bz) - floor,
                    Mathf.Max(below - floor, 0));
            }

            Wall(Side.Right, gx + 1, gz, gx + 1, gz, gx + 1, gz + 1);
            Wall(Side.Left, gx - 1, gz, gx, gz, gx, gz + 1);
            Wall(Side.Forward, gx, gz + 1, gx, gz + 1, gx + 1, gz + 1);
            Wall(Side.Back, gx, gz - 1, gx, gz, gx + 1, gz);
        }

        /// <summary>
        /// Стенка в слое блока под краем поверхности, который на ребре идёт прямо от высоты a (t = 0) до b (t = 1)
        /// (шестнадцатые от пола слоя): от низа low до края, обрезанного потолком слоя.
        /// </summary>
        private static void AddSurfaceWall(ChunkMeshData output, byte material, int index, Vector3 p, Side side,
            float a, float b, float low)
        {
            const float top = BlockShape.MaxHeight;
            if (low >= top || a <= low && b <= low)
                return;

            float Edge(float t) => Mathf.Min(top, Mathf.Lerp(a, b, t));
            float Cross(float level) => (level - a) / (b - a);

            // Где край выше низа стенки, и где он выходит за потолок слоя.
            float from = 0f, to = 1f;
            if (a < low)
                from = Cross(low);
            else if (b < low)
                to = Cross(low);
            Span<float> stops = stackalloc float[3];
            var count = 0;
            stops[count++] = from;
            if (a < top != b < top)
            {
                var kink = Cross(top);
                if (kink > from && kink < to)
                    stops[count++] = kink;
            }
            stops[count++] = to;

            for (var i = 0; i + 1 < count; i++)
            {
                float t0 = stops[i], t1 = stops[i + 1];
                float h0 = Edge(t0), h1 = Edge(t1);
                if (h0 - low < 1e-4f)
                    AddSideTriangle(output, material, index, p, side, (t0, low), (t1, h1), (t1, low));
                else if (h1 - low < 1e-4f)
                    AddSideTriangle(output, material, index, p, side, (t0, low), (t0, h0), (t1, low));
                else
                    AddSideQuad(output, material, index, p, side, (t0, low), (t0, h0), (t1, h1), (t1, low));
            }
        }

        /// <summary>
        /// Треугольник гладкой земли в подмеше травы. Трава освещается мягко (нормали по соседним углам),
        /// камень — гранями: чем каменнее вершина, тем ближе её нормаль к нормали треугольника поверхности face.
        /// </summary>
        private static void AddSurfaceTriangle(ChunkMeshData output, int index, Vector3 p, int floor, Vector3 face,
            SurfacePoint a, SurfacePoint b, SurfacePoint c)
        {
            output.AddTriangle(BlockMaterials.Grass, index,
                SurfacePosition(p, floor, a), SurfacePosition(p, floor, b), SurfacePosition(p, floor, c),
                Vector3.Lerp(a.Normal.normalized, face, a.Rock).normalized,
                Vector3.Lerp(b.Normal.normalized, face, b.Rock).normalized,
                Vector3.Lerp(c.Normal.normalized, face, c.Rock).normalized,
                new Vector2(p.x + a.X, p.z + a.Z), new Vector2(p.x + b.X, p.z + b.Z), new Vector2(p.x + c.X, p.z + c.Z),
                a.Rock, b.Rock, c.Rock);
        }

        /// <summary>
        /// Сосед по локальной координате: внутри чанка — напрямую, снаружи — через мир.
        /// Ниже дна мира считаем сплошной камень, чтобы не рисовать невидимое дно.
        /// </summary>
        private static Block Neighbor(VoxelWorld world, ChunkData data, Vector3Int origin, int x, int y, int z)
        {
            var global = origin + new Vector3Int(x, y, z);
            var block = ChunkData.IsInside(x, y, z) ? data.Get(x, y, z)
                : global.y < 0 ? Block.Full(BlockMaterials.Stone) : world.GetBlock(global);
            // Неполные блоки нетронутой земли не видны: на их месте гладкая поверхность, которая может быть
            // ниже их углов. Грани соседей к ним рисуем, иначе у построек на земле остаются щели.
            var surface = world.Surface;
            if (surface != null && !block.IsAir && !block.Shape.IsFull && surface.IsNatural(global.x, global.z)
                && global.y <= surface.TopY(global.x, global.z))
                return Block.Air;
            return block;
        }

        /// <summary>
        /// Трава с соседних столбцов на верх блока земли или камня: маска соседей-травы (<see cref="GrassEdge"/>)
        /// в вершины верха, добавленные начиная с start.
        /// </summary>
        private static void AddGrassEdge(ChunkMeshData output, VoxelWorld world, int start, Vector3 p, BlockShape s,
            Vector3Int block)
        {
            var mask = GrassEdge.NeighbourMask(world, block, s);
            if (mask != 0)
                output.SetGrass(start, p, mask);
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

            foreach (var prism in BlockForms.Prisms(s.Form))
                AddPrism(output, material, index, p, prism, s.Rotation, sideClosed);
        }

        /// <summary>
        /// Призма из каталога: стенка на каждое ребро профиля и два торца веером из первой точки профиля.
        /// Соседние стенки с изломом меньше <see cref="SmoothAngle"/> получают общую нормаль в вершине —
        /// круглый столб и изгиб полуарки освещаются гладко. Грань на стороне ячейки не рисуется,
        /// если сторону закрывает полный блок соседа.
        /// </summary>
        private static void AddPrism(ChunkMeshData output, byte material, int index, Vector3 p, FormPrism prism,
            int rotation, ReadOnlySpan<bool> sideClosed)
        {
            var profile = prism.Profile;
            var count = profile.Length;
            var clockwise = prism.SignedArea < 0f;

            Span<Vector3> edgeNormals = stackalloc Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var d = profile[(i + 1) % count] - profile[i];
                var outward = clockwise ? new Vector2(-d.y, d.x) : new Vector2(d.y, -d.x);
                edgeNormals[i] = RotateVector(prism.Point(outward.normalized, 0f), rotation);
            }

            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var a0 = PrismPoint(p, prism, profile[i], prism.From, rotation);
                var a1 = PrismPoint(p, prism, profile[i], prism.To, rotation);
                var b0 = PrismPoint(p, prism, profile[next], prism.From, rotation);
                var b1 = PrismPoint(p, prism, profile[next], prism.To, rotation);
                var normal = edgeNormals[i];
                var side = CellSide(p, normal, a0, a1, b0);
                if (side >= 0 && sideClosed[side])
                    continue;
                var na = SmoothedNormal(normal, edgeNormals[(i + count - 1) % count]);
                var nb = SmoothedNormal(normal, edgeNormals[next]);
                AddFacingTriangle(output, material, index, normal, a0, a1, b1, na, na, nb);
                AddFacingTriangle(output, material, index, normal, a0, b1, b0, na, nb, nb);
            }

            var axis = RotateVector(prism.Point(Vector2.zero, 1f), rotation);
            AddPrismCap(output, material, index, p, prism, prism.From, -axis, rotation, sideClosed);
            AddPrismCap(output, material, index, p, prism, prism.To, axis, rotation, sideClosed);
        }

        private const float SmoothAngle = 30f;

        private static void AddPrismCap(ChunkMeshData output, byte material, int index, Vector3 p, FormPrism prism,
            int at, Vector3 normal, int rotation, ReadOnlySpan<bool> sideClosed)
        {
            var profile = prism.Profile;
            var first = PrismPoint(p, prism, profile[0], at, rotation);
            var side = CellSide(p, normal, first, PrismPoint(p, prism, profile[1], at, rotation),
                PrismPoint(p, prism, profile[2], at, rotation));
            if (side >= 0 && sideClosed[side])
                return;
            for (var i = 1; i < profile.Length - 1; i++)
            {
                var b = PrismPoint(p, prism, profile[i], at, rotation);
                var c = PrismPoint(p, prism, profile[i + 1], at, rotation);
                AddFacingTriangle(output, material, index, normal, first, b, c, normal, normal, normal);
            }
        }

        /// <summary>Точка призмы в координатах чанка: профиль → ячейка (шестнадцатые) → поворот → единицы блока.</summary>
        private static Vector3 PrismPoint(Vector3 p, FormPrism prism, Vector2 point, float at, int rotation)
        {
            const float n = BlockShape.MaxHeight;
            var v = prism.Point(point, at);
            for (var i = 0; i < (rotation & 3); i++)
                v = new Vector3(n - v.z, v.y, v.x);
            return p + v * Sixteenth;
        }

        /// <summary>Поворот направления на четверти против часовой стрелки (вид сверху), как у точек ячейки.</summary>
        private static Vector3 RotateVector(Vector3 v, int rotation)
        {
            for (var i = 0; i < (rotation & 3); i++)
                v = new Vector3(-v.z, v.y, v.x);
            return v;
        }

        /// <summary>
        /// Сторона ячейки (+X, −X, +Y, −Y, +Z, −Z → 0..5), на которой лежит плоская грань с нормалью normal
        /// и точками a, b, c; −1 — грань не на стороне ячейки.
        /// </summary>
        private static int CellSide(Vector3 p, Vector3 normal, Vector3 a, Vector3 b, Vector3 c)
        {
            const float e = 1e-4f;
            for (var axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(Mathf.Abs(normal[axis]) - 1f) > e)
                    continue;
                var plane = normal[axis] > 0f ? p[axis] + 1f : p[axis];
                if (Mathf.Abs(a[axis] - plane) > e || Mathf.Abs(b[axis] - plane) > e || Mathf.Abs(c[axis] - plane) > e)
                    return -1;
                return axis * 2 + (normal[axis] > 0f ? 0 : 1);
            }
            return -1;
        }

        /// <summary>Нормаль вершины на стыке двух стенок: общая при пологом изломе, своя при остром.</summary>
        private static Vector3 SmoothedNormal(Vector3 own, Vector3 neighbour) =>
            Vector3.Angle(own, neighbour) < SmoothAngle ? (own + neighbour).normalized : own;

        /// <summary>Треугольник лицом в сторону faceNormal: при обратном обходе вершины b и c меняются местами.</summary>
        private static void AddFacingTriangle(ChunkMeshData output, byte material, int index, Vector3 faceNormal,
            Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), faceNormal) < 0f)
            {
                (b, c) = (c, b);
                (nb, nc) = (nc, nb);
            }
            output.AddTriangle(material, index, a, b, c, na, nb, nc,
                SurfaceUv(a, faceNormal), SurfaceUv(b, faceNormal), SurfaceUv(c, faceNormal));
        }

        /// <summary>UV по главной оси нормали — как у граней коробок: верх/низ — XZ, стороны X — ZY, стороны Z — XY.</summary>
        private static Vector2 SurfaceUv(Vector3 v, Vector3 normal)
        {
            var ax = Mathf.Abs(normal.x);
            var ay = Mathf.Abs(normal.y);
            var az = Mathf.Abs(normal.z);
            if (ay >= ax && ay >= az)
                return new Vector2(v.x, v.z);
            return ax >= az ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y);
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
