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
    /// и лежит на верхе ближайшего блока под ним. Если опору выкопали, <see cref="Settle"/> опускает его
    /// на следующий блок, а если на его месте поставили блок — поднимает на верх этого блока.
    /// Ресурсы одного вида в одной клетке складываются в одну кучку.
    /// Только данные: показывает их вид, забирают рабочие.
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
            return Drop(world, position, drop);
        }

        /// <summary>
        /// Положить ресурс в центр клетки cell на ближайший блок под ней (например, рабочий бросил ношу).
        /// Возвращает кучку, в которой он оказался.
        /// </summary>
        public DroppedItem Drop(VoxelWorld world, Vector3Int cell, ItemDrop drop)
        {
            var item = new DroppedItem(drop, new Vector3(cell.x + 0.5f, RestHeight(world, cell), cell.z + 0.5f));
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
        /// Взять из кучки до count единиц; опустевшая кучка исчезает. Возвращает false, если кучки уже нет.
        /// </summary>
        public bool Take(DroppedItem pile, int count, out ItemDrop taken)
        {
            taken = default;
            if (count <= 0 || !items.Contains(pile))
                return false;
            var amount = Mathf.Min(count, pile.Drop.Count);
            taken = new ItemDrop(pile.Drop.ItemId, amount);
            if (amount == pile.Drop.Count)
                items.Remove(pile);
            else
                pile.Drop = new ItemDrop(pile.Drop.ItemId, pile.Drop.Count - amount);
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
        /// и ниже. Высота простого блока берётся средней по углам, чтобы на склоне предмет не висел над землёй;
        /// у фигурной формы, которая не закрывает пол (столб, балка, полуарка), — её верх, чтобы предмет не утонул в ней.
        /// Если под предметом до дна мира пусто, он лежит на дне. На нетронутой гладкой земле
        /// (<see cref="TerrainSurface"/>) — высота поверхности в середине клетки.
        /// </summary>
        public static float RestHeight(VoxelWorld world, Vector3Int cell)
        {
            var top = world.SizeInBlocks.y - 1;
            for (var y = Mathf.Min(cell.y, top); y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(cell.x, y, cell.z));
                if (!block.IsAir && world.Surface != null && world.Surface.TryGroundHeight(cell.x, y, cell.z, out var ground))
                    return ground;
                if (!block.IsAir)
                    return y + (block.Shape.IsCatalog && !block.Shape.CoversFloor ? block.Shape.Top / (float)BlockShape.MaxHeight : block.Shape.Fill);
            }
            return 0f;
        }
    }
}
