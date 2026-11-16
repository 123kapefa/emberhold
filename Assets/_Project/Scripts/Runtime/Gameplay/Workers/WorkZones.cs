using System.Collections.Generic;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Зона работ: область на карте, в которой отряд сам ставит приказы одного вида. Ставится черновиком (<see cref="IsDraft"/>):
    /// игрок настраивает её и начинает. Живёт, пока игрок её не снимет
    /// (зона копки ещё и сама уходит, когда копать нечего). Зона — задание (<see cref="WorkAssignment"/>): её приказы
    /// держат не больше <see cref="WorkAssignment.MaxWorkers"/> человек и получают уровень зоны (<see cref="Priority"/>).
    /// Меняет зону только <see cref="WorkZones"/>.
    /// </summary>
    public abstract class WorkZone
    {
        private protected WorkZone(WorkKind kind, int maxWorkers) => Assignment = new WorkAssignment(kind, maxWorkers);

        /// <summary>Какие приказы ставит зона.</summary>
        public WorkKind Kind => Assignment.Kind;

        /// <summary>Уровень, который получают приказы зоны.</summary>
        public OrderPriority Priority { get; internal set; }

        /// <summary>Задание зоны: её приказы и кто их держит.</summary>
        public WorkAssignment Assignment { get; }

        /// <summary>
        /// Черновик: зона видна и настраивается, но приказов не ставит и рабочих не зовёт, пока её не начнут
        /// (<see cref="WorkZones.Start"/>). Черновик сам не уходит, даже если делать в нём нечего.
        /// </summary>
        public bool IsDraft { get; internal set; }

        /// <summary>Зона сделала всё, что могла, и уходит (у зоны рубки так не бывает: она ждёт новых деревьев).</summary>
        public bool IsFinished { get; private protected set; }

        /// <summary>Столбец клетки в зоне (высота не важна).</summary>
        public abstract bool Covers(Vector3Int cell);

        /// <summary>Середина зоны на карте, в координатах мира блоков (x, z): по ней выбирается ближайшая из перекрытых зон.</summary>
        public abstract Vector2 Center { get; }

        /// <summary>Приказ ещё нужен зоне после смены её размеров или уровня; ненужные снимаются.</summary>
        internal virtual bool Keeps(WorkOrder order) => Covers(order.Cell);

        /// <summary>Поставить приказы, которых не хватает; take — забрать приказ в зону (false — его забрать нельзя).</summary>
        internal abstract void Refresh(VoxelWorld world, Forest forest, WorkOrders orders, System.Func<WorkOrder, bool> take);
    }

    /// <summary>
    /// Зона рубки (#167): круг, в котором рабочие рубят все деревья и корчуют все пни, в том числе те, что
    /// появятся в нём позже (пень срубленного дерева тоже).
    /// </summary>
    public sealed class ChopZone : WorkZone
    {
        public const int MinRadius = 1;
        public const int MaxRadius = 16;
        public const int DefaultRadius = 4;

        internal ChopZone(Vector3Int cell, int radius, int maxWorkers) : base(WorkKind.Chop, maxWorkers)
        {
            Cell = cell;
            Radius = Mathf.Clamp(radius, MinRadius, MaxRadius);
        }

        /// <summary>Клетка центра зоны: пустая клетка над землёй, куда кликнул игрок.</summary>
        public Vector3Int Cell { get; }

        /// <summary>Радиус в блоках: дерево или пень в зоне, если середина его клетки не дальше радиуса от середины центра.</summary>
        public int Radius { get; internal set; }

        public override Vector2 Center => new(Cell.x + 0.5f, Cell.z + 0.5f);

        public override bool Covers(Vector3Int cell)
        {
            var dx = cell.x - Cell.x;
            var dz = cell.z - Cell.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }

        internal override void Refresh(VoxelWorld world, Forest forest, WorkOrders orders, System.Func<WorkOrder, bool> take)
        {
            if (forest == null)
                return;
            foreach (var tree in forest.Trees)
            {
                if (tree.IsStanding && Covers(tree.Cell))
                    take(orders.At(tree.Cell) ?? orders.AddChop(tree));
            }
            foreach (var stump in forest.Stumps)
            {
                if (stump.IsPresent && Covers(stump.Cell))
                    take(orders.At(stump.Cell) ?? orders.AddUproot(stump));
            }
        }
    }

    /// <summary>Что зона с уровнем делает с землёй. Пока только выкапывает; выравнивание (#169) добавит режимы сюда.</summary>
    public enum LevelZoneMode
    {
        /// <summary>Выкопать всё выше уровня.</summary>
        Dig,
    }

    /// <summary>
    /// Зона с уровнем (#136): прямоугольник столбцов и целевой уровень <see cref="Level"/> — высота блока, который остаётся
    /// верхним. В режиме <see cref="LevelZoneMode.Dig"/> рабочие выкапывают всё выше уровня слоями сверху: приказ стоит
    /// только на верхнем блоке столбца, следующий появится, когда его выкопают (а соседние столбцы держит #146).
    /// Скала и коренная порода (#97) и блоки под пнями не копаются: такой столбец считается готовым. Тёмные приказы ждут
    /// света. Когда во всём прямоугольнике выше уровня копать нечего, зона уходит.
    /// </summary>
    public sealed class LevelZone : WorkZone
    {
        public const int MinSize = 1;
        public const int MaxSize = 32;

        internal LevelZone(Vector2Int min, Vector2Int size, int level, int maxWorkers) : base(WorkKind.Dig, maxWorkers)
        {
            Min = min;
            Size = ClampSize(size);
            Level = level;
        }

        public LevelZoneMode Mode => LevelZoneMode.Dig;

        /// <summary>Ближний угол прямоугольника: наименьшие x и z столбцов.</summary>
        public Vector2Int Min { get; }

        /// <summary>Длина (по x) и ширина (по z) в столбцах; угол <see cref="Min"/> при смене размера стоит на месте.</summary>
        public Vector2Int Size { get; internal set; }

        /// <summary>Целевой уровень: высота (y) блока, который останется верхним; всё выше выкапывается.</summary>
        public int Level { get; internal set; }

        public override Vector2 Center => new(Min.x + Size.x * 0.5f, Min.y + Size.y * 0.5f);

        public override bool Covers(Vector3Int cell) =>
            cell.x >= Min.x && cell.x < Min.x + Size.x && cell.z >= Min.y && cell.z < Min.y + Size.y;

        internal override bool Keeps(WorkOrder order) => Covers(order.Cell) && order.Cell.y > Level;

        internal static Vector2Int ClampSize(Vector2Int size) =>
            new(Mathf.Clamp(size.x, MinSize, MaxSize), Mathf.Clamp(size.y, MinSize, MaxSize));

        internal override void Refresh(VoxelWorld world, Forest forest, WorkOrders orders, System.Func<WorkOrder, bool> take)
        {
            var work = false;
            for (var z = Min.y; z < Min.y + Size.y; z++)
            for (var x = Min.x; x < Min.x + Size.x; x++)
            {
                if (!TryTopToDig(world, forest, x, z, Level, out var cell))
                    continue;
                work = true;
                var order = orders.At(cell);
                if (order == null)
                    take(orders.AddDig(cell));
                else if (order.Kind == WorkKind.Dig)
                    take(order);
            }
            IsFinished = !work;
        }

        /// <summary>Верхний блок столбца выше уровня level, если его можно копать: не скала и не под пнём.</summary>
        internal static bool TryTopToDig(VoxelWorld world, Forest forest, int x, int z, int level, out Vector3Int cell)
        {
            cell = default;
            if (x < 0 || z < 0 || x >= world.SizeInBlocks.x || z >= world.SizeInBlocks.z)
                return false;
            for (var y = world.SizeInBlocks.y - 1; y > level; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (block.IsAir)
                    continue;
                cell = new Vector3Int(x, y, z);
                return BlockMaterials.IsBreakable(block.Material) && (forest == null || !forest.BlocksEdit(cell));
            }
            return false;
        }
    }

    /// <summary>
    /// Все зоны работ отряда: рубки (<see cref="ChopZone"/>) и с уровнем (<see cref="LevelZone"/>). <see cref="Update"/> раз
    /// в кадр ставит в зонах приказы и забирает в зону уже стоящие там одиночные приказы того же вида, если их ещё никто
    /// не взял. Клетка в нескольких зонах достаётся той, что поставлена раньше. Чистая логика, без сцены.
    /// </summary>
    public sealed class WorkZones
    {
        public const int DefaultMaxWorkers = 2;

        private readonly List<WorkZone> zones = new();
        private readonly List<WorkOrder> scratch = new();

        public IReadOnlyList<WorkZone> All => zones;

        /// <summary>Номер изменения: растёт, когда зону поставили, сняли или поменяли.</summary>
        public int Version { get; private set; }

        /// <summary>Зона, в которую входит приказ, или null — приказ одиночный.</summary>
        public WorkZone ZoneOf(WorkOrder order)
        {
            foreach (var zone in zones)
            {
                if (order.Assignment == zone.Assignment)
                    return zone;
            }
            return null;
        }

        /// <summary>Зона ещё стоит: её не сняли, и она не ушла сама.</summary>
        public bool Contains(WorkZone zone) => zones.Contains(zone);

        /// <summary>
        /// Поставить зону рубки с центром в клетке cell; радиус и число людей обрезаются до допустимых. draft — черновиком:
        /// работа начнётся по <see cref="Start"/>.
        /// </summary>
        public ChopZone AddChop(Vector3Int cell, int radius, int maxWorkers = DefaultMaxWorkers, bool draft = false) =>
            Add(new ChopZone(cell, radius, maxWorkers) { IsDraft = draft });

        /// <summary>
        /// Поставить зону копки на прямоугольник между столбцами двух блоков (в любом порядке); уровень — на блок ниже
        /// блока from (снять один слой).
        /// </summary>
        public LevelZone AddLevel(Vector3Int from, Vector3Int to, int maxWorkers = DefaultMaxWorkers, bool draft = false)
        {
            var min = new Vector2Int(Mathf.Min(from.x, to.x), Mathf.Min(from.z, to.z));
            var size = new Vector2Int(Mathf.Abs(to.x - from.x) + 1, Mathf.Abs(to.z - from.z) + 1);
            return Add(new LevelZone(min, size, from.y - 1, maxWorkers) { IsDraft = draft });
        }

        /// <summary>Начать черновик: со следующего <see cref="Update"/> зона ставит приказы. false — зона не черновик или её нет.</summary>
        public bool Start(WorkZone zone)
        {
            if (!zones.Contains(zone) || !zone.IsDraft)
                return false;
            zone.IsDraft = false;
            Version++;
            return true;
        }

        private T Add<T>(T zone) where T : WorkZone
        {
            zones.Add(zone);
            Version++;
            return zone;
        }

        /// <summary>
        /// Зона вида kind (null — любая), в которую попадает столбец клетки; из нескольких — с ближайшей серединой.
        /// Null — ни в какой.
        /// </summary>
        public WorkZone At(Vector3Int cell, WorkKind? kind = null)
        {
            WorkZone best = null;
            var bestDistance = float.PositiveInfinity;
            var point = new Vector2(cell.x + 0.5f, cell.z + 0.5f);
            foreach (var zone in zones)
            {
                if (kind != null && zone.Kind != kind || !zone.Covers(cell))
                    continue;
                var distance = (zone.Center - point).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                best = zone;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>Снять зону: её приказы снимаются, даже взятые (рабочий бросает дело). false — такой зоны нет.</summary>
        public bool Remove(WorkZone zone, WorkOrders orders)
        {
            if (!zones.Remove(zone))
                return false;
            CancelWhere(zone, orders, _ => true);
            Version++;
            return true;
        }

        /// <summary>Сменить радиус зоны рубки; приказы, оставшиеся за кругом, снимаются. Возвращает новый радиус.</summary>
        public int SetRadius(ChopZone zone, int radius, WorkOrders orders)
        {
            radius = Mathf.Clamp(radius, ChopZone.MinRadius, ChopZone.MaxRadius);
            if (zone.Radius != radius)
            {
                zone.Radius = radius;
                Reshaped(zone, orders);
            }
            return radius;
        }

        /// <summary>Сменить длину и ширину зоны с уровнем (угол Min на месте); лишние приказы снимаются. Возвращает новый размер.</summary>
        public Vector2Int SetSize(LevelZone zone, Vector2Int size, WorkOrders orders)
        {
            size = LevelZone.ClampSize(size);
            if (zone.Size != size)
            {
                zone.Size = size;
                Reshaped(zone, orders);
            }
            return size;
        }

        /// <summary>Сменить целевой уровень; приказы на уровне и ниже снимаются, новые встанут в <see cref="Update"/>.</summary>
        public void SetLevel(LevelZone zone, int level, WorkOrders orders)
        {
            if (zone.Level == level)
                return;
            zone.Level = level;
            Reshaped(zone, orders);
        }

        /// <summary>Сменить уровень зоны: его получают все её приказы.</summary>
        public void SetPriority(WorkZone zone, OrderPriority priority)
        {
            zone.Priority = priority;
            foreach (var order in zone.Assignment.Orders)
                order.Priority = priority;
            Version++;
        }

        /// <summary>Сменить, сколько людей держат зону: от одного до limit. Лишние уходят, доделав свой приказ.</summary>
        public int SetMaxWorkers(WorkZone zone, int maxWorkers, int limit)
        {
            zone.Assignment.MaxWorkers = Mathf.Clamp(maxWorkers, 1, Mathf.Max(1, limit));
            Version++;
            return zone.Assignment.MaxWorkers;
        }

        /// <summary>
        /// Поставить приказы во всех зонах; зоны, которым делать больше нечего, уходят. Возвращает, сколько приказов
        /// добавлено в зоны. forest может быть null (тогда зоны рубки ничего не ставят).
        /// </summary>
        public int Update(VoxelWorld world, Forest forest, WorkOrders orders)
        {
            var added = 0;
            foreach (var zone in zones)
            {
                // Черновик только виден и настраивается: приказов не ставит и не уходит.
                if (zone.IsDraft)
                    continue;
                zone.Refresh(world, forest, orders, order =>
                {
                    if (!Take(zone, orders, order))
                        return false;
                    added++;
                    return true;
                });
            }
            for (var i = zones.Count - 1; i >= 0; i--)
            {
                if (!zones[i].IsDraft && zones[i].IsFinished)
                    Remove(zones[i], orders);
            }
            return added;
        }

        private void Reshaped(WorkZone zone, WorkOrders orders)
        {
            CancelWhere(zone, orders, order => !zone.Keeps(order));
            Version++;
        }

        /// <summary>
        /// Забрать приказ в зону с её уровнем: только того же вида, ничей (не другой зоны), ещё не взятый, и если клетка
        /// достаётся этой зоне (а не поставленной раньше).
        /// </summary>
        private bool Take(WorkZone zone, WorkOrders orders, WorkOrder order)
        {
            if (order == null || order.Kind != zone.Kind || order.Assignment == zone.Assignment || IsZoneOrder(order)
                || Owner(order.Cell, zone.Kind) != zone || !orders.Assign(order, zone.Assignment))
                return false;
            order.Priority = zone.Priority;
            return true;
        }

        /// <summary>Зона вида kind, которой достаётся клетка: первая поставленная из накрывающих её.</summary>
        private WorkZone Owner(Vector3Int cell, WorkKind kind)
        {
            foreach (var zone in zones)
            {
                if (zone.Kind == kind && !zone.IsDraft && zone.Covers(cell))
                    return zone;
            }
            return null;
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

        private void CancelWhere(WorkZone zone, WorkOrders orders, System.Predicate<WorkOrder> match)
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
