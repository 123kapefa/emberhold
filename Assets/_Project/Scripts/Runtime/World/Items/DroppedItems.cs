using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Items
{
    /// <summary>Ресурс, лежащий на земле. Position — точка опоры в координатах мира блоков (низ предмета).</summary>
    public sealed class DroppedItem
    {
        public DroppedItem(ItemDrop drop, Vector3 position)
        {
            Drop = drop;
            Position = position;
        }

        public ItemDrop Drop { get; }
        public Vector3 Position { get; internal set; }
    }

    /// <summary>
    /// Ресурсы, выпавшие из разрушенных блоков. Предмет появляется в центре клетки разрушенного блока
    /// и лежит на верхе ближайшего блока под ним. Если опору выкопали, <see cref="Settle"/> опускает его
    /// на следующий блок, а если на его месте поставили блок — поднимает на верх этого блока.
    /// Только данные: показывает их вид, позже их заберут рабочие.
    /// </summary>
    public sealed class DroppedItems
    {
        private readonly List<DroppedItem> items = new();
        private readonly IBlockDrops drops;

        public DroppedItems(IBlockDrops drops)
        {
            this.drops = drops;
        }

        public IReadOnlyList<DroppedItem> Items => items;

        /// <summary>Номер изменения: растёт, когда предмет появился, исчез или сдвинулся.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// Блок разрушен: если из его материала что-то выпадает, кладём предмет на его место.
        /// Возвращает выпавший предмет или null.
        /// </summary>
        public DroppedItem OnBlockDestroyed(VoxelWorld world, Vector3Int position, Block destroyed)
        {
            if (destroyed.IsAir || !drops.TryGetDrop(destroyed.Material, out var drop))
                return null;
            var item = new DroppedItem(drop, new Vector3(position.x + 0.5f, RestHeight(world, position), position.z + 0.5f));
            items.Add(item);
            Version++;
            return item;
        }

        /// <summary>Убрать предмет (например, его подобрал рабочий).</summary>
        public bool Remove(DroppedItem item)
        {
            if (!items.Remove(item))
                return false;
            Version++;
            return true;
        }

        /// <summary>Положить все предметы на опору заново, если мир под ними изменился. Возвращает true, если что-то сдвинулось.</summary>
        public bool Settle(VoxelWorld world)
        {
            var moved = false;
            foreach (var item in items)
            {
                var p = item.Position;
                var cell = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                var height = RestHeight(world, cell);
                if (Mathf.Approximately(height, p.y))
                    continue;
                item.Position = new Vector3(p.x, height, p.z);
                moved = true;
            }
            if (moved)
                Version++;
            return moved;
        }

        /// <summary>
        /// Высота, на которой лежит предмет в клетке cell: верх первого непустого блока, начиная с самой клетки
        /// и ниже. Высота блока берётся средней по углам, чтобы на склоне предмет не висел над землёй.
        /// Если под предметом до дна мира пусто, он лежит на дне.
        /// </summary>
        public static float RestHeight(VoxelWorld world, Vector3Int cell)
        {
            var top = world.SizeInBlocks.y - 1;
            for (var y = Mathf.Min(cell.y, top); y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(cell.x, y, cell.z));
                if (!block.IsAir)
                    return y + block.Shape.Fill;
            }
            return 0f;
        }
    }
}
