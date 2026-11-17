using System;
using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Зона работ: область на карте, в которой отряд сам ставит приказы (у зоны рубки — рубить, у зоны с уровнем — копать
    /// и наращивать). Ставится черновиком (<see cref="IsDraft"/>): игрок настраивает её и начинает. Живёт, пока игрок её
    /// не снимет (зона с уровнем ещё и сама уходит, когда в её режиме делать нечего). Зона — задание (<see cref="WorkAssignment"/>): её приказы
    /// держат не больше <see cref="WorkAssignment.MaxWorkers"/> человек и получают уровень зоны (<see cref="Priority"/>).
    /// Меняет зону только <see cref="WorkZones"/>.
    /// </summary>
    public abstract class WorkZone
    {
        private protected WorkZone(int maxWorkers, params WorkKind[] kinds) => Assignment = new WorkAssignment(maxWorkers, kinds);

        /// <summary>Главный вид приказов зоны: по нему зона находится (<see cref="WorkZones.At"/>).</summary>
        public WorkKind Kind => Assignment.Kind;

        /// <summary>Приказы такого вида зона ставит и забирает себе.</summary>
        public bool Accepts(WorkKind kind) => Assignment.Accepts(kind);

        /// <summary>Уровень, который получают приказы зоны.</summary>
        public OrderPriority Priority { get; internal set; }

        /// <summary>Задание зоны: её приказы и кто их держит.</summary>
        public WorkAssignment Assignment { get; }

        /// <summary>
        /// Черновик: зона видна и настраивается, но приказов не ставит и рабочих не зовёт, пока её не начнут
        /// (<see cref="WorkZones.Start"/>). Черновик сам не уходит, даже если делать в нём нечего.
        /// </summary>
        public bool IsDraft { get; internal set; }

        /// <summary>Зона сделала всё, что могла, и уходит (у зон рубки и сбора так не бывает: они ждут новых деревьев и травы).</summary>
        public bool IsFinished { get; private protected set; }

        /// <summary>Столбец клетки в зоне (высота не важна).</summary>
        public abstract bool Covers(Vector3Int cell);

        /// <summary>Середина зоны на карте, в координатах мира блоков (x, z): по ней выбирается ближайшая из перекрытых зон.</summary>
        public abstract Vector2 Center { get; }

        /// <summary>Приказ ещё нужен зоне после смены её размеров или уровня; ненужные снимаются.</summary>
        internal virtual bool Keeps(WorkOrder order) => Covers(order.Cell);

        /// <summary>Поставить приказы, которых не хватает; take — забрать приказ в зону (false — его забрать нельзя).</summary>
        internal abstract void Refresh(ZoneRefresh refresh, Func<WorkOrder, bool> take);
    }

    /// <summary>
    /// Зона-круг: центр в клетке и радиус в блоках. Общее у зоны рубки (<see cref="ChopZone"/>) и зоны сбора
    /// (<see cref="GatherZone"/>): ручки переноса и радиуса, радиус кнопками и [ ].
    /// </summary>
    public abstract class CircleZone : WorkZone
    {
        public const int MinRadius = 1;
        public const int MaxRadius = 16;
        public const int DefaultRadius = 4;

        private protected CircleZone(Vector3Int cell, int radius, int maxWorkers, WorkKind kind) : base(maxWorkers, kind)
        {
            Cell = cell;
            Radius = Mathf.Clamp(radius, MinRadius, MaxRadius);
        }

        /// <summary>Клетка центра зоны: пустая клетка над землёй, куда кликнул игрок (или куда перенесли зону).</summary>
        public Vector3Int Cell { get; internal set; }

        /// <summary>Радиус в блоках: дерево, пень или куст в зоне, если середина его клетки не дальше радиуса от середины центра.</summary>
        public int Radius { get; internal set; }

        public override Vector2 Center => new(Cell.x + 0.5f, Cell.z + 0.5f);

        public override bool Covers(Vector3Int cell)
        {
            var dx = cell.x - Cell.x;
            var dz = cell.z - Cell.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }
    }

    /// <summary>
    /// Зона рубки (#167): круг, в котором рабочие рубят все деревья и корчуют все пни, в том числе те, что
    /// появятся в нём позже (пень срубленного дерева тоже). Кусты не трогает.
    /// </summary>
    public sealed class ChopZone : CircleZone
    {
        internal ChopZone(Vector3Int cell, int radius, int maxWorkers) : base(cell, radius, maxWorkers, WorkKind.Chop)
        {
        }

        internal override void Refresh(ZoneRefresh refresh, Func<WorkOrder, bool> take)
        {
            var forest = refresh.Forest;
            var orders = refresh.Orders;
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

    /// <summary>
    /// Зона сбора (#88, клавиша H): круг, в котором рабочие собирают траву со всех зелёных кустов. Деревья и пни не трогает.
    /// Голый куст ждёт: зона не уходит, и когда куст снова зелёный, в нём снова встаёт приказ. Как у рубки, тёмные кусты
    /// ждут света (приказ стоит, но рабочие берут только освещённые).
    /// </summary>
    public sealed class GatherZone : CircleZone
    {
        internal GatherZone(Vector3Int cell, int radius, int maxWorkers) : base(cell, radius, maxWorkers, WorkKind.Gather)
        {
        }

        internal override void Refresh(ZoneRefresh refresh, Func<WorkOrder, bool> take)
        {
            var thicket = refresh.Forest?.Thicket;
            var orders = refresh.Orders;
            if (thicket == null)
                return;
            foreach (var bush in thicket.Bushes)
            {
                if (bush.IsPresent && bush.IsGrown && Covers(bush.Cell))
                    take(orders.At(bush.Cell) ?? orders.AddGather(bush));
            }
        }
    }

    /// <summary>Что зона с уровнем делает с землёй (#169). Номера записаны в данных: новые режимы — в конец.</summary>
    public enum LevelZoneMode
    {
        /// <summary>Только выкопать всё выше уровня (жёлтое).</summary>
        Dig,
        /// <summary>Только нарастить землёй до уровня (голубое).</summary>
        Fill,
        /// <summary>Выровнять: сначала выкопать всё выше уровня, потом, когда копать нечего, нарастить до уровня.</summary>
        Level,
    }

    /// <summary>Что нужно зонам, чтобы поставить приказы: мир, лес, список приказов, цена блока и где наращивать нельзя.</summary>
    internal readonly struct ZoneRefresh
    {
        public ZoneRefresh(VoxelWorld world, Forest forest, WorkOrders orders, Func<byte, ItemDrop?> costOf, Func<Vector3Int, bool> isBlocked)
        {
            World = world;
            Forest = forest;
            Orders = orders;
            CostOf = costOf;
            IsBlocked = isBlocked;
        }

        public VoxelWorld World { get; }
        public Forest Forest { get; }
        public WorkOrders Orders { get; }
        public Func<byte, ItemDrop?> CostOf { get; }
        public Func<Vector3Int, bool> IsBlocked { get; }
    }

    /// <summary>
    /// Зона с уровнем (#136, #169): прямоугольник столбцов, целевой уровень <see cref="Level"/> — высота блока, который
    /// остаётся верхним, — и коробка <see cref="Bottom"/> ≤ Уровень ≤ <see cref="Top"/>.
    /// Копают (режимы <see cref="LevelZoneMode.Dig"/> и <see cref="LevelZoneMode.Level"/>) всё в (Уровень, Верх] слоями
    /// сверху: приказ стоит только на верхнем блоке столбца, следующий появится, когда его выкопают (а соседние столбцы
    /// держит #146). Скала и коренная порода (#97) и блоки под пнями не копаются: такой столбец считается готовым.
    /// Наращивают (режимы <see cref="LevelZoneMode.Fill"/> и <see cref="LevelZoneMode.Level"/>) клетки в (Низ, Уровень]
    /// чертежами блоков земли снизу вверх: чертёж стоит только на нижней пустой клетке столбца, под которой есть блок.
    /// Столбец, чья земля ниже низа коробки, не наращивают (класть не на что). В режиме выравнивания наращивать начинают,
    /// только когда во всей зоне копать нечего. Тёмные приказы ждут света. Когда в режиме зоны делать нечего, зона уходит.
    /// </summary>
    public sealed class LevelZone : WorkZone
    {
        public const int MinSize = 1;
        public const int MaxSize = 32;

        /// <summary>Чем наращивают: блок земли.</summary>
        public static readonly Block FillBlock = Block.Full(BlockMaterials.Dirt);

        internal LevelZone(Vector2Int min, Vector2Int size, int level, int top, int bottom, int maxWorkers,
            LevelZoneMode mode = LevelZoneMode.Level) : base(maxWorkers, WorkKind.Dig, WorkKind.Build)
        {
            Mode = mode;
            Min = min;
            Size = ClampSize(size);
            Level = Mathf.Max(0, level);
            Top = Mathf.Max(Level, top);
            Bottom = Mathf.Clamp(bottom, 0, Level);
        }

        /// <summary>Что зона делает: копает, наращивает или то и другое. Меняет <see cref="WorkZones.SetMode"/>.</summary>
        public LevelZoneMode Mode { get; internal set; }

        /// <summary>В этом режиме копают (жёлтое).</summary>
        public bool Digs => Mode != LevelZoneMode.Fill;

        /// <summary>В этом режиме наращивают (голубое).</summary>
        public bool Fills => Mode != LevelZoneMode.Dig;

        /// <summary>Ближний угол прямоугольника: наименьшие x и z столбцов.</summary>
        public Vector2Int Min { get; internal set; }

        /// <summary>Длина (по x) и ширина (по z) в столбцах; угол <see cref="Min"/> при смене размера стоит на месте.</summary>
        public Vector2Int Size { get; internal set; }

        /// <summary>Целевой уровень: высота (y) блока, который останется верхним; блоки выше (до <see cref="Top"/>) выкапываются.</summary>
        public int Level { get; internal set; }

        /// <summary>
        /// Верх коробки зоны: высота (y) самого высокого блока, который можно копать. Блоки выше не трогают (если под ними
        /// выкопали, они падают по обычной устойчивости). Не ниже <see cref="Level"/>.
        /// </summary>
        public int Top { get; internal set; }

        /// <summary>
        /// Низ коробки зоны: наращивают (#169) клетки выше этой высоты и до уровня включительно; ниже не трогают.
        /// Не выше <see cref="Level"/>.
        /// </summary>
        public int Bottom { get; internal set; }

        public override Vector2 Center => new(Min.x + Size.x * 0.5f, Min.y + Size.y * 0.5f);

        public override bool Covers(Vector3Int cell) =>
            cell.x >= Min.x && cell.x < Min.x + Size.x && cell.z >= Min.y && cell.z < Min.y + Size.y;

        internal override bool Keeps(WorkOrder order) => Covers(order.Cell) && order.Kind switch
        {
            WorkKind.Dig => Digs && order.Cell.y > Level && order.Cell.y <= Top,
            WorkKind.Build => Fills && order.Cell.y > Bottom && order.Cell.y <= Level,
            _ => false,
        };

        /// <summary>
        /// Границы коробки по умолчанию для прямоугольника: верх — самый высокий блок в нём, низ — самая низкая земля
        /// (верхний блок самого низкого столбца; если он неполный — блок под ним, чтобы его досыпали). Верх не ниже уровня,
        /// низ не выше. Пустые столбцы не считаются.
        /// </summary>
        public static (int top, int bottom) DefaultBounds(VoxelWorld world, Vector2Int min, Vector2Int size, int level)
        {
            var top = level;
            var bottom = level;
            for (var z = min.y; z < min.y + size.y; z++)
            for (var x = min.x; x < min.x + size.x; x++)
            {
                if (!TryTopBlock(world, x, z, world.SizeInBlocks.y - 1, -1, out var y))
                    continue;
                top = Mathf.Max(top, y);
                // Неполный верхний блок (#78) — тоже что досыпать: низ под ним, чтобы он попал в (Низ, Уровень].
                var shape = world.GetBlock(new Vector3Int(x, y, z)).Shape;
                bottom = Mathf.Min(bottom, shape.IsFull || shape.IsCatalog ? y : y - 1);
            }
            return (top, bottom);
        }

        /// <summary>Самый высокий непустой блок столбца от высоты from вниз, выше below. false — столбец вне мира или пуст.</summary>
        private static bool TryTopBlock(VoxelWorld world, int x, int z, int from, int below, out int y)
        {
            y = 0;
            if (x < 0 || z < 0 || x >= world.SizeInBlocks.x || z >= world.SizeInBlocks.z)
                return false;
            for (y = Mathf.Min(from, world.SizeInBlocks.y - 1); y > below; y--)
            {
                if (!world.GetBlock(new Vector3Int(x, y, z)).IsAir)
                    return true;
            }
            return false;
        }

        internal static Vector2Int ClampSize(Vector2Int size) =>
            new(Mathf.Clamp(size.x, MinSize, MaxSize), Mathf.Clamp(size.y, MinSize, MaxSize));

        /// <summary>
        /// Сейчас наращивают: режим это позволяет, и (в выравнивании) копать в зоне уже нечего. Ставит
        /// <see cref="WorkZones.Update"/>; черновик и зона без работы — false.
        /// </summary>
        public bool IsFilling { get; private set; }

        internal override void Refresh(ZoneRefresh refresh, Func<WorkOrder, bool> take)
        {
            var world = refresh.World;
            var orders = refresh.Orders;
            var digWork = false;
            if (Digs)
            {
                for (var z = Min.y; z < Min.y + Size.y; z++)
                for (var x = Min.x; x < Min.x + Size.x; x++)
                {
                    if (!TryTopToDig(world, refresh.Forest, x, z, Level, Top, out var cell))
                        continue;
                    digWork = true;
                    var order = orders.At(cell);
                    if (order == null)
                        take(orders.AddDig(cell));
                    else if (order.Kind == WorkKind.Dig)
                        take(order);
                }
            }

            var fillWork = false;
            IsFilling = Fills && !digWork;
            if (IsFilling)
            {
                for (var z = Min.y; z < Min.y + Size.y; z++)
                for (var x = Min.x; x < Min.x + Size.x; x++)
                {
                    if (!TryNextToFill(world, refresh.Forest, refresh.IsBlocked, x, z, Level, Bottom, out var cell, out var block, out var topUp))
                        continue;
                    fillWork = true;
                    var order = orders.At(cell);
                    if (order == null)
                        take(orders.AddBuild(cell, block, topUp, refresh.CostOf?.Invoke(block.Material)));
                    else if (order.Kind == WorkKind.Build)
                        take(order);
                }
            }
            else if (Fills)
                // Пока есть что копать, не наращивают: чертежи, которые ещё никто не взял, снимаются (например, игрок
                // поднял уровень во время копки), и встанут снова, когда копать будет нечего.
                CancelUntaken(orders, WorkKind.Build);
            IsFinished = !digWork && !fillWork;
        }

        private readonly List<WorkOrder> untaken = new();

        private void CancelUntaken(WorkOrders orders, WorkKind kind)
        {
            untaken.Clear();
            foreach (var order in Assignment.Orders)
            {
                if (order.Kind == kind && order.Worker == null)
                    untaken.Add(order);
            }
            foreach (var order in untaken)
                orders.Cancel(order);
            untaken.Clear();
        }

        /// <summary>
        /// Что наращивать следующим в столбце, снизу вверх по (bottom, level]:
        /// неполный блок (не полный и не каталожной формы, #78) с пустой клеткой над ним — дополнить до полного тем же
        /// материалом (topUp, чертёж <see cref="WorkOrder.FillsExisting"/>); иначе нижняя пустая клетка, под которой стоит
        /// блок, — новый блок земли (<see cref="FillBlock"/>). Пустая клетка над пустой — класть не на что: выше в столбце
        /// тоже не наращивают. false и тогда, когда клетку держит дерево, пень, куст или isBlocked (костёр, поддон) — в ней или
        /// над дополняемым блоком.
        /// </summary>
        internal static bool TryNextToFill(VoxelWorld world, Forest forest, Func<Vector3Int, bool> isBlocked, int x, int z,
            int level, int bottom, out Vector3Int cell, out Block block, out bool topUp)
        {
            cell = default;
            block = FillBlock;
            topUp = false;
            if (x < 0 || z < 0 || x >= world.SizeInBlocks.x || z >= world.SizeInBlocks.z)
                return false;
            var from = Mathf.Max(bottom + 1, 1);
            var to = Mathf.Min(level, world.SizeInBlocks.y - 2);
            for (var y = from; y <= to; y++)
            {
                var here = new Vector3Int(x, y, z);
                var current = world.GetBlock(here);
                if (!current.IsAir)
                {
                    // Неполный верхний блок досыпают до полного; погребённый под другим блоком не трогают.
                    if (current.Shape.IsCatalog || current.Shape.IsFull || !world.GetBlock(here + Vector3Int.up).IsAir)
                        continue;
                    cell = here;
                    block = Block.Full(current.Material);
                    topUp = true;
                    return IsFree(forest, isBlocked, here) && IsFree(forest, isBlocked, here + Vector3Int.up);
                }
                if (world.GetBlock(here + Vector3Int.down).IsAir)
                    return false;
                cell = here;
                return IsFree(forest, isBlocked, here);
            }
            return false;
        }

        /// <summary>В клетке нет пня, стоящего дерева, куста (#88), и isBlocked её не держит.</summary>
        private static bool IsFree(Forest forest, Func<Vector3Int, bool> isBlocked, Vector3Int cell) =>
            !(forest != null && (forest.StumpAt(cell) != null || forest.Thicket.BlocksPlace(cell)
                                 || forest.At(new Vector2Int(cell.x, cell.z)) is { IsStanding: true } tree && tree.Cell == cell))
            && (isBlocked == null || !isBlocked(cell));

        /// <summary>
        /// С какой высоты в столбце (x, z) начнут наращивать — низ голубого объёма: у неполного блока — его верх
        /// (surface — высота поверхности столбца), у пустой клетки — её пол. null — наращивать в столбце нечего. Та же
        /// проверка, что у приказов (<see cref="TryNextToFill"/>): голубое — ровно то, что засыплют.
        /// </summary>
        public static float? FillFrom(VoxelWorld world, Forest forest, Func<Vector3Int, bool> isBlocked, Func<Vector2Int, float> surface,
            int x, int z, int level, int bottom)
        {
            if (!TryNextToFill(world, forest, isBlocked, x, z, level, bottom, out var cell, out _, out var topUp))
                return null;
            return topUp ? Mathf.Clamp(surface(new Vector2Int(x, z)), cell.y, cell.y + 1f) : cell.y;
        }

        /// <summary>
        /// Верхний блок столбца в (level, top] — что копать следующим, если его можно копать: не скала и не под пнём.
        /// Блоки выше top не считаются.
        /// </summary>
        internal static bool TryTopToDig(VoxelWorld world, Forest forest, int x, int z, int level, int top, out Vector3Int cell)
        {
            cell = default;
            if (!TryTopBlock(world, x, z, top, level, out var y))
                return false;
            cell = new Vector3Int(x, y, z);
            var block = world.GetBlock(cell);
            return BlockMaterials.IsBreakable(block.Material) && (forest == null || !forest.BlocksEdit(cell));
        }
    }

    /// <summary>
    /// Все зоны работ отряда: рубки (<see cref="ChopZone"/>), сбора (<see cref="GatherZone"/>) и с уровнем (<see cref="LevelZone"/>). <see cref="Update"/> раз
    /// в кадр ставит в зонах приказы и забирает в зону уже стоящие там одиночные приказы видов зоны, если их ещё никто
    /// не взял. Клетка в нескольких зонах достаётся той, что поставлена раньше. Чистая логика, без сцены.
    /// </summary>
    public sealed class WorkZones
    {
        public const int DefaultMaxWorkers = 2;

        private readonly List<WorkZone> zones = new();
        private readonly List<WorkOrder> scratch = new();

        public IReadOnlyList<WorkZone> All => zones;

        /// <summary>Сколько стоит блок по чертежу (null — функции нет или блок бесплатный). Ставит отряд из таблицы цен.</summary>
        public Func<byte, ItemDrop?> CostOf { get; set; }

        /// <summary>Клетку нельзя наращивать: в ней костёр, поддон и т. п. Ставит отряд; null — можно везде.</summary>
        public Func<Vector3Int, bool> IsBlocked { get; set; }

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

        /// <summary>Поставить зону сбора травы (#88) с центром в клетке cell; как <see cref="AddChop"/>, но берёт только кусты.</summary>
        public GatherZone AddGather(Vector3Int cell, int radius, int maxWorkers = DefaultMaxWorkers, bool draft = false) =>
            Add(new GatherZone(cell, radius, maxWorkers) { IsDraft = draft });

        /// <summary>
        /// Поставить зону с уровнем (по умолчанию — выравнивание) на прямоугольник между столбцами двух блоков (в любом порядке); уровень — на блок ниже
        /// блока from (снять один слой). С world верх и низ коробки — самый высокий блок и самая низкая земля прямоугольника
        /// (<see cref="LevelZone.DefaultBounds"/>), без него — блок from и уровень.
        /// </summary>
        public LevelZone AddLevel(Vector3Int from, Vector3Int to, int maxWorkers = DefaultMaxWorkers, bool draft = false,
            VoxelWorld world = null, LevelZoneMode mode = LevelZoneMode.Level)
        {
            var min = new Vector2Int(Mathf.Min(from.x, to.x), Mathf.Min(from.z, to.z));
            var size = LevelZone.ClampSize(new Vector2Int(Mathf.Abs(to.x - from.x) + 1, Mathf.Abs(to.z - from.z) + 1));
            var level = from.y - 1;
            var (top, bottom) = world != null ? LevelZone.DefaultBounds(world, min, size, level) : (from.y, level);
            return Add(new LevelZone(min, size, level, top, bottom, maxWorkers, mode) { IsDraft = draft });
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
                if (kind != null && !zone.Accepts(kind.Value) || !zone.Covers(cell))
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

        /// <summary>Сменить радиус зоны-круга (рубки, сбора); приказы, оставшиеся за кругом, снимаются. Возвращает новый радиус.</summary>
        public int SetRadius(CircleZone zone, int radius, WorkOrders orders)
        {
            radius = Mathf.Clamp(radius, CircleZone.MinRadius, CircleZone.MaxRadius);
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

        /// <summary>Поставить прямоугольник зоны с уровнем целиком (угол и размер); лишние приказы снимаются.</summary>
        public void SetRect(LevelZone zone, Vector2Int min, Vector2Int size, WorkOrders orders)
        {
            size = LevelZone.ClampSize(size);
            if (zone.Min == min && zone.Size == size)
                return;
            zone.Min = min;
            zone.Size = size;
            Reshaped(zone, orders);
        }

        /// <summary>
        /// Протянуть стороны прямоугольника к столбцу column (ручка на стороне или угле): dx = −1 — двигается сторона меньших x,
        /// +1 — больших, 0 — по x ничего; так же dz. Противоположная сторона стоит; сторону нельзя протянуть за противоположную
        /// (остаётся хотя бы столбец), и не шире <see cref="LevelZone.MaxSize"/>.
        /// </summary>
        public void DragEdges(LevelZone zone, int dx, int dz, Vector2Int column, WorkOrders orders)
        {
            var (minX, sizeX) = DragAxis(zone.Min.x, zone.Size.x, dx, column.x);
            var (minZ, sizeZ) = DragAxis(zone.Min.y, zone.Size.y, dz, column.y);
            SetRect(zone, new Vector2Int(minX, minZ), new Vector2Int(sizeX, sizeZ), orders);
        }

        private static (int min, int size) DragAxis(int min, int size, int side, int column)
        {
            var max = min + size - 1;
            if (side < 0)
            {
                var newMin = Mathf.Clamp(column, max - LevelZone.MaxSize + 1, max);
                return (newMin, max - newMin + 1);
            }
            if (side > 0)
            {
                var newMax = Mathf.Clamp(column, min, min + LevelZone.MaxSize - 1);
                return (min, newMax - min + 1);
            }
            return (min, size);
        }

        /// <summary>
        /// Перенести зону: у зоны с уровнем ближний угол встаёт в столбец min (размер и уровень прежние), у зоны-круга центр —
        /// в клетку center. Приказы, оставшиеся вне зоны, снимаются, новые встанут в <see cref="Update"/>.
        /// </summary>
        public void Move(LevelZone zone, Vector2Int min, WorkOrders orders) => SetRect(zone, min, zone.Size, orders);

        /// <inheritdoc cref="Move(LevelZone, Vector2Int, WorkOrders)"/>
        public void Move(CircleZone zone, Vector3Int center, WorkOrders orders)
        {
            if (zone.Cell == center)
                return;
            zone.Cell = center;
            Reshaped(zone, orders);
        }

        /// <summary>Радиус зоны-круга — до точки point на карте (x, z в координатах мира блоков), с округлением до блока.</summary>
        public int SetRadiusTo(CircleZone zone, Vector2 point, WorkOrders orders) =>
            SetRadius(zone, Mathf.RoundToInt(Vector2.Distance(zone.Center, point)), orders);

        /// <summary>
        /// Сменить целевой уровень; верх и низ подвигаются, чтобы было Низ ≤ Уровень ≤ Верх. Приказы вне (Уровень, Верх]
        /// снимаются, новые встанут в <see cref="Update"/>.
        /// </summary>
        public void SetLevel(LevelZone zone, int level, WorkOrders orders)
        {
            level = Mathf.Max(0, level);
            SetBounds(zone, Mathf.Max(zone.Top, level), level, Mathf.Min(zone.Bottom, level), orders);
        }

        /// <summary>Сменить верх коробки; уровень и низ опускаются, если верх ниже них. Приказы выше верха снимаются.</summary>
        public void SetTop(LevelZone zone, int top, WorkOrders orders)
        {
            top = Mathf.Max(0, top);
            var level = Mathf.Min(zone.Level, top);
            SetBounds(zone, top, level, Mathf.Min(zone.Bottom, level), orders);
        }

        /// <summary>Сменить низ коробки; уровень и верх поднимаются, если низ выше них.</summary>
        public void SetBottom(LevelZone zone, int bottom, WorkOrders orders)
        {
            bottom = Mathf.Max(0, bottom);
            var level = Mathf.Max(zone.Level, bottom);
            SetBounds(zone, Mathf.Max(zone.Top, level), level, bottom, orders);
        }

        private void SetBounds(LevelZone zone, int top, int level, int bottom, WorkOrders orders)
        {
            if (zone.Top == top && zone.Level == level && zone.Bottom == bottom)
                return;
            zone.Top = top;
            zone.Level = level;
            zone.Bottom = bottom;
            Reshaped(zone, orders);
        }

        /// <summary>
        /// Сменить режим зоны с уровнем; приказы, ненужные в новом режиме (копать — без копки, чертежи — без наращивания),
        /// снимаются, новые встанут в <see cref="Update"/>.
        /// </summary>
        public void SetMode(LevelZone zone, LevelZoneMode mode, WorkOrders orders)
        {
            if (zone.Mode == mode)
                return;
            zone.Mode = mode;
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
        /// добавлено в зоны. forest может быть null (тогда зоны рубки и сбора ничего не ставят).
        /// </summary>
        public int Update(VoxelWorld world, Forest forest, WorkOrders orders)
        {
            var added = 0;
            var refresh = new ZoneRefresh(world, forest, orders, CostOf, IsBlocked);
            foreach (var zone in zones)
            {
                // Черновик только виден и настраивается: приказов не ставит и не уходит.
                if (zone.IsDraft)
                    continue;
                zone.Refresh(refresh, order =>
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
        /// Забрать приказ в зону с её уровнем: только вида, который зона ставит, ничей (не другой зоны), ещё не взятый, и если клетка
        /// достаётся этой зоне (а не поставленной раньше).
        /// </summary>
        private bool Take(WorkZone zone, WorkOrders orders, WorkOrder order)
        {
            if (order == null || !zone.Accepts(order.Kind) || order.Assignment == zone.Assignment || IsZoneOrder(order)
                || Owner(order.Cell, order.Kind) != zone || !orders.Assign(order, zone.Assignment))
                return false;
            order.Priority = zone.Priority;
            return true;
        }

        /// <summary>Зона вида kind, которой достаётся клетка: первая поставленная из накрывающих её.</summary>
        private WorkZone Owner(Vector3Int cell, WorkKind kind)
        {
            foreach (var zone in zones)
            {
                if (zone.Accepts(kind) && !zone.IsDraft && zone.Covers(cell))
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

        private void CancelWhere(WorkZone zone, WorkOrders orders, Predicate<WorkOrder> match)
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
