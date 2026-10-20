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
    /// <summary>Рабочие строят новый костёр по чертежу: носят поленья по одному, потом ставят огонь.</summary>
    public sealed class CampfireWorkerTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private WorkerSettings settings;
        private List<Vector3Int> built;

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
            settings = ScriptableObject.CreateInstance<WorkerSettings>();
            built = new List<Vector3Int>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void Worker_BuildsCampfireFromSixLogs()
        {
            var cell = new Vector3Int(14, Ground, 14);
            items.Drop(world, new Vector3Int(5, Ground, 5), new ItemDrop("wood", 8));
            var order = orders.AddCampfire(cell, new ItemDrop("wood", 6));
            Assert.That(order, Is.Not.Null);
            Assert.That(orders.AddCampfire(cell, new ItemDrop("wood", 6)), Is.Null, "В клетке уже чертёж");
            var worker = Spawn(new Vector2Int(9, 9));

            var trips = 0;
            var carrying = false;
            Run(120f, () =>
            {
                if (worker.Carrying != null && !carrying)
                    trips++;
                carrying = worker.Carrying != null;
                return orders.Orders.Count == 0;
            }, worker);

            Assert.That(built, Is.EqualTo(new[] { cell }), "Костёр поставлен в клетку чертежа");
            Assert.That(trips, Is.EqualTo(6), "По полену за ходку");
            Assert.That(order.Delivered, Is.EqualTo(6));
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 2)), "Лишние поленья остались в кучке");
            Assert.That(world.GetBlock(cell).IsAir, Is.True, "Костёр — не блок");
            Assert.That(worker.State, Is.EqualTo(WorkerState.Idle));
        }

        [Test]
        public void Worker_DoesNotBuildCampfireWithoutEnoughLogs()
        {
            var cell = new Vector3Int(14, Ground, 14);
            items.Drop(world, new Vector3Int(5, Ground, 5), new ItemDrop("wood", 3));
            var order = orders.AddCampfire(cell, new ItemDrop("wood", 6));
            var worker = Spawn(new Vector2Int(9, 9));

            Run(60f, () => false, worker);

            Assert.That(built, Is.Empty, "Без шести поленьев костра нет");
            Assert.That(order.Delivered, Is.EqualTo(3), "Всё, что было, принесено к чертежу");
            Assert.That(orders.Contains(order), Is.True, "Чертёж ждёт поленья");
        }

        [Test]
        public void CampfireOrder_NeedsEmptyCellOnGround()
        {
            Assert.That(WorkOrders.CanHoldCampfire(world, new Vector3Int(10, Ground, 10)), Is.True);
            Assert.That(WorkOrders.CanHoldCampfire(world, new Vector3Int(10, Ground - 1, 10)), Is.False, "Клетка занята блоком");
            Assert.That(WorkOrders.CanHoldCampfire(world, new Vector3Int(10, Ground + 1, 10)), Is.False, "Под клеткой пусто");
        }

        [Test]
        public void CampfireOrder_CancelledWhenCellTakenOrGroundDug()
        {
            var taken = orders.AddCampfire(new Vector3Int(10, Ground, 10), new ItemDrop("wood", 6));
            var dug = orders.AddCampfire(new Vector3Int(20, Ground, 20), new ItemDrop("wood", 6));
            var cancelled = new List<WorkOrder>();
            orders.Cancelled += cancelled.Add;

            world.SetBlock(taken.Cell, Block.Full(BlockMaterials.Dirt));
            world.SetBlock(dug.Cell + Vector3Int.down, Block.Air);

            Assert.That(orders.Prune(world), Is.EqualTo(2));
            Assert.That(cancelled, Is.EquivalentTo(new[] { taken, dug }));
        }

        private Worker Spawn(Vector2Int column)
        {
            var context = new WorkerContext(world, orders, items, new BlockDamage(new UniformBlockDurability(100f)),
                new BlockStability(), settings, null, null, null, null, campfireBuilt: built.Add);
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
