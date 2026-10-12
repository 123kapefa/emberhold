using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Вид тумана в текущий момент и его плотность в зависимости от расстояния за краем света.
    /// Ту же формулу считает шейдер LightFog, поэтому плотность здесь совпадает с тем, что видно на экране.
    /// Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public readonly struct FogLook
    {
        public readonly Color Color;
        public readonly float Opacity;
        public readonly float Falloff;

        public FogLook(Color color, float opacity, float falloff)
        {
            Color = color;
            Opacity = Mathf.Clamp01(opacity);
            Falloff = Mathf.Max(0.0001f, falloff);
        }

        /// <summary>
        /// Плотность тумана от 0 до Opacity: 0 внутри света и на его краю, дальше растёт
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
    }
}
