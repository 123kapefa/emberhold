using System;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Рельеф гор за краем мира (#102): только картинка, блоков там нет. У самого края высота точно равна краю
    /// мира, дальше хребет переходит в свои горы и всё ниже опускается в море тумана, так что ровной линии обреза
    /// не видно. Координаты — в блоках сетки мира: (0, 0) — угол мира, внутри мира x в 0..sizeX, z в 0..sizeZ.
    /// Только чистая арифметика: считается и в тестах без сцены.
    /// </summary>
    public sealed class WorldSkirtShape
    {
        /// <summary>На каком расстоянии от края высота края целиком сменяется своими горами.</summary>
        public const float Blend = 40f;
        /// <summary>Дальше этого расстояния горы опускаются в туман (в среднем: вдоль края оно гуляет вдвое).</summary>
        public const float SinkFrom = 12f;
        /// <summary>На сколько блоков опускаются горы на каждый блок дальше <see cref="SinkFrom"/>.</summary>
        public const float SinkRate = 0.32f;
        /// <summary>Ниже этого рельеф не опускается: под пеленой всё равно не видно.</summary>
        public const float Floor = -12f;

        // Свои горы — от высоты края на ±MountainRise / 2: гребни выше края и седловины ниже.
        private const float MountainRise = 55f;
        private const float MountainScale = 1f / 90f;
        // Насколько вдоль края гуляет дальность, с которой горы уходят в туман: 0.5..1.5 от расстояния.
        private const float SinkWarpScale = 1f / 70f;
        // Полуширина сглаживания профиля края на каждый блок от края.
        private const float EdgeSmoothing = 0.5f;
        // Мелкий рельеф поверх гор, как у хребта мира: рёбра и кулуары, а по ним неровности в пару блоков.
        // Появляется за DetailFrom блоков от края, чтобы у самого края высота оставалась высотой земли.
        private const float RibRise = 16f;
        private const float RibScale = 1f / 26f;
        private const float RoughRise = 2f;
        private const float RoughScale = 1f / 7f;
        private const float DetailFrom = 6f;

        private readonly float[] south, north, west, east;
        // Суммы высот края с начала стороны: среднее по любому отрезку края за два обращения.
        private readonly double[] southSum, northSum, westSum, eastSum;
        private readonly CampaignNoise noise;

        public int SizeX { get; }
        public int SizeZ { get; }

        /// <param name="cornerHeight">Высота угла сетки мира (x, z) в блоках; берутся только углы на краю.</param>
        public WorldSkirtShape(Func<int, int, float> cornerHeight, int sizeX, int sizeZ, int seed)
        {
            if (cornerHeight == null)
                throw new ArgumentNullException(nameof(cornerHeight));
            SizeX = sizeX;
            SizeZ = sizeZ;
            south = new float[sizeX + 1];
            north = new float[sizeX + 1];
            for (var x = 0; x <= sizeX; x++)
            {
                south[x] = cornerHeight(x, 0);
                north[x] = cornerHeight(x, sizeZ);
            }
            west = new float[sizeZ + 1];
            east = new float[sizeZ + 1];
            for (var z = 0; z <= sizeZ; z++)
            {
                west[z] = cornerHeight(0, z);
                east[z] = cornerHeight(sizeX, z);
            }
            southSum = Sums(south);
            northSum = Sums(north);
            westSum = Sums(west);
            eastSum = Sums(east);
            noise = new CampaignNoise(seed ^ 0x5eed);
        }

        /// <summary>
        /// Расстояние по горизонтали от точки до мира: снаружи — до ближайшей точки края, внутри — 0.
        /// </summary>
        public float Outside(float x, float z)
        {
            var dx = Math.Max(Math.Max(-x, x - SizeX), 0f);
            var dz = Math.Max(Math.Max(-z, z - SizeZ), 0f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Высота рельефа в точке, в блоках.</summary>
        public float Height(float x, float z)
        {
            var d = Outside(x, z);
            // Чем дальше от края, тем глаже его профиль: мелкие неровности края не тянутся полосами наружу.
            var edge = EdgeHeight(x, z, d * EdgeSmoothing);
            var h = CampaignNoise.Lerp(edge, edge + Mountains(x, z), Spread(d));
            h += Detail(x, z) * CampaignNoise.SmoothStep(0f, DetailFrom, d);
            // Дальность до тумана своя в каждом месте: линия, где горы тонут, не повторяет край мира.
            var warped = d * (0.5f + noise.Fbm(x * SinkWarpScale + 71f, z * SinkWarpScale - 13f, 3));
            h -= SinkRate * Math.Max(warped - SinkFrom, 0f);
            return Math.Max(h, Floor);
        }

        /// <summary>Насколько в точке уже свои горы, а не продолжение края: 0 у края, 1 дальше <see cref="Blend"/>.</summary>
        public float Spread(float distance) => CampaignNoise.SmoothStep(0f, Blend, distance);

        /// <summary>Нормаль рельефа в точке по соседним высотам.</summary>
        public void Normal(float x, float z, out float nx, out float ny, out float nz)
        {
            const float step = 1f;
            var gx = (Height(x + step, z) - Height(x - step, z)) / (2f * step);
            var gz = (Height(x, z + step) - Height(x, z - step)) / (2f * step);
            var length = (float)Math.Sqrt(gx * gx + 1f + gz * gz);
            nx = -gx / length;
            ny = 1f / length;
            nz = -gz / length;
        }

        /// <summary>
        /// Высота края мира в ближайшей к точке точке края; при radius больше блока — средняя по краю
        /// на radius блоков в обе стороны.
        /// </summary>
        public float EdgeHeight(float x, float z, float radius = 0f)
        {
            var cx = Math.Min(Math.Max(x, 0f), SizeX);
            var cz = Math.Min(Math.Max(z, 0f), SizeZ);
            // Ближайшая точка края: у точки внутри мира — на ближайшей стороне.
            var toWest = cx;
            var toEast = SizeX - cx;
            var toSouth = cz;
            var toNorth = SizeZ - cz;
            var nearest = Math.Min(Math.Min(toWest, toEast), Math.Min(toSouth, toNorth));
            if (nearest == toSouth)
                return Along(south, southSum, cx, radius);
            if (nearest == toNorth)
                return Along(north, northSum, cx, radius);
            if (nearest == toWest)
                return Along(west, westSum, cz, radius);
            return Along(east, eastSum, cz, radius);
        }

        private static double[] Sums(float[] edge)
        {
            var sums = new double[edge.Length + 1];
            for (var i = 0; i < edge.Length; i++)
                sums[i + 1] = sums[i] + edge[i];
            return sums;
        }

        private float Detail(float x, float z)
        {
            // Повёрнутые координаты: у шума на решётке иначе видны прямые углы вдоль осей мира.
            float rx = x * 0.8f - z * 0.6f, rz = x * 0.6f + z * 0.8f;
            var ribs = RibRise * (noise.Ridged(rx * RibScale + 113f, rz * RibScale - 57f) - 0.45f);
            var rough = RoughRise * (noise.Fbm(rz * RoughScale - 211f, -rx * RoughScale + 89f, 2) - 0.5f) * 2f;
            return ribs + rough;
        }

        private float Mountains(float x, float z)
        {
            return MountainRise * (noise.Ridged(x * MountainScale, z * MountainScale) - 0.5f);
        }

        private static float Along(float[] edge, double[] sum, float t, float radius)
        {
            if (radius >= 1f)
            {
                var from = Math.Max((int)Math.Round(t - radius), 0);
                var to = Math.Min((int)Math.Round(t + radius), edge.Length - 1);
                return (float)((sum[to + 1] - sum[from]) / (to - from + 1));
            }
            var i = (int)Math.Floor(t);
            if (i >= edge.Length - 1)
                return edge[edge.Length - 1];
            if (i < 0)
                return edge[0];
            return CampaignNoise.Lerp(edge[i], edge[i + 1], t - i);
        }
    }
}
