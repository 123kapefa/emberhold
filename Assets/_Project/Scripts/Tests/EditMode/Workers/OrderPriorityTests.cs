using System;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>Уровни приказов (#163): обычный, высокий, срочный; срочный снимает ближайшего рабочего с несрочной работы.</summary>
    public sealed class OrderPriorityTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private BlockDamage damage;
        private WorkerSettings settings;

        [SetUp]
        public void SetUp()
        {
            // Ровная земля 32×32: блоки y = 0..3, рабочие стоят на высоте 4.
            world = new VoxelWorld(new Vector3Int(2, 1, 2));
            for (var z = 0; z < 32; z++)
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(BlockMaterials.Dirt));
            orders = new WorkOrders();
            items = new DroppedItems(new StoneDrops());
            damage = new BlockDamage(new UniformBlockDurability(100f));
            damage.Destroyed += (block, destroyed) => items.OnBlockDestroyed(world, block, destroyed);
            settings = ScriptableObject.CreateInstance<WorkerSettings>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void NewOrders_FuelIsHigh_OthersNormal()
        {
            var dig = orders.AddDig(new Vector3Int(5, Ground - 1, 5));
            var build = orders.AddBuild(new Vector3Int(6, Ground, 6), Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var campfire = orders.AddCampfire(new Vector3Int(8, Ground, 8), new ItemDrop("wood", 3));
            var fuel = orders.AddFuel(new Vector3Int(10, Ground, 10), new Fire(), "wood", 1);

            Assert.That(dig.Priority, Is.EqualTo(OrderPriority.Normal));
            Assert.That(build.Priority, Is.EqualTo(OrderPriority.Normal));
            Assert.That(campfire.Priority, Is.EqualTo(OrderPriority.Normal));
            Assert.That(fuel.Priority, Is.EqualTo(OrderPriority.High));
        }

        [Test]
        public void RaisePriority_CyclesNormalHighUrgentNormal()
        {
            var dig = orders.AddDig(new Vector3Int(5, Ground - 1, 5));

            Assert.That(orders.RaisePriority(dig), Is.True);
            Assert.That(dig.Priority, Is.EqualTo(OrderPriority.High));
            Assert.That(orders.RaisePriority(dig), Is.True);
            Assert.That(dig.Priority, Is.EqualTo(OrderPriority.Urgent));
            Assert.That(orders.RaisePriority(dig), Is.True);
            Assert.That(dig.Priority, Is.EqualTo(OrderPriority.Normal));

            orders.Cancel(dig);
            Assert.That(orders.RaisePriority(dig), Is.False, "Снятый приказ не меняется");
            Assert.That(orders.RaisePriority(null), Is.False);
        }

        [Test]
        public void Labels_ShowHighAndUrgent_NotNormalOrFuel()
        {
            var dig = orders.AddDig(new Vector3Int(5, Ground - 1, 5));
            var fuel = orders.AddFuel(new Vector3Int(10, Ground, 10), new Fire(), "wood", 1);

            Assert.That(PriorityLabel.Text(dig), Is.Null);
            orders.RaisePriority(dig);
            Assert.That(PriorityLabel.Text(dig), Is.EqualTo("высокий"));
            orders.RaisePriority(dig);
            Assert.That(PriorityLabel.Text(dig), Is.EqualTo("срочный"));
            Assert.That(PriorityLabel.Text(fuel), Is.Null, "Над огнём подписи нет");
        }

        [Test]
        public void Worker_TakesHigherLevelFirst_EvenIfFarther()
        {
            var near = orders.AddDig(new Vector3Int(6, Ground - 1, 5));
            var far = orders.AddDig(new Vector3Int(20, Ground - 1, 20));
            orders.RaisePriority(far);
            var worker = Spawn(new Vector2Int(5, 5));

            worker.Tick(Step);

            Assert.That(worker.Order, Is.SameAs(far));
            Assert.That(near.Worker, Is.Null);
        }

        [Test]
        public void Worker_SameLevel_TakesNearest()
        {
            var near = orders.AddDig(new Vector3Int(6, Ground - 1, 5));
            var far = orders.AddDig(new Vector3Int(20, Ground - 1, 20));
            orders.RaisePriority(near);
            orders.RaisePriority(far);
            var worker = Spawn(new Vector2Int(5, 5));

            worker.Tick(Step);

            Assert.That(worker.Order, Is.SameAs(near));
        }

        [Test]
        public void Urgent_InterruptsNearestBusyWorker_AnotherPicksUpDroppedTask()
        {
            var normal = orders.AddDig(new Vector3Int(6, Ground - 1, 5));
            var busy = Spawn(new Vector2Int(5, 5));
            var other = Spawn(new Vector2Int(26, 26));
            Run(10f, () => busy.State == WorkerState.Working, busy, other);
            Assert.That(busy.Order, Is.SameAs(normal));

            var urgent = MakeUrgent(orders.AddDig(new Vector3Int(9, Ground - 1, 9)));
            Assert.That(UrgentCall.Dispatch(orders, new[] { busy, other }), Is.EqualTo(1));

            Assert.That(busy.Order, Is.SameAs(urgent), "Ближний бросил обычную задачу ради срочной");
            Assert.That(orders.Contains(normal), Is.True, "Брошенная задача осталась");
            Assert.That(normal.Worker, Is.Null);
            Run(5f, () => normal.Worker == other, busy, other);
            Assert.That(normal.Worker, Is.SameAs(other), "Брошенную задачу подхватил другой");
        }

        [Test]
        public void Urgent_CallsNearestEvenIfAnotherIsIdle_AndFarIdleStaysPut()
        {
            // Занятый стоит у срочного приказа, свободный далеко: идёт ближний.
            var normal = orders.AddDig(new Vector3Int(20, Ground - 1, 20));
            var worker = Spawn(new Vector2Int(19, 19));
            Run(10f, () => worker.State == WorkerState.Working, worker);
            var idle = Spawn(new Vector2Int(2, 2));
            var urgent = MakeUrgent(orders.AddDig(new Vector3Int(22, Ground - 1, 22)));

            UrgentCall.Dispatch(orders, new[] { idle, worker });

            Assert.That(urgent.Worker, Is.SameAs(worker));
            Assert.That(normal.Worker, Is.Null);
        }

        [Test]
        public void Urgent_PrefersNearIdleOverBusyWorker()
        {
            var normal = orders.AddDig(new Vector3Int(20, Ground - 1, 20));
            var busy = Spawn(new Vector2Int(19, 19));
            Run(10f, () => busy.State == WorkerState.Working, busy);
            var idle = Spawn(new Vector2Int(5, 5));
            var urgent = MakeUrgent(orders.AddDig(new Vector3Int(6, Ground - 1, 6)));

            UrgentCall.Dispatch(orders, new[] { busy, idle });

            Assert.That(urgent.Worker, Is.SameAs(idle));
            Assert.That(busy.Order, Is.SameAs(normal), "Занятый, но дальний, работает дальше");
        }

        [Test]
        public void Urgent_DoesNotInterruptUrgentWork()
        {
            var first = MakeUrgent(orders.AddDig(new Vector3Int(6, Ground - 1, 5)));
            var worker = Spawn(new Vector2Int(5, 5));
            worker.Tick(Step);
            Assert.That(worker.Order, Is.SameAs(first));
            var second = MakeUrgent(orders.AddDig(new Vector3Int(7, Ground - 1, 7)));

            Assert.That(UrgentCall.Dispatch(orders, new[] { worker }), Is.EqualTo(0));
            Assert.That(worker.Order, Is.SameAs(first));
            Assert.That(second.Worker, Is.Null);
        }

        [Test]
        public void Urgent_InterruptedCarrier_DropsLoadOnGround_BlueprintKeepsProgress()
        {
            items.Drop(world, new Vector3Int(6, Ground, 5), new ItemDrop("stone", 1));
            var blueprint = orders.AddBuild(new Vector3Int(25, Ground, 25), Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 2));
            var worker = Spawn(new Vector2Int(5, 5));
            Run(10f, () => worker.Carrying != null, worker);
            Assert.That(worker.Carrying, Is.Not.Null);
            Assert.That(items.Items, Is.Empty);

            var urgent = MakeUrgent(orders.AddDig(new Vector3Int(8, Ground - 1, 8)));
            UrgentCall.Dispatch(orders, new[] { worker });

            Assert.That(worker.Order, Is.SameAs(urgent));
            Assert.That(worker.Carrying, Is.Null);
            Assert.That(items.Items, Has.Count.EqualTo(1), "Камень лёг на землю, а не пропал");
            Assert.That(items.Items[0].Drop.ItemId, Is.EqualTo("stone"));
            Assert.That(blueprint.Worker, Is.Null);
            Assert.That(orders.Contains(blueprint), Is.True);
        }

        [Test]
        public void Urgent_InDarkWorkerIsNotCalled()
        {
            // Рабочий стоит в темноте (x ≥ 16), приказ в свету: звать некого.
            var urgent = MakeUrgent(orders.AddDig(new Vector3Int(6, Ground - 1, 5)));
            var worker = Spawn(new Vector2Int(20, 5), c => c.x < 16);

            Assert.That(worker.CanAnswer(urgent), Is.False);
            Assert.That(UrgentCall.Dispatch(orders, new[] { worker }), Is.EqualTo(0));
        }

        private WorkOrder MakeUrgent(WorkOrder order)
        {
            while (order.Priority != OrderPriority.Urgent)
                orders.RaisePriority(order);
            return order;
        }

        private Worker Spawn(Vector2Int column, Func<Vector2Int, bool> isLit = null)
        {
            var context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, isLit);
            return new Worker(context, context.Surface.StandPoint(column));
        }

        /// <summary>Гонять рабочих шагами по 0.1 с, пока done не вернёт true или не выйдет время.</summary>
        private void Run(float seconds, Func<bool> done, params Worker[] workers)
        {
            for (var t = 0f; t < seconds; t += Step)
            {
                orders.Prune(world);
                foreach (var worker in workers)
                    worker.Tick(Step);
                items.Settle(world);
                if (done())
                    return;
            }
        }

        private sealed class Fire : IFuelTarget
        {
            public float FuelSpace => 1f;
            public float FuelFill => 0f;
            public float AddFuel(float amount) => 0f;
        }

        private sealed class StoneDrops : IBlockDrops
        {
            public bool TryGetDrop(byte material, out ItemDrop drop)
            {
                drop = material == BlockMaterials.Stone ? new ItemDrop("stone", 1) : default;
                return material == BlockMaterials.Stone;
            }
        }
    }
}
