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
            new("Подъём между уровнями: 12 блоков, поворот на 90°",
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

        private const float RiseLowStrip = 4f;    // ровный нижний уровень спереди
        private const float RiseInner = 10f;      // радиус внутреннего угла поворота
        private const float RiseOuter = 28f;      // радиус внешнего борта
        /// <summary>
        /// Центр поворота «Подъёма» в координатах площадки — внутренний угол, где сходятся линия начала
        /// подъёма (вдоль X) и линия выхода наверх (вдоль Z).
        /// </summary>
        public static readonly Vector2 RisePivot = new(40f, RiseLowStrip);

        /// <summary>
        /// Подъём как на референсе: проход поворачивает на 90°. Склон начинается вдоль ровной линии
        /// спереди (идёт от центра поворота влево, вдоль X) и выходит на верхний уровень вдоль
        /// перпендикулярной линии (от центра поворота вперёд, вдоль Z). Высота растёт с углом вокруг
        /// центра поворота, так что линии одной высоты расходятся веером. Поперёк прохода (по радиусу)
        /// профиль вогнутый. Внутри внутреннего радиуса и за внешним — верхний уровень с обрывами:
        /// ближе к центру поворота склон был бы круче блока на блок.
        /// </summary>
        private static float Rise(float u, float v, float s)
        {
            var high = 12f * s;
            if (v < RiseLowStrip)
                return 0f;
            var offset = new Vector2(u, v) - RisePivot;
            // Справа от линии выхода — верхний уровень.
            if (offset.x > 0f)
                return high;

            var radius = offset.magnitude;
            if (radius < RiseInner || radius > RiseOuter)
                return high;

            // Угол от линии начала (−X) к линии выхода (+Z): 0 … 1.
            var turn = Mathf.Atan2(offset.y, -offset.x) / (Mathf.PI * 0.5f);
            var along = Mathf.Lerp(turn, Mathf.SmoothStep(0f, 1f, turn), 0.5f);
            var middle = (RiseInner + RiseOuter) * 0.5f;
            var across = (radius - middle) / ((RiseOuter - RiseInner) * 0.5f);
            var dish = 2f * s * across * across;
            return Mathf.Min(high, along * high + dish);
        }

        /// <summary>Точка на оси «Подъёма» (посередине между бортами) при доле поворота t от 0 до 1.</summary>
        public static Vector2 RiseCentre(float t)
        {
            var angle = Mathf.Clamp01(t) * Mathf.PI * 0.5f;
            var middle = (RiseInner + RiseOuter) * 0.5f;
            return RisePivot + new Vector2(-Mathf.Cos(angle), Mathf.Sin(angle)) * middle;
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
