using System;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Макеты склонов для песочницы. Спереди сетка 3 × 3 маленьких площадок (по чанку 16 × 16),
    /// за ними большая площадка «Подъём» 48 × 32. Рельеф площадки задан функцией высоты в блоках
    /// от координат угла (u, v) внутри площадки; дальше — тот же путь, что у долины: квантование,
    /// выравнивание по слоям, заполнение столбцов.
    /// </summary>
    public static class SlopeShowcase
    {
        public const int PadsPerRow = 3;
        public const int PadSize = ChunkData.Size;
        public static readonly Vector3Int SizeInChunks = new(PadsPerRow, 2, PadsPerRow + 2);

        /// <summary>
        /// Площадки: сначала маленькие слева направо и по рядам, затем большая. Рельеф маленьких сходит
        /// на нет к краям, чтобы на стыках не было обрывов, которые путались бы с тем, что мы оцениваем.
        /// </summary>
        public static readonly Pad[] Pads =
        {
            Small(0, "Пологие склоны 1:4", (u, v, s) => Mesa(u, v, 1.5f * s, 0.25f * s)),
            Small(1, "Склоны 1:1 (45°)", (u, v, s) => Mesa(u, v, 4f * s, 1f * s)),
            Small(2, "Крутые склоны 2:1", (u, v, s) => Mesa(u, v, 5f * s, 2f * s)),
            Small(3, "Ромб: склоны по диагонали", (u, v, s) =>
                Mathf.Clamp(0.75f * s * (7f - Mathf.Abs(u - 8f) - Mathf.Abs(v - 8f)), 0f, 3f * s)),
            Small(4, "Холм", (u, v, s) => 6f * s * Mathf.Exp(-Dist2(u, v) / 14f) * Window(u, v)),
            Small(5, "Котлован", (u, v, s) => -3f * s * Mathf.Clamp01((6f - Mathf.Sqrt(Dist2(u, v))) / 3f)),
            Small(6, "Терраса: внешние и внутренний угол", (u, v, s) => Mathf.Min(
                Mesa(u, v, 3f * s, s),
                Mathf.Clamp(Mathf.Max(8f - u, 8f - v) * s, 0f, 3f * s))),
            Small(7, "Седло", (u, v, s) => (2f + ((u - 8f) * (u - 8f) - (v - 8f) * (v - 8f)) / 24f) * s * Window(u, v)),
            Small(8, "Шумный рельеф", null),
            new("Подъём между уровнями: 12 блоков, ~25°, поворот на 45°",
                new RectInt(0, PadsPerRow * PadSize, PadsPerRow * PadSize, 2 * PadSize), Rise, flatBorder: false),
        };

        public readonly struct Pad
        {
            public readonly string Name;
            /// <summary>Где площадка лежит в мире, в блоках по X и Z.</summary>
            public readonly RectInt Area;
            /// <summary>Высота над ровной землёй в блоках от (u, v) внутри площадки; s — множитель крутизны. null — шум.</summary>
            public readonly Func<float, float, float, float> Height;
            /// <summary>Ближний и левый края площадки — ровная земля (стыкуются с соседями без обрыва).</summary>
            public readonly bool FlatBorder;

            public Pad(string name, RectInt area, Func<float, float, float, float> height, bool flatBorder)
            {
                Name = name;
                Area = area;
                Height = height;
                FlatBorder = flatBorder;
            }

            public Vector2 Center => Area.center;
        }

        /// <summary>Центр площадки в блоках мира (по X и Z).</summary>
        public static Vector2 PadCenter(int pad) => Pads[pad].Center;

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
                var pad = Pads[PadAt(x, z)];
                var u = x - pad.Area.x;
                var v = z - pad.Area.y;
                var relief = pad.Height != null
                    ? pad.Height(u, v, steepness)
                    : Mathf.PerlinNoise(noiseOffset.x + x / 5f, noiseOffset.y + z / 5f) * 4f * steepness * Window(u, v);
                // Угол на границе общий с соседней площадкой: держим его на уровне ровной земли.
                if (pad.FlatBorder && (u == 0 || v == 0))
                    relief = 0f;
                corners[x, z] = CornerTerrain.Quantize(baseHeight + relief, heightStep, size.y);
            }

            if (snapToLayers)
                CornerTerrain.SnapToLayers(corners);
            CornerTerrain.Fill(world, corners, dirtDepth);
        }

        /// <summary>Площадка, которой принадлежит угол: та, что на нём начинается; на дальних краях мира — та, где он лежит.</summary>
        private static int PadAt(int x, int z)
        {
            var fallback = 0;
            for (var i = 0; i < Pads.Length; i++)
            {
                var a = Pads[i].Area;
                if (x < a.xMin || x > a.xMax || z < a.yMin || z > a.yMax)
                    continue;
                if (x < a.xMax && z < a.yMax)
                    return i;
                fallback = i;
            }
            return fallback;
        }

        private static Pad Small(int index, string name, Func<float, float, float, float> height) =>
            new(name, new RectInt(index % PadsPerRow * PadSize, index / PadsPerRow * PadSize, PadSize, PadSize),
                height, flatBorder: true);

        private const float RiseLowStrip = 4f;      // ровный нижний уровень спереди
        private const float RiseTop = 30f;          // где склон выходит на верхний уровень (по Z)
        private const float RiseLength = 30f;       // длина оси прохода
        private const float RiseTurn = 45f;         // поворот прохода, градусы
        private const float RiseTurnStart = 0.15f;  // доли длины оси, где идёт поворот
        private const float RiseTurnEnd = 0.5f;
        private const float RiseHalfWidth = 12f;
        private const int RiseSamples = 96;
        private static readonly Vector2[] RiseAxis = BuildRiseAxis();

        /// <summary>
        /// Ось прохода «Подъём» в координатах площадки, t от 0 (вход) до 1 (выход). Как на референсе:
        /// вход смотрит прямо (+Z), в первой половине проход сворачивает вправо на 45°, дальше идёт
        /// прямо по диагонали, так что борт прохода — ровная диагональная стена.
        /// </summary>
        public static Vector2 RiseCentre(float t)
        {
            var f = Mathf.Clamp01(t) * RiseSamples;
            var i = Mathf.Min((int)f, RiseSamples - 1);
            return Vector2.Lerp(RiseAxis[i], RiseAxis[i + 1], f - i);
        }

        private static Vector2[] BuildRiseAxis()
        {
            var axis = new Vector2[RiseSamples + 1];
            var point = new Vector2(16f, RiseLowStrip);
            var step = RiseLength / RiseSamples;
            axis[0] = point;
            for (var i = 1; i <= RiseSamples; i++)
            {
                var t = (i - 0.5f) / RiseSamples;
                var heading = RiseTurn * Mathf.Deg2Rad * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RiseTurnStart, RiseTurnEnd, t));
                point += new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * step;
                axis[i] = point;
            }
            return axis;
        }

        /// <summary>
        /// Подъём как на референсе: спереди нижний уровень, сзади и по бокам верхний (на 12 блоков выше),
        /// между ними широкий травяной проход, который сворачивает на 45°. Высота растёт к дальнему краю
        /// (по Z) плавной кривой, в среднем ~25°: если считать её вдоль оси, на внутренней стороне поворота
        /// путь слишком короткий и склон рассыпается на уступы. Поперёк прохода профиль вогнутый:
        /// середина ниже краёв, края поднимаются к обрывам.
        /// </summary>
        private static float Rise(float u, float v, float s)
        {
            var high = 12f * s;
            if (v < RiseLowStrip)
                return 0f;

            // Расстояние до ближайшей точки оси.
            var point = new Vector2(u, v);
            var best = float.MaxValue;
            foreach (var p in RiseAxis)
                best = Mathf.Min(best, (point - p).sqrMagnitude);

            var across = Mathf.Sqrt(best) / RiseHalfWidth;
            if (across > 1f)
                return high;
            var t = Mathf.InverseLerp(RiseLowStrip, RiseTop, v);
            var along = Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), 0.5f);
            var dish = 2f * s * across * across;
            return Mathf.Min(high, along * high + dish);
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
