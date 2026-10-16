using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class WorkerTests
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
        public void Flood_GoesAroundHighWallThroughGap()
        {
            // Стена в два блока поперёк карты с проходом у z = 20.
            for (var z = 0; z < 32; z++)
            {
                if (z == 20)
                    continue;
                world.SetBlock(new Vector3Int(10, Ground, z), Block.Full(BlockMaterials.Stone));
                world.SetBlock(new Vector3Int(10, Ground + 1, z), Block.Full(BlockMaterials.Stone));
            }
            var surface = new WalkSurface(world, 1f);
            var flood = new ColumnFlood();
            flood.Run(surface, new Vector2Int(5, 5), null);

            var path = new List<Vector2Int>();
            Assert.That(flood.TryPath(new Vector2Int(15, 5), path), Is.True);
            Assert.That(path, Does.Contain(new Vector2Int(10, 20)), "Путь идёт через проход");
            Assert.That(path[^1], Is.EqualTo(new Vector2Int(15, 5)));
            Assert.That(flood.Distance(new Vector2Int(15, 5)), Is.GreaterThan(20f));
        }

        [Test]
        public void Flood_ClimbsOneBlockButNotTwo()
        {
            var surface = new WalkSurface(world, 1f);
            world.SetBlock(new Vector3Int(6, Ground, 5), Block.Full(BlockMaterials.Stone));
            world.SetBlock(new Vector3Int(8, Ground, 5), Block.Full(BlockMaterials.Stone));
            world.SetBlock(new Vector3Int(8, Ground + 1, 5), Block.Full(BlockMaterials.Stone));

            Assert.That(surface.CanStep(new Vector2Int(5, 5), new Vector2Int(6, 5)), Is.True);
            Assert.That(surface.CanStep(new Vector2Int(7, 5), new Vector2Int(8, 5)), Is.False);
            Assert.That(surface.Height(new Vector2Int(6, 5)), Is.EqualTo(Ground + 1f));
        }

        [Test]
        public void Flood_DoesNotEnterDarkColumns()
        {
            var flood = new ColumnFlood();
            flood.Run(new WalkSurface(world, 1f), new Vector2Int(5, 5), c => c.x < 16);

            Assert.That(flood.Reached(new Vector2Int(15, 5)), Is.True);
            Assert.That(flood.Reached(new Vector2Int(16, 5)), Is.False);
        }

        [Test]
        public void Orders_OnePerCellAndPrunedWhenImpossible()
        {
            var cell = new Vector3Int(5, Ground, 5);
            var blueprint = orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            Assert.That(blueprint, Is.Not.Null);
            Assert.That(orders.AddDig(cell), Is.Null, "В клетке уже есть задача");
            var dig = orders.AddDig(new Vector3Int(8, Ground - 1, 8));
            var cancelled = new List<WorkOrder>();
            orders.Cancelled += cancelled.Add;

            // Клетку чертежа заняли, блок под приказом выкопали.
            world.SetBlock(cell, Block.Full(BlockMaterials.Dirt));
            world.SetBlock(dig.Cell, Block.Air);

            Assert.That(orders.Prune(world), Is.EqualTo(2));
            Assert.That(orders.Orders, Is.Empty);
            Assert.That(cancelled, Is.EquivalentTo(new[] { blueprint, dig }));
        }

        [Test]
        public void Worker_CarriesStoneFromPileAndBuildsBlueprint()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 1));
            var cell = new Vector3Int(14, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));

            var carried = false;
            Run(30f, () =>
            {
                carried |= worker.Carrying != null;
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(carried, Is.True, "Рабочий нёс камень");
            Assert.That(world.GetBlock(cell), Is.EqualTo(Block.Full(BlockMaterials.Stone)));
            Assert.That(items.Items, Is.Empty, "Камень из кучки ушёл в блок");
            Assert.That(worker.State, Is.EqualTo(WorkerState.Idle));
            Assert.That(worker.Carrying, Is.Null);
        }

        [Test]
        public void Worker_CarriesOneStonePerTrip()
        {
            // Чертежу нужно два камня: рабочий носит по одному — две ходки, в кучке остаётся лишний.
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 3));
            var cell = new Vector3Int(10, Ground, 10);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 2));
            var worker = Spawn(new Vector2Int(8, 8));

            var most = 0;
            var trips = 0;
            var wasCarrying = false;
            Run(40f, () =>
            {
                var carrying = worker.Carrying is { ItemId: "stone" };
                if (carrying)
                    most = Mathf.Max(most, worker.Carrying.Value.Count);
                if (carrying && !wasCarrying)
                    trips++;
                wasCarrying = carrying;
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(most, Is.EqualTo(1), "За ходку один камень");
            Assert.That(trips, Is.EqualTo(2), "Две ходки");
            Assert.That(world.GetBlock(cell).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("stone", 1)));
        }

        [Test]
        public void Worker_BuildsFreeBlueprintWithoutItems()
        {
            var cell = new Vector3Int(12, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Dirt), false, null);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(20f, () => orders.Orders.Count == 0, worker);

            Assert.That(world.GetBlock(cell), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
        }

        [Test]
        public void Worker_WaitsForStoneThenBuilds()
        {
            var cell = new Vector3Int(12, Ground, 12);
            var blueprint = orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));

            Run(5f, () => false, worker);
            Assert.That(world.GetBlock(cell).IsAir, Is.True, "Без камня не строит");
            Assert.That(blueprint.Worker, Is.Null, "Чертёж не занят, пока камня нет");

            items.Drop(world, new Vector3Int(3, Ground, 3), new ItemDrop("stone", 1));
            Run(30f, () => orders.Orders.Count == 0, worker);
            Assert.That(world.GetBlock(cell).Material, Is.EqualTo(BlockMaterials.Stone));
        }

        [Test]
        public void Worker_DigsOrderedBlockOneHitPerSecondAndStoneDrops()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            orders.AddDig(cell);
            var worker = Spawn(new Vector2Int(9, 9));

            // Дошёл до блока и ударил ровно один раз за секунду анимации.
            Run(20f, () => damage.Taken(cell) > 0f, worker);
            Assert.That(damage.Taken(cell), Is.EqualTo(settings.DigHitDamage));
            Run(settings.DigHitSeconds * 0.75f, () => false, worker);
            Assert.That(damage.Taken(cell), Is.EqualTo(settings.DigHitDamage), "Следующий удар — через секунду");

            Run(10f, () => orders.Orders.Count == 0, worker);
            Assert.That(world.GetBlock(cell).IsAir, Is.True);
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("stone", 1)));
        }

        [Test]
        public void TwoWorkers_OneBlockIsDugByOneWorker()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            var order = orders.AddDig(cell);
            var first = Spawn(new Vector2Int(9, 9));
            var second = Spawn(new Vector2Int(9, 10));

            Run(20f, () => damage.Taken(cell) > 0f, first, second);

            var digger = order.Worker;
            Assert.That(digger, Is.Not.Null);
            var other = digger == first ? second : first;
            Assert.That(other.Order, Is.Null);
            Assert.That(other.State, Is.EqualTo(WorkerState.Idle));
        }

        [Test]
        public void Worker_StaysInLight()
        {
            Func<Vector2Int, bool> lit = c => c.x < 16;
            // Камень лежит в темноте: за ним не идут, чертёж ждёт.
            items.Drop(world, new Vector3Int(24, Ground, 8), new ItemDrop("stone", 1));
            var cell = new Vector3Int(12, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9), lit);

            Run(20f, () =>
            {
                Assert.That(lit(worker.Column), Is.True, $"Рабочий вышел из света: {worker.Position}");
                return false;
            }, worker);
            Assert.That(world.GetBlock(cell).IsAir, Is.True);
            Assert.That(items.Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void Worker_InDarknessGoesBackToLight()
        {
            Func<Vector2Int, bool> lit = c => c.x < 16;
            var worker = Spawn(new Vector2Int(25, 9), lit);

            Run(20f, () => lit(worker.Column) && worker.State == WorkerState.Idle, worker);

            Assert.That(lit(worker.Column), Is.True);
        }

        [Test]
        public void Worker_DropsStoneWhenBlueprintCancelled()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 1));
            var blueprint = orders.AddBuild(new Vector3Int(20, Ground, 20), Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));
            Run(20f, () => worker.Carrying != null, worker);
            Assert.That(worker.Carrying, Is.Not.Null);

            orders.Cancel(blueprint);
            worker.Tick(Step);

            Assert.That(worker.Carrying, Is.Null);
            Assert.That(worker.Order, Is.Null);
            Assert.That(items.Items, Has.Count.EqualTo(1), "Камень снова лежит на земле");
        }

        [Test]
        public void Worker_LightShrinksWhileDigging_DropsOrderAndGoesBackToLight()
        {
            var litLimit = 32;
            Func<Vector2Int, bool> lit = c => c.x < litLimit;
            var cell = new Vector3Int(20, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            var order = orders.AddDig(cell);
            var worker = Spawn(new Vector2Int(17, 12), lit);
            Run(20f, () => worker.State == WorkerState.Working && damage.Taken(cell) > 0f, worker);
            Assume.That(worker.State, Is.EqualTo(WorkerState.Working));

            // Костёр сжался: рабочий оказался в темноте посреди копки.
            litLimit = 16;
            Run(20f, () => lit(worker.Column) && worker.State == WorkerState.Idle, worker);

            Assert.That(lit(worker.Column), Is.True, $"Рабочий остался в темноте: {worker.Position}");
            Assert.That(order.Worker, Is.Null, "Задача отпущена");
            Assert.That(orders.Contains(order), Is.True, "Задача осталась ждать света");
        }

        [Test]
        public void Worker_TopsUpPartialBlock()
        {
            var cell = new Vector3Int(12, Ground, 12);
            world.SetBlock(cell, Block.Flat(BlockMaterials.Dirt, BlockShape.MaxHeight / 2));
            orders.AddBuild(cell, Block.Full(BlockMaterials.Dirt), true, null);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(20f, () => orders.Orders.Count == 0, worker);

            Assert.That(world.GetBlock(cell), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
        }

        [Test]
        public void TwoWorkers_ShareOnePileForTwoBlueprints()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 2));
            var a = new Vector3Int(12, Ground, 12);
            var b = new Vector3Int(14, Ground, 8);
            orders.AddBuild(a, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            orders.AddBuild(b, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var first = Spawn(new Vector2Int(9, 9));
            var second = Spawn(new Vector2Int(9, 10));

            Run(40f, () => orders.Orders.Count == 0, first, second);

            Assert.That(world.GetBlock(a).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(world.GetBlock(b).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(items.Items, Is.Empty);
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
