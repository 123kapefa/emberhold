using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    public enum WorkerState
    {
        /// <summary>Ждёт задачу.</summary>
        Idle,
        /// <summary>Идёт к кучке за ресурсом.</summary>
        ToPile,
        /// <summary>Поднимает ресурс.</summary>
        PickingUp,
        /// <summary>Идёт к задаче (с ресурсом, если он нужен).</summary>
        ToSite,
        /// <summary>Ставит блок, копает или рубит.</summary>
        Working,
        /// <summary>Оказался в темноте и уходит к ближайшему свету.</summary>
        Fleeing,
        /// <summary>
        /// Пришёл с ношей к огню или чертежу и кладёт её: ресурс засчитывается в конце (через PutSeconds).
        /// Если задачу отменили раньше, ноша падает у ног.
        /// </summary>
        Putting,
    }

    /// <summary>
    /// Рабочий: сам берёт свободную задачу из <see cref="WorkOrders"/> — сначала самую важную (топливо для огня),
    /// среди равных ближайшую. Для чертежа приносит ресурс из ближайшей кучки и ставит блок (или костёр), для топлива приносит
    /// полено и кладёт в огонь (за ходку — одна единица ресурса, положить её — PutSeconds), для приказа копать бьёт блок через <see cref="Emberhold.World.Editing.BlockDamage"/>,
    /// для приказа рубить бьёт дерево; один удар за анимацию копки. Ресурс для задачи берёт из кучки на земле или с поддона, что ближе.
    /// Когда свободных задач нет, относит ничейные кучки на поддоны (<see cref="Pallets"/>), тоже по одной единице за ходку.
    /// Ходит и работает только на освещённых столбцах; если свет ушёл из-под ног, бросает задачу и ношу и уходит к ближайшему свету.
    /// Чистая логика: положение — в координатах мира блоков, вид рисует <see cref="WorkerCrew"/>.
    /// </summary>
    public sealed class Worker
    {
        private readonly WorkerContext context;
        private readonly List<Vector2Int> path = new();
        private int pathIndex;
        private float timer;
        private float fleeTimer;
        private ItemSource source;
        // Ноша на поддон: из какой кучки (пока не подняли), на какой поддон и какой ресурс.
        private DroppedItem haulPile;
        private Pallet haulTo;
        private string haulItemId;

        public Worker(WorkerContext context, Vector3 position)
        {
            this.context = context;
            Position = position;
        }

        /// <summary>Где стоит рабочий: точка на верхе столбца в координатах мира блоков.</summary>
        public Vector3 Position { get; private set; }

        public Vector2Int Column => WalkSurface.ColumnOf(Position);

        public WorkerState State { get; private set; }

        /// <summary>Задача, которую рабочий держит за собой, или null.</summary>
        public WorkOrder Order { get; private set; }

        /// <summary>Что рабочий несёт, или null.</summary>
        public ItemDrop? Carrying { get; private set; }

        /// <summary>Поддон, на который рабочий относит ничейный ресурс, или null.</summary>
        public Pallet HaulTarget => haulTo;

        /// <summary>За каким ресурсом рабочий идёт или какой несёт по задаче или на поддон; null — ни за каким.</summary>
        public string WantedItemId => Order?.ItemId ?? haulItemId;

        /// <summary>Куда рабочий сейчас идёт; пустой, если стоит.</summary>
        public IReadOnlyList<Vector2Int> Path => path;

        public void Tick(float deltaTime)
        {
            if (Order != null && !context.Orders.Contains(Order))
            {
                // Задачу отменили или её выполнил мир (блок выкопали вручную): бросаем всё.
                Order = null;
                Abandon();
            }

            if (haulTo != null && !context.Pallets.Contains(haulTo))
            {
                // Поддон убрали: ноша падает у ног.
                Abandon();
            }

            if (State != WorkerState.Fleeing && !context.IsLit(Column))
            {
                // Свет ушёл из-под ног: бросаем всё и раз в IdleSearchSeconds ищем путь к свету.
                if (State != WorkerState.Idle || Order != null || Carrying != null)
                    Abandon();
                fleeTimer -= deltaTime;
                if (fleeTimer <= 0f)
                {
                    fleeTimer = context.Settings.IdleSearchSeconds;
                    TryFlee();
                }
                if (State != WorkerState.Fleeing)
                {
                    SnapToGround();
                    return;
                }
            }

            switch (State)
            {
                case WorkerState.Idle:
                    SnapToGround();
                    timer -= deltaTime;
                    if (timer <= 0f)
                    {
                        timer = context.Settings.IdleSearchSeconds;
                        if (!TryTakeOrder())
                            TryHaul();
                    }
                    break;
                case WorkerState.ToPile:
                case WorkerState.ToSite:
                case WorkerState.Fleeing:
                    if (Walk(deltaTime))
                        OnArrived();
                    break;
                case WorkerState.PickingUp:
                    SnapToGround();
                    timer -= deltaTime;
                    if (timer <= 0f)
                        PickUp();
                    break;
                case WorkerState.Putting:
                    SnapToGround();
                    timer -= deltaTime;
                    if (timer <= 0f)
                        PutDown();
                    break;
                case WorkerState.Working:
                    SnapToGround();
                    Work(deltaTime);
                    break;
            }
        }

        /// <summary>Бросить задачу и ношу и встать.</summary>
        public void Abandon()
        {
            if (Order != null && Order.Worker == this)
                Order.Worker = null;
            Order = null;
            source = default;
            ReleaseHaul();
            DropCarried();
            path.Clear();
            State = WorkerState.Idle;
        }

        /// <summary>Взять самую важную, а среди равных ближайшую свободную задачу. false — брать нечего.</summary>
        private bool TryTakeOrder()
        {
            if (!HasFreeOrder())
                return false;
            context.Flood.Run(context.Surface, Column, context.IsLit);
            WorkOrder best = null;
            var bestSource = default(ItemSource);
            var bestStand = default(Vector2Int);
            var bestCost = float.PositiveInfinity;
            var bestPriority = int.MinValue;
            foreach (var order in context.Orders.Orders)
            {
                if (order.Worker != null || order.Priority < bestPriority || !WorkOrders.IsValid(context.World, order)
                    || order.Kind == WorkKind.Chop && context.Forest == null
                    || !context.TryStandSpot(order.Cell, false, out var stand, out var cost))
                    continue;
                var from = default(ItemSource);
                if (order.NeedsItems)
                {
                    from = context.NearestSource(order.ItemId, out stand, out var toPile);
                    if (from.IsNone)
                        continue;
                    // Путь от кучки или поддона до чертежа оцениваем по прямой: точный разлив для каждой пары дорог.
                    cost = toPile + Vector2.Distance(from.Column, new Vector2(order.Cell.x, order.Cell.z));
                }
                if (order.Priority == bestPriority && cost >= bestCost)
                    continue;
                best = order;
                bestPriority = order.Priority;
                bestSource = from;
                bestStand = stand;
                bestCost = cost;
            }

            if (best == null)
                return false;
            Order = best;
            best.Worker = this;
            source = bestSource;
            WalkTo(bestStand, !bestSource.IsNone ? WorkerState.ToPile : WorkerState.ToSite);
            return true;
        }

        /// <summary>Задач нет: пойти за ничейной кучкой, чтобы отнести одну её единицу на поддон.</summary>
        private void TryHaul()
        {
            if (context.Pallets.All.Count == 0 || context.Items.Items.Count == 0)
                return;
            context.Flood.Run(context.Surface, Column, context.IsLit);
            if (!context.TryFindHaul(out var pile, out var pallet, out var stand) || !context.Pallets.Reserve(pallet, pile.Drop.ItemId))
                return;
            haulPile = pile;
            haulTo = pallet;
            haulItemId = pile.Drop.ItemId;
            context.HaulClaims[pile] = context.HaulClaims.TryGetValue(pile, out var claimed) ? claimed + 1 : 1;
            source = new ItemSource(pile, null);
            WalkTo(stand, WorkerState.ToPile);
        }

        /// <summary>Есть свободная задача, которую можно начать: ресурс для неё где-то лежит, на земле или на поддоне.</summary>
        private bool HasFreeOrder()
        {
            foreach (var order in context.Orders.Orders)
            {
                if (order.Worker == null && (!order.NeedsItems || context.HasItem(order.ItemId)))
                    return true;
            }
            return false;
        }

        /// <summary>Найти кучку или поддон с ресурсом для своего чертежа и пойти туда; если ресурса нет — отпустить задачу.</summary>
        private void FetchItems()
        {
            context.Flood.Run(context.Surface, Column, context.IsLit);
            source = context.NearestSource(Order.ItemId, out var stand, out _);
            if (source.IsNone)
            {
                Abandon();
                return;
            }
            WalkTo(stand, WorkerState.ToPile);
        }

        private void PickUp()
        {
            // За одну ходку — одна единица: одно бревно или одна полная корзина. Нужно больше — ещё ходки.
            if (!context.TakeOne(source, out var taken))
            {
                // Кучку или поддон опустошил другой рабочий. Носильщик ищет ношу заново с нуля.
                if (haulTo != null)
                {
                    Abandon();
                    return;
                }
                FetchItems();
                return;
            }
            source = default;
            Carrying = taken;
            ReleaseClaim();
            context.Flood.Run(context.Surface, Column, context.IsLit);
            if (haulTo != null)
            {
                if (!context.TryPalletStand(haulTo, out var spot, out _))
                {
                    Abandon();
                    return;
                }
                WalkTo(spot, WorkerState.ToSite);
                return;
            }
            if (!context.TryStandSpot(Order.Cell, false, out var stand, out _))
            {
                Abandon();
                return;
            }
            WalkTo(stand, WorkerState.ToSite);
        }

        private void OnArrived()
        {
            fleeTimer = 0f;
            switch (State)
            {
                case WorkerState.ToPile:
                    State = WorkerState.PickingUp;
                    timer = context.Settings.PickUpSeconds;
                    break;
                case WorkerState.ToSite:
                    if (haulTo != null)
                    {
                        State = WorkerState.Putting;
                        timer = context.Settings.PutSeconds;
                        break;
                    }
                    if (Order.Kind is WorkKind.Dig or WorkKind.Chop)
                    {
                        State = WorkerState.Working;
                        timer = context.Settings.DigHitSeconds;
                        break;
                    }
                    // С ношей — сначала положить её (анимация «положить»), ресурс засчитается в конце.
                    if (Carrying is { } carried && carried.ItemId == Order.ItemId)
                    {
                        State = WorkerState.Putting;
                        timer = context.Settings.PutSeconds;
                        break;
                    }
                    AfterDelivery();
                    break;
                default:
                    State = WorkerState.Idle;
                    timer = 0f;
                    break;
            }
        }

        /// <summary>Ноша положена: в огонь или на чертёж; дальше — как после доставки.</summary>
        private void PutDown()
        {
            if (haulTo != null)
            {
                StoreHaul();
                return;
            }
            if (Order.Kind != WorkKind.Fuel && Carrying is { } carried && carried.ItemId == Order.ItemId)
            {
                Order.Delivered += carried.Count;
                Carrying = null;
            }
            AfterDelivery();
        }

        /// <summary>
        /// Что делать у задачи, когда ноши уже нет: огонь — подкормить и закончить; чертёж — сходить ещё за ресурсом
        /// или ставить блок.
        /// </summary>
        private void AfterDelivery()
        {
            if (Order.Kind == WorkKind.Fuel)
            {
                FeedFire();
                return;
            }
            if (Order.NeedsItems)
            {
                FetchItems();
                return;
            }
            State = WorkerState.Working;
            timer = context.Settings.BuildSeconds;
        }

        private void Work(float deltaTime)
        {
            timer -= deltaTime;
            if (timer > 0f)
                return;

            if (Order.Kind == WorkKind.Chop)
            {
                // Урон копится на дереве, даже если рабочий уйдёт; срубленное дерево само кладёт поленья.
                timer += context.Settings.DigHitSeconds;
                if (context.Forest.Hit(context.World, Order.Tree, context.Settings.DigHitDamage))
                    Finish();
                return;
            }

            if (Order.Kind == WorkKind.Dig)
            {
                // Один удар за анимацию копки; снятые ХП остаются у блока, даже если рабочий уйдёт.
                timer += context.Settings.DigHitSeconds;
                if (!context.Damage.Hit(context.World, Order.Cell, context.Settings.DigHitDamage))
                    return;
                context.Settle(Order.Cell);
                Finish();
                return;
            }

            if (!WorkOrders.IsValid(context.World, Order))
            {
                context.Orders.Cancel(Order);
                Order = null;
                Abandon();
                return;
            }
            if (Order.Kind == WorkKind.Campfire)
            {
                context.BuildCampfire(Order.Cell);
                Finish();
                return;
            }
            context.World.SetBlock(Order.Cell, Order.Block);
            // Новый блок начинает с целой прочностью.
            context.Damage.Forget(Order.Cell);
            context.Settle(Order.Cell);
            Finish();
        }

        /// <summary>
        /// Положить принесённое полено в огонь. Если оно не поместилось (огонь успели подкормить), остаётся лежать у ног.
        /// Задача на одну ходку: если место ещё есть, отряд заведёт новую.
        /// </summary>
        private void FeedFire()
        {
            if (Carrying is { } carried && carried.ItemId == Order.ItemId)
            {
                var accepted = Order.FuelTarget.AddFuel(carried.Count);
                // Полено, принятое хоть частично, сгорает целиком.
                var used = Mathf.Clamp(Mathf.CeilToInt(accepted - 1e-4f), 0, carried.Count);
                Order.Delivered += used;
                Carrying = used < carried.Count ? new ItemDrop(carried.ItemId, carried.Count - used) : null;
                DropCarried();
            }
            Finish();
        }

        /// <summary>Положить ношу на поддон. Если не легла (поддон чем-то занят), она падает у ног.</summary>
        private void StoreHaul()
        {
            if (Carrying is { } carried && context.Pallets.Store(haulTo, carried))
            {
                // Бронь ушла в положенное.
                Carrying = null;
                haulTo = null;
                haulItemId = null;
            }
            Abandon();
            timer = 0f;
        }

        /// <summary>Отпустить ношу на поддон: занятую часть кучки и бронь места.</summary>
        private void ReleaseHaul()
        {
            ReleaseClaim();
            if (haulTo != null)
                context.Pallets.Release(haulTo);
            haulTo = null;
            haulItemId = null;
        }

        private void ReleaseClaim()
        {
            if (haulPile == null)
                return;
            if (context.HaulClaims.TryGetValue(haulPile, out var claimed))
            {
                if (claimed <= 1)
                    context.HaulClaims.Remove(haulPile);
                else
                    context.HaulClaims[haulPile] = claimed - 1;
            }
            haulPile = null;
        }

        private void Finish()
        {
            context.Orders.Complete(Order);
            Order = null;
            State = WorkerState.Idle;
            timer = 0f;
        }

        private bool TryFlee()
        {
            // Если не горит ни один огонь, идти некуда.
            if (!context.HasAnyLight())
                return false;
            // Из темноты можно идти по любым столбцам, лишь бы к свету.
            context.Flood.Run(context.Surface, Column, null);
            if (!context.Flood.TryNearest(context.IsLit, out var lit))
                return false;
            WalkTo(lit, WorkerState.Fleeing);
            return true;
        }

        private void WalkTo(Vector2Int column, WorkerState state)
        {
            context.Flood.TryPath(column, path);
            pathIndex = 0;
            State = state;
        }

        /// <summary>Пройти по пути отрезок за кадр. true — дошли до конца.</summary>
        private bool Walk(float deltaTime)
        {
            var remaining = context.Settings.MoveSpeed * deltaTime;
            while (pathIndex < path.Count)
            {
                var target = context.Surface.StandPoint(path[pathIndex]);
                var flat = new Vector2(target.x - Position.x, target.z - Position.z);
                var distance = flat.magnitude;
                if (distance <= remaining)
                {
                    Position = target;
                    remaining -= distance;
                    pathIndex++;
                    continue;
                }
                var t = remaining / distance;
                Position = new Vector3(Position.x + flat.x * t, Mathf.Lerp(Position.y, target.y, t), Position.z + flat.y * t);
                return false;
            }
            path.Clear();
            return true;
        }

        private void SnapToGround()
        {
            // Блок под ногами могли выкопать или поставить рядом с рабочим.
            var height = context.Surface.Height(Column);
            if (!Mathf.Approximately(height, Position.y))
                Position = new Vector3(Position.x, height, Position.z);
        }

        private void DropCarried()
        {
            if (Carrying is not { } carried)
                return;
            Carrying = null;
            // Столбец может быть заполнен до потолка мира: тогда кладём на самый верх.
            var cell = new Vector3Int(Mathf.FloorToInt(Position.x),
                Mathf.Min(Mathf.FloorToInt(Position.y), context.World.SizeInBlocks.y - 1), Mathf.FloorToInt(Position.z));
            if (context.World.IsInsideBlocks(cell))
                context.Items.Drop(context.World, cell, carried);
        }
    }
}
