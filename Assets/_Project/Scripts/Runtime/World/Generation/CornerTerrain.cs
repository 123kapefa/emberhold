using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Рельеф по высотам в углах сетки блоков (в шестнадцатых от дна мира). Точка (x, z) — общий угол
    /// четырёх столбцов вокруг неё, поэтому соседние столбцы стыкуются без щелей. Общий код для
    /// генератора долины и песочницы склонов.
    /// </summary>
    public static class CornerTerrain
    {
        /// <summary>Высота в блоках → шестнадцатые, кратные step, от одного шага до верха мира.</summary>
        public static int Quantize(float blocks, int step, int worldHeightInBlocks)
        {
            step = Mathf.Clamp(step, 1, BlockShape.MaxHeight);
            var sixteenths = Mathf.RoundToInt(blocks * BlockShape.MaxHeight / step) * step;
            return Mathf.Clamp(sixteenths, step, worldHeightInBlocks * BlockShape.MaxHeight);
        }

        /// <summary>
        /// Заполнить мир столбцами под углами: corners размером (ширина + 1) × (длина + 1) мира.
        /// Изменённые чанки помечаются для перестройки.
        /// </summary>
        /// <param name="edgeRounding">
        /// На сколько шестнадцатых опустить верхнюю кромку над обрывом (скругление края скалы или впадины).
        /// </param>
        /// <param name="footRounding">На сколько шестнадцатых поднять землю у подножия обрыва.</param>
        public static void Fill(VoxelWorld world, int[,] corners, int dirtDepth, int edgeRounding = 0, int footRounding = 0)
        {
            var size = world.SizeInBlocks;
            if (corners.GetLength(0) != size.x + 1 || corners.GetLength(1) != size.z + 1)
                throw new ArgumentException($"Нужна сетка углов {size.x + 1}×{size.z + 1}", nameof(corners));

            // Углы каждого столбца отдельно: на обрыве у соседей они уже разные.
            var columns = new int[size.x, size.z, 4];
            var topY = new int[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                topY[x, z] = ShapeColumn(corners[x, z], corners[x + 1, z], corners[x, z + 1], corners[x + 1, z + 1],
                    columns, x, z);

            if (edgeRounding > 0 || footRounding > 0)
                RoundCliffEdges(columns, edgeRounding, footRounding);

            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                WriteColumn(world, dirtDepth, x, z, topY[x, z], columns);
        }

        // Углы столбца в массиве: 0 — C00, 1 — C10, 2 — C01, 3 — C11.
        // Стороны: пары углов на ребре и соответствующие углы соседа за ребром.
        private static readonly (int dx, int dz, int a0, int a1, int b0, int b1)[] Sides =
        {
            (1, 0, 1, 3, 0, 2),   // +X: наши C10, C11 — его C00, C01
            (-1, 0, 0, 2, 1, 3),  // −X
            (0, 1, 2, 3, 0, 1),   // +Z: наши C01, C11 — его C00, C10
            (0, -1, 0, 1, 2, 3),  // −Z
        };

        /// <summary>
        /// Скругление обрывов: если столбец выше соседа за ребром хотя бы на блок, его углы на этом ребре
        /// опускаются (кромка скалы или впадины скашивается), а углы соседа у подножия чуть поднимаются.
        /// Угол на двух обрывах сразу (внешний угол скалы) опускается сильнее, чтобы ступенчатый
        /// в плане край читался округлым.
        /// </summary>
        private static void RoundCliffEdges(int[,,] columns, int edgeRounding, int footRounding)
        {
            var sizeX = columns.GetLength(0);
            var sizeZ = columns.GetLength(1);
            var cliffEdges = new int[sizeX, sizeZ, 4];
            var raise = new int[sizeX, sizeZ, 4];

            for (var z = 0; z < sizeZ; z++)
            for (var x = 0; x < sizeX; x++)
            foreach (var (dx, dz, a0, a1, b0, b1) in Sides)
            {
                var nx = x + dx;
                var nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                    continue;
                var ours = Mathf.Min(columns[x, z, a0], columns[x, z, a1]);
                var theirs = Mathf.Max(columns[nx, nz, b0], columns[nx, nz, b1]);
                if (ours - theirs < BlockShape.MaxHeight)
                    continue;
                cliffEdges[x, z, a0]++;
                cliffEdges[x, z, a1]++;
                raise[nx, nz, b0] = footRounding;
                raise[nx, nz, b1] = footRounding;
            }

            for (var z = 0; z < sizeZ; z++)
            for (var x = 0; x < sizeX; x++)
            for (var i = 0; i < 4; i++)
            {
                var edges = cliffEdges[x, z, i];
                var lower = edges == 0 ? 0 : edges == 1 ? edgeRounding : edgeRounding * 3 / 2;
                columns[x, z, i] += raise[x, z, i] - lower;
            }
        }

        /// <summary>
        /// Склон круче блока на блок в блоки не уложить. Такие места превращаем в чистые скалы: углы
        /// крутых столбцов сдвигаем на ближайший уровень террас (кратный levelSixteenths). Углы общие
        /// с соседями, поэтому сдвиг может сделать крутым соседа — повторяем, пока крутых столбцов
        /// с углами вне уровней не останется. Угол на уровне больше не двигается, так что проходы
        /// заканчиваются. Столбец между двумя уровнями потом станет ровным (<see cref="ShapeColumn"/>),
        /// и между уровнями встанет вертикальная стенка по линии уровня, без зубцов от склонов рядом.
        /// </summary>
        public static void SnapSteepToLevels(int[,] corners, int levelSixteenths)
        {
            if (levelSixteenths <= 0)
                return;
            var sizeX = corners.GetLength(0) - 1;
            var sizeZ = corners.GetLength(1) - 1;
            for (var pass = 0; pass < 64; pass++)
            {
                var changed = false;
                for (var z = 0; z < sizeZ; z++)
                for (var x = 0; x < sizeX; x++)
                {
                    var (min, max) = Span(corners, x, z);
                    if (max - min <= BlockShape.MaxHeight)
                        continue;
                    for (var i = 0; i < 4; i++)
                    {
                        var (cx, cz) = (x + (i & 1), z + (i >> 1));
                        var value = corners[cx, cz];
                        var snapped = Mathf.RoundToInt((float)value / levelSixteenths) * levelSixteenths;
                        // Ниже первого уровня не опускаем: у столбца должен остаться хотя бы один блок.
                        if (snapped == value || snapped <= 0)
                            continue;
                        corners[cx, cz] = snapped;
                        changed = true;
                    }
                }
                if (!changed)
                    return;
            }
        }

        /// <summary>
        /// Склон умещается только внутри одного блока. Если углы столбца лежат по разные стороны границы
        /// слоя (кратной 16), сдвигаем на границу углы одной стороны: той, что ближе, а если так соседний
        /// столбец станет круче блока на блок — другой. Углы общие у соседних столбцов, поэтому рельеф
        /// остаётся непрерывным, просто переход между слоями проходит ровно по границе. Угол на границе
        /// больше не двигается, так что проходы быстро заканчиваются. Что исправить не удалось
        /// (склон круче блока на блок), <see cref="ShapeColumn"/> сделает ровным, и там встанет стенка.
        /// </summary>
        public static void SnapToLayers(int[,] corners)
        {
            const int h = BlockShape.MaxHeight;
            var sizeX = corners.GetLength(0) - 1;
            var sizeZ = corners.GetLength(1) - 1;
            for (var pass = 0; pass < 16; pass++)
            {
                var changed = false;
                for (var z = 0; z < sizeZ; z++)
                for (var x = 0; x < sizeX; x++)
                {
                    var (min, max) = Span(corners, x, z);
                    var boundary = (min / h + 1) * h;
                    if (boundary >= max || max - min > h)
                        continue;

                    var raiseFirst = boundary - min <= max - boundary;
                    if (TrySnap(corners, x, z, boundary, raiseFirst) || TrySnap(corners, x, z, boundary, !raiseFirst))
                        changed = true;
                }
                if (!changed)
                    return;
            }
        }

        /// <summary>Сдвинуть на границу углы столбца ниже (raise) или выше неё, если ни один столбец вокруг не станет круче блока.</summary>
        private static bool TrySnap(int[,] corners, int x, int z, int boundary, bool raise)
        {
            Span<int> saved = stackalloc int[4];
            for (var i = 0; i < 4; i++)
            {
                var (cx, cz) = (x + (i & 1), z + (i >> 1));
                saved[i] = corners[cx, cz];
                if (raise ? saved[i] < boundary : saved[i] > boundary)
                    corners[cx, cz] = boundary;
            }

            // Затронуты столбцы вокруг четырёх углов: от (x−1, z−1) до (x+1, z+1).
            var sizeX = corners.GetLength(0) - 1;
            var sizeZ = corners.GetLength(1) - 1;
            for (var nz = Mathf.Max(0, z - 1); nz <= Mathf.Min(sizeZ - 1, z + 1); nz++)
            for (var nx = Mathf.Max(0, x - 1); nx <= Mathf.Min(sizeX - 1, x + 1); nx++)
            {
                var (min, max) = Span(corners, nx, nz);
                if (max - min <= BlockShape.MaxHeight)
                    continue;
                for (var i = 0; i < 4; i++)
                    corners[x + (i & 1), z + (i >> 1)] = saved[i];
                return false;
            }
            return true;
        }

        private static (int min, int max) Span(int[,] corners, int x, int z)
        {
            int a = corners[x, z], b = corners[x + 1, z], c = corners[x, z + 1], d = corners[x + 1, z + 1];
            return (Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)), Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)));
        }

        /// <summary>
        /// Форма столбца под четырьмя углами (высоты в шестнадцатых от дна мира): поверхность — один блок
        /// слоя topY, ниже — полные блоки. Столбец круче блока на блок становится ровным на высоте
        /// ближайшего к средней угла: соседи по обе стороны ровные на своих уровнях, и между ними встаёт
        /// чистая вертикальная стенка, а не уступ на каждом столбце. Возвращает topY.
        /// </summary>
        private static int ShapeColumn(int c00, int c10, int c01, int c11, int[,,] columns, int x, int z)
        {
            const int h = BlockShape.MaxHeight;
            var average = (c00 + c10 + c01 + c11) / 4f;
            var min = Mathf.Min(Mathf.Min(c00, c10), Mathf.Min(c01, c11));
            var max = Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11));
            if (max - min > h)
            {
                var level = NearestTo(average, c00, c10, c01, c11);
                c00 = c10 = c01 = c11 = level;
                average = level;
            }

            columns[x, z, 0] = c00;
            columns[x, z, 1] = c10;
            columns[x, z, 2] = c01;
            columns[x, z, 3] = c11;
            // Слой, в котором лежит средняя высота; ровная поверхность ровно на границе — полный блок снизу.
            return Mathf.Max(0, Mathf.FloorToInt((average - 0.5f) / h));
        }

        /// <summary>Значение, ближайшее к target; при равенстве — большее (стенка остаётся за верхним уровнем).</summary>
        private static int NearestTo(float target, params int[] values)
        {
            var best = values[0];
            foreach (var value in values)
            {
                var d = Mathf.Abs(value - target);
                var bestD = Mathf.Abs(best - target);
                if (d < bestD || Mathf.Approximately(d, bestD) && value > best)
                    best = value;
            }
            return best;
        }

        /// <summary>Записать столбец: углы обрезаются до блока поверхности (если углы вышли за слой — уступ).</summary>
        private static void WriteColumn(VoxelWorld world, int dirtDepth, int x, int z, int topY, int[,,] columns)
        {
            // Подъём у подножия мог вылезти над полным блоком: тогда поверхность — в слое выше.
            var min = Mathf.Min(Mathf.Min(columns[x, z, 0], columns[x, z, 1]), Mathf.Min(columns[x, z, 2], columns[x, z, 3]));
            var max = Mathf.Max(Mathf.Max(columns[x, z, 0], columns[x, z, 1]), Mathf.Max(columns[x, z, 2], columns[x, z, 3]));
            if (max > (topY + 1) * BlockShape.MaxHeight && min >= (topY + 1) * BlockShape.MaxHeight)
                topY++;
            var floor = topY * BlockShape.MaxHeight;
            var surface = new BlockShape(columns[x, z, 0] - floor, columns[x, z, 1] - floor,
                columns[x, z, 2] - floor, columns[x, z, 3] - floor);

            for (var y = 0; y < topY; y++)
            {
                var depth = topY - y;
                var material = depth <= dirtDepth ? BlockMaterials.Dirt : BlockMaterials.Stone;
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(material));
            }
            // Скругление может опустить углы до пола слоя; совсем пустым блок поверхности не оставляем.
            world.SetBlock(new Vector3Int(x, topY, z),
                new Block(BlockMaterials.Grass, surface.IsEmpty ? BlockShape.Flat(1) : surface));

            // Мир мог быть заполнен раньше: всё выше поверхности — воздух.
            for (var y = topY + 1; y < world.SizeInBlocks.y; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Air);
        }
    }
}
