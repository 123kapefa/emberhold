using Emberhold.World.Trees;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Форма зоны местности на карте.</summary>
    public enum TerrainZoneShape
    {
        Circle = 0,
        Rectangle = 1,
    }

    /// <summary>
    /// Зона местности в координатах сетки блоков мира (X, Z): где на карте действует свой профиль.
    /// Обычные данные, чтобы генератор и тесты работали без сцены; из сцены их собирает <see cref="TerrainZone"/>.
    /// Очерченная граница — край влияния зоны: на ней вес 0, к середине он плавно растёт и на расстоянии
    /// <see cref="BlendWidth"/> от границы становится 1. За пределы очерченной области зона не выходит.
    /// </summary>
    public struct TerrainZoneArea
    {
        public TerrainZoneShape Shape;

        /// <summary>Центр зоны в блоках сетки мира.</summary>
        public Vector2 Center;

        /// <summary>Размер в блоках: ширина (локальный X) и длина (локальный Z) прямоугольника, у круга — диаметр по X.</summary>
        public Vector2 Size;

        /// <summary>Поворот вокруг вертикали в градусах, как у Transform.</summary>
        public float Yaw;

        /// <summary>Ширина плавного перехода от границы внутрь, в блоках. 0 — резкая граница.</summary>
        public float BlendWidth;

        public TerrainProfileParameters Profile;

        /// <summary>Лес профиля зоны; null — в зоне деревья не растут.</summary>
        public ForestMix Forest;

        /// <summary>Половина размера по локальным осям; у круга — радиус по обеим.</summary>
        public Vector2 Extents => Shape == TerrainZoneShape.Circle
            ? Vector2.one * Mathf.Max(Size.x, 0f) * 0.5f
            : Vector2.Max(Size, Vector2.zero) * 0.5f;

        /// <summary>Точка карты в осях зоны: начало в центре, оси повёрнуты вместе с зоной.</summary>
        public Vector2 ToLocal(Vector2 point)
        {
            var d = point - Center;
            var radians = Yaw * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
        }

        /// <summary>Расстояние от точки до границы: положительное внутри, отрицательное снаружи.</summary>
        public float DistanceInside(Vector2 point)
        {
            var local = ToLocal(point);
            var extents = Extents;
            if (Shape == TerrainZoneShape.Circle)
                return extents.x - local.magnitude;

            var dx = extents.x - Mathf.Abs(local.x);
            var dz = extents.y - Mathf.Abs(local.y);
            if (dx >= 0f && dz >= 0f)
                return Mathf.Min(dx, dz);
            // Снаружи — честное расстояние до прямоугольника, со скруглением у углов.
            return -new Vector2(Mathf.Max(-dx, 0f), Mathf.Max(-dz, 0f)).magnitude;
        }

        /// <summary>Вес зоны в точке: 0 на границе и снаружи, 1 глубже BlendWidth, между ними S-кривая.</summary>
        public float Weight(Vector2 point)
        {
            var inside = DistanceInside(point);
            if (inside <= 0f)
                return 0f;
            if (BlendWidth <= 0f)
                return 1f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / BlendWidth));
        }

        /// <summary>
        /// Доли (u, v) рамки зоны, 0..1 по локальным X и Z: в них профиль строит форму, как на всей карте.
        /// Гора встаёт в центре зоны, долина идёт вдоль её длины.
        /// </summary>
        public Vector2 ToProfileUV(Vector2 point)
        {
            var local = ToLocal(point);
            var extents = Vector2.Max(Extents, Vector2.one * 0.5f);
            return new Vector2(local.x / (extents.x * 2f) + 0.5f, local.y / (extents.y * 2f) + 0.5f);
        }
    }
}
