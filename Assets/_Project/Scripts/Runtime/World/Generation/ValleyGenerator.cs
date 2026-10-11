using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Простая долина: вдоль оси Z петляет ровное дно, к краям по X поднимаются склоны, сверху — мягкий шум.
    /// Поверхность квантуется до <see cref="ValleyParameters.HeightStep"/> шестнадцатых, верхний блок столбца
    /// получается неполным. Сверху трава, под ней земля, глубже камень.
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

            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var height = SurfaceHeight(p, size, x, z, noiseOffset, meanderPhase);
                FillColumn(world, p, x, z, height);
            }
        }

        /// <summary>Высота поверхности столбца в шестнадцатых блока, уже квантованная и в пределах мира.</summary>
        private static int SurfaceHeight(ValleyParameters p, Vector3Int size, int x, int z, Vector2 noiseOffset, float meanderPhase)
        {
            var u = (x + 0.5f) / size.x;
            var v = (z + 0.5f) / size.z;

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

        private static void FillColumn(VoxelWorld world, ValleyParameters p, int x, int z, int heightSixteenths)
        {
            var fullBlocks = heightSixteenths / BlockShape.MaxHeight;
            var remainder = heightSixteenths % BlockShape.MaxHeight;
            // Самый верхний непустой блок столбца — трава, даже если он неполный.
            var surfaceY = remainder > 0 ? fullBlocks : fullBlocks - 1;

            for (var y = 0; y <= surfaceY; y++)
            {
                var depth = surfaceY - y;
                var material = depth == 0 ? BlockMaterials.Grass
                    : depth <= p.DirtDepth ? BlockMaterials.Dirt
                    : BlockMaterials.Stone;
                var block = y < fullBlocks ? Block.Full(material) : Block.Flat(material, remainder);
                world.SetBlock(new Vector3Int(x, y, z), block);
            }

            // Мир мог быть заполнен раньше: всё выше поверхности — воздух.
            for (var y = surfaceY + 1; y < world.SizeInBlocks.y; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Air);
        }
    }
}
