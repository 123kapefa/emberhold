using System;
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
    /// <summary>Рабочие и дерево: рубят деревья по приказу и носят поленья в огонь.</summary>
    public sealed class WoodWorkerTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private Forest forest;
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
        public void Worker_ChopsTreeByHitsAndWoodFallsAtBase()
        {
            var tree = forest.Plant(new Vector3Int(14, Ground, 12), birch);
            Assert.That(orders.AddChop(tree), Is.Not.Null);
            Assert.That(orders.AddChop(tree), Is.Null, "Одно дерево — один приказ");
            var worker = Spawn(new Vector2Int(9, 9));

            var hitsSeen = 0;
            var lastDamage = 0f;
            Run(30f, () =>
            {
                if (tree.Damage > lastDamage)
                {
                    hitsSeen++;
                    lastDamage = tree.Damage;
                }
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(tree.IsStanding, Is.False, "Дерево срублено");
            Assert.That(hitsSeen, Is.EqualTo(3), "Берёза падает с третьего удара");
            Assert.That(forest.Trees, Is.Empty);
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 3)));
            Assert.That(items.Items[0].Cell, Is.EqualTo(tree.Cell));
            Assert.That(worker.State, Is.EqualTo(WorkerState.Idle));
        }

        [Test]
        public void ChopOrder_CancelledWhenTreeFallsOtherwise()
        {
            var tree = forest.Plant(new Vector3Int(14, Ground, 12), birch);
            orders.AddChop(tree);
            world.SetBlock(tree.Cell + Vector3Int.down, Block.Air);
            forest.Settle(world);

            Assert.That(orders.Prune(world), Is.EqualTo(1));
            Assert.That(orders.Orders, Is.Empty);
        }

        [Test]
        public void Worker_CarriesWoodToFire()
        {
            var fire = new Fire(10f, 5f);
            items.Drop(world, new Vector3Int(5, Ground, 5), new ItemDrop("wood", 3));
            orders.AddFuel(new Vector3Int(14, Ground, 14), fire, "wood", 2);
            var worker = Spawn(new Vector2Int(9, 9));

            var carried = false;
            Run(30f, () =>
            {
                carried |= worker.Carrying is { ItemId: "wood" };
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(carried, Is.True, "Рабочий нёс поленья");
            Assert.That(fire.Amount, Is.EqualTo(7f), "Два полена в огне");
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 1)), "Третье осталось в кучке");
            Assert.That(worker.Carrying, Is.Null);
        }

        [Test]
        public void Worker_LeavesWoodThatDoesNotFitNextToFire()
        {
            // Пока рабочий шёл, огонь успели накормить: место осталось под одно полено.
            var fire = new Fire(10f, 5f);
            items.Drop(world, new Vector3Int(5, Ground, 5), new ItemDrop("wood", 2));
            orders.AddFuel(new Vector3Int(14, Ground, 14), fire, "wood", 2);
            var worker = Spawn(new Vector2Int(9, 9));
            Run(30f, () =>
            {
                if (worker.Carrying != null)
                    fire.Amount = 9f;
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(fire.Amount, Is.EqualTo(10f));
            Assert.That(items.Items, Has.Count.EqualTo(1), "Лишнее полено лежит у огня");
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 1)));
            Assert.That(Vector2.Distance(new Vector2(items.Items[0].Cell.x, items.Items[0].Cell.z), new Vector2(14f, 14f)),
                Is.LessThan(2f));
        }

        [Test]
        public void Worker_FeedsFireBeforeBuilding()
        {
            var fire = new Fire(10f, 0f);
            items.Drop(world, new Vector3Int(20, Ground, 20), new ItemDrop("wood", 2));
            // Бесплатный чертёж прямо рядом, огонь — дальше.
            var blueprint = orders.AddBuild(new Vector3Int(10, Ground, 9), Block.Full(BlockMaterials.Dirt), false, null);
            orders.AddFuel(new Vector3Int(25, Ground, 25), fire, "wood", 2);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(1f, () => false, worker);
            Assert.That(worker.Order, Is.Not.Null);
            Assert.That(worker.Order.Kind, Is.EqualTo(WorkKind.Fuel), "Огонь важнее стройки");

            Run(60f, () => orders.Orders.Count == 0, worker);
            Assert.That(fire.Amount, Is.EqualTo(2f));
            Assert.That(orders.Contains(blueprint), Is.False, "Потом достроил чертёж");
        }

        [Test]
        public void Worker_SkipsFuelWithoutWoodAndDoesOtherWork()
        {
            var fire = new Fire(10f, 0f);
            orders.AddFuel(new Vector3Int(25, Ground, 25), fire, "wood", 2);
            var cell = new Vector3Int(12, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Dirt), false, null);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(20f, () => !world.GetBlock(cell).IsAir, worker);

            Assert.That(world.GetBlock(cell).IsAir, Is.False, "Без поленьев рабочий не ждёт, а строит");
            Assert.That(orders.Orders, Has.Count.EqualTo(1), "Задача топлива ждёт поленья");
        }

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
                foreach (var worker in workers)
                    worker.Tick(Step);
                items.Settle(world);
                if (done())
                    return;
            }
        }

        private sealed class Fire : IFuelTarget
        {
            private readonly float capacity;

            public Fire(float capacity, float amount)
            {
                this.capacity = capacity;
                Amount = amount;
            }

            public float Amount { get; set; }

            public float FuelSpace => capacity - Amount;

            public float AddFuel(float amount)
            {
                var accepted = Mathf.Clamp(amount, 0f, FuelSpace);
                Amount += accepted;
                return accepted;
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
