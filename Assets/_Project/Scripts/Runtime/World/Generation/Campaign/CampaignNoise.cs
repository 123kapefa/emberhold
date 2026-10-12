using System;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Шум для карты кампании: значения на целочисленной решётке по хешу, сглаженные между узлами.
    /// Свой, а не Mathf.PerlinNoise: только чистая арифметика, поэтому строки карты считаются параллельно,
    /// а результат зависит лишь от seed и координат. Повторяет шум из наброска карты (#59).
    /// </summary>
    public readonly struct CampaignNoise
    {
        private readonly int seed;

        public CampaignNoise(int seed) => this.seed = seed;

        /// <summary>Значение узла решётки, 0..1.</summary>
        public float Hash(int x, int y)
        {
            unchecked
            {
                var n = x * 374761393 + y * 668265263 + seed * 1442695041;
                n = (n ^ (int)((uint)n >> 13)) * 1274126177;
                return (uint)(n ^ (int)((uint)n >> 16)) / 4294967295f;
            }
        }

        /// <summary>Сглаженный шум 0..1 с узлами через 1.</summary>
        public float Value(float x, float y)
        {
            var xi = (int)Math.Floor(x);
            var yi = (int)Math.Floor(y);
            var xf = x - xi;
            var yf = y - yi;
            var u = xf * xf * (3f - 2f * xf);
            var v = yf * yf * (3f - 2f * yf);
            var a = Lerp(Hash(xi, yi), Hash(xi + 1, yi), u);
            var b = Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), u);
            return Lerp(a, b, v);
        }

        /// <summary>Сумма октав шума, 0..1: крупные холмы плюс мелкая рябь.</summary>
        public float Fbm(float x, float y, int octaves = 4)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                sum += amplitude * Value(x * frequency + i * 17.3f, y * frequency - i * 9.1f);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>«Хребтовый» шум 0..1: острые гребни там, где обычный шум проходит через середину.</summary>
        public float Ridged(float x, float y)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (var i = 0; i < 5; i++)
            {
                var r = 1f - Math.Abs(Value(x * frequency + i * 31.7f, y * frequency + i * 5.3f) * 2f - 1f);
                sum += amplitude * r * r;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>Плавный минимум: как Min, но стык двух поверхностей скруглён на ширину k.</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            var h = Clamp01(0.5f + 0.5f * (b - a) / k);
            return Lerp(b, a, h) - k * h * (1f - h);
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
