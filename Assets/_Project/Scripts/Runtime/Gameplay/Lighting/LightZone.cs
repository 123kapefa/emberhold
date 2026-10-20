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

        /// <summary>
        /// Свет доходит до блока: выше источника — весь круг, ниже — только шар радиуса вокруг источника.
        /// Глубоко под огнём темно, поэтому к забою туннеля нужно нести свет.
        /// </summary>
        public bool Reaches(Vector3 point)
        {
            var dx = point.x - Center.x;
            var dy = point.y - Center.y;
            var dz = point.z - Center.z;
            var flat = dx * dx + dz * dz;
            var radius = Radius * Radius;
            return flat <= radius && (dy >= 0f || flat + dy * dy <= radius);
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

        public IReadOnlyList<LightCircle> Circles => circles;

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

        /// <summary>Свет хотя бы одного источника доходит до блока (см. <see cref="LightCircle.Reaches"/>).</summary>
        public bool Reaches(Vector3 point)
        {
            foreach (var circle in circles)
            {
                if (circle.Reaches(point))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Сколько метров по горизонтали от точки до ближайшего освещённого места: 0 внутри света,
        /// бесконечность, если источников нет. По этому расстоянию густеет туман.
        /// </summary>
        public float DistanceOutside(Vector3 point)
        {
            var best = float.PositiveInfinity;
            foreach (var circle in circles)
            {
                var dx = point.x - circle.Center.x;
                var dz = point.z - circle.Center.z;
                var outside = Mathf.Sqrt(dx * dx + dz * dz) - circle.Radius;
                if (outside < best)
                    best = outside;
            }
            return Mathf.Max(0f, best);
        }
    }
}
