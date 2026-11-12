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
    /// <summary>Роли задают работу (#161): кто какие задачи берёт, кто что носит на поддоны, где ждёт без дела.</summary>
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
            context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, null, pallets: pallets);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void Roles_TakeTheirWorkKinds()
        {
            foreach (WorkKind kind in Enum.GetValues(typeof(WorkKind)))
            {
                Assert.That(WorkerRoles.Takes(WorkerRole.Free, kind), Is.True, $"Свободный берёт {kind}");
                Assert.That(WorkerRoles.Takes(WorkerRole.Digger, kind), Is.EqualTo(kind == WorkKind.Dig), $"Копатель и {kind}");
                Assert.That(WorkerRoles.Takes(WorkerRole.Woodcutter, kind), Is.EqualTo(kind == WorkKind.Chop), $"Лесоруб и {kind}");
                Assert.That(WorkerRoles.Takes(WorkerRole.Builder, kind), Is.EqualTo(kind is WorkKind.Build or WorkKind.Campfire),
                    $"Строитель и {kind}");
                Assert.That(WorkerRoles.Takes(WorkerRole.Stoker, kind), Is.EqualTo(kind == WorkKind.Fuel), $"Истопник и {kind}");
                foreach (var warrior in new[] { WorkerRole.Swordsman, WorkerRole.Archer, WorkerRole.Spearman })
                    Assert.That(WorkerRoles.Takes(warrior, kind), Is.False, $"{warrior} работ не берёт");
            }

            Assert.That(WorkerRoles.Hauls(WorkerRole.Free, "stone", "wood"), Is.True);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Free, "wood", "wood"), Is.True);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Woodcutter, "wood", "wood"), Is.True);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Woodcutter, "stone", "wood"), Is.False);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Digger, "stone", "wood"), Is.True);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Digger, "wood", "wood"), Is.False);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Builder, "stone", "wood"), Is.False);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Stoker, "wood", "wood"), Is.False);
            Assert.That(WorkerRoles.Hauls(WorkerRole.Swordsman, "stone", "wood"), Is.False);
        }

        [Test]
        public void Digger_DigsAndLeavesBlueprint_BuilderBuildsAndLeavesDig()
        {
            var (dig, build) = DigAndBuildOrders();
            var digger = Spawn(new Vector2Int(9, 9), WorkerRole.Digger);
            var builder = Spawn(new Vector2Int(9, 10), WorkerRole.Builder);

            var diggerTookBuild = false;
            var builderTookDig = false;
            Run(30f, () =>
            {
                diggerTookBuild |= digger.Order == build;
                builderTookDig |= builder.Order == dig;
                return orders.Orders.Count == 0;
            }, digger, builder);

            Assert.That(orders.Orders, Is.Empty, "Обе задачи сделаны");
            Assert.That(diggerTookBuild, Is.False, "Копатель не строит");
            Assert.That(builderTookDig, Is.False, "Строитель не копает");
        }

        [Test]
        public void Specialist_WithoutOwnWorkStaysIdle_FreeTakesAnything()
        {
            DigAndBuildOrders();
            var stoker = Spawn(new Vector2Int(9, 9), WorkerRole.Stoker);
            var swordsman = Spawn(new Vector2Int(20, 20), WorkerRole.Swordsman);
            var start = swordsman.Position;

            Run(5f, () => false, stoker, swordsman);
            Assert.That(stoker.Order, Is.Null, "У истопника нет задач топлива");
            Assert.That(swordsman.Order, Is.Null, "Воин работ не берёт");
            Assert.That(swordsman.State, Is.EqualTo(WorkerState.Idle));
            Assert.That(swordsman.Position, Is.EqualTo(start), "Без огней ждёт, где стоит");

            var free = Spawn(new Vector2Int(9, 10));
            Assert.That(free.Role, Is.EqualTo(WorkerRole.Free), "По умолчанию рабочий свободный");
            Run(30f, () => orders.Orders.Count == 0, stoker, swordsman, free);
            Assert.That(orders.Orders, Is.Empty, "Свободный сделал и копку, и стройку");
        }

        [Test]
        public void Stoker_CarriesFuel_BuilderDoesNot()
        {
            items.Drop(world, new Vector3Int(6, Ground, 6), new ItemDrop("wood", 2));
            var fire = new Fire(4f, 0f);
            var fuel = orders.AddFuel(new Vector3Int(14, Ground, 14), fire, "wood", 1);
            var builder = Spawn(new Vector2Int(9, 9), WorkerRole.Builder);

            Run(5f, () => false, builder);
            Assert.That(builder.Order, Is.Null, "Строитель огонь не кормит");
            Assert.That(fire.Amount, Is.Zero);

            var stoker = Spawn(new Vector2Int(9, 10), WorkerRole.Stoker);
            Run(30f, () => !orders.Contains(fuel), builder, stoker);
            Assert.That(fire.Amount, Is.EqualTo(1f), "Истопник принёс полено");
        }

        [Test]
        public void Haul_DiggerCarriesStone_WoodcutterCarriesLogs()
        {
            items.Drop(world, new Vector3Int(6, Ground, 6), new ItemDrop("stone", 2));
            items.Drop(world, new Vector3Int(6, Ground, 12), new ItemDrop("wood", 2));
            var first = pallets.Add(world, new Vector3Int(16, Ground, 6));
            var second = pallets.Add(world, new Vector3Int(16, Ground, 12));
            var digger = Spawn(new Vector2Int(9, 9), WorkerRole.Digger);
            var builder = Spawn(new Vector2Int(9, 10), WorkerRole.Builder);

            var diggerCarriedWood = false;
            var builderHauled = false;
            Run(60f, () =>
            {
                diggerCarriedWood |= digger.Carrying?.ItemId == "wood";
                builderHauled |= builder.HaulTarget != null;
                return Stored("stone") == 2;
            }, digger, builder);

            Assert.That(Stored("stone"), Is.EqualTo(2), "Копатель отнёс камень");
            Assert.That(Stored("wood"), Is.Zero, "Поленья копатель не носит");
            Assert.That(diggerCarriedWood, Is.False);
            Assert.That(builderHauled, Is.False, "Строитель добычу на поддоны не носит");

            var woodcutter = Spawn(new Vector2Int(9, 11), WorkerRole.Woodcutter);
            Run(60f, () => Stored("wood") == 2, digger, builder, woodcutter);
            Assert.That(Stored("wood"), Is.EqualTo(2), "Лесоруб отнёс поленья");
            Assert.That(items.Items, Is.Empty);

            int Stored(string itemId) =>
                (first.ItemId == itemId ? first.Count : 0) + (second.ItemId == itemId ? second.Count : 0);
        }

        [Test]
        public void RoleChange_DropsWorkTheNewRoleDoesNotTake()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            var dig = orders.AddDig(cell);
            var worker = Spawn(new Vector2Int(9, 9), WorkerRole.Digger);

            Run(20f, () => worker.State == WorkerState.Working, worker);
            Assert.That(worker.Order, Is.SameAs(dig));

            worker.Role = WorkerRole.Builder;
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Builder));
            Assert.That(worker.Order, Is.Null, "Строитель копку бросил");
            Assert.That(dig.Worker, Is.Null, "Задача снова свободна");
            Assert.That(orders.Contains(dig), Is.True, "Задача осталась");
            var taken = damage.Taken(cell);
            Run(5f, () => false, worker);
            Assert.That(damage.Taken(cell), Is.EqualTo(taken), "Больше не бьёт");

            worker.Role = WorkerRole.Digger;
            Run(20f, () => !orders.Contains(dig), worker);
            Assert.That(world.GetBlock(cell).IsAir, Is.True, "Снова копатель — докопал");
        }

        [Test]
        public void RoleChange_KeepsWorkTheNewRoleTakes()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 1));
            var build = orders.AddBuild(new Vector3Int(14, Ground, 12), Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));

            Run(20f, () => worker.Carrying != null, worker);
            Assert.That(worker.Order, Is.SameAs(build));

            worker.Role = WorkerRole.Builder;
            Assert.That(worker.Order, Is.SameAs(build), "Строитель продолжает стройку");
            Assert.That(worker.Carrying, Is.Not.Null);

            worker.Role = WorkerRole.Woodcutter;
            Assert.That(worker.Order, Is.Null, "Лесоруб стройку бросил");
            Assert.That(worker.Carrying, Is.Null, "Камень упал у ног");
            Assert.That(items.Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void Idle_GoesToNearestFireAndWaits()
        {
            var near = new Vector3(20.5f, Ground, 20.5f);
            context.Fires.Add(new Vector3(2.5f, Ground, 28.5f));
            context.Fires.Add(near);
            var worker = Spawn(new Vector2Int(9, 9), WorkerRole.Swordsman);

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
            var worker = Spawn(new Vector2Int(3, 3), WorkerRole.Builder);
            Run(5f, () => worker.State == WorkerState.ToFire, worker);
            Assert.That(worker.State, Is.EqualTo(WorkerState.ToFire));

            var build = orders.AddBuild(new Vector3Int(6, Ground, 6), Block.Full(BlockMaterials.Dirt), false, null);
            Run(20f, () => !orders.Contains(build), worker);
            Assert.That(world.GetBlock(build.Cell).Material, Is.EqualTo(BlockMaterials.Dirt), "Развернулся и построил");
        }

        [Test]
        public void StartRoles_FromSettingsThenFree()
        {
            Assert.That(settings.StartRole(0), Is.EqualTo(WorkerRole.Woodcutter));
            Assert.That(settings.StartRole(1), Is.EqualTo(WorkerRole.Digger));
            Assert.That(settings.StartRole(2), Is.EqualTo(WorkerRole.Stoker));
            Assert.That(settings.StartRole(3), Is.EqualTo(WorkerRole.Free));
            Assert.That(settings.StartRole(settings.Count - 1), Is.EqualTo(WorkerRole.Free), "В стартовом составе есть свободный");
            Assert.That(settings.StartRole(50), Is.EqualTo(WorkerRole.Free));
        }

        /// <summary>Приказ копать камень в (12, 12) и бесплатный чертёж земли в (14, 8).</summary>
        private (WorkOrder dig, WorkOrder build) DigAndBuildOrders()
        {
            var cell = new Vector3Int(12, Ground - 1, 12);
            world.SetBlock(cell, Block.Full(BlockMaterials.Stone));
            return (orders.AddDig(cell), orders.AddBuild(new Vector3Int(14, Ground, 8), Block.Full(BlockMaterials.Dirt), false, null));
        }

        private Worker Spawn(Vector2Int column, WorkerRole role = WorkerRole.Free) =>
            new(context, context.Surface.StandPoint(column), role);

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
