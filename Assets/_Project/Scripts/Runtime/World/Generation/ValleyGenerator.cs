using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Простая долина: вдоль оси Z петляет ровное дно, к краям по X поднимаются склоны, сверху — мягкий шум.
    /// Высота считается в углах сетки блоков и квантуется до <see cref="ValleyParameters.HeightStep"/>
    /// шестнадцатых; углы верхнего блока столбца берутся из соседних точек сетки, поэтому рельеф
    /// получается склонами, а не ступеньками. Поверхность — трава, под ней земля, глубже камень.
    /// </summary>
    public static class ValleyGenerator
    {
        public static VoxelWorld Generate(ValleyParameters p)
        {
            var world = new VoxelWorld(p.SizeInChunks);
            Fill(world, p);
            return world;
        }

        /// <summary>Заполнить существующий мир; изменённые чанки помечаются для перестройки.</summary>
        public static void Fill(VoxelWorld world, ValleyParameters p)
        {
            var size = world.SizeInBlocks;
            // Смещения шума и фаза изгибов зависят только от seed.
            var random = new System.Random(p.Seed);
            var noiseOffset = new Vector2((float)random.NextDouble() * 10000f, (float)random.NextDouble() * 10000f);
            var meanderPhase = (float)(random.NextDouble() * Math.PI * 2);

            // Высоты в углах: точка (x, z) — общий угол четырёх столбцов вокруг неё.
            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
                corners[x, z] = CornerHeight(p, size, x, z, noiseOffset, meanderPhase);
            SnapToLayers(corners);

            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                FillColumn(world, p, x, z, corners[x, z], corners[x + 1, z], corners[x, z + 1], corners[x + 1, z + 1]);
        }

        /// <summary>Высота поверхности в углу сетки в шестнадцатых блока, уже квантованная и в пределах мира.</summary>
        private static int CornerHeight(ValleyParameters p, Vector3Int size, int x, int z, Vector2 noiseOffset, float meanderPhase)
        {
            var u = (float)x / size.x;
            var v = (float)z / size.z;

            var center = 0.5f + p.MeanderAmplitude * Mathf.Sin(v * p.MeanderWaves * Mathf.PI * 2f + meanderPhase);
            var distance = Mathf.Abs(u - center);
            var slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.FloorHalfWidth, 0.5f, distance));

            var noiseScale = Mathf.Max(p.NoiseScale, 1f);
            var noise = Mathf.PerlinNoise(noiseOffset.x + x / noiseScale, noiseOffset.y + z / noiseScale) * 2f - 1f;
            var blocks = Mathf.Lerp(p.FloorHeight, p.RimHeight, slope) + noise * p.NoiseAmplitude;

            var step = Mathf.Clamp(p.HeightStep, 1, BlockShape.MaxHeight);
            var sixteenths = Mathf.RoundToInt(blocks * BlockShape.MaxHeight / step) * step;
            // Хотя бы один шаг над дном и не выше мира.
            return Mathf.Clamp(sixteenths, step, size.y * BlockShape.MaxHeight);
        }

        /// <summary>
        /// Склон умещается только внутри одного блока. Если углы столбца лежат по разные стороны границы
        /// слоя (кратной 16), сдвигаем на границу углы одной стороны: той, что ближе, а если так соседний
        /// столбец станет круче блока на блок — другой. Углы общие у соседних столбцов, поэтому рельеф
        /// остаётся непрерывным, просто переход между слоями проходит ровно по границе. Угол на границе
        /// больше не двигается, так что проходы быстро заканчиваются. Что исправить не удалось
        /// (склон круче блока на блок), обрежет <see cref="FillColumn"/> небольшим уступом.
        /// </summary>
        private static void SnapToLayers(int[,] corners)
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
        /// слоя k, ниже — полные блоки. Если углы всё же пересекают границу слоя (столбец круче блока
        /// на блок), слой берём по средней высоте, а углы обрезаем до него: на стыке с соседом получается уступ. Стопка из нескольких склонов друг на друге
        /// дала бы «пилу»: каждый блок поднимался бы на всю ширину и обрывался вниз.
        /// </summary>
        private static void FillColumn(VoxelWorld world, ValleyParameters p, int x, int z, int c00, int c10, int c01, int c11)
        {
            const int h = BlockShape.MaxHeight;
            var average = (c00 + c10 + c01 + c11) / 4f;
            // Слой, в котором лежит средняя высота; ровная поверхность ровно на границе — полный блок снизу.
            var topY = Mathf.Max(0, Mathf.FloorToInt((average - 0.5f) / h));
            var floor = topY * h;
            var surface = new BlockShape(c00 - floor, c10 - floor, c01 - floor, c11 - floor);

            for (var y = 0; y < topY; y++)
            {
                var depth = topY - y;
                var material = depth <= p.DirtDepth ? BlockMaterials.Dirt : BlockMaterials.Stone;
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(material));
            }
            world.SetBlock(new Vector3Int(x, topY, z), new Block(BlockMaterials.Grass, surface));

            // Мир мог быть заполнен раньше: всё выше поверхности — воздух.
            for (var y = topY + 1; y < world.SizeInBlocks.y; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Air);
        }
    }
}
