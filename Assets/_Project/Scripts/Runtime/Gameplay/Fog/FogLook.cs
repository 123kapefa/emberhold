using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Вид тумана в текущий момент и его плотность в зависимости от расстояния за краем света и высоты.
    /// Ту же формулу считает шейдер LightFog, поэтому плотность здесь совпадает с тем, что видно на экране.
    /// Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public readonly struct FogLook
    {
        public readonly Color Color;
        public readonly float Opacity;
        public readonly float Falloff;
        /// <summary>Ниже этой высоты туман в полную силу.</summary>
        public readonly float DenseBelow;
        /// <summary>Выше этой высоты тумана нет; между DenseBelow и ней он плавно редеет.</summary>
        public readonly float ClearAbove;

        /// <summary>Туман без границы по высоте: на любой высоте в полную силу.</summary>
        public FogLook(Color color, float opacity, float falloff)
            : this(color, opacity, falloff, float.PositiveInfinity, float.PositiveInfinity)
        {
        }

        public FogLook(Color color, float opacity, float falloff, float denseBelow, float clearAbove)
        {
            Color = color;
            Opacity = Mathf.Clamp01(opacity);
            Falloff = Mathf.Max(0.0001f, falloff);
            DenseBelow = denseBelow;
            ClearAbove = Mathf.Max(denseBelow, clearAbove);
        }

        /// <summary>
        /// Плотность тумана от 0 до Opacity без учёта высоты: 0 внутри света и на его краю, дальше растёт
        /// и стремится к Opacity. Бесконечное расстояние (источников нет) — полный туман.
        /// </summary>
        public float DensityAt(float distanceOutside)
        {
            if (distanceOutside <= 0f)
                return 0f;
            if (float.IsPositiveInfinity(distanceOutside))
                return Opacity;
            return Opacity * (1f - Mathf.Exp(-distanceOutside / Falloff));
        }

        /// <summary>
        /// Плотность с учётом высоты: плотность от света, умноженная на долю тумана на этой высоте.
        /// Внутри света тумана нет на любой высоте.
        /// </summary>
        public float DensityAt(float distanceOutside, float height) => DensityAt(distanceOutside) * HeightFactor(height);

        /// <summary>Доля тумана на высоте: 1 ниже DenseBelow, 0 выше ClearAbove, между ними плавный спад.</summary>
        public float HeightFactor(float height)
        {
            if (height <= DenseBelow)
                return 1f;
            if (height >= ClearAbove)
                return 0f;
            var t = (ClearAbove - height) / (ClearAbove - DenseBelow);
            return t * t * (3f - 2f * t);
        }
    }
}
