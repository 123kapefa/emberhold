using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Что делает ручка зоны, если её тянуть.</summary>
    public enum ZoneHandleKind
    {
        /// <summary>Перенести зону целиком (середина зоны).</summary>
        Move,
        /// <summary>Сторона или угол прямоугольника: <see cref="ZoneHandle.Dx"/>, <see cref="ZoneHandle.Dz"/> — какие стороны.</summary>
        Edge,
        /// <summary>Уровень зоны копки: стрелка вверх-вниз над зоной.</summary>
        Level,
        /// <summary>Радиус зоны рубки: ручка на краю круга.</summary>
        Radius,
    }

    /// <summary>Ручка на выбранной зоне: что делает и где стоит (в координатах мира блоков).</summary>
    public readonly struct ZoneHandle
    {
        public ZoneHandle(ZoneHandleKind kind, Vector3 position, int dx = 0, int dz = 0)
        {
            Kind = kind;
            Position = position;
            Dx = dx;
            Dz = dz;
        }

        public ZoneHandleKind Kind { get; }
        public Vector3 Position { get; }

        /// <summary>Какую сторону по x тянет ручка стороны или угла: −1 — меньших x, +1 — больших, 0 — никакую. Так же <see cref="Dz"/>.</summary>
        public int Dx { get; }
        public int Dz { get; }
    }

    /// <summary>
    /// Где стоят ручки зоны (#136): у прямоугольника — по середине каждой стороны, на углах, перенос в середине и стрелка
    /// уровня над ней; у круга рубки — перенос в центре и радиус на краю круга (со стороны больших x). Всё по земле:
    /// высоту даёт ground (у сторон и углов — земля столбца внутри зоны у ручки). Чистая геометрия, без сцены.
    /// </summary>
    public static class ZoneHandleLayout
    {
        /// <summary>Насколько стрелка уровня выше плоскости уровня или земли в середине зоны, что выше.</summary>
        public const float LevelLift = 2f;

        private const float GroundLift = 0.15f;

        public static void Build(WorkZone zone, Func<Vector2Int, float> ground, List<ZoneHandle> into)
        {
            into.Clear();
            if (zone is ChopZone chop)
            {
                var c = chop.Center;
                into.Add(new ZoneHandle(ZoneHandleKind.Move, OnGround(ground, c, c)));
                var edge = c + new Vector2(chop.Radius, 0f);
                into.Add(new ZoneHandle(ZoneHandleKind.Radius, OnGround(ground, edge, edge)));
                return;
            }
            if (zone is not LevelZone level)
                return;
            float x0 = level.Min.x, z0 = level.Min.y;
            float x1 = x0 + level.Size.x, z1 = z0 + level.Size.y;
            var center = level.Center;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                var point = new Vector2(dx < 0 ? x0 : dx > 0 ? x1 : center.x, dz < 0 ? z0 : dz > 0 ? z1 : center.y);
                // Высоту берём у столбца внутри зоны, ближайшего к ручке.
                var inside = new Vector2(Mathf.Clamp(point.x, x0 + 0.5f, x1 - 0.5f), Mathf.Clamp(point.y, z0 + 0.5f, z1 - 0.5f));
                into.Add(new ZoneHandle(ZoneHandleKind.Edge, OnGround(ground, point, inside), dx, dz));
            }
            var move = OnGround(ground, center, center);
            into.Add(new ZoneHandle(ZoneHandleKind.Move, move));
            var levelY = Mathf.Max(level.Level + 1f, move.y) + LevelLift;
            into.Add(new ZoneHandle(ZoneHandleKind.Level, new Vector3(center.x, levelY, center.y)));
        }

        private static Vector3 OnGround(Func<Vector2Int, float> ground, Vector2 point, Vector2 sample)
        {
            var column = new Vector2Int(Mathf.FloorToInt(sample.x), Mathf.FloorToInt(sample.y));
            return new Vector3(point.x, ground(column) + GroundLift, point.y);
        }
    }
}
