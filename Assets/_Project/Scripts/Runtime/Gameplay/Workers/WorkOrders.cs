using System;
using System.Collections.Generic;
using Emberhold.World.Bushes;
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
        /// <summary>
        /// Собрать траву с куста (#88): рабочий срезает куст ударами, трава падает у основания, и он сам несёт её на поддон.
        /// Куст остаётся голым и отрастает.
        /// </summary>
        Gather,
    }

    /// <summary>
    /// Уровень приказа: сначала рабочие берут срочные, потом высокие, потом обычные. Срочный приказ ещё и снимает
    /// ближайшего рабочего с несрочной работы (<see cref="UrgentCall"/>). Порядок значений — порядок важности.
    /// </summary>
    public enum OrderPriority
    {
        Normal,
        High,
        Urgent,
    }

    /// <summary>Куда рабочие носят топливо (костёр). Отдельно от LightSource, чтобы рабочих проверяли без сцены.</summary>
    public interface IFuelTarget
    {
        /// <summary>Сколько топлива ещё помещается.</summary>
        float FuelSpace { get; }

        /// <summary>Насколько огонь полон: 0 — пуст, 1 — полон. Рабочие сначала несут дрова в самый пустой.</summary>
        float FuelFill { get; }

        /// <summary>Подбросить топливо; возвращает, сколько принято.</summary>
        float AddFuel(float amount);
    }

    /// <summary>
    /// Задание: группа приказов, за которую держится рабочий. Взяв приказ задания, рабочий получает класс по виду этого
    /// приказа (<see cref="WorkerRoles.For"/>) и берёт следующие приказы только из задания, пока они есть; потом снова свободный.
    /// Одиночный приказ — своё задание из одного приказа на одного человека; зона (<see cref="WorkZone"/>) — задание из многих
    /// приказов, число людей в нём игрок меняет в панели зоны. Зона выравнивания (#169) держит приказы двух видов —
    /// копать и чертежи (<see cref="Accepts"/>): людей на неё один общий счётчик, а кто кем станет — по взятому приказу.
    /// </summary>
    public sealed class WorkAssignment
    {
        private readonly List<WorkOrder> orders = new();
        private readonly List<Worker> workers = new();

        private readonly WorkKind[] kinds;

        public WorkAssignment(WorkKind kind, int maxWorkers = 1) : this(maxWorkers, kind)
        {
        }

        /// <summary>Задание из приказов нескольких видов; первый — главный (<see cref="Kind"/>).</summary>
        public WorkAssignment(int maxWorkers, params WorkKind[] kinds)
        {
            if (kinds == null || kinds.Length == 0)
                throw new ArgumentException("Нужен хотя бы один вид работы", nameof(kinds));
            this.kinds = (WorkKind[])kinds.Clone();
            MaxWorkers = Math.Max(1, maxWorkers);
        }

        /// <summary>Главный вид работы задания (у зоны копки — копать, даже когда она наращивает).</summary>
        public WorkKind Kind => kinds[0];

        /// <summary>Класс главного вида работы. Рабочий в задании получает класс своего приказа, а не этот.</summary>
        public WorkerRole Role => WorkerRoles.For(Kind);

        /// <summary>
        /// Приказ такого вида может войти в задание: вид — один из видов задания или даёт тот же класс (чертёж блока и
        /// костра — оба строителю).
        /// </summary>
        public bool Accepts(WorkKind kind)
        {
            var role = WorkerRoles.For(kind);
            foreach (var own in kinds)
            {
                if (own == kind || WorkerRoles.For(own) == role)
                    return true;
            }
            return false;
        }

        /// <summary>Сколько людей самое большее держат задание. Не меньше одного.</summary>
        public int MaxWorkers
        {
            get => maxWorkers;
            internal set => maxWorkers = Math.Max(1, value);
        }

        private int maxWorkers;

        /// <summary>Невыполненные приказы задания.</summary>
        public IReadOnlyList<WorkOrder> Orders => orders;

        /// <summary>Кто держит задание.</summary>
        public IReadOnlyList<Worker> Workers => workers;

        /// <summary>Приказов не осталось: выполнены или сняты игроком.</summary>
        public bool IsOver => orders.Count == 0;

        /// <summary>Есть место ещё для одного человека.</summary>
        public bool HasRoom => workers.Count < MaxWorkers;

        /// <summary>
        /// Рабочий лишний: людей в задании больше, чем теперь можно (игрок уменьшил число), и он пришёл позже других.
        /// Лишний доделывает свой приказ и уходит из задания.
        /// </summary>
        public bool IsExtra(Worker worker) => workers.IndexOf(worker) >= MaxWorkers;

        internal void AddOrder(WorkOrder order) => orders.Add(order);
        internal void RemoveOrder(WorkOrder order) => orders.Remove(order);
        internal void Join(Worker worker) => workers.Add(worker);
        internal void Leave(Worker worker) => workers.Remove(worker);
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
            Priority = DefaultPriority(kind);
        }

        /// <summary>С каким уровнем приказ ставится: кормить огонь — высокий, остальное — обычный.</summary>
        public static OrderPriority DefaultPriority(WorkKind kind) => kind == WorkKind.Fuel ? OrderPriority.High : OrderPriority.Normal;

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

        /// <summary>Куст под приказом собрать траву (<see cref="WorkKind.Gather"/>), иначе null.</summary>
        public Bush Bush { get; internal set; }

        /// <summary>Куда нести топливо (для <see cref="WorkKind.Fuel"/>), иначе null.</summary>
        public IFuelTarget FuelTarget { get; internal set; }

        /// <summary>Уровень приказа: чем выше, тем раньше рабочие берут задачу. Меняет <see cref="WorkOrders.RaisePriority"/>.</summary>
        public OrderPriority Priority { get; internal set; }

        /// <summary>Рабочий, который делает задачу, или null.</summary>
        public Worker Worker { get; internal set; }

        /// <summary>Задание, в которое входит приказ; у одиночного приказа — своё, из него одного.</summary>
        public WorkAssignment Assignment { get; internal set; }
    }

    /// <summary>
    /// Общий список задач рабочих: чертежи блоков и костров, приказы копать, рубить и собирать траву, топливо для огня; не больше одной задачи на клетку.
    /// Рабочие сами берут ближайшую свободную. Задача, которую больше нельзя выполнить (клетку заняли,
    /// блок уже выкопали), убирается в <see cref="Prune"/>.
    /// </summary>
    public sealed class WorkOrders
    {
        private readonly List<WorkOrder> orders = new();
        private readonly Dictionary<Vector3Int, WorkOrder> byCell = new();

        public IReadOnlyList<WorkOrder> Orders => orders;

        /// <summary>Номер изменения: растёт, когда задача появилась, исчезла или перешла в другое задание.</summary>
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

        /// <summary>Поручить собрать траву с куста. Null, если куст голый или пропал, или его клетка уже занята задачей.</summary>
        public WorkOrder AddGather(Bush bush)
        {
            if (bush == null || !bush.IsPresent || !bush.IsGrown || byCell.ContainsKey(bush.Cell))
                return null;
            return Add(new WorkOrder(WorkKind.Gather, bush.Cell, Block.Air, false, null, 0) { Bush = bush });
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

        /// <summary>
        /// Перенести приказ в задание (зона из многих приказов, #167). Только приказ вида, который задание принимает
        /// (<see cref="WorkAssignment.Accepts"/>), и которого ещё никто не взял. false — нельзя.
        /// </summary>
        public bool Assign(WorkOrder order, WorkAssignment assignment)
        {
            if (!Contains(order) || assignment == null || order.Worker != null || !assignment.Accepts(order.Kind))
                return false;
            order.Assignment.RemoveOrder(order);
            order.Assignment = assignment;
            assignment.AddOrder(order);
            // Вид рисует приказы зоны иначе, чем одиночные: пусть перерисует.
            Version++;
            return true;
        }

        /// <summary>Отменить задачу (игрок передумал или её нельзя выполнить).</summary>
        public bool Cancel(WorkOrder order)
        {
            if (!Remove(order))
                return false;
            Cancelled?.Invoke(order);
            return true;
        }

        /// <summary>
        /// Поднять уровень приказа по кругу: обычный → высокий → срочный → обычный. false — такой задачи нет.
        /// </summary>
        public bool RaisePriority(WorkOrder order)
        {
            if (!Contains(order))
                return false;
            order.Priority = order.Priority == OrderPriority.Urgent ? OrderPriority.Normal : order.Priority + 1;
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
        /// приказ копать — блок на месте, приказ рубить — дерево или пень стоит, собрать — куст стоит и зелёный,
        /// костёр — клетка пуста, а под ней земля. Топливо носить можно всегда.
        /// </summary>
        public static bool IsValid(VoxelWorld world, WorkOrder order)
        {
            if (order.Kind == WorkKind.Fuel)
                return true;
            if (order.Kind == WorkKind.Gather)
                return order.Bush is { IsPresent: true, IsGrown: true };
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
            order.Assignment ??= new WorkAssignment(order.Kind);
            order.Assignment.AddOrder(order);
            Version++;
            return order;
        }

        private bool Remove(WorkOrder order)
        {
            if (!Contains(order))
                return false;
            orders.Remove(order);
            byCell.Remove(order.Cell);
            order.Assignment.RemoveOrder(order);
            Version++;
            return true;
        }
    }
}
