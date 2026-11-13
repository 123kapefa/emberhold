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
    /// <summary>Класс даёт задание (#161): какой класс у рабочего, сколько он держит задание, где ждёт без дела.</summary>
    public sealed class WorkerRoleWorkTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private BlockDamage damage;
        private WorkerSettings settings;
        private Pallets pallets;
        private WorkerContext context;
        private Forest forest;
        private TreeSpecies birch;

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
            pallets = new Pallets(_ => 20);
            forest = new Forest(items);
            birch = TreeSpecies.Create("Берёза", 75f, 3);
            context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, null, forest: forest, pallets: pallets);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(birch);
        }

        [Test]
        public void Roles_ComeFromWorkKind()
        {
            Assert.That(WorkerRoles.For(WorkKind.Dig), Is.EqualTo(WorkerRole.Digger));
            Assert.That(WorkerRoles.For(WorkKind.Chop), Is.EqualTo(WorkerRole.Woodcutter));
            Assert.That(WorkerRoles.For(WorkKind.Build), Is.EqualTo(WorkerRole.Builder));
            Assert.That(WorkerRoles.For(WorkKind.Campfire), Is.EqualTo(WorkerRole.Builder));
            Assert.That(WorkerRoles.For(WorkKind.Fuel), Is.EqualTo(WorkerRole.Stoker));
        }

        [Test]
        public void Worker_GetsClassOfTaskAndIsFreeAfterIt()
        {
            var (dig, _) = DigAndBuildOrders();
            orders.Cancel(orders.Orders[1]);
            var worker = Spawn(new Vector2Int(9, 9));
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Без задания — свободный");

            Run(5f, () => worker.Order != null, worker);
            Assert.That(worker.Order, Is.SameAs(dig));
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Digger), "Взял копку — копатель");
            Assert.That(worker.Assignment, Is.SameAs(dig.Assignment));

            Run(20f, () => !orders.Contains(dig), worker);
            Assert.That(orders.Contains(dig), Is.False);
            // Выпал камень, но поддона нет: нести некуда, кучка ничейная, рабочий снова свободный.
            Run(1f, () => false, worker);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Задание кончилось — снова свободный");
            Assert.That(context.MinedBy, Is.Empty, "Кучка без поддона стала ничейной");
            Assert.That(worker.Assignment, Is.Null);
            Assert.That(dig.Assignment.Workers, Is.Empty);
        }

        [Test]
        public void Worker_IsFreeAgainWhenPlayerCancelsTask()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 1));
            var build = orders.AddBuild(new Vector3Int(14, Ground, 12), Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));

            Run(20f, () => worker.Carrying != null, worker);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Builder), "Несёт камень к чертежу — строитель");

            orders.Cancel(build);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Чертёж сняли — сразу свободный");
            Run(0.2f, () => false, worker);
            Assert.That(worker.Order, Is.Null);
            Assert.That(worker.Carrying, Is.Null, "Камень упал у ног");
        }

        [Test]
        public void Each_TaskKindGivesItsClass()
        {
            var (dig, build) = DigAndBuildOrders();
            items.Drop(world, new Vector3Int(20, Ground, 20), new ItemDrop("wood", 1));
            var fire = new Fire(4f, 0f);
            var fuel = orders.AddFuel(new Vector3Int(24, Ground, 24), fire, "wood", 1);
            var workers = new[] { Spawn(new Vector2Int(9, 9)), Spawn(new Vector2Int(9, 10)), Spawn(new Vector2Int(21, 21)) };

            var seen = new System.Collections.Generic.Dictionary<WorkOrder, WorkerRole>();
            Run(40f, () =>
            {
                foreach (var worker in workers)
                {
                    if (worker.Order != null)
                        seen[worker.Order] = worker.Role;
                }
                return orders.Orders.Count == 0;
            }, workers);

            Assert.That(orders.Orders, Is.Empty, "Все задачи сделаны");
            Assert.That(seen[dig], Is.EqualTo(WorkerRole.Digger));
            Assert.That(seen[build], Is.EqualTo(WorkerRole.Builder));
            Assert.That(seen[fuel], Is.EqualTo(WorkerRole.Stoker));
            Assert.That(fire.Amount, Is.EqualTo(1f));
            Run(1f, () => false, workers);
            foreach (var worker in workers)
                Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Без заданий все свободные");
        }

        [Test]
        public void Worker_HoldsAssignmentUntilItsOrdersAreDone()
        {
            // Задание из двух приказов копать (как будущая зона, #167) и чертёж, который ближе второго приказа.
            var first = new Vector3Int(11, Ground - 1, 9);
            var second = new Vector3Int(22, Ground - 1, 22);
            world.SetBlock(first, Block.Full(BlockMaterials.Stone));
            world.SetBlock(second, Block.Full(BlockMaterials.Stone));
            var zone = new WorkAssignment(WorkKind.Dig);
            var digA = orders.AddDig(first);
            var digB = orders.AddDig(second);
            Assert.That(orders.Assign(digA, zone), Is.True);
            Assert.That(orders.Assign(digB, zone), Is.True);
            var build = orders.AddBuild(new Vector3Int(13, Ground, 9), Block.Full(BlockMaterials.Dirt), false, null);
            Assert.That(orders.Assign(build, zone), Is.False, "Чертёж в задание копать не входит");
            var worker = Spawn(new Vector2Int(9, 9));

            var tookBuildEarly = false;
            Run(60f, () =>
            {
                tookBuildEarly |= worker.Order == build && orders.Contains(digB);
                return !orders.Contains(digA) && !orders.Contains(digB);
            }, worker);

            Assert.That(orders.Contains(digB), Is.False, "Задание выкопано целиком");
            Assert.That(tookBuildEarly, Is.False, "Пока в задании есть приказы, чужие не берёт");
            Assert.That(zone.IsOver, Is.True);

            Run(20f, () => !orders.Contains(build), worker);
            Assert.That(orders.Contains(build), Is.False, "Потом, свободным, построил чертёж");
        }

        [Test]
        public void Assignment_TakesNoMorePeopleThanItAllows()
        {
            var zone = new WorkAssignment(WorkKind.Dig);
            for (var x = 12; x <= 13; x++)
            {
                var cell = new Vector3Int(x, Ground - 1, 15);
                world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
                orders.Assign(orders.AddDig(cell), zone);
            }
            var a = Spawn(new Vector2Int(9, 9));
            var b = Spawn(new Vector2Int(9, 10));

            var together = false;
            Run(30f, () =>
            {
                together |= a.Order != null && b.Order != null;
                return zone.IsOver;
            }, a, b);

            Assert.That(zone.IsOver, Is.True);
            Assert.That(together, Is.False, "Одиночное задание держит один человек");
        }

        [Test]
        public void Worker_LeavesAssignmentWithNothingToDoNow()
        {
            // Задание из двух приказов, второй — в темноте: доделав первый, рабочий его отпускает и берёт чужую работу.
            var lit = new Vector3Int(11, Ground - 1, 9);
            var dark = new Vector3Int(28, Ground - 1, 28);
            world.SetBlock(lit, Block.Full(BlockMaterials.Stone));
            world.SetBlock(dark, Block.Full(BlockMaterials.Stone));
            var zone = new WorkAssignment(WorkKind.Dig);
            orders.Assign(orders.AddDig(lit), zone);
            var darkDig = orders.AddDig(dark);
            orders.Assign(darkDig, zone);
            var build = orders.AddBuild(new Vector3Int(13, Ground, 9), Block.Full(BlockMaterials.Dirt), false, null);
            context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, c => c.x < 24, pallets: pallets);
            var worker = Spawn(new Vector2Int(9, 9));

            Run(40f, () => !orders.Contains(build), worker);
            Assert.That(orders.Contains(build), Is.False, "Чертёж построен");
            Assert.That(orders.Contains(darkDig), Is.True, "Приказ в темноте ждёт света");
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free));
        }

        [Test]
        public void Digger_CarriesOwnStoneToPalletAsDigger()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            orders.AddDig(cell);
            var pallet = pallets.Add(world, new Vector3Int(18, Ground, 12));
            var worker = Spawn(new Vector2Int(9, 9));

            var carried = false;
            var classWhileCarrying = WorkerRole.Free;
            Run(60f, () =>
            {
                if (worker.HaulTarget != null && worker.Carrying != null)
                {
                    carried = true;
                    classWhileCarrying = worker.Role;
                }
                return pallet.Count == 1;
            }, worker);

            Assert.That(pallet.Count, Is.EqualTo(1), "Камень на поддоне");
            Assert.That(carried, Is.True);
            Assert.That(classWhileCarrying, Is.EqualTo(WorkerRole.Digger), "Несёт свою добычу копателем");
            Run(1f, () => false, worker);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free), "Отнёс — снова свободный");
            Assert.That(items.Items, Is.Empty);
        }

        [Test]
        public void MinedPile_IsHauledOnlyByItsMiner()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            var dig = orders.AddDig(cell);
            var miner = Spawn(new Vector2Int(9, 9));
            Run(20f, () => !orders.Contains(dig), miner);
            Assert.That(items.Items, Has.Count.EqualTo(1));
            var pile = items.Items[0];
            Assert.That(context.MinedBy[pile], Is.SameAs(miner), "Кучка записана на того, кто копал");
            Assert.That(miner.Role, Is.EqualTo(WorkerRole.Digger), "Пока добыча не отнесена, держит класс");

            // Поддон появился, а рядом с кучкой свободный без дела: он её не берёт, относит добытчик.
            var pallet = pallets.Add(world, new Vector3Int(18, Ground, 12));
            var other = Spawn(new Vector2Int(13, 13));
            var otherHauled = false;
            Run(40f, () =>
            {
                otherHauled |= other.HaulTarget != null;
                return pallet.Count == 1;
            }, other, miner);

            Assert.That(pallet.Count, Is.EqualTo(1));
            Assert.That(otherHauled, Is.False, "Чужую добычу не трогает");
        }

        [Test]
        public void Woodcutter_CarriesOwnLogs_AsWoodcutter()
        {
            var tree = forest.Plant(new Vector3Int(14, Ground, 12), birch);
            orders.AddChop(tree);
            var pallet = pallets.Add(world, new Vector3Int(18, Ground, 8));
            var worker = Spawn(new Vector2Int(9, 9));

            var roles = new System.Collections.Generic.HashSet<WorkerRole>();
            Run(90f, () =>
            {
                if (worker.Carrying != null)
                    roles.Add(worker.Role);
                return pallet.Count == 3;
            }, worker);

            Assert.That(pallet.Count, Is.EqualTo(3), "Все три полена на поддоне");
            Assert.That(pallet.ItemId, Is.EqualTo("wood"));
            Assert.That(roles, Is.EquivalentTo(new[] { WorkerRole.Woodcutter }), "Носит поленья лесорубом");
            Run(1f, () => false, worker);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free));
        }

        [Test]
        public void Zone_DigsAndHaulsInTurn()
        {
            // Задание из двух приказов: камень первого относит до того, как начнёт второй.
            var first = new Vector3Int(11, Ground - 1, 9);
            var second = new Vector3Int(11, Ground - 1, 14);
            world.SetBlock(first, Block.Full(BlockMaterials.Stone));
            world.SetBlock(second, Block.Full(BlockMaterials.Stone));
            var zone = new WorkAssignment(WorkKind.Dig);
            orders.Assign(orders.AddDig(first), zone);
            orders.Assign(orders.AddDig(second), zone);
            var pallet = pallets.Add(world, new Vector3Int(18, Ground, 12));
            var worker = Spawn(new Vector2Int(9, 11));

            var storedBeforeSecondOrder = -1;
            var taken = 0;
            WorkOrder last = null;
            var classes = new System.Collections.Generic.HashSet<WorkerRole>();
            Run(90f, () =>
            {
                classes.Add(worker.Role);
                if (worker.Order != null && worker.Order != last)
                {
                    last = worker.Order;
                    if (++taken == 2)
                        storedBeforeSecondOrder = pallet.Count;
                }
                return pallet.Count == 2;
            }, worker);

            Assert.That(pallet.Count, Is.EqualTo(2));
            Assert.That(storedBeforeSecondOrder, Is.EqualTo(1), "Первый камень отнесён до второго приказа");
            Assert.That(classes, Is.EquivalentTo(new[] { WorkerRole.Free, WorkerRole.Digger }), "Всё задание — копатель");
        }

        [Test]
        public void StartingCrew_IsFiveFreeWorkers()
        {
            Assert.That(settings.Count, Is.EqualTo(5));
        }

        [Test]
        public void Idle_GoesToNearestFireAndWaits()
        {
            var near = new Vector3(20.5f, Ground, 20.5f);
            context.Fires.Add(new Vector3(2.5f, Ground, 28.5f));
            context.Fires.Add(near);
            var worker = Spawn(new Vector2Int(9, 9));

            var walkedToFire = false;
            Run(30f, () =>
            {
                walkedToFire |= worker.State == WorkerState.ToFire;
                return walkedToFire && worker.State == WorkerState.Idle;
            }, worker);

            Assert.That(walkedToFire, Is.True, "Пошёл к огню");
            Assert.That(WorkerStatus.Text(WorkerState.ToFire, null, false, null, null, false), Is.EqualTo("идёт к огню"));
            var distance = Flat(worker.Position, near);
            Assert.That(distance, Is.LessThanOrEqualTo(settings.FireWaitDistance + 0.01f), "Ждёт у ближайшего огня");
            Assert.That(distance, Is.GreaterThanOrEqualTo(0.99f), "Не в самом огне");

            var standing = worker.Position;
            Run(5f, () => false, worker);
            Assert.That(worker.Position, Is.EqualTo(standing), "У огня стоит на месте");
        }

        [Test]
        public void WalkingToFire_TakesOrderThatAppears()
        {
            context.Fires.Add(new Vector3(28.5f, Ground, 28.5f));
            var worker = Spawn(new Vector2Int(3, 3));
            Run(5f, () => worker.State == WorkerState.ToFire, worker);
            Assert.That(worker.State, Is.EqualTo(WorkerState.ToFire));

            var build = orders.AddBuild(new Vector3Int(6, Ground, 6), Block.Full(BlockMaterials.Dirt), false, null);
            Run(20f, () => !orders.Contains(build), worker);
            Assert.That(world.GetBlock(build.Cell).Material, Is.EqualTo(BlockMaterials.Dirt), "Развернулся и построил");
        }

        /// <summary>Приказ копать камень в (12, 12) и бесплатный чертёж земли в (14, 8).</summary>
        private (WorkOrder dig, WorkOrder build) DigAndBuildOrders()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            return (orders.AddDig(cell), orders.AddBuild(new Vector3Int(14, Ground, 8), Block.Full(BlockMaterials.Dirt), false, null));
        }

        private Worker Spawn(Vector2Int column) => new(context, context.Surface.StandPoint(column));

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

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
            private readonly float capacity;

            public Fire(float capacity, float amount)
            {
                this.capacity = capacity;
                Amount = amount;
            }

            public float Amount { get; private set; }

            public float FuelSpace => capacity - Amount;

            public float FuelFill => Amount / capacity;

            public float AddFuel(float amount)
            {
                var accepted = Mathf.Clamp(amount, 0f, FuelSpace);
                Amount += accepted;
                return accepted;
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
