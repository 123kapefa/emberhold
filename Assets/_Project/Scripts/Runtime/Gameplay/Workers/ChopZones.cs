using System.Collections.Generic;
using Emberhold.World.Trees;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Зона рубки (#167): круг на карте, в котором рабочие рубят все деревья и корчуют все пни, в том числе те, что
    /// появятся в нём позже (пень срубленного дерева тоже). Живёт, пока игрок её не снимет. Зона — задание
    /// (<see cref="WorkAssignment"/>): её приказы держат не больше <see cref="WorkAssignment.MaxWorkers"/> человек
    /// и получают уровень зоны. Меняет зону только <see cref="ChopZones"/>.
    /// </summary>
    public sealed class ChopZone
    {
        internal ChopZone(Vector3Int cell, int radius, int maxWorkers)
        {
            Cell = cell;
            Radius = radius;
            Assignment = new WorkAssignment(WorkKind.Chop, maxWorkers);
        }

        /// <summary>Клетка центра зоны: пустая клетка над землёй, куда кликнул игрок.</summary>
        public Vector3Int Cell { get; }

        /// <summary>Радиус в блоках: дерево или пень в зоне, если середина его клетки не дальше радиуса от середины центра.</summary>
        public int Radius { get; internal set; }

        /// <summary>Уровень, который получают приказы зоны.</summary>
        public OrderPriority Priority { get; internal set; }

        /// <summary>Задание зоны: её приказы и кто их держит.</summary>
        public WorkAssignment Assignment { get; }

        /// <summary>Клетка в круге зоны (высота не важна).</summary>
        public bool Covers(Vector3Int cell)
        {
            var dx = cell.x - Cell.x;
            var dz = cell.z - Cell.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }
    }

    /// <summary>
    /// Все зоны рубки отряда. <see cref="Update"/> раз в кадр ставит приказы рубить и корчевать на деревья и пни в зонах
    /// и забирает в зону уже стоящие там одиночные приказы, если их ещё никто не взял. Дерево в нескольких зонах достаётся
    /// той, что поставлена раньше. Чистая логика, без сцены.
    /// </summary>
    public sealed class ChopZones
    {
        public const int MinRadius = 1;
        public const int MaxRadius = 16;
        public const int DefaultRadius = 4;
        public const int DefaultMaxWorkers = 2;

        private readonly List<ChopZone> zones = new();
        private readonly List<WorkOrder> scratch = new();

        public IReadOnlyList<ChopZone> All => zones;

        /// <summary>Номер изменения: растёт, когда зону поставили, сняли или поменяли.</summary>
        public int Version { get; private set; }

        /// <summary>Поставить зону с центром в клетке cell; радиус и число людей обрезаются до допустимых.</summary>
        public ChopZone Add(Vector3Int cell, int radius, int maxWorkers = DefaultMaxWorkers)
        {
            var zone = new ChopZone(cell, Mathf.Clamp(radius, MinRadius, MaxRadius), maxWorkers);
            zones.Add(zone);
            Version++;
            return zone;
        }

        /// <summary>Зона, в круг которой попадает клетка; из нескольких — с ближайшим центром. Null — ни в какой.</summary>
        public ChopZone At(Vector3Int cell)
        {
            ChopZone best = null;
            var bestDistance = int.MaxValue;
            foreach (var zone in zones)
            {
                var dx = cell.x - zone.Cell.x;
                var dz = cell.z - zone.Cell.z;
                var distance = dx * dx + dz * dz;
                if (!zone.Covers(cell) || distance >= bestDistance)
                    continue;
                best = zone;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>Снять зону: её приказы снимаются, даже взятые (рабочий бросает дело). false — такой зоны нет.</summary>
        public bool Remove(ChopZone zone, WorkOrders orders)
        {
            if (!zones.Remove(zone))
                return false;
            CancelWhere(zone, orders, _ => true);
            Version++;
            return true;
        }

        /// <summary>Сменить радиус; приказы, оставшиеся за кругом, снимаются. Возвращает новый радиус.</summary>
        public int SetRadius(ChopZone zone, int radius, WorkOrders orders)
        {
            radius = Mathf.Clamp(radius, MinRadius, MaxRadius);
            if (zone.Radius == radius)
                return radius;
            zone.Radius = radius;
            CancelWhere(zone, orders, order => !zone.Covers(order.Cell));
            Version++;
            return radius;
        }

        /// <summary>Сменить уровень зоны: его получают все её приказы.</summary>
        public void SetPriority(ChopZone zone, OrderPriority priority)
        {
            zone.Priority = priority;
            foreach (var order in zone.Assignment.Orders)
                order.Priority = priority;
            Version++;
        }

        /// <summary>Сменить, сколько людей держат зону: от одного до limit. Лишние уходят, доделав свой приказ.</summary>
        public int SetMaxWorkers(ChopZone zone, int maxWorkers, int limit)
        {
            zone.Assignment.MaxWorkers = Mathf.Clamp(maxWorkers, 1, Mathf.Max(1, limit));
            Version++;
            return zone.Assignment.MaxWorkers;
        }

        /// <summary>Поставить приказы на стоящие деревья и пни в зонах. Возвращает, сколько приказов добавлено в зоны.</summary>
        public int Update(Forest forest, WorkOrders orders)
        {
            if (forest == null || zones.Count == 0)
                return 0;
            var added = 0;
            foreach (var tree in forest.Trees)
            {
                if (tree.IsStanding && Owner(tree.Cell) is { } zone)
                    added += Take(zone, orders, orders.At(tree.Cell) ?? orders.AddChop(tree));
            }
            foreach (var stump in forest.Stumps)
            {
                if (stump.IsPresent && Owner(stump.Cell) is { } zone)
                    added += Take(zone, orders, orders.At(stump.Cell) ?? orders.AddUproot(stump));
            }
            return added;
        }

        /// <summary>Зона, которой достаётся клетка: первая поставленная из накрывающих её.</summary>
        private ChopZone Owner(Vector3Int cell)
        {
            foreach (var zone in zones)
            {
                if (zone.Covers(cell))
                    return zone;
            }
            return null;
        }

        /// <summary>Забрать приказ рубить в зону с её уровнем; чужой вид, уже чей-то или взятый приказ не трогаем.</summary>
        private int Take(ChopZone zone, WorkOrders orders, WorkOrder order)
        {
            if (order == null || order.Kind != WorkKind.Chop || order.Assignment == zone.Assignment || IsZoneOrder(order)
                || !orders.Assign(order, zone.Assignment))
                return 0;
            order.Priority = zone.Priority;
            return 1;
        }

        private bool IsZoneOrder(WorkOrder order)
        {
            foreach (var zone in zones)
            {
                if (order.Assignment == zone.Assignment)
                    return true;
            }
            return false;
        }

        private void CancelWhere(ChopZone zone, WorkOrders orders, System.Predicate<WorkOrder> match)
        {
            scratch.Clear();
            foreach (var order in zone.Assignment.Orders)
            {
                if (match(order))
                    scratch.Add(order);
            }
            foreach (var order in scratch)
                orders.Cancel(order);
            scratch.Clear();
        }
    }
}
