using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Объёмы зоны с уровнем для её вида (#136, #169): что выкопать — от уровня до земли столбца, но не выше верха коробки,
    /// где земля выше уровня и блок можно копать; что нарастить — от земли до уровня, где земля ниже уровня, но не ниже
    /// низа коробки. Столбец, чья земля ниже низа, не наращивают (класть не на что) — у него голубого нет.
    /// Соседние столбцы одного объёма сливаются: грани между ними не строятся, остаются верх и внешние стенки. Объём чуть
    /// раздут (<see cref="Inflate"/>), чтобы не мерцать с гранями блоков. Чистая геометрия в координатах мира блоков, без сцены.
    /// </summary>
    public static class LevelZoneShape
    {
        public const float Inflate = 0.02f;

        private static readonly Vector2Int[] Sides = { Vector2Int.right, Vector2Int.left, new(0, 1), new(0, -1) };

        /// <summary>
        /// Построить объёмы прямоугольника min/size с уровнем level и коробкой: верх top (высота самого высокого блока,
        /// который копают), низ bottom (наращивают клетки выше него). height — высота земли столбца, diggable — в столбце
        /// есть что копать в (уровень, верх]. Треугольники объёма «выкопать» — в dig, «нарастить» — в fill; showDig = false —
        /// «выкопать» не строится (режим без копки), showFill = false — «нарастить». fillFrom — с какой высоты в столбце
        /// начнут наращивать (null — нечего; <see cref="LevelZone.FillFrom"/>): так голубое совпадает с тем, что засыплют.
        /// Без fillFrom голубое считается по высоте земли и низу коробки.
        /// </summary>
        public static void Build(Vector2Int min, Vector2Int size, int level, int top, int bottom, Func<Vector2Int, float> height,
            Func<Vector2Int, bool> diggable, List<Vector3> vertices, List<int> dig, List<int> fill, bool showDig = true,
            bool showFill = true, Func<Vector2Int, float?> fillFrom = null)
        {
            vertices.Clear();
            dig.Clear();
            fill.Clear();
            var plane = level + 1f;
            var ceiling = top + 1f;
            var floor = bottom + 1f;
            for (var z = min.y; z < min.y + size.y; z++)
            for (var x = min.x; x < min.x + size.x; x++)
            {
                var column = new Vector2Int(x, z);
                foreach (var (triangles, isDig) in new[] { (dig, true), (fill, false) })
                {
                    if (isDig ? !showDig : !showFill)
                        continue;
                    if (!TryInterval(column, isDig, min, size, plane, ceiling, floor, height, diggable, fillFrom, out var low, out var high))
                        continue;
                    // Верх: у «выкопать» — по земле, у «нарастить» — плоскость уровня.
                    AddQuad(vertices, triangles,
                        new Vector3(x, high + Inflate, z), new Vector3(x, high + Inflate, z + 1),
                        new Vector3(x + 1, high + Inflate, z + 1), new Vector3(x + 1, high + Inflate, z));
                    foreach (var side in Sides)
                    {
                        var hasNeighbour = TryInterval(column + side, isDig, min, size, plane, ceiling, floor, height, diggable, fillFrom, out var nLow, out var nHigh);
                        // Видимые куски стенки: свой отрезок минус отрезок соседа того же объёма.
                        if (!hasNeighbour || nHigh <= low || nLow >= high)
                            AddSide(vertices, triangles, column, side, low, high);
                        else
                        {
                            if (nLow > low)
                                AddSide(vertices, triangles, column, side, low, nLow);
                            if (nHigh < high)
                                AddSide(vertices, triangles, column, side, nHigh, high);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Отрезок объёма в столбце: «выкопать» — [уровень, min(земля, верх)], «нарастить» — [max(земля, низ), уровень].
        /// false — пусто.
        /// </summary>
        private static bool TryInterval(Vector2Int column, bool isDig, Vector2Int min, Vector2Int size, float plane, float ceiling,
            float floor, Func<Vector2Int, float> height, Func<Vector2Int, bool> diggable, Func<Vector2Int, float?> fillFrom,
            out float low, out float high)
        {
            low = high = 0f;
            if (column.x < min.x || column.y < min.y || column.x >= min.x + size.x || column.y >= min.y + size.y)
                return false;
            if (!isDig && fillFrom != null)
            {
                if (fillFrom(column) is not { } from || from >= plane - 1e-3f)
                    return false;
                low = from;
                high = plane;
                return true;
            }
            var ground = height(column);
            if (isDig)
            {
                high = Mathf.Min(ground, ceiling);
                if (high <= plane + 1e-3f || !diggable(column))
                    return false;
                low = plane;
                return true;
            }
            // Земля ниже низа коробки: нижней клетке (низ + 1) не на что лечь.
            if (ground <= floor - 1f + 1e-3f)
                return false;
            low = Mathf.Max(ground, floor);
            if (low >= plane - 1e-3f)
                return false;
            high = plane;
            return true;
        }

        /// <summary>Стенка столбца со стороны side от высоты low до high, чуть наружу.</summary>
        private static void AddSide(List<Vector3> vertices, List<int> triangles, Vector2Int column, Vector2Int side, float low, float high)
        {
            float x0 = column.x, x1 = column.x + 1, z0 = column.y, z1 = column.y + 1;
            if (side.x > 0)
                AddQuad(vertices, triangles, V(x1 + Inflate, low, z0), V(x1 + Inflate, high, z0), V(x1 + Inflate, high, z1), V(x1 + Inflate, low, z1));
            else if (side.x < 0)
                AddQuad(vertices, triangles, V(x0 - Inflate, low, z1), V(x0 - Inflate, high, z1), V(x0 - Inflate, high, z0), V(x0 - Inflate, low, z0));
            else if (side.y > 0)
                AddQuad(vertices, triangles, V(x1, low, z1 + Inflate), V(x1, high, z1 + Inflate), V(x0, high, z1 + Inflate), V(x0, low, z1 + Inflate));
            else
                AddQuad(vertices, triangles, V(x0, low, z0 - Inflate), V(x0, high, z0 - Inflate), V(x1, high, z0 - Inflate), V(x1, low, z0 - Inflate));
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        /// <summary>Четырёхугольник a-b-c-d по часовой стрелке, если смотреть снаружи (лицевая сторона наружу).</summary>
        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
            triangles.Add(i);
            triangles.Add(i + 2);
            triangles.Add(i + 3);
        }
    }
}
