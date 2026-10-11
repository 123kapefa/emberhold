using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Items
{
    /// <summary>
    /// Кучка ресурса на земле: один вид ресурса в одной клетке. Position — точка опоры
    /// в координатах мира блоков (низ кучки), Drop.Count — сколько в ней единиц.
    /// </summary>
    public sealed class DroppedItem
    {
        public DroppedItem(ItemDrop drop, Vector3 position)
        {
            Drop = drop;
            Position = position;
        }

        public ItemDrop Drop { get; internal set; }
        public Vector3 Position { get; internal set; }

        /// <summary>Клетка мира, в которой лежит кучка.</summary>
        public Vector3Int Cell => new(Mathf.FloorToInt(Position.x), Mathf.FloorToInt(Position.y), Mathf.FloorToInt(Position.z));
    }

    /// <summary>
    /// Ресурсы, выпавшие из разрушенных блоков. Предмет появляется в центре клетки разрушенного блока
    /// и лежит на верхе ближайшего блока под ним. Ресурсы одного вида в одной клетке складываются в одну кучку. Если опору выкопали, <see cref="Settle"/> опускает его
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
            Version++;
            if (TryMerge(item, out var pile))
                return pile;
            items.Add(item);
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

        /// <summary>
        /// Положить все предметы на опору заново, если мир под ними изменился; упавшая кучка сливается
        /// с кучкой того же ресурса, на которую упала. Возвращает true, если что-то сдвинулось.
        /// </summary>
        public bool Settle(VoxelWorld world)
        {
            var moved = false;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var p = item.Position;
                var height = RestHeight(world, item.Cell);
                if (Mathf.Approximately(height, p.y))
                    continue;
                item.Position = new Vector3(p.x, height, p.z);
                moved = true;
                items.RemoveAt(i);
                if (!TryMerge(item, out _))
                    items.Insert(i, item);
                else
                    i--;
            }
            if (moved)
                Version++;
            return moved;
        }

        /// <summary>Добавить item в кучку того же ресурса в той же клетке, если такая есть.</summary>
        private bool TryMerge(DroppedItem item, out DroppedItem pile)
        {
            foreach (var other in items)
            {
                if (other.Drop.ItemId == item.Drop.ItemId && other.Cell == item.Cell)
                {
                    other.Drop = new ItemDrop(other.Drop.ItemId, other.Drop.Count + item.Drop.Count);
                    pile = other;
                    return true;
                }
            }
            pile = null;
            return false;
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
