using System;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Макеты склонов для песочницы: сетка 3 × 3 площадок, каждая — один чанк 16 × 16.
    /// Рельеф площадки задан функцией высоты в блоках от координат угла (u, v) внутри площадки;
    /// дальше — тот же путь, что у долины: квантование, выравнивание по слоям, заполнение столбцов.
    /// </summary>
    public static class SlopeShowcase
    {
        public const int PadsPerRow = 3;
        public const int PadSize = ChunkData.Size;
        public static readonly Vector3Int SizeInChunks = new(PadsPerRow, 2, PadsPerRow);

        /// <summary>
        /// Площадки по порядку: слева направо, затем следующий ряд. Рельеф каждой сходит на нет к краям,
        /// чтобы на стыке площадок не было обрывов, которые путались бы с тем, что мы оцениваем.
        /// </summary>
        public static readonly Pad[] Pads =
        {
            new("Пологие склоны 1:4", (u, v, s) => Mesa(u, v, 1.5f * s, 0.25f * s)),
            new("Склоны 1:1 (45°)", (u, v, s) => Mesa(u, v, 4f * s, 1f * s)),
            new("Крутые склоны 2:1", (u, v, s) => Mesa(u, v, 5f * s, 2f * s)),
            new("Ромб: склоны по диагонали", (u, v, s) =>
                Mathf.Clamp(0.75f * s * (7f - Mathf.Abs(u - 8f) - Mathf.Abs(v - 8f)), 0f, 3f * s)),
            new("Холм", (u, v, s) => 6f * s * Mathf.Exp(-Dist2(u, v) / 14f) * Window(u, v)),
            new("Котлован", (u, v, s) => -3f * s * Mathf.Clamp01((6f - Mathf.Sqrt(Dist2(u, v))) / 3f)),
            new("Терраса: внешние и внутренний угол", (u, v, s) => Mathf.Min(
                Mesa(u, v, 3f * s, s),
                Mathf.Clamp(Mathf.Max(8f - u, 8f - v) * s, 0f, 3f * s))),
            new("Седло", (u, v, s) => (2f + ((u - 8f) * (u - 8f) - (v - 8f) * (v - 8f)) / 24f) * s * Window(u, v)),
            new("Шумный рельеф", null),
        };

        public readonly struct Pad
        {
            public readonly string Name;
            /// <summary>Высота над ровной землёй в блоках; s — множитель крутизны. null — шум.</summary>
            public readonly Func<float, float, float, float> Height;

            public Pad(string name, Func<float, float, float, float> height)
            {
                Name = name;
                Height = height;
            }
        }

        /// <summary>Центр площадки в блоках мира (по X и Z).</summary>
        public static Vector2 PadCenter(int pad) =>
            new((pad % PadsPerRow + 0.5f) * PadSize, (pad / PadsPerRow + 0.5f) * PadSize);

        public static VoxelWorld Build(SlopeSandboxSettings settings)
        {
            var world = new VoxelWorld(SizeInChunks);
            Fill(world, settings.HeightStep, settings.SnapToLayers, settings.Steepness, settings.BaseHeight,
                settings.DirtDepth, settings.Seed);
            return world;
        }

        /// <summary>Заполнить мир макетами; изменённые чанки помечаются для перестройки.</summary>
        public static void Fill(VoxelWorld world, int heightStep, bool snapToLayers, float steepness, float baseHeight,
            int dirtDepth, int seed)
        {
            var size = world.SizeInBlocks;
            var random = new System.Random(seed);
            var noiseOffset = new Vector2((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f);

            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
            {
                // Угол на границе площадок относим к той, что левее/ниже, кроме самого края мира.
                var padX = Mathf.Min(x / PadSize, PadsPerRow - 1);
                var padZ = Mathf.Min(z / PadSize, PadsPerRow - 1);
                var u = x - padX * PadSize;
                var v = z - padZ * PadSize;
                var pad = Pads[padZ * PadsPerRow + padX];
                var relief = pad.Height != null
                    ? pad.Height(u, v, steepness)
                    : Mathf.PerlinNoise(noiseOffset.x + x / 5f, noiseOffset.y + z / 5f) * 4f * steepness * Window(u, v);
                // Край площадки — ровная земля: угол на границе общий с соседней площадкой.
                if (u == 0 || v == 0)
                    relief = 0f;
                corners[x, z] = CornerTerrain.Quantize(baseHeight + relief, heightStep, size.y);
            }

            if (snapToLayers)
                CornerTerrain.SnapToLayers(corners);
            CornerTerrain.Fill(world, corners, dirtDepth);
        }

        private static float Dist2(float u, float v) => (u - 8f) * (u - 8f) + (v - 8f) * (v - 8f);

        /// <summary>
        /// Плато высотой rise, со всех сторон склоны с наклоном slope (блоков на блок), которые начинаются
        /// в блоке от края площадки. По углам плато — внешние углы склонов.
        /// </summary>
        private static float Mesa(float u, float v, float rise, float slope)
        {
            var fromEdge = Mathf.Min(Mathf.Min(u, PadSize - u), Mathf.Min(v, PadSize - v)) - 1f;
            return Mathf.Clamp(slope * fromEdge, 0f, rise);
        }

        /// <summary>Плавно гасит рельеф к краям площадки: 1 в середине, 0 на краю.</summary>
        private static float Window(float u, float v) =>
            Mathf.Sin(Mathf.PI * u / PadSize) * Mathf.Sin(Mathf.PI * v / PadSize);
    }
}
