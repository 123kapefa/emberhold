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
    /// <summary>
    /// Выравнивание (#169): зона с уровнем наращивает землёй до уровня (голубое), снизу вверх; в режиме «выровнять» —
    /// только когда копать уже нечего. Людей на зону один счётчик, класс — по взятому приказу.
    /// </summary>
    public sealed class LevelZoneFillTests
    {
        private const float Step = 0.1f;
        // Земля y = 0..3: верхний блок на высоте 3.
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
        public void Assignment_HoldsDigAndBuild_NotChop()
        {
            var assignment = new WorkAssignment(2, WorkKind.Dig, WorkKind.Build);

            Assert.That(assignment.Kind, Is.EqualTo(WorkKind.Dig));
            Assert.That(assignment.Accepts(WorkKind.Dig), Is.True);
            Assert.That(assignment.Accepts(WorkKind.Build), Is.True);
            Assert.That(assignment.Accepts(WorkKind.Chop), Is.False);
            Assert.That(orders.Assign(orders.AddBuild(new Vector3Int(1, Top + 1, 1), LevelZone.FillBlock, false, null), assignment), Is.True);
            Assert.That(orders.Assign(orders.AddDig(new Vector3Int(2, Top, 1)), assignment), Is.True);
            Assert.That(assignment.Orders, Has.Count.EqualTo(2));
        }

        [Test]
        public void Fill_OrdersLowestSupportedCell_LayerByLayer_ThenLeaves()
        {
            // Яма глубиной в два блока в столбце (6, 5); уровень — верх земли.
            Hole(6, 5, 2);
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(7, Top + 1, 5), world: world, mode: LevelZoneMode.Fill);
            Assert.That((zone.Bottom, zone.Level, zone.Top), Is.EqualTo((Top - 2, Top, Top)));

            zones.Update(world, forest, orders);
            var order = zone.Assignment.Orders.Single();
            Assert.That(order.Kind, Is.EqualTo(WorkKind.Build));
            Assert.That(order.Cell, Is.EqualTo(new Vector3Int(6, Top - 1, 5)), "Сначала нижняя клетка ямы");
            Assert.That(order.Block, Is.EqualTo(Block.Full(BlockMaterials.Dirt)), "Наращивают землёй");
            Assert.That(order.ItemId, Is.Null, "Без таблицы цен — бесплатно");
            Assert.That(zone.IsFilling, Is.True);

            // Нижний слой положили: следующий встаёт на него.
            world.SetBlock(order.Cell, LevelZone.FillBlock);
            orders.Prune(world);
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Single().Cell, Is.EqualTo(new Vector3Int(6, Top, 5)));

            world.SetBlock(new Vector3Int(6, Top, 5), LevelZone.FillBlock);
            orders.Prune(world);
            zones.Update(world, forest, orders);
            Assert.That(zones.All, Is.Empty, "Наращивать нечего — зона ушла");
        }

        [Test]
        public void Fill_ColumnBelowBottom_IsNotFilled()
        {
            Hole(6, 5, 2);
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(7, Top + 1, 5), draft: true, world: world,
                mode: LevelZoneMode.Fill);
            zones.SetBottom(zone, Top - 1, orders);
            zones.Start(zone);

            zones.Update(world, forest, orders);

            Assert.That(orders.Orders, Is.Empty, "Земля ниже низа — класть не на что");
            Assert.That(zones.All, Is.Empty);
        }

        [Test]
        public void Fill_UsesBuildCost_SkipsBlockedCellsTreesAndStumps()
        {
            Hole(5, 5, 1);
            Hole(6, 5, 1);
            Hole(7, 5, 1);
            Hole(8, 5, 1);
            forest.Plant(new Vector3Int(7, Top, 5), birch);
            var stumpTree = forest.Plant(new Vector3Int(8, Top, 5), birch);
            Assert.That(forest.Hit(world, stumpTree, 1000f), Is.True);
            zones.CostOf = material => material == BlockMaterials.Dirt ? new ItemDrop("dirt", 2) : null;
            zones.IsBlocked = cell => cell == new Vector3Int(6, Top, 5);
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(8, Top + 1, 5), world: world, mode: LevelZoneMode.Fill);

            zones.Update(world, forest, orders);

            var order = zone.Assignment.Orders.Single();
            Assert.That(order.Cell, Is.EqualTo(new Vector3Int(5, Top, 5)), "Костёр, поддон, дерево и пень не засыпают");
            Assert.That(order.ItemId, Is.EqualTo("dirt"));
            Assert.That(order.Needed, Is.EqualTo(2), "Цена — из таблицы цен чертежей");
        }

        [Test]
        public void Level_DigsFirst_FillsWhenNothingToDig()
        {
            // Бугор в (5, 5), яма в (7, 5); уровень — верх земли.
            world.SetBlock(new Vector3Int(5, Top + 1, 5), Block.Full(BlockMaterials.Dirt));
            Hole(7, 5, 1);
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(7, Top + 1, 5), world: world);
            Assert.That((zone.Bottom, zone.Level, zone.Top), Is.EqualTo((Top - 1, Top, Top + 1)));
            Assert.That(zone.Mode, Is.EqualTo(LevelZoneMode.Level));

            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => (o.Kind, o.Cell)),
                Is.EquivalentTo(new[] { (WorkKind.Dig, new Vector3Int(5, Top + 1, 5)) }), "Пока есть что копать, не наращивают");
            Assert.That(zone.IsFilling, Is.False);

            world.SetBlock(new Vector3Int(5, Top + 1, 5), Block.Air);
            orders.Prune(world);
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => (o.Kind, o.Cell)),
                Is.EquivalentTo(new[] { (WorkKind.Build, new Vector3Int(7, Top, 5)) }));
            Assert.That(zone.IsFilling, Is.True);

            // Снова появилось что копать: невзятые чертежи снимаются, копают первыми.
            world.SetBlock(new Vector3Int(6, Top + 1, 5), Block.Full(BlockMaterials.Dirt));
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => o.Kind), Is.EquivalentTo(new[] { WorkKind.Dig }));
        }

        [Test]
        public void SetMode_DropsOrdersOfOtherWork()
        {
            world.SetBlock(new Vector3Int(5, Top + 1, 5), Block.Full(BlockMaterials.Dirt));
            Hole(6, 5, 1);
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(6, Top + 1, 5), world: world, mode: LevelZoneMode.Dig);
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => o.Kind), Is.EquivalentTo(new[] { WorkKind.Dig }), "«Выкопать» ямы не засыпает");

            zones.SetMode(zone, LevelZoneMode.Fill, orders);
            Assert.That(orders.Orders, Is.Empty, "Без копки приказы копать сняты");
            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => (o.Kind, o.Cell)),
                Is.EquivalentTo(new[] { (WorkKind.Build, new Vector3Int(6, Top, 5)) }), "«Нарастить» бугор не копает");

            zones.SetMode(zone, LevelZoneMode.Dig, orders);
            Assert.That(orders.Orders, Is.Empty, "Без наращивания чертежи сняты");
        }

        [Test]
        public void Workers_FillHoleBottomUp_AsBuilders()
        {
            Hole(10, 10, 2);
            var zone = zones.AddLevel(new Vector3Int(10, Top + 1, 10), new Vector3Int(10, Top + 1, 10), world: world);
            var worker = Spawn(new Vector2Int(8, 8));
            var sawBuilder = false;

            Run(120f, () =>
            {
                sawBuilder |= worker.Role == WorkerRole.Builder;
                return zones.All.Count == 0;
            }, worker);

            Assert.That(zones.All, Is.Empty, "Яма засыпана — зона ушла");
            Assert.That(sawBuilder, Is.True, "Наращивает строитель");
            Assert.That(world.GetBlock(new Vector3Int(10, Top - 1, 10)), Is.EqualTo(LevelZone.FillBlock));
            Assert.That(world.GetBlock(new Vector3Int(10, Top, 10)), Is.EqualTo(LevelZone.FillBlock));
            Assert.That(world.GetBlock(new Vector3Int(10, Top + 1, 10)).IsAir, Is.True, "Выше уровня не кладут");
            Assert.That(zone.Assignment.Orders, Is.Empty);
        }

        [Test]
        public void Workers_Level_OnePeopleCounter_DiggerThenBuilder()
        {
            // Бугор в (10, 10), яма в (12, 10); на зону один человек.
            var bump = new Vector3Int(10, Top + 1, 10);
            world.SetBlock(bump, Block.Full(BlockMaterials.Dirt));
            Hole(12, 10, 1);
            var zone = zones.AddLevel(bump, new Vector3Int(12, Top + 1, 10), 1, world: world);
            var a = Spawn(new Vector2Int(8, 8));
            var b = Spawn(new Vector2Int(14, 14));
            var sawDigger = false;
            var sawBuilder = false;
            var builderBeforeDug = false;
            var mostPeople = 0;

            Run(120f, () =>
            {
                foreach (var worker in new[] { a, b })
                {
                    sawDigger |= worker.Role == WorkerRole.Digger;
                    sawBuilder |= worker.Role == WorkerRole.Builder;
                    builderBeforeDug |= worker.Role == WorkerRole.Builder && !world.GetBlock(bump).IsAir;
                }
                mostPeople = Mathf.Max(mostPeople, zone.Assignment.Workers.Count);
                return zones.All.Count == 0;
            }, a, b);

            Assert.That(zones.All, Is.Empty, "Выровнено — зона ушла");
            Assert.That(world.GetBlock(bump).IsAir, Is.True);
            Assert.That(world.GetBlock(new Vector3Int(12, Top, 10)), Is.EqualTo(LevelZone.FillBlock));
            Assert.That(sawDigger && sawBuilder, Is.True, "В одной зоне и копатель, и строитель");
            Assert.That(builderBeforeDug, Is.False, "Наращивают, только когда выкопано");
            Assert.That(mostPeople, Is.EqualTo(1), "Один общий счётчик людей");
        }

        [Test]
        public void Fill_TopsUpPartialBlockWithItsMaterial_ThenBuildsAbove()
        {
            // Гладкая земля (#78): в (6, 5) верхний блок — половинка травы, в (7, 5) — пустая клетка над половинкой земли.
            world.SetBlock(new Vector3Int(6, Top, 5), Block.Flat(BlockMaterials.Grass, 8));
            Hole(7, 5, 1);
            world.SetBlock(new Vector3Int(7, Top - 1, 5), Block.Flat(BlockMaterials.Dirt, 4));
            zones.CostOf = material => material == BlockMaterials.Grass ? new ItemDrop("grass", 1) : null;
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(7, Top + 1, 5), world: world, mode: LevelZoneMode.Fill);
            Assert.That(zone.Bottom, Is.EqualTo(Top - 2), "Низ — под неполным блоком самого низкого столбца");

            zones.Update(world, forest, orders);

            var grass = orders.At(new Vector3Int(6, Top, 5));
            Assert.That(grass?.Assignment, Is.SameAs(zone.Assignment));
            Assert.That(grass.FillsExisting, Is.True, "Неполный блок дополняют до полного");
            Assert.That(grass.Block, Is.EqualTo(Block.Full(BlockMaterials.Grass)), "Тем же материалом");
            Assert.That(grass.ItemId, Is.EqualTo("grass"), "Цена — по материалу блока");
            var dirt = orders.At(new Vector3Int(7, Top - 1, 5));
            Assert.That(dirt?.FillsExisting, Is.True);
            Assert.That(dirt.Block, Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
            Assert.That(orders.At(new Vector3Int(7, Top, 5)), Is.Null, "Выше — после того, как нижний станет полным");

            // Дополнили: выше встаёт обычный чертёж земли; зона уходит, только когда всё ровно.
            world.SetBlock(new Vector3Int(7, Top - 1, 5), Block.Full(BlockMaterials.Dirt));
            orders.Prune(world);
            zones.Update(world, forest, orders);
            Assert.That(orders.At(new Vector3Int(7, Top, 5)) is { FillsExisting: false } above && above.Block == LevelZone.FillBlock, Is.True);
            Assert.That(zones.All, Has.Member(zone));
        }

        [Test]
        public void Level_AfterDigging_TopsUpPartialBlocks()
        {
            world.SetBlock(new Vector3Int(5, Top + 1, 5), Block.Full(BlockMaterials.Dirt));
            world.SetBlock(new Vector3Int(7, Top, 5), Block.Flat(BlockMaterials.Dirt, 4));
            var zone = zones.AddLevel(new Vector3Int(5, Top + 1, 5), new Vector3Int(7, Top + 1, 5), world: world);

            zones.Update(world, forest, orders);
            Assert.That(zone.Assignment.Orders.Select(o => o.Kind), Is.EquivalentTo(new[] { WorkKind.Dig }), "Сначала копают");

            world.SetBlock(new Vector3Int(5, Top + 1, 5), Block.Air);
            orders.Prune(world);
            zones.Update(world, forest, orders);
            var order = zone.Assignment.Orders.Single();
            Assert.That((order.Kind, order.Cell, order.FillsExisting), Is.EqualTo((WorkKind.Build, new Vector3Int(7, Top, 5), true)));
        }

        [Test]
        public void FillFrom_MatchesWhatIsFilled()
        {
            world.SetBlock(new Vector3Int(6, Top, 5), Block.Flat(BlockMaterials.Dirt, 8));
            Hole(7, 5, 1);
            float Surface(Vector2Int c) => c.x == 6 ? Top + 0.5f : Top + 1f;

            Assert.That(LevelZone.FillFrom(world, forest, null, Surface, 6, 5, Top, Top - 1), Is.EqualTo(Top + 0.5f),
                "Над неполным блоком — от его верха");
            Assert.That(LevelZone.FillFrom(world, forest, null, Surface, 7, 5, Top, Top - 1), Is.EqualTo((float)Top), "Пустая клетка — от её пола");
            Assert.That(LevelZone.FillFrom(world, forest, null, Surface, 5, 5, Top, Top - 1), Is.Null, "Ровно — засыпать нечего");
            Assert.That(LevelZone.FillFrom(world, forest, c => c.x == 7, Surface, 7, 5, Top, Top - 1), Is.Null, "Занятую клетку не засыпают");
        }

        [Test]
        public void Workers_TopUpPartialBlocks_UntilFlat()
        {
            world.SetBlock(new Vector3Int(10, Top, 10), Block.Flat(BlockMaterials.Dirt, 8));
            Hole(11, 10, 1);
            world.SetBlock(new Vector3Int(11, Top - 1, 10), Block.Flat(BlockMaterials.Dirt, 4));
            var zone = zones.AddLevel(new Vector3Int(10, Top + 1, 10), new Vector3Int(11, Top + 1, 10), world: world, mode: LevelZoneMode.Fill);
            var worker = Spawn(new Vector2Int(8, 8));

            Run(120f, () => zones.All.Count == 0, worker);

            Assert.That(zones.All, Is.Empty, "Всё ровно — зона ушла");
            Assert.That(world.GetBlock(new Vector3Int(10, Top, 10)), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
            Assert.That(world.GetBlock(new Vector3Int(11, Top - 1, 10)), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
            Assert.That(world.GetBlock(new Vector3Int(11, Top, 10)), Is.EqualTo(LevelZone.FillBlock));
            Assert.That(zone.Assignment.Orders, Is.Empty);
        }

        /// <summary>Вынуть depth верхних блоков столбца (x, z).</summary>
        private void Hole(int x, int z, int depth)
        {
            for (var i = 0; i < depth; i++)
                world.SetBlock(new Vector3Int(x, Top - i, z), Block.Air);
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
