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
            CornerTerrain.SnapToLayers(corners);
            CornerTerrain.Fill(world, corners, p.DirtDepth);
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

            return CornerTerrain.Quantize(blocks, p.HeightStep, size.y);
        }
    }
}
