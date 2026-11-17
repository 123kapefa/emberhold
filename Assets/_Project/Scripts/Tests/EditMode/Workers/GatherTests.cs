using System;
using System.Collections.Generic;
using System.Linq;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Bushes;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>
    /// Сбор травы (#88): приказ на куст, класс сборщика, трава на поддон, зона сбора берёт только кусты и ждёт отрастания.
    /// </summary>
    public sealed class GatherTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private Forest forest;
        private WorkZones zones;
        private Pallets pallets;
        private WorkerSettings settings;
        private WorkerContext context;
        private BushSpecies bushSpecies;
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
            pallets = new Pallets(settings.PalletCapacity);
            bushSpecies = BushSpecies.Create("Куст", 50f, 3, 2f);
            birch = TreeSpecies.Create("Берёза", 75f, 3);
            context = new WorkerContext(world, orders, items, new BlockDamage(new UniformBlockDurability(100f)), new BlockStability(),
                settings, null, forest: forest, pallets: pallets);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(bushSpecies);
            Object.DestroyImmediate(birch);
        }

        [Test]
        public void GatherKind_GivesGathererRole()
        {
            Assert.That(WorkerRoles.For(WorkKind.Gather), Is.EqualTo(WorkerRole.Gatherer));
            Assert.That(new WorkAssignment(WorkKind.Gather).Accepts(WorkKind.Chop), Is.False, "Сбор и рубка — разные задания");
            Assert.That(new WorkAssignment(WorkKind.Chop).Accepts(WorkKind.Gather), Is.False);
            Assert.That(settings.HoodFor(WorkerRole.Gatherer), Is.Not.EqualTo(settings.HoodFor(WorkerRole.Woodcutter)), "У сборщика свой капюшон");
        }

        [Test]
        public void Order_OnlyOnGreenBush_OnePerBush()
        {
            var bush = Plant(12, 12);
            Assert.That(orders.AddGather(bush), Is.Not.Null);
            Assert.That(orders.AddGather(bush), Is.Null, "Один куст — один приказ");
            orders.Cancel(orders.At(bush.Cell));
            forest.Thicket.Hit(world, bush, 1000f);
            Assert.That(orders.AddGather(bush), Is.Null, "Голый куст не поручить: трава не отросла");
        }

        [Test]
        public void Worker_CutsBush_AndCarriesGrassToPalletAsGatherer()
        {
            var bush = Plant(14, 12);
            orders.AddGather(bush);
            var pallet = pallets.Add(world, new Vector3Int(18, Ground, 8));
            var worker = Spawn(new Vector2Int(9, 9));

            var roles = new HashSet<WorkerRole>();
            var carriedGrass = false;
            var cutHits = 0;
            var lastDamage = 0f;
            Run(90f, () =>
            {
                roles.Add(worker.Role);
                carriedGrass |= worker.Carrying is { ItemId: "grass" } && worker.HaulTarget == pallet;
                if (bush.Damage > lastDamage)
                    cutHits++;
                lastDamage = bush.Damage;
                return pallet.Count == 3;
            }, worker);

            Assert.That(bush.IsPresent, Is.True, "Куст остался");
            Assert.That(bush.IsGrown, Is.False, "Голый");
            Assert.That(cutHits, Is.EqualTo(1), "Срезан со второго удара: первый копит урон, второй срезает");
            Assert.That(pallet.ItemId, Is.EqualTo("grass"));
            Assert.That(pallet.Count, Is.EqualTo(3), "Вся трава на поддоне");
            Assert.That(carriedGrass, Is.True, "Несёт траву сам");
            Assert.That(roles, Does.Contain(WorkerRole.Gatherer));
            Assert.That(roles, Does.Not.Contain(WorkerRole.Woodcutter));
            Assert.That(WorkerCarryBuilder.CarryFor("grass", WorkerRole.Gatherer), Is.EqualTo(WorkerCarry.Sheaf), "Сноп на плече");
            Run(1f, () => false, worker);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Отнёс — снова свободный");
            Assert.That(orders.Orders, Is.Empty, "Одиночный приказ выполнен и после отрастания сам не встаёт");
        }

        [Test]
        public void GrassPallet_FillsUpTo70()
        {
            Assert.That(settings.PalletCapacity("grass"), Is.EqualTo(70));
        }

        [Test]
        public void Zone_TakesOnlyBushesInCircle_NotTrees()
        {
            var inside = Plant(12, 10);
            var outside = Plant(16, 10);
            var tree = forest.Plant(new Vector3Int(10, Ground, 12), birch);
            var stumpTree = forest.Plant(new Vector3Int(8, Ground, 10), birch);
            forest.Hit(world, stumpTree, 1000f);
            var zone = zones.AddGather(new Vector3Int(10, Ground, 10), 4);

            Assert.That(zones.Update(world, forest, orders), Is.EqualTo(1));

            Assert.That(orders.At(inside.Cell)?.Kind, Is.EqualTo(WorkKind.Gather));
            Assert.That(orders.At(inside.Cell).Assignment, Is.SameAs(zone.Assignment));
            Assert.That(orders.At(outside.Cell), Is.Null);
            Assert.That(orders.At(tree.Cell), Is.Null, "Деревья зона сбора не трогает");
            Assert.That(orders.At(stumpTree.Cell), Is.Null, "И пни тоже");
            Assert.That(zone.Kind, Is.EqualTo(WorkKind.Gather));
            Assert.That(zones.At(inside.Cell, WorkKind.Gather), Is.SameAs(zone));
            Assert.That(zones.At(inside.Cell, WorkKind.Chop), Is.Null);
            Assert.That(WorkZoneView.Label(zone), Is.EqualTo("сбор травы · люди 0/2"));
        }

        [Test]
        public void ChopZone_DoesNotTakeBushes()
        {
            var bush = Plant(11, 10);
            zones.AddChop(new Vector3Int(10, Ground, 10), 4);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(bush.Cell), Is.Null);
        }

        [Test]
        public void Zone_WaitsForBareBushToRegrow_AndWorkerComesBack()
        {
            var bush = Plant(12, 10);
            zones.AddGather(new Vector3Int(10, Ground, 10), 3);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(40f, () => !bush.IsGrown, worker);
            Assert.That(bush.IsGrown, Is.False, "Первый срез");
            Run(2f, () => false, worker);
            Assert.That(orders.At(bush.Cell), Is.Null, "Голый куст ждёт: приказа нет");
            Assert.That(zones.All, Has.Count.EqualTo(1), "Зона не ушла");
            var grassAfterFirst = items.Items.Sum(i => i.Drop.ItemId == "grass" ? i.Drop.Count : 0);

            forest.Thicket.Grow(bushSpecies.RegrowDays);
            Assert.That(bush.IsGrown, Is.True);
            Run(40f, () => !bush.IsGrown, worker);

            Assert.That(bush.IsGrown, Is.False, "Рабочие вернулись к отросшему кусту");
            Assert.That(items.Items.Sum(i => i.Drop.ItemId == "grass" ? i.Drop.Count : 0), Is.EqualTo(grassAfterFirst + 3),
                "Вторая охапка травы");
        }

        [Test]
        public void DarkBush_WaitsForLight()
        {
            var bush = Plant(20, 10);
            var dark = new WorkerContext(world, orders, items, new BlockDamage(new UniformBlockDurability(100f)), new BlockStability(),
                settings, column => column.x < 16, forest: forest, pallets: pallets);
            zones.AddGather(new Vector3Int(18, Ground, 10), 4);
            var worker = new Worker(dark, dark.Surface.StandPoint(new Vector2Int(9, 9)));

            Run(20f, () => false, worker);

            Assert.That(orders.At(bush.Cell), Is.Not.Null, "Приказ стоит");
            Assert.That(bush.IsGrown, Is.True, "Но в темноте его не берут");
            Assert.That(worker.Order, Is.Null);
        }

        [Test]
        public void LevelZone_DoesNotFillBushCell_AndDiggingUnderRemovesIt()
        {
            var bush = Plant(12, 10);
            Assert.That(LevelZone.FillFrom(world, forest, null, _ => Ground, 12, 10, Ground, Ground - 1), Is.Null,
                "В клетку куста землю не насыпают");
            Assert.That(LevelZone.FillFrom(world, forest, null, _ => Ground, 13, 10, Ground, Ground - 1), Is.Not.Null);

            orders.AddGather(bush);
            world.SetBlock(bush.Cell + Vector3Int.down, Block.Air);
            forest.Settle(world);
            Assert.That(bush.IsPresent, Is.False);
            Assert.That(orders.Prune(world), Is.EqualTo(1), "Приказ на пропавший куст снят");
        }

        private Bush Plant(int x, int z) => forest.Thicket.Plant(new Vector3Int(x, Ground, z), bushSpecies);

        private Worker Spawn(Vector2Int column) => new(context, context.Surface.StandPoint(column));

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
