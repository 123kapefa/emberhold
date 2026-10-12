using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// По чему ходят рабочие: карта столбцов (x, z) мира блоков, рабочий стоит на верхе самого верхнего блока
    /// в столбце. Пещер и мостов в прототипе нет, поэтому одной высоты на столбец хватает.
    /// Шагнуть в соседний столбец можно, если подъём не больше <see cref="MaxStep"/>, а спуск — не больше <see cref="MaxDrop"/>.
    /// Координаты — мира блоков (локальные координаты VoxelWorldView).
    /// </summary>
    public sealed class WalkSurface
    {
        private readonly VoxelWorld world;
        private readonly Dictionary<Vector2Int, float> cachedHeights = new();
        private bool caching;

        public WalkSurface(VoxelWorld world, float maxStep)
            : this(world, maxStep, maxStep)
        {
        }

        public WalkSurface(VoxelWorld world, float maxStep, float maxDrop)
        {
            this.world = world;
            MaxStep = maxStep;
            MaxDrop = maxDrop;
        }

        public VoxelWorld World => world;

        /// <summary>Самый большой перепад высот между соседними столбцами, который рабочий проходит, в блоках.</summary>
        public float MaxStep { get; }

        /// <summary>С какой высоты можно спуститься в соседний столбец, в блоках. Рабочим — как MaxStep, ползунам — любой.</summary>
        public float MaxDrop { get; }

        public bool Contains(Vector2Int column)
        {
            var size = world.SizeInBlocks;
            return (uint)column.x < size.x && (uint)column.y < size.z;
        }

        /// <summary>Высота, на которой стоит рабочий в столбце: верх верхнего блока (средний по углам, как у кучек).</summary>
        public float Height(Vector2Int column)
        {
            if (caching && cachedHeights.TryGetValue(column, out var cached))
                return cached;
            var height = DroppedItems.RestHeight(world, new Vector3Int(column.x, world.SizeInBlocks.y - 1, column.y));
            if (caching)
                cachedHeights[column] = height;
            return height;
        }

        /// <summary>
        /// Запоминать высоты столбцов, пока мир не меняется (на время одного поиска пути):
        /// каждый шаг спрашивает высоту нескольких столбцов, а она ищется сверху вниз по всему столбцу.
        /// </summary>
        public void BeginCache()
        {
            cachedHeights.Clear();
            caching = true;
        }

        public void EndCache()
        {
            caching = false;
            cachedHeights.Clear();
        }

        /// <summary>
        /// Можно ли шагнуть из столбца from в соседний столбец to (в том числе по диагонали).
        /// По диагонали — только если проходимы оба прямых обхода, чтобы не срезать угол стены.
        /// </summary>
        public bool CanStep(Vector2Int from, Vector2Int to)
        {
            if (!Contains(to) || !CanClimb(from, to))
                return false;
            if (from.x == to.x || from.y == to.y)
                return true;
            var a = new Vector2Int(to.x, from.y);
            var b = new Vector2Int(from.x, to.y);
            return CanClimb(from, a) && CanClimb(a, to) && CanClimb(from, b) && CanClimb(b, to);
        }

        private bool CanClimb(Vector2Int from, Vector2Int to)
        {
            if (!Contains(to))
                return false;
            var rise = Height(to) - Height(from);
            return rise <= MaxStep + 1e-3f && -rise <= MaxDrop + 1e-3f;
        }

        /// <summary>Столбец, в котором стоит точка мира блоков.</summary>
        public static Vector2Int ColumnOf(Vector3 position) =>
            new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));

        /// <summary>Точка, где стоит рабочий в столбце: центр столбца на его верхе.</summary>
        public Vector3 StandPoint(Vector2Int column) => new(column.x + 0.5f, Height(column), column.y + 0.5f);
    }
}
