using System;
using System.Collections.Generic;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Откуда взять ресурс: кучка на земле или поддон. Ровно одно из двух не null, если источник есть.
    /// </summary>
    public readonly struct ItemSource
    {
        public ItemSource(DroppedItem pile, Pallet pallet)
        {
            Pile = pile;
            Pallet = pallet;
        }

        public DroppedItem Pile { get; }
        public Pallet Pallet { get; }
        public bool IsNone => Pile == null && Pallet == null;

        /// <summary>Где источник на карте: клетка кучки или середина поддона.</summary>
        public Vector2 Column => Pile != null
            ? new Vector2(Pile.Cell.x, Pile.Cell.z)
            : Pallet != null ? new Vector2(Pallet.Center.x - 0.5f, Pallet.Center.z - 0.5f) : default;
    }

    /// <summary>
    /// Всё, с чем работают рабочие одного отряда: мир, задачи, ресурсы на земле и на поддонах, деревья, урон блоков,
    /// устойчивость и где светло. Без Unity-объектов, чтобы EditMode-тесты гоняли рабочих на маленьком мире.
    /// </summary>
    public sealed class WorkerContext
    {
        private readonly List<BlockFall> falls = new();
        private readonly Action<Vector3Int> worldChanged;
        private readonly Action<Vector3Int> campfireBuilt;

        public WorkerContext(VoxelWorld world, WorkOrders orders, DroppedItems items, BlockDamage damage,
            BlockStability stability, WorkerSettings settings, Func<Vector2Int, bool> isLit, Func<bool> hasAnyLight = null,
            Action<Vector3Int> worldChanged = null, Forest forest = null, Pallets pallets = null, Func<Vector3Int, bool> isCellLit = null,
            Action<Vector3Int> campfireBuilt = null)
        {
            this.worldChanged = worldChanged;
            this.campfireBuilt = campfireBuilt;
            Forest = forest;
            World = world;
            Orders = orders;
            Items = items;
            Damage = damage;
            Stability = stability;
            Settings = settings;
            IsLit = isLit ?? (_ => true);
            IsCellLit = isCellLit ?? (cell => IsLit(new Vector2Int(cell.x, cell.z)));
            HasAnyLight = hasAnyLight ?? (() => true);
            Pallets = pallets ?? new Pallets(settings.PalletCapacity);
            Surface = new WalkSurface(world, settings.MaxStep);
            Flood = new ColumnFlood(settings.SearchLimit);
        }

        public VoxelWorld World { get; }
        public WorkOrders Orders { get; }
        public DroppedItems Items { get; }

        /// <summary>Поддоны: свободные рабочие носят на них ничейные кучки, для задач ресурс берут и с них.</summary>
        public Pallets Pallets { get; }

        /// <summary>
        /// Сколько единиц каждой кучки уже идут относить на поддоны: другие носильщики берут только остаток.
        /// </summary>
        public Dictionary<DroppedItem, int> HaulClaims { get; } = new();
        public BlockDamage Damage { get; }
        public BlockStability Stability { get; }
        public WorkerSettings Settings { get; }

        /// <summary>Деревья, которые рубят рабочие; null — рубить нечего.</summary>
        public Forest Forest { get; }

        /// <summary>Столбец освещён: рабочие ходят и работают только там.</summary>
        public Func<Vector2Int, bool> IsLit { get; }

        /// <summary>Свет доходит до клетки: глубоко под огнём темно, хотя столбец сверху освещён.</summary>
        public Func<Vector3Int, bool> IsCellLit { get; }

        /// <summary>
        /// Задачу можно делать: её клетка в свете. Чертежи и приказы, оставшиеся в темноте, ждут света;
        /// топливо носят и к погасшему огню.
        /// </summary>
        public bool IsOrderLit(WorkOrder order) => order.Kind == WorkKind.Fuel || IsCellLit(order.Cell);

        /// <summary>Горит хотя бы один огонь: если нет, из темноты идти некуда.</summary>
        public Func<bool> HasAnyLight { get; }

        public WalkSurface Surface { get; }

        /// <summary>Один разлив на всех: рабочие обновляются по очереди и сразу забирают результат.</summary>
        public ColumnFlood Flood { get; }

        /// <summary>
        /// Мир изменился в клетке: блоки без опоры падают, их урон едет вместе с ними. В сцене это делает
        /// редактор (worldChanged), чтобы падение было видно так же, как после правки игрока.
        /// </summary>
        public void Settle(Vector3Int changed)
        {
            if (worldChanged != null)
            {
                worldChanged(changed);
                return;
            }
            falls.Clear();
            if (Stability.Settle(World, changed, falls) > 0)
                Damage.Move(falls);
        }

        /// <summary>Рабочий построил костёр в клетке: в сцене отряд ставит туда огонь (campfireBuilt).</summary>
        public void BuildCampfire(Vector3Int cell) => campfireBuilt?.Invoke(cell);

        /// <summary>
        /// Можно ли из столбца stand дотянуться до клетки cell: столбец соседний (или тот же, если sameColumn),
        /// а клетка не дальше <see cref="WorkerSettings.ReachDown"/> блоков ниже ног и
        /// <see cref="WorkerSettings.ReachUp"/> выше.
        /// </summary>
        public bool CanReach(Vector2Int stand, Vector3Int cell, bool sameColumn)
        {
            var dx = Mathf.Abs(stand.x - cell.x);
            var dz = Mathf.Abs(stand.y - cell.z);
            if (dx > 1 || dz > 1 || dx == 0 && dz == 0 && !sameColumn)
                return false;
            var feet = Surface.Height(stand);
            return cell.y + 1 > feet - Settings.ReachDown + 1e-3f && cell.y < feet + Settings.ReachUp;
        }

        /// <summary>
        /// Ближайший по последнему разливу освещённый столбец, откуда достать до клетки. false — не достать.
        /// </summary>
        public bool TryStandSpot(Vector3Int cell, bool sameColumn, out Vector2Int stand, out float distance)
        {
            stand = default;
            distance = float.PositiveInfinity;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var column = new Vector2Int(cell.x + dx, cell.z + dz);
                var d = Flood.Distance(column);
                if (d >= distance || !CanReach(column, cell, sameColumn))
                    continue;
                distance = d;
                stand = column;
            }
            return !float.IsPositiveInfinity(distance);
        }

        /// <summary>
        /// Над клеткой (в том же или соседнем столбце, на один-два блока выше) есть приказ копать, до которого можно
        /// дотянуться по последнему разливу. Такой блок рано копать: яму копают слоями сверху вниз, иначе рабочие роют
        /// колодцы у края, между ними остаются гребни, а в яму с отвесными стенами в два блока уже не спуститься (#146).
        /// Недосягаемый приказ сверху (высоко на обрыве, в темноте) не мешает: блок под ним копают, верх осыпается.
        /// </summary>
        public bool HasReachableDigAbove(Vector3Int cell)
        {
            for (var dy = 1; dy <= 2; dy++)
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var above = Orders.At(new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz));
                if (above != null && above.Kind == WorkKind.Dig && IsOrderLit(above) && TryStandSpot(above.Cell, false, out _, out _))
                    return true;
            }
            return false;
        }

        /// <summary>Ближайшая по последнему разливу освещённая кучка ресурса itemId, до которой можно дотянуться.</summary>
        public DroppedItem NearestPile(string itemId, out Vector2Int stand, out float distance)
        {
            DroppedItem best = null;
            stand = default;
            distance = float.PositiveInfinity;
            foreach (var pile in Items.Items)
            {
                if (pile.Drop.ItemId != itemId || !IsLit(new Vector2Int(pile.Cell.x, pile.Cell.z)))
                    continue;
                if (!TryStandSpot(pile.Cell, true, out var spot, out var d) || d >= distance)
                    continue;
                best = pile;
                stand = spot;
                distance = d;
            }
            return best;
        }

        /// <summary>Лежит ли где-нибудь ресурс itemId, на земле или на поддоне: без него задачу не начать.</summary>
        public bool HasItem(string itemId)
        {
            foreach (var pile in Items.Items)
            {
                if (pile.Drop.ItemId == itemId)
                    return true;
            }
            foreach (var pallet in Pallets.All)
            {
                if (pallet.ItemId == itemId && pallet.Count > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Ближайший по последнему разливу источник ресурса itemId: кучка на земле или поддон, на котором он есть.
        /// </summary>
        public ItemSource NearestSource(string itemId, out Vector2Int stand, out float distance)
        {
            var pile = NearestPile(itemId, out stand, out distance);
            Pallet best = null;
            foreach (var pallet in Pallets.All)
            {
                if (pallet.ItemId != itemId || pallet.Count <= 0 || !TryPalletStand(pallet, out var spot, out var d) || d >= distance)
                    continue;
                best = pallet;
                stand = spot;
                distance = d;
            }
            return best != null ? new ItemSource(null, best) : new ItemSource(pile, null);
        }

        /// <summary>Взять одну единицу из источника. false — его уже опустошили.</summary>
        public bool TakeOne(ItemSource source, out ItemDrop taken)
        {
            taken = default;
            if (source.Pile != null)
                return Items.Take(source.Pile, 1, out taken);
            return source.Pallet != null && Pallets.Take(source.Pallet, 1, out taken);
        }

        /// <summary>Ближайший по последнему разливу освещённый столбец, откуда достать до поддона. false — не достать.</summary>
        public bool TryPalletStand(Pallet pallet, out Vector2Int stand, out float distance)
        {
            stand = default;
            distance = float.PositiveInfinity;
            foreach (var cell in pallet.FloorCells)
            {
                if (!IsLit(new Vector2Int(cell.x, cell.z)) || !TryStandSpot(cell, true, out var spot, out var d) || d >= distance)
                    continue;
                stand = spot;
                distance = d;
            }
            return !float.IsPositiveInfinity(distance);
        }

        /// <summary>
        /// Что отнести на поддон: ближайшая (по последнему разливу) освещённая кучка, у которой осталось
        /// не занятое другими носильщиками, и ближайший к ней поддон с местом. Поддон, где уже лежит этот ресурс,
        /// лучше пустого: так один вид не расползается по всем поддонам. false — носить нечего или некуда.
        /// </summary>
        public bool TryFindHaul(out DroppedItem pile, out Pallet pallet, out Vector2Int stand)
        {
            pile = null;
            pallet = null;
            stand = default;
            if (Pallets.All.Count == 0)
                return false;
            var bestCost = float.PositiveInfinity;
            foreach (var candidate in Items.Items)
            {
                var itemId = candidate.Drop.ItemId;
                HaulClaims.TryGetValue(candidate, out var claimed);
                if (candidate.Drop.Count - claimed <= 0 || !IsLit(new Vector2Int(candidate.Cell.x, candidate.Cell.z))
                    || !TryStandSpot(candidate.Cell, true, out var spot, out var toPile) || toPile >= bestCost)
                    continue;
                // До поддона тоже должен быть путь, иначе рабочий поднимет ношу и бросит её у кучки.
                var target = NearestPalletFor(itemId, candidate.Cell);
                if (target == null || !TryPalletStand(target, out _, out _))
                    continue;
                // Путь от кучки до поддона оцениваем по прямой, как путь от кучки до чертежа.
                var cost = toPile + Vector2.Distance(new Vector2(candidate.Cell.x, candidate.Cell.z), new Vector2(target.Center.x, target.Center.z));
                if (cost >= bestCost)
                    continue;
                bestCost = cost;
                pile = candidate;
                pallet = target;
                stand = spot;
            }
            return pile != null;
        }

        /// <summary>Ближайший к клетке поддон с местом под itemId: сначала из тех, где он уже лежит, потом пустые.</summary>
        private Pallet NearestPalletFor(string itemId, Vector3Int from)
        {
            Pallet same = null, empty = null;
            float sameDistance = float.PositiveInfinity, emptyDistance = float.PositiveInfinity;
            foreach (var pallet in Pallets.All)
            {
                if (Pallets.Space(pallet, itemId) < 1)
                    continue;
                var d = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(pallet.Center.x, pallet.Center.z));
                if (pallet.ItemId == itemId && d < sameDistance)
                {
                    same = pallet;
                    sameDistance = d;
                }
                else if (pallet.ItemId == null && d < emptyDistance)
                {
                    empty = pallet;
                    emptyDistance = d;
                }
            }
            return same ?? empty;
        }
    }
}
