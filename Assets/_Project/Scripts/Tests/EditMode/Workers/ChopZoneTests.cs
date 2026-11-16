using System;
using System.Linq;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>Зона рубки (#167): круг, в котором рабочие рубят все деревья и корчуют пни; уровень и число людей — у зоны.</summary>
    public sealed class ChopZoneTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private Forest forest;
        private WorkZones zones;
        private WorkerSettings settings;
        private TreeSpecies birch;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new Vector3Int(2, 1, 2));
            for (var z = 0; z < 32; z++)
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(BlockMaterials.Dirt));
            orders = new WorkOrders();
            items = new DroppedItems(new NoDrops());
            forest = new Forest(items);
            zones = new WorkZones();
            settings = ScriptableObject.CreateInstance<WorkerSettings>();
            birch = TreeSpecies.Create("Берёза", 75f, 3);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(birch);
        }

        [Test]
        public void Zone_OrdersTreesInsideCircleOnly()
        {
            var inside = forest.Plant(Cell(12, 10), birch);
            var edge = forest.Plant(Cell(10, 14), birch);
            var outside = forest.Plant(Cell(16, 10), birch);
            var zone = zones.AddChop(Cell(10, 10), 4);

            Assert.That(zones.Update(world, forest, orders), Is.EqualTo(2));

            Assert.That(orders.At(inside.Cell)?.Assignment, Is.SameAs(zone.Assignment));
            Assert.That(orders.At(edge.Cell)?.Assignment, Is.SameAs(zone.Assignment), "Радиус включительно");
            Assert.That(orders.At(outside.Cell), Is.Null);
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(2));
            Assert.That(zones.Update(world, forest, orders), Is.EqualTo(0), "Повторно приказы не ставятся");
        }

        [Test]
        public void Zone_TakesStumpsAndTreesThatAppearLater()
        {
            var zone = zones.AddChop(Cell(10, 10), 5);
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders, Is.Empty);

            var tree = forest.Plant(Cell(11, 11), birch);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(tree.Cell)?.Tree, Is.SameAs(tree), "Новое дерево в зоне — тоже под приказом");

            // Срубленное дерево оставляет пень: зона ставит на него приказ корчевать.
            Assert.That(forest.Hit(world, tree, 1000f), Is.True);
            orders.Prune(world);
            var stump = forest.StumpAt(tree.Cell);
            Assert.That(stump, Is.Not.Null);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(stump.Cell)?.Stump, Is.SameAs(stump));
            Assert.That(orders.At(stump.Cell).Assignment, Is.SameAs(zone.Assignment));
        }

        [Test]
        public void Zone_AdoptsFreeSingleOrder_ButNotTakenOne()
        {
            var free = forest.Plant(Cell(11, 10), birch);
            var taken = forest.Plant(Cell(9, 10), birch);
            var single = orders.AddChop(free);
            var held = orders.AddChop(taken);
            var worker = Spawn(new Vector2Int(8, 10));
            worker.Tick(Step);
            Assert.That(held.Worker ?? single.Worker, Is.SameAs(worker));
            var heldNow = held.Worker != null ? held : single;
            var freeNow = heldNow == held ? single : held;

            var zone = zones.AddChop(Cell(10, 10), 3);
            zones.Update(world, forest, orders);

            Assert.That(freeNow.Assignment, Is.SameAs(zone.Assignment), "Свободный одиночный приказ перешёл в зону");
            Assert.That(heldNow.Assignment, Is.Not.SameAs(zone.Assignment), "Взятый приказ остаётся у рабочего");
        }

        [Test]
        public void Zone_PriorityGoesToAllOrders_AndToNewOnes()
        {
            forest.Plant(Cell(11, 10), birch);
            var zone = zones.AddChop(Cell(10, 10), 4);
            zones.Update(world, forest, orders);

            zones.SetPriority(zone, OrderPriority.Urgent);
            Assert.That(zone.Assignment.Orders.All(o => o.Priority == OrderPriority.Urgent), Is.True);

            var later = forest.Plant(Cell(9, 9), birch);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(later.Cell).Priority, Is.EqualTo(OrderPriority.Urgent));
        }

        [Test]
        public void Zone_ShrinkCancelsOutsideOrders_RemoveCancelsAll()
        {
            var near = forest.Plant(Cell(11, 10), birch);
            var far = forest.Plant(Cell(14, 10), birch);
            var zone = zones.AddChop(Cell(10, 10), 5);
            zones.Update(world, forest, orders);
            Assert.That(orders.Orders, Has.Count.EqualTo(2));

            Assert.That(zones.SetRadius(zone, 2, orders), Is.EqualTo(2));
            Assert.That(orders.At(far.Cell), Is.Null);
            Assert.That(orders.At(near.Cell), Is.Not.Null);
            Assert.That(zones.SetRadius(zone, 100, orders), Is.EqualTo(ChopZone.MaxRadius));

            Assert.That(zones.Remove(zone, orders), Is.True);
            Assert.That(orders.Orders, Is.Empty);
            Assert.That(zones.All, Is.Empty);
            zones.Update(world, forest, orders);
            Assert.That(orders.Orders, Is.Empty, "Снятая зона приказов больше не ставит");
        }

        [Test]
        public void Zone_At_FindsCoveringZone_OverlapGoesToFirst()
        {
            var first = zones.AddChop(Cell(10, 10), 3);
            var second = zones.AddChop(Cell(14, 10), 3);
            var shared = forest.Plant(Cell(12, 10), birch);

            Assert.That(zones.At(Cell(9, 10)), Is.SameAs(first));
            Assert.That(zones.At(Cell(15, 10)), Is.SameAs(second));
            Assert.That(zones.At(Cell(25, 25)), Is.Null);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(shared.Cell).Assignment, Is.SameAs(first.Assignment));
        }

        [Test]
        public void Zone_MaxWorkersLimitsPeople_AndCanBeRaised()
        {
            forest.Plant(Cell(10, 12), birch);
            forest.Plant(Cell(12, 12), birch);
            forest.Plant(Cell(14, 12), birch);
            var zone = zones.AddChop(Cell(12, 12), 4, 1);
            var a = Spawn(new Vector2Int(8, 8));
            var b = Spawn(new Vector2Int(16, 8));

            Run(1f, () => false, a, b);
            Assert.That(zone.Assignment.Workers, Has.Count.EqualTo(1), "В зоне один человек");
            Assert.That(new[] { a, b }.Count(w => w.Order != null), Is.EqualTo(1));

            Assert.That(zones.SetMaxWorkers(zone, 2, 2), Is.EqualTo(2));
            Run(1f, () => false, a, b);
            Assert.That(zone.Assignment.Workers, Has.Count.EqualTo(2));
            Assert.That(zones.SetMaxWorkers(zone, 9, 2), Is.EqualTo(2), "Не больше людей, чем в отряде");
        }

        [Test]
        public void Zone_LoweredMaxWorkers_ExtraLeavesAfterItsOrder()
        {
            for (var x = 8; x <= 16; x += 2)
                forest.Plant(Cell(x, 12), birch);
            var zone = zones.AddChop(Cell(12, 12), 5, 2);
            var a = Spawn(new Vector2Int(8, 8));
            var b = Spawn(new Vector2Int(16, 8));
            Run(1f, () => false, a, b);
            Assert.That(zone.Assignment.Workers, Has.Count.EqualTo(2));

            zones.SetMaxWorkers(zone, 1, 2);
            Run(20f, () => zone.Assignment.Workers.Count == 1, a, b);
            Assert.That(zone.Assignment.Workers, Has.Count.EqualTo(1), "Лишний ушёл из зоны, доделав приказ");
        }

        [Test]
        public void Workers_ClearZone_TreesAndTheirStumps()
        {
            forest.Plant(Cell(10, 12), birch);
            forest.Plant(Cell(13, 12), birch);
            var keep = forest.Plant(Cell(25, 25), birch);
            var zone = zones.AddChop(Cell(11, 12), 3);
            var worker = Spawn(new Vector2Int(8, 8));

            Run(120f, () => forest.Trees.Count == 1 && forest.Stumps.All(s => !zone.Covers(s.Cell)), worker);

            Assert.That(forest.Trees, Is.EqualTo(new[] { keep }), "Деревья в зоне срублены, за ней — нет");
            Assert.That(forest.Stumps.Where(s => s.IsPresent && zone.Covers(s.Cell)), Is.Empty, "Пни в зоне выкорчеваны");
            Assert.That(zone.Assignment.Orders, Is.Empty);
        }

        [Test]
        public void Draft_ChopZoneWaitsForStart()
        {
            var tree = forest.Plant(Cell(11, 10), birch);
            var zone = zones.AddChop(Cell(10, 10), 4, draft: true);
            zones.Update(world, forest, orders);
            Assert.That(orders.Orders, Is.Empty, "Черновик приказов не ставит");
            Assert.That(WorkZoneView.Label(zone), Is.EqualTo("черновик · рубка · люди 0/2"));

            Assert.That(zones.Start(zone), Is.True);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(tree.Cell)?.Assignment, Is.SameAs(zone.Assignment));
        }

        [Test]
        public void Labels_ShowZoneLevelAndPeople()
        {
            var zone = zones.AddChop(Cell(10, 10), 4, 2);
            Assert.That(WorkZoneView.Label(zone), Is.EqualTo("рубка · люди 0/2"));
            zones.SetPriority(zone, OrderPriority.High);
            Assert.That(WorkZoneView.Label(zone), Is.EqualTo("рубка · люди 0/2"), "Приоритет на карте не показывается, только в панели");
            Assert.That(PriorityLabel.Name(OrderPriority.Urgent), Is.EqualTo("срочный"));
        }

        private static Vector3Int Cell(int x, int z) => new(x, Ground, z);

        private Worker Spawn(Vector2Int column)
        {
            var context = new WorkerContext(world, orders, items, new BlockDamage(new UniformBlockDurability(100f)),
                new BlockStability(), settings, null, null, null, forest);
            return new Worker(context, context.Surface.StandPoint(column));
        }

        private void Run(float seconds, Func<bool> done, params Worker[] workers)
        {
            for (var t = 0f; t < seconds; t += Step)
            {
                orders.Prune(world);
                zones.Update(world, forest, orders);
                foreach (var worker in workers)
                    worker.Tick(Step);
                items.Settle(world);
                if (done())
                    return;
            }
        }

        private sealed class NoDrops : IBlockDrops
        {
            public bool TryGetDrop(byte material, out ItemDrop drop)
            {
                drop = default;
                return false;
            }
        }
    }
}
