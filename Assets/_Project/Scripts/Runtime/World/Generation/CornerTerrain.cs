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
        public static void Fill(VoxelWorld world, int[,] corners, int dirtDepth)
        {
            var size = world.SizeInBlocks;
            if (corners.GetLength(0) != size.x + 1 || corners.GetLength(1) != size.z + 1)
                throw new ArgumentException($"Нужна сетка углов {size.x + 1}×{size.z + 1}", nameof(corners));

            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                FillColumn(world, dirtDepth, x, z, corners[x, z], corners[x + 1, z], corners[x, z + 1], corners[x + 1, z + 1]);
        }

        /// <summary>
        /// Склон круче блока на блок в блоки не уложить. Такие места превращаем в чистые скалы: углы
        /// крутых столбцов сдвигаем на ближайший уровень террас (кратный levelSixteenths). Углы общие
        /// с соседями, поэтому сдвиг может сделать крутым соседа — повторяем, пока крутых столбцов
        /// с углами вне уровней не останется. Угол на уровне больше не двигается, так что проходы
        /// заканчиваются. Столбец между двумя уровнями потом станет ровным (<see cref="FillColumn"/>),
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
        /// (склон круче блока на блок), <see cref="FillColumn"/> сделает ровным, и там встанет стенка.
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
        /// Столбец под четырьмя углами (высоты в шестнадцатых от дна мира). Поверхность столбца — один блок
        /// слоя k, ниже — полные блоки. Столбец круче блока на блок становится ровным (см. ниже); если углы
        /// всё же пересекают границу слоя, слой берём по средней высоте, а углы обрезаем до него. Стопка из нескольких склонов друг на друге
        /// дала бы «пилу»: каждый блок поднимался бы на всю ширину и обрывался вниз.
        /// </summary>
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

        private static void FillColumn(VoxelWorld world, int dirtDepth, int x, int z, int c00, int c10, int c01, int c11)
        {
            const int h = BlockShape.MaxHeight;
            var average = (c00 + c10 + c01 + c11) / 4f;

            // Круче блока на блок склон в один блок не уместить: такой столбец делаем ровным на высоте
            // ближайшего к средней угла. Соседи по обе стороны ровные на своих уровнях, и между ними
            // встаёт чистая вертикальная стенка, а не уступ на каждом столбце.
            var min = Mathf.Min(Mathf.Min(c00, c10), Mathf.Min(c01, c11));
            var max = Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11));
            if (max - min > h)
            {
                var level = NearestTo(average, c00, c10, c01, c11);
                c00 = c10 = c01 = c11 = level;
                average = level;
            }

            // Слой, в котором лежит средняя высота; ровная поверхность ровно на границе — полный блок снизу.
            var topY = Mathf.Max(0, Mathf.FloorToInt((average - 0.5f) / h));
            var floor = topY * h;
            var surface = new BlockShape(c00 - floor, c10 - floor, c01 - floor, c11 - floor);

            for (var y = 0; y < topY; y++)
            {
                var depth = topY - y;
                var material = depth <= dirtDepth ? BlockMaterials.Dirt : BlockMaterials.Stone;
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(material));
            }
            world.SetBlock(new Vector3Int(x, topY, z), new Block(BlockMaterials.Grass, surface));

            // Мир мог быть заполнен раньше: всё выше поверхности — воздух.
            for (var y = topY + 1; y < world.SizeInBlocks.y; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Air);
        }
    }
}
