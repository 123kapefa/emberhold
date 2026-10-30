using System;
using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    public enum WorkKind
    {
        /// <summary>Чертёж: рабочие приносят ресурс и ставят блок.</summary>
        Build,
        /// <summary>Приказ копать: рабочий разбивает блок ударами.</summary>
        Dig,
        /// <summary>
        /// Приказ рубить: рабочий рубит дерево ударами, поленья падают у основания. Тот же приказ корчует пень
        /// (<see cref="WorkOrder.Stump"/>).
        /// </summary>
        Chop,
        /// <summary>Подбросить топливо: рабочий приносит полено и кладёт его в огонь.</summary>
        Fuel,
        /// <summary>Чертёж костра: рабочие приносят поленья и ставят новый костёр, пустой, без топлива.</summary>
        Campfire,
    }

    /// <summary>Куда рабочие носят топливо (костёр). Отдельно от LightSource, чтобы рабочих проверяли без сцены.</summary>
    public interface IFuelTarget
    {
        /// <summary>Сколько топлива ещё помещается.</summary>
        float FuelSpace { get; }

        /// <summary>Подбросить топливо; возвращает, сколько принято.</summary>
        float AddFuel(float amount);
    }

    /// <summary>
    /// Задача для рабочих в одной клетке мира блоков. Задачу делает только один рабочий (<see cref="Worker"/>):
    /// пока он её держит, другие её не берут.
    /// </summary>
    public sealed class WorkOrder
    {
        internal WorkOrder(WorkKind kind, Vector3Int cell, Block block, bool fillsExisting, string itemId, int needed)
        {
            Kind = kind;
            Cell = cell;
            Block = block;
            FillsExisting = fillsExisting;
            ItemId = itemId;
            Needed = needed;
        }

        public WorkKind Kind { get; }
        public Vector3Int Cell { get; }

        /// <summary>Какой блок встанет по чертежу.</summary>
        public Block Block { get; }

        /// <summary>Чертёж досыпает неполный блок до полного, а не ставит новый.</summary>
        public bool FillsExisting { get; }

        /// <summary>Какой ресурс нужен на блок (null — блок бесплатный) и сколько.</summary>
        public string ItemId { get; }
        public int Needed { get; }

        /// <summary>Сколько ресурса уже принесли к чертежу.</summary>
        public int Delivered { get; internal set; }

        public bool NeedsItems => ItemId != null && Delivered < Needed;

        /// <summary>Дерево под приказом рубить, иначе null.</summary>
        public TreeStand Tree { get; internal set; }

        /// <summary>Пень под приказом выкорчевать (вид задачи — <see cref="WorkKind.Chop"/>), иначе null.</summary>
        public TreeStump Stump { get; internal set; }

        /// <summary>Куда нести топливо (для <see cref="WorkKind.Fuel"/>), иначе null.</summary>
        public IFuelTarget FuelTarget { get; internal set; }

        /// <summary>Чем больше, тем раньше рабочие берут задачу: огонь важнее стройки и копки.</summary>
        public int Priority => Kind == WorkKind.Fuel ? 1 : 0;

        /// <summary>Рабочий, который делает задачу, или null.</summary>
        public Worker Worker { get; internal set; }
    }

    /// <summary>
    /// Общий список задач рабочих: чертежи блоков и костров, приказы копать и рубить, топливо для огня; не больше одной задачи на клетку.
    /// Рабочие сами берут ближайшую свободную. Задача, которую больше нельзя выполнить (клетку заняли,
    /// блок уже выкопали), убирается в <see cref="Prune"/>.
    /// </summary>
    public sealed class WorkOrders
    {
        private readonly List<WorkOrder> orders = new();
        private readonly Dictionary<Vector3Int, WorkOrder> byCell = new();

        public IReadOnlyList<WorkOrder> Orders => orders;

        /// <summary>Номер изменения: растёт, когда задача появилась или исчезла.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// Задача отменена или стала невыполнимой. Принесённый к чертежу ресурс нужно вернуть на землю.
        /// </summary>
        public event Action<WorkOrder> Cancelled;

        public WorkOrder At(Vector3Int cell) => byCell.TryGetValue(cell, out var order) ? order : null;

        public bool Contains(WorkOrder order) => order != null && At(order.Cell) == order;

        /// <summary>Поставить чертёж блока; cost — что он стоит (null — бесплатно). Null, если клетка уже занята задачей.</summary>
        public WorkOrder AddBuild(Vector3Int cell, Block block, bool fillsExisting, ItemDrop? cost)
        {
            if (block.IsAir || byCell.ContainsKey(cell))
                return null;
            return Add(new WorkOrder(WorkKind.Build, cell, block, fillsExisting, cost?.ItemId, cost?.Count ?? 0));
        }

        /// <summary>Поручить выкопать блок. Null, если клетка уже занята задачей.</summary>
        public WorkOrder AddDig(Vector3Int cell)
        {
            if (byCell.ContainsKey(cell))
                return null;
            return Add(new WorkOrder(WorkKind.Dig, cell, Block.Air, false, null, 0));
        }

        /// <summary>Поручить срубить дерево. Null, если клетка дерева уже занята задачей.</summary>
        public WorkOrder AddChop(TreeStand tree)
        {
            if (tree == null || !tree.IsStanding || byCell.ContainsKey(tree.Cell))
                return null;
            return Add(new WorkOrder(WorkKind.Chop, tree.Cell, Block.Air, false, null, 0) { Tree = tree });
        }

        /// <summary>Поручить выкорчевать пень. Null, если клетка пня уже занята задачей.</summary>
        public WorkOrder AddUproot(TreeStump stump)
        {
            if (stump == null || !stump.IsPresent || byCell.ContainsKey(stump.Cell))
                return null;
            return Add(new WorkOrder(WorkKind.Chop, stump.Cell, Block.Air, false, null, 0) { Stump = stump });
        }

        /// <summary>
        /// Поручить принести count единиц топлива itemId в огонь target, стоящий в клетке cell.
        /// Null, если клетка уже занята задачей.
        /// </summary>
        public WorkOrder AddFuel(Vector3Int cell, IFuelTarget target, string itemId, int count)
        {
            if (target == null || string.IsNullOrEmpty(itemId) || count <= 0 || byCell.ContainsKey(cell))
                return null;
            return Add(new WorkOrder(WorkKind.Fuel, cell, Block.Air, false, itemId, count) { FuelTarget = target });
        }

        /// <summary>
        /// Поставить чертёж костра в пустой клетке над землёй; cost — сколько поленьев принести на постройку.
        /// Null, если клетка уже занята задачей.
        /// </summary>
        public WorkOrder AddCampfire(Vector3Int cell, ItemDrop cost)
        {
            if (string.IsNullOrEmpty(cost.ItemId) || cost.Count <= 0 || byCell.ContainsKey(cell))
                return null;
            return Add(new WorkOrder(WorkKind.Campfire, cell, Block.Air, false, cost.ItemId, cost.Count));
        }

        /// <summary>Отменить задачу (игрок передумал или её нельзя выполнить).</summary>
        public bool Cancel(WorkOrder order)
        {
            if (!Remove(order))
                return false;
            Cancelled?.Invoke(order);
            return true;
        }

        /// <summary>Задача выполнена.</summary>
        public bool Complete(WorkOrder order) => Remove(order);

        /// <summary>Отменить задачи, которые в нынешнем мире выполнить нельзя. Возвращает, сколько отменено.</summary>
        public int Prune(VoxelWorld world)
        {
            var cancelled = 0;
            for (var i = orders.Count - 1; i >= 0; i--)
            {
                var order = orders[i];
                if (IsValid(world, order))
                    continue;
                Cancel(order);
                cancelled++;
            }
            return cancelled;
        }

        /// <summary>
        /// Можно ли ещё выполнить задачу: чертёж — клетка свободна (или там тот же неполный блок, который досыпаем),
        /// приказ копать — блок на месте, приказ рубить — дерево или пень стоит, костёр — клетка пуста, а под ней земля.
        /// Топливо носить можно всегда.
        /// </summary>
        public static bool IsValid(VoxelWorld world, WorkOrder order)
        {
            if (order.Kind == WorkKind.Fuel)
                return true;
            if (order.Kind == WorkKind.Chop)
                return order.Stump?.IsPresent ?? order.Tree.IsStanding;
            if (!world.IsInsideBlocks(order.Cell))
                return false;
            var current = world.GetBlock(order.Cell);
            if (order.Kind == WorkKind.Campfire)
                return CanHoldCampfire(world, order.Cell);
            if (order.Kind == WorkKind.Dig)
                return !current.IsAir && BlockMaterials.IsBreakable(current.Material);
            if (!order.FillsExisting)
                return current.IsAir;
            return !current.IsAir && current.Material == order.Block.Material && !current.Shape.IsCatalog && !current.Shape.IsFull;
        }

        /// <summary>Костёр может стоять в клетке: она пуста, а под ней блок, на который он встанет.</summary>
        public static bool CanHoldCampfire(VoxelWorld world, Vector3Int cell)
        {
            var below = cell + Vector3Int.down;
            return world.IsInsideBlocks(cell) && world.IsInsideBlocks(below)
                && world.GetBlock(cell).IsAir && !world.GetBlock(below).IsAir;
        }

        private WorkOrder Add(WorkOrder order)
        {
            orders.Add(order);
            byCell[order.Cell] = order;
            Version++;
            return order;
        }

        private bool Remove(WorkOrder order)
        {
            if (!Contains(order))
                return false;
            orders.Remove(order);
            byCell.Remove(order.Cell);
            Version++;
            return true;
        }
    }
}
