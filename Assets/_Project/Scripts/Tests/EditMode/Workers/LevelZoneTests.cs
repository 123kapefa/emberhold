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
    /// <summary>Зона копки (#136): прямоугольник с целевым уровнем, рабочие выкапывают всё выше уровня слоями сверху.</summary>
    public sealed class LevelZoneTests
    {
        private const float Step = 0.1f;
        // Земля y = 0..3: верхний блок на высоте 3, рабочие стоят на 4.
        private const int Top = 3;

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
            for (var y = 0; y <= Top; y++)
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
        public void AddLevel_RectBetweenBlocks_LevelOneBelowStart()
        {
            var zone = zones.AddLevel(new Vector3Int(8, Top, 6), new Vector3Int(5, Top, 7), world: world);

            Assert.That(zone.Min, Is.EqualTo(new Vector2Int(5, 6)));
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(4, 2)));
            Assert.That(zone.Level, Is.EqualTo(Top - 1), "Снять один слой");
            Assert.That(zone.Top, Is.EqualTo(Top), "Верх — самый высокий блок");
            Assert.That(zone.Bottom, Is.EqualTo(Top - 1), "Земля ровная и выше уровня — низ на уровне");
            Assert.That(zone.Kind, Is.EqualTo(WorkKind.Dig));
            Assert.That(zone.Mode, Is.EqualTo(LevelZoneMode.Level), "По умолчанию — выровнять");
            Assert.That(zone.Covers(new Vector3Int(8, 0, 7)), Is.True);
            Assert.That(zone.Covers(new Vector3Int(9, 0, 7)), Is.False);
        }

        [Test]
        public void Update_OrdersOnlyTopBlockOfEachColumn()
        {
            // В одном столбце лишний блок сверху: приказ только на него, не на блок под ним.
            world.SetBlock(new Vector3Int(6, Top + 1, 6), Block.Full(BlockMaterials.Dirt));
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(7, Top, 6), world: world);

            Assert.That(zones.Update(world, forest, orders), Is.EqualTo(6));

            Assert.That(orders.At(new Vector3Int(6, Top + 1, 6))?.Assignment, Is.SameAs(zone.Assignment));
            Assert.That(orders.At(new Vector3Int(6, Top, 6)), Is.Null, "Нижний слой — после верхнего");
            Assert.That(zone.Assignment.Orders.All(o => o.Kind == WorkKind.Dig), Is.True);

            // Верхний выкопали: следующий слой этого столбца встаёт сам, пока он выше уровня.
            world.SetBlock(new Vector3Int(6, Top + 1, 6), Block.Air);
            orders.Prune(world);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(new Vector3Int(6, Top, 6))?.Assignment, Is.SameAs(zone.Assignment));
            Assert.That(orders.At(new Vector3Int(6, Top - 1, 6)), Is.Null, "Уровень не копают");
        }

        [Test]
        public void Update_SkipsCliffBedrockAndStumps()
        {
            world.SetBlock(new Vector3Int(5, Top, 5), Block.Full(BlockMaterials.Cliff));
            world.SetBlock(new Vector3Int(6, Top, 5), Block.Full(BlockMaterials.Bedrock));
            var tree = forest.Plant(new Vector3Int(7, Top + 1, 5), birch);
            Assert.That(forest.Hit(world, tree, 1000f), Is.True);
            Assert.That(forest.StumpAt(new Vector3Int(7, Top + 1, 5)), Is.Not.Null);
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(8, Top, 5), world: world);

            zones.Update(world, forest, orders);

            Assert.That(zone.Assignment.Orders.Select(o => o.Cell), Is.EquivalentTo(new[] { new Vector3Int(8, Top, 5) }));
        }

        [Test]
        public void Zone_WithNothingAboveLevel_Leaves()
        {
            world.SetBlock(new Vector3Int(5, Top, 5), Block.Full(BlockMaterials.Cliff));
            zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(5, Top, 5), world: world);

            zones.Update(world, forest, orders);

            Assert.That(zones.All, Is.Empty, "Копать нечего — зона ушла");
            Assert.That(orders.Orders, Is.Empty);
        }

        [Test]
        public void SetLevelAndSize_RecalculateOrders()
        {
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(8, Top, 5), world: world);
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(4));

            Assert.That(zones.SetSize(zone, new Vector2Int(2, 1), orders), Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(zone.Assignment.Orders.Select(o => o.Cell.x), Is.EquivalentTo(new[] { 5, 6 }), "Лишние приказы сняты");
            Assert.That(orders.Orders, Has.Count.EqualTo(2));
            Assert.That(zones.SetSize(zone, new Vector2Int(99, 0), orders), Is.EqualTo(new Vector2Int(LevelZone.MaxSize, LevelZone.MinSize)));

            zones.SetSize(zone, new Vector2Int(2, 1), orders);
            zones.SetLevel(zone, Top, orders);
            Assert.That(orders.Orders, Is.Empty, "На уровне и ниже не копают");
            zones.Update(world, forest, orders);
            Assert.That(zones.All, Is.Empty, "Поднятый до верха уровень — копать нечего");
        }

        [Test]
        public void Zone_AdoptsFreeSingleDigOrder_NotOtherKinds()
        {
            var single = orders.AddDig(new Vector3Int(5, Top, 5));
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), world: world);
            zones.SetPriority(zone, OrderPriority.High);

            zones.Update(world, forest, orders);

            Assert.That(single.Assignment, Is.SameAs(zone.Assignment));
            Assert.That(single.Priority, Is.EqualTo(OrderPriority.High));
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(2));
        }

        [Test]
        public void Workers_DigRectDownToLevel_ThenZoneLeaves()
        {
            var zone = zones.AddLevel(new Vector3Int(10, Top, 10), new Vector3Int(12, Top, 11), world: world);
            var a = Spawn(new Vector2Int(8, 8));
            var b = Spawn(new Vector2Int(14, 14));

            Run(120f, () => zones.All.Count == 0, a, b);

            Assert.That(zones.All, Is.Empty, "Зона ушла, когда копать стало нечего");
            for (var z = 10; z <= 11; z++)
            for (var x = 10; x <= 12; x++)
            {
                Assert.That(world.GetBlock(new Vector3Int(x, Top, z)).IsAir, Is.True, $"Слой выкопан: {x},{z}");
                Assert.That(world.GetBlock(new Vector3Int(x, Top - 1, z)).IsAir, Is.False, $"Уровень цел: {x},{z}");
            }
            Assert.That(world.GetBlock(new Vector3Int(13, Top, 10)).IsAir, Is.False, "За зоной не копают");
            Assert.That(zone.Assignment.Orders, Is.Empty);
        }

        [Test]
        public void Workers_DigTwoLayers_TopFirst()
        {
            var zone = zones.AddLevel(new Vector3Int(10, Top, 10), new Vector3Int(11, Top, 10), world: world);
            zones.SetLevel(zone, Top - 2, orders);
            var worker = Spawn(new Vector2Int(8, 8));
            var lowerBeforeUpper = false;

            Run(120f, () =>
            {
                // Нижний слой не трогают, пока над ним (в том же или соседнем столбце) стоит верхний.
                lowerBeforeUpper |= world.GetBlock(new Vector3Int(10, Top - 1, 10)).IsAir && !world.GetBlock(new Vector3Int(11, Top, 10)).IsAir
                                    || world.GetBlock(new Vector3Int(11, Top - 1, 10)).IsAir && !world.GetBlock(new Vector3Int(10, Top, 10)).IsAir;
                return zones.All.Count == 0;
            }, worker);

            Assert.That(zones.All, Is.Empty);
            Assert.That(lowerBeforeUpper, Is.False, "Копают слоями сверху вниз");
            Assert.That(world.GetBlock(new Vector3Int(10, Top - 1, 10)).IsAir, Is.True);
            Assert.That(world.GetBlock(new Vector3Int(10, Top - 2, 10)).IsAir, Is.False);
        }

        [Test]
        public void DarkColumns_WaitForLight()
        {
            // Светло только при x < 12: зона на 10..13 копается наполовину и ждёт.
            var zone = zones.AddLevel(new Vector3Int(10, Top, 10), new Vector3Int(13, Top, 10), world: world);
            var worker = Spawn(new Vector2Int(8, 10), c => c.x < 12);

            Run(60f, () => world.GetBlock(new Vector3Int(10, Top, 10)).IsAir && world.GetBlock(new Vector3Int(11, Top, 10)).IsAir, worker);
            Run(5f, () => false, worker);

            Assert.That(world.GetBlock(new Vector3Int(11, Top, 10)).IsAir, Is.True);
            Assert.That(world.GetBlock(new Vector3Int(12, Top, 10)).IsAir, Is.False, "Тёмный блок ждёт света");
            Assert.That(zones.All, Has.Member(zone), "Зона ждёт, пока не выкопано всё");
        }

        [Test]
        public void Draft_PlacesNoOrders_DoesNotLeave_StartsOnStart()
        {
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), draft: true, world: world);
            var worker = Spawn(new Vector2Int(8, 8));

            Run(3f, () => false, worker);
            Assert.That(orders.Orders, Is.Empty, "Черновик приказов не ставит");
            Assert.That(worker.Order, Is.Null, "Рабочий за черновик не берётся");

            // Черновик, где копать нечего, сам не уходит.
            zones.SetLevel(zone, Top + 3, orders);
            zones.Update(world, forest, orders);
            Assert.That(zones.All, Has.Member(zone));
            zones.SetLevel(zone, Top - 1, orders);

            // Настройки черновика держатся до старта.
            zones.SetPriority(zone, OrderPriority.High);
            zones.SetSize(zone, new Vector2Int(3, 1), orders);
            Assert.That(zones.Start(zone), Is.True);
            Assert.That(zones.Start(zone), Is.False, "Уже начата");
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(3));
            Assert.That(zone.Assignment.Orders.All(o => o.Priority == OrderPriority.High), Is.True);
            Assert.That(WorkZoneView.Label(zone), Does.Not.StartWith("черновик"));
        }

        [Test]
        public void Draft_DoesNotTakeOrdersFromWorkingZone()
        {
            var draft = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), draft: true, world: world);
            var working = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), world: world);
            zones.Update(world, forest, orders);

            Assert.That(working.Assignment.Orders, Has.Count.EqualTo(2), "Клетки, накрытые черновиком, достаются работающей зоне");
            Assert.That(draft.Assignment.Orders, Is.Empty);
            Assert.That(WorkZoneView.Label(draft), Is.EqualTo($"черновик · выровнять до {Top - 1} · люди 0/2"));
        }

        [Test]
        public void Defaults_TopIsHighestBlock_BottomIsLowestGround()
        {
            world.SetBlock(new Vector3Int(6, Top + 1, 6), Block.Full(BlockMaterials.Dirt));
            world.SetBlock(new Vector3Int(7, Top, 6), Block.Air);
            world.SetBlock(new Vector3Int(7, Top - 1, 6), Block.Air);

            var zone = zones.AddLevel(new Vector3Int(5, Top, 6), new Vector3Int(8, Top, 6), world: world);

            Assert.That(zone.Level, Is.EqualTo(Top - 1));
            Assert.That(zone.Top, Is.EqualTo(Top + 1));
            Assert.That(zone.Bottom, Is.EqualTo(Top - 2), "Низ — верхний блок самого низкого столбца");
        }

        [Test]
        public void Top_BlocksAboveAreNotDug_ChangingTopRecalculatesOrders()
        {
            // Столб в столбце (6, 5): блоки до высоты Top + 3.
            for (var y = Top + 1; y <= Top + 3; y++)
                world.SetBlock(new Vector3Int(6, y, 5), Block.Full(BlockMaterials.Dirt));
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), world: world);
            Assert.That(zone.Top, Is.EqualTo(Top + 3));

            zones.SetTop(zone, Top + 1, orders);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(new Vector3Int(6, Top + 1, 5))?.Assignment, Is.SameAs(zone.Assignment), "Копают с верха коробки");
            Assert.That(orders.At(new Vector3Int(6, Top + 3, 5)), Is.Null, "Выше верха не трогают");
            Assert.That(orders.At(new Vector3Int(6, Top + 2, 5)), Is.Null);

            zones.SetTop(zone, Top, orders);
            Assert.That(orders.At(new Vector3Int(6, Top + 1, 5)), Is.Null, "Приказ выше нового верха снят");
            zones.Update(world, forest, orders);
            Assert.That(orders.At(new Vector3Int(6, Top, 5))?.Assignment, Is.SameAs(zone.Assignment));

            zones.SetTop(zone, Top + 3, orders);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(new Vector3Int(6, Top + 3, 5))?.Assignment, Is.SameAs(zone.Assignment), "Поднятый верх — снова копают столб");
        }

        [Test]
        public void Bounds_StayOrdered_NeighboursMove()
        {
            var zone = zones.AddLevel(new Vector3Int(5, Top, 5), new Vector3Int(6, Top, 5), world: world);
            zones.SetTop(zone, 10, orders);
            zones.SetBottom(zone, 0, orders);
            zones.SetLevel(zone, 5, orders);

            zones.SetTop(zone, 3, orders);
            Assert.That((zone.Bottom, zone.Level, zone.Top), Is.EqualTo((0, 3, 3)), "Верх ниже уровня тянет уровень вниз");

            zones.SetBottom(zone, 7, orders);
            Assert.That((zone.Bottom, zone.Level, zone.Top), Is.EqualTo((7, 7, 7)), "Низ выше уровня тянет уровень и верх вверх");

            zones.SetLevel(zone, 2, orders);
            Assert.That((zone.Bottom, zone.Level, zone.Top), Is.EqualTo((2, 2, 7)));
            zones.SetBottom(zone, -5, orders);
            Assert.That(zone.Bottom, Is.EqualTo(0));
        }

        [Test]
        public void Label_ShowsLevelPriorityAndPeople()
        {
            var zone = zones.AddLevel(new Vector3Int(10, Top, 10), new Vector3Int(11, Top, 10), 3, world: world);
            Assert.That(WorkZoneView.Label(zone), Is.EqualTo($"выровнять до {Top - 1} · люди 0/3"));
        }

        [Test]
        public void At_FiltersByKind()
        {
            var chop = zones.AddChop(new Vector3Int(10, Top + 1, 10), 4);
            var dig = zones.AddLevel(new Vector3Int(10, Top, 10), new Vector3Int(11, Top, 11), world: world);

            Assert.That(zones.At(new Vector3Int(10, Top, 10), WorkKind.Dig), Is.SameAs(dig));
            Assert.That(zones.At(new Vector3Int(10, Top, 10), WorkKind.Chop), Is.SameAs(chop));
            Assert.That(zones.At(new Vector3Int(13, Top, 10), WorkKind.Dig), Is.Null);
        }

        private Worker Spawn(Vector2Int column, Func<Vector2Int, bool> isLit = null)
        {
            var context = new WorkerContext(world, orders, items, new BlockDamage(new UniformBlockDurability(100f)),
                new BlockStability(), settings, isLit, null, null, forest);
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
