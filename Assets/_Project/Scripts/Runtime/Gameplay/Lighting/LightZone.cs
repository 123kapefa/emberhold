using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>Круг света на земле: центр источника и радиус в метрах (1 блок = 1 метр).</summary>
    public readonly struct LightCircle
    {
        public readonly Vector3 Center;
        public readonly float Radius;

        public LightCircle(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        /// <summary>Точка внутри круга. Высоту не учитываем: свет — это область на карте, как будущий туман.</summary>
        public bool Contains(Vector3 point)
        {
            var dx = point.x - Center.x;
            var dz = point.z - Center.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }
    }

    /// <summary>
    /// Освещённая зона — объединение кругов всех активных источников света.
    /// Чистая логика без Unity-объектов, чтобы её проверяли EditMode-тесты.
    /// </summary>
    public sealed class LightZone
    {
        private readonly List<LightCircle> circles = new();

        public int Count => circles.Count;

        public void Clear() => circles.Clear();

        public void Add(LightCircle circle) => circles.Add(circle);

        public bool Contains(Vector3 point)
        {
            foreach (var circle in circles)
            {
                if (circle.Contains(point))
                    return true;
            }
            return false;
        }
    }
}
