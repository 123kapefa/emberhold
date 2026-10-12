using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// По чему ходят рабочие: карта столбцов (x, z) мира блоков, рабочий стоит на верхе самого верхнего блока
    /// в столбце. Пещер и мостов в прототипе нет, поэтому одной высоты на столбец хватает.
    /// Шагнуть в соседний столбец можно, если перепад высот не больше <see cref="MaxStep"/>.
    /// Координаты — мира блоков (локальные координаты VoxelWorldView).
    /// </summary>
    public sealed class WalkSurface
    {
        private readonly VoxelWorld world;

        public WalkSurface(VoxelWorld world, float maxStep)
        {
            this.world = world;
            MaxStep = maxStep;
        }

        public VoxelWorld World => world;

        /// <summary>Самый большой перепад высот между соседними столбцами, который рабочий проходит, в блоках.</summary>
        public float MaxStep { get; }

        public bool Contains(Vector2Int column)
        {
            var size = world.SizeInBlocks;
            return (uint)column.x < size.x && (uint)column.y < size.z;
        }

        /// <summary>Высота, на которой стоит рабочий в столбце: верх верхнего блока (средний по углам, как у кучек).</summary>
        public float Height(Vector2Int column) =>
            DroppedItems.RestHeight(world, new Vector3Int(column.x, world.SizeInBlocks.y - 1, column.y));

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

        private bool CanClimb(Vector2Int from, Vector2Int to) =>
            Contains(to) && Mathf.Abs(Height(to) - Height(from)) <= MaxStep + 1e-3f;

        /// <summary>Столбец, в котором стоит точка мира блоков.</summary>
        public static Vector2Int ColumnOf(Vector3 position) =>
            new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));

        /// <summary>Точка, где стоит рабочий в столбце: центр столбца на его верхе.</summary>
        public Vector3 StandPoint(Vector2Int column) => new(column.x + 0.5f, Height(column), column.y + 0.5f);
    }
}
