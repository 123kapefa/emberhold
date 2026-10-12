using System;
using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Рельеф по профилю местности (<see cref="TerrainProfileParameters"/>): форма профиля (ровная местность,
    /// долина, гора, озеро) на базовой высоте, сверху мягкий шум. Рельеф собирается в террасы: ровные площадки
    /// через <see cref="TerrainProfileParameters.TerraceHeight"/> блоков, между ними склоны, а местами
    /// (шум «скалистости») — вертикальные скалы. Высота считается в углах сетки блоков и квантуется до
    /// <see cref="TerrainProfileParameters.HeightStep"/> шестнадцатых; углы верхнего блока столбца берутся
    /// из соседних точек сетки, поэтому склоны гладкие, без ступенек. Материалы столбца — из профиля.
    /// Разные места карты могут брать разные профили: зоны (<see cref="TerrainZoneArea"/>) поверх фонового
    /// профиля, высоты на стыках смешиваются по весам.
    /// Генератор только пишет блоки через <see cref="VoxelWorld"/> и не знает, как хранятся чанки.
    /// </summary>
    public static class TerrainGenerator
    {
        public static VoxelWorld Generate(TerrainProfileParameters profile, int seed, Vector3Int sizeInChunks,
            IReadOnlyList<TerrainZoneArea> zones = null)
        {
            var world = new VoxelWorld(sizeInChunks);
            Fill(world, profile, seed, zones);
            return world;
        }

        /// <summary>
        /// Заполнить существующий мир; изменённые чанки помечаются для перестройки. p — фоновый профиль,
        /// zones — зоны со своими профилями (<see cref="TerrainZoneArea"/>). Высота в каждом углу —
        /// смесь высот профилей по весам (<see cref="ProfileWeights"/>), скала — если за неё больше
        /// половины веса. Шаг высоты, уровень скал, материалы и дно берутся у профиля с наибольшим весом.
        /// Купола на шапках и скругление обрывов — у фонового профиля. Без зон рельеф тот же, что был до зон.
        /// </summary>
        public static void Fill(VoxelWorld world, TerrainProfileParameters p, int seed,
            IReadOnlyList<TerrainZoneArea> zones = null)
        {
            var size = world.SizeInBlocks;
            // Смещения шума и фаза изгибов зависят только от seed. Порядок чисел не менять:
            // от него зависит, что долина по одному seed остаётся той же.
            var random = new System.Random(seed);
            var noiseOffset = new Vector2((float)random.NextDouble() * 10000f, (float)random.NextDouble() * 10000f);
            var meanderPhase = (float)(random.NextDouble() * Math.PI * 2);
            var cliffOffset = new Vector2((float)random.NextDouble() * 10000f, (float)random.NextDouble() * 10000f);

            // Профиль 0 — фон, профиль i + 1 — зона i.
            var zoneCount = zones?.Count ?? 0;
            var profiles = new TerrainProfileParameters[zoneCount + 1];
            profiles[0] = p;
            for (var i = 0; i < zoneCount; i++)
                profiles[i + 1] = zones[i].Profile;
            for (var i = 0; i < profiles.Length; i++)
                profiles[i].Materials.Validate();
            var weights = new float[profiles.Length];

            // Высоты в углах: точка (x, z) — общий угол четырёх столбцов вокруг неё.
            var corners = new int[size.x + 1, size.z + 1];
            var cliffs = new bool[size.x + 1, size.z + 1];
            var levels = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
            {
                var point = new Vector2(x, z);
                ProfileWeights(zones, point, weights);
                var blocks = 0f;
                var cliffWeight = 0f;
                var dominant = 0;
                for (var i = 0; i < profiles.Length; i++)
                {
                    var w = weights[i];
                    if (w <= 0f)
                        continue;
                    var uv = i == 0 ? new Vector2((float)x / size.x, (float)z / size.z) : zones[i - 1].ToProfileUV(point);
                    var cliff = IsCliff(profiles[i], x, z, cliffOffset);
                    blocks += w * ProfileHeight(profiles[i], uv, x, z, noiseOffset, meanderPhase, cliff);
                    if (cliff)
                        cliffWeight += w;
                    if (w > weights[dominant])
                        dominant = i;
                }
                cliffs[x, z] = cliffWeight >= 0.5f;
                corners[x, z] = CornerTerrain.Quantize(blocks, profiles[dominant].HeightStep, size.y);
                levels[x, z] = Mathf.RoundToInt(Mathf.Max(profiles[dominant].TerraceHeight, 1f) * BlockShape.MaxHeight);
            }
            // Вне скал крутые места становятся проходимыми склонами, в скалах — чистыми стенами.
            CornerTerrain.LimitSlopes(corners, cliffs);
            CornerTerrain.SnapSteepToLevels(corners, levels, cliffs);
            CornerTerrain.RoundCaps(corners, Mathf.RoundToInt(p.CapDepth * BlockShape.MaxHeight), p.CapWidth, p.HeightStep);
            // Сдвиги на уровни и купола могли сделать крутыми соседние склоны — возвращаем их к 45°.
            CornerTerrain.LimitSlopes(corners, cliffs);
            CornerTerrain.SnapToLayers(corners);

            // Материалы и дно столбца — у профиля, который весит больше всех в середине столбца.
            var materials = new TerrainMaterials[size.x, size.z];
            var bedLevels = new int[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                ProfileWeights(zones, new Vector2(x + 0.5f, z + 0.5f), weights);
                var dominant = 0;
                for (var i = 1; i < weights.Length; i++)
                    if (weights[i] > weights[dominant])
                        dominant = i;
                materials[x, z] = profiles[dominant].Materials;
                bedLevels[x, z] = Mathf.RoundToInt(profiles[dominant].WaterLevel * BlockShape.MaxHeight);
            }
            CornerTerrain.Fill(world, corners, materials, bedLevels, p.CliffEdgeRounding, p.CliffFootRounding);
        }

        /// <summary>
        /// Веса профилей в точке карты (в блоках сетки): weights[0] — фон, weights[i + 1] — зона i, сумма 1.
        /// Пока зоны вместе весят меньше 1, остаток достаётся фону; где больше — зоны делят вес
        /// пропорционально, и фона там нет. weights должен вмещать зоны + 1.
        /// </summary>
        public static void ProfileWeights(IReadOnlyList<TerrainZoneArea> zones, Vector2 point, float[] weights)
        {
            var zoneCount = zones?.Count ?? 0;
            var total = 0f;
            for (var i = 0; i < zoneCount; i++)
            {
                weights[i + 1] = zones[i].Weight(point);
                total += weights[i + 1];
            }
            if (total <= 1f)
            {
                weights[0] = 1f - total;
                return;
            }
            weights[0] = 0f;
            for (var i = 1; i <= zoneCount; i++)
                weights[i] /= total;
        }

        /// <summary>Разрешена ли в этом углу скала: области по шуму «скалистости», доля — CliffAmount.</summary>
        private static bool IsCliff(TerrainProfileParameters p, int x, int z, Vector2 cliffOffset)
        {
            var cliffScale = Mathf.Max(p.CliffNoiseScale, 1f);
            return Mathf.PerlinNoise(cliffOffset.x + x / cliffScale, cliffOffset.y + z / cliffScale) < p.CliffAmount;
        }

        /// <summary>
        /// Высота профиля в углу сетки в блоках, с шумом и террасами, ещё не квантованная.
        /// uv — доли области, по которой профиль строит форму: всей карты у фона, рамки зоны у зоны.
        /// </summary>
        private static float ProfileHeight(TerrainProfileParameters p, Vector2 uv, int x, int z, Vector2 noiseOffset,
            float meanderPhase, bool cliff)
        {
            var noiseScale = Mathf.Max(p.NoiseScale, 1f);
            var noise = Mathf.PerlinNoise(noiseOffset.x + x / noiseScale, noiseOffset.y + z / noiseScale) * 2f - 1f;
            var blocks = ShapeHeight(p, uv.x, uv.y, meanderPhase) + noise * p.NoiseAmplitude;

            return Terrace(blocks, p.TerraceHeight, p.TerraceSlope, cliff);
        }

        /// <summary>Высота формы профиля в блоках без шума; u, v — доли карты (у зоны — её рамки) по X и Z.</summary>
        public static float ShapeHeight(TerrainProfileParameters p, float u, float v, float meanderPhase = 0f)
        {
            switch (p.Shape)
            {
                case TerrainShape.Valley:
                {
                    var center = 0.5f + p.MeanderAmplitude * Mathf.Sin(v * p.MeanderWaves * Mathf.PI * 2f + meanderPhase);
                    var distance = Mathf.Abs(u - center);
                    // Шире половины карты дно не бывает: иначе InverseLerp перевернётся и борта уйдут вниз.
                    var slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Min(p.ShapeWidth, 0.5f), 0.5f, distance));
                    return Mathf.Lerp(p.BaseHeight, p.BaseHeight + p.ShapeHeight, slope);
                }
                case TerrainShape.Mountain:
                    return p.BaseHeight + p.ShapeHeight * Dome(u, v, p.ShapeWidth);
                case TerrainShape.Lake:
                    return p.BaseHeight - p.ShapeHeight * Dome(u, v, p.ShapeWidth);
                default:
                    return p.BaseHeight;
            }
        }

        /// <summary>1 в центре карты, плавно до 0 на расстоянии radius (доля ширины) и дальше.</summary>
        private static float Dome(float u, float v, float radius)
        {
            var distance = new Vector2(u - 0.5f, v - 0.5f).magnitude / Mathf.Max(radius, 0.01f);
            return 1f - Mathf.SmoothStep(0f, 1f, distance);
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
