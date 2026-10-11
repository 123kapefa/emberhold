using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Простая долина: вдоль оси Z петляет ровное дно, к краям по X поднимаются склоны, сверху — мягкий шум.
    /// Рельеф собирается в террасы: ровные площадки через <see cref="ValleyParameters.TerraceHeight"/> блоков,
    /// между ними склоны, а местами (шум «скалистости») — вертикальные скалы. Высота считается в углах
    /// сетки блоков и квантуется до <see cref="ValleyParameters.HeightStep"/> шестнадцатых; углы верхнего
    /// блока столбца берутся из соседних точек сетки, поэтому склоны гладкие, без ступенек.
    /// Поверхность — трава, под ней земля, глубже камень.
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
            var cliffOffset = new Vector2((float)random.NextDouble() * 10000f, (float)random.NextDouble() * 10000f);

            // Высоты в углах: точка (x, z) — общий угол четырёх столбцов вокруг неё.
            var corners = new int[size.x + 1, size.z + 1];
            var cliffs = new bool[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
            {
                cliffs[x, z] = IsCliff(p, x, z, cliffOffset);
                corners[x, z] = CornerHeight(p, size, x, z, noiseOffset, meanderPhase, cliffs[x, z]);
            }
            // Вне скал крутые места становятся проходимыми склонами, в скалах — чистыми стенами.
            CornerTerrain.LimitSlopes(corners, cliffs);
            CornerTerrain.SnapSteepToLevels(corners, Mathf.RoundToInt(Mathf.Max(p.TerraceHeight, 1f) * BlockShape.MaxHeight), cliffs);
            CornerTerrain.RoundCaps(corners, Mathf.RoundToInt(p.CapDepth * BlockShape.MaxHeight), p.CapWidth, p.HeightStep);
            // Сдвиги на уровни и купола могли сделать крутыми соседние склоны — возвращаем их к 45°.
            CornerTerrain.LimitSlopes(corners, cliffs);
            CornerTerrain.SnapToLayers(corners);
            CornerTerrain.Fill(world, corners, p.DirtDepth, p.CliffEdgeRounding, p.CliffFootRounding);
        }

        /// <summary>Высота поверхности в углу сетки в шестнадцатых блока, уже квантованная и в пределах мира.</summary>
        /// <summary>Разрешена ли в этом углу скала: области по шуму «скалистости», доля — CliffAmount.</summary>
        private static bool IsCliff(ValleyParameters p, int x, int z, Vector2 cliffOffset)
        {
            var cliffScale = Mathf.Max(p.CliffNoiseScale, 1f);
            return Mathf.PerlinNoise(cliffOffset.x + x / cliffScale, cliffOffset.y + z / cliffScale) < p.CliffAmount;
        }

        private static int CornerHeight(ValleyParameters p, Vector3Int size, int x, int z, Vector2 noiseOffset, float meanderPhase,
            bool cliff)
        {
            var u = (float)x / size.x;
            var v = (float)z / size.z;

            var center = 0.5f + p.MeanderAmplitude * Mathf.Sin(v * p.MeanderWaves * Mathf.PI * 2f + meanderPhase);
            var distance = Mathf.Abs(u - center);
            var slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.FloorHalfWidth, 0.5f, distance));

            var noiseScale = Mathf.Max(p.NoiseScale, 1f);
            var noise = Mathf.PerlinNoise(noiseOffset.x + x / noiseScale, noiseOffset.y + z / noiseScale) * 2f - 1f;
            var blocks = Mathf.Lerp(p.FloorHeight, p.RimHeight, slope) + noise * p.NoiseAmplitude;

            blocks = Terrace(blocks, p.TerraceHeight, p.TerraceSlope, cliff);

            return CornerTerrain.Quantize(blocks, p.HeightStep, size.y);
        }

        /// <summary>
        /// Террасы: высота собирается в ровные уровни через levelHeight блоков. Верхняя доля slopeShare
        /// каждого перепада — плавный склон (S-кривая) к следующему уровню, остальное — ровная площадка.
        /// На скале переход — ступенька посередине перепада: столбец на ней круче блока на блок,
        /// его выровняет <see cref="CornerTerrain"/>, и встанет вертикальная стенка.
        /// </summary>
        public static float Terrace(float blocks, float levelHeight, float slopeShare, bool cliff)
        {
            if (levelHeight <= 0f)
                return blocks;
            var t = blocks / levelHeight;
            var level = Mathf.Floor(t);
            var fraction = t - level;
            float shaped;
            if (cliff)
                shaped = fraction < 0.5f ? 0f : 1f;
            else
            {
                var share = Mathf.Clamp(slopeShare, 0.05f, 1f);
                // Почти прямой склон со слегка скруглёнными концами: у S-кривой середина в полтора раза
                // круче, и на крутых бортах склоны превращались бы в скалы — уровни не соединялись бы подъёмами.
                var ramp = Mathf.InverseLerp(1f - share, 1f, fraction);
                shaped = Mathf.Lerp(ramp, Mathf.SmoothStep(0f, 1f, ramp), 0.3f);
            }
            return (level + shaped) * levelHeight;
        }
    }
}
