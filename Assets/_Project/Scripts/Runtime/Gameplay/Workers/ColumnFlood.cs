using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Поиск путей по столбцам <see cref="WalkSurface"/>: из одной точки разливаемся по всем доступным столбцам
    /// (Дейкстра, прямой шаг стоит 1, диагональный √2) и помним, откуда пришли. После одного разлива можно
    /// сразу узнать расстояние и путь до любого столбца — так рабочий выбирает ближайшую задачу и кучку.
    /// Куда можно заходить, решает canEnter (например, только освещённые столбцы); стартовый столбец
    /// доступен всегда, чтобы рабочий мог выйти из темноты.
    /// </summary>
    public sealed class ColumnFlood
    {
        private static readonly Vector2Int[] Steps =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        private readonly Dictionary<Vector2Int, float> distance = new();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new();
        private readonly HashSet<Vector2Int> settled = new();
        private readonly Dictionary<Vector2Int, bool> enterable = new();
        private readonly List<(float cost, Vector2Int column)> heap = new();

        public ColumnFlood(int searchLimit = 4096)
        {
            SearchLimit = searchLimit;
        }

        /// <summary>Сколько столбцов разлив обходит самое большее.</summary>
        public int SearchLimit { get; }

        public Vector2Int Start { get; private set; }

        /// <summary>Сколько столбцов достигнуто последним разливом.</summary>
        public int Count => distance.Count;

        /// <summary>Разлиться из start по столбцам, куда можно заходить.</summary>
        public void Run(WalkSurface surface, Vector2Int start, Func<Vector2Int, bool> canEnter)
        {
            distance.Clear();
            cameFrom.Clear();
            heap.Clear();
            settled.Clear();
            enterable.Clear();
            Start = start;
            if (!surface.Contains(start))
                return;

            surface.BeginCache();
            try
            {
                Flood(surface, canEnter);
            }
            finally
            {
                surface.EndCache();
            }
        }

        private void Flood(WalkSurface surface, Func<Vector2Int, bool> canEnter)
        {
            var start = Start;

            distance[start] = 0f;
            Push(0f, start);
            while (heap.Count > 0 && settled.Count < SearchLimit)
            {
                var (cost, column) = Pop();
                if (!settled.Add(column))
                    continue;
                foreach (var step in Steps)
                {
                    var next = column + step;
                    if (settled.Contains(next) || !CanEnter(next, canEnter) || !surface.CanStep(column, next))
                        continue;
                    var nextCost = cost + (step.x != 0 && step.y != 0 ? 1.41421356f : 1f);
                    if (distance.TryGetValue(next, out var known) && known <= nextCost)
                        continue;
                    distance[next] = nextCost;
                    cameFrom[next] = column;
                    Push(nextCost, next);
                }
            }
        }

        /// <summary>canEnter для столбца — один раз за разлив.</summary>
        private bool CanEnter(Vector2Int column, Func<Vector2Int, bool> canEnter)
        {
            if (canEnter == null)
                return true;
            if (!enterable.TryGetValue(column, out var result))
                enterable[column] = result = canEnter(column);
            return result;
        }

        public bool Reached(Vector2Int column) => distance.ContainsKey(column);

        /// <summary>Длина пути до столбца; бесконечность, если не дошли.</summary>
        public float Distance(Vector2Int column) =>
            distance.TryGetValue(column, out var value) ? value : float.PositiveInfinity;

        /// <summary>Путь от старта до столбца, без стартового столбца. false — не дошли.</summary>
        public bool TryPath(Vector2Int goal, List<Vector2Int> path)
        {
            path.Clear();
            if (!distance.ContainsKey(goal))
                return false;
            for (var column = goal; column != Start; column = cameFrom[column])
                path.Add(column);
            path.Reverse();
            return true;
        }

        /// <summary>Ближайший достигнутый столбец, для которого isGoal вернул true.</summary>
        public bool TryNearest(Func<Vector2Int, bool> isGoal, out Vector2Int nearest)
        {
            nearest = default;
            var best = float.PositiveInfinity;
            foreach (var (column, cost) in distance)
            {
                if (cost < best && isGoal(column))
                {
                    best = cost;
                    nearest = column;
                }
            }
            return !float.IsPositiveInfinity(best);
        }

        private void Push(float cost, Vector2Int column)
        {
            heap.Add((cost, column));
            var i = heap.Count - 1;
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                if (heap[parent].cost <= heap[i].cost)
                    break;
                (heap[parent], heap[i]) = (heap[i], heap[parent]);
                i = parent;
            }
        }

        private (float cost, Vector2Int column) Pop()
        {
            var top = heap[0];
            var last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            var i = 0;
            while (true)
            {
                var left = i * 2 + 1;
                if (left >= heap.Count)
                    break;
                var right = left + 1;
                var child = right < heap.Count && heap[right].cost < heap[left].cost ? right : left;
                if (heap[i].cost <= heap[child].cost)
                    break;
                (heap[i], heap[child]) = (heap[child], heap[i]);
                i = child;
            }
            return top;
        }
    }
}
