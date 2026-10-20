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
    public sealed class PalletTests
    {
        private const float Step = 0.1f;
        private const int Ground = 4;

        private VoxelWorld world;
        private WorkOrders orders;
        private DroppedItems items;
        private BlockDamage damage;
        private WorkerSettings settings;
        private Pallets pallets;
        private int capacity;
        private WorkerContext context;

        [SetUp]
        public void SetUp()
        {
            // Ровная земля 32×32: блоки y = 0..3, поддоны и рабочие стоят на высоте 4.
            world = new VoxelWorld(new Vector3Int(2, 1, 2));
            for (var z = 0; z < 32; z++)
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(BlockMaterials.Dirt));
            orders = new WorkOrders();
            items = new DroppedItems(new NoDrops());
            damage = new BlockDamage(new UniformBlockDurability(100f));
            settings = ScriptableObject.CreateInstance<WorkerSettings>();
            capacity = 20;
            pallets = new Pallets(_ => capacity);
            context = null;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void Place_NeedsFreeSolidGroundAndNoOverlap()
        {
            var origin = new Vector3Int(5, Ground, 5);
            Assert.That(pallets.Add(world, origin), Is.Not.Null);
            Assert.That(pallets.Add(world, origin + new Vector3Int(1, 0, 1)), Is.Null, "Задевает первый поддон");
            Assert.That(pallets.At(origin + new Vector3Int(1, 1, 1)), Is.Not.Null, "Поддон занимает 2×2 и 2 в высоту");
            Assert.That(pallets.At(origin + new Vector3Int(2, 0, 0)), Is.Null);

            // Над площадкой блок на втором слое, под площадкой яма.
            world.SetBlock(new Vector3Int(11, Ground + 1, 10), Block.Full(BlockMaterials.Stone));
            Assert.That(pallets.CanPlace(world, new Vector3Int(10, Ground, 10)), Is.False);
            world.SetBlock(new Vector3Int(16, Ground - 1, 17), Block.Air);
            Assert.That(pallets.CanPlace(world, new Vector3Int(15, Ground, 16)), Is.False);
            Assert.That(pallets.CanPlace(world, new Vector3Int(20, Ground, 20)), Is.True);
        }

        [Test]
        public void Store_KeepsOneTypeUpToCapacity_AndForgetsTypeWhenEmpty()
        {
            capacity = 2;
            var pallet = pallets.Add(world, new Vector3Int(5, Ground, 5));

            Assert.That(pallets.Reserve(pallet, "wood"), Is.True);
            Assert.That(pallet.ItemId, Is.EqualTo("wood"), "Тип задаёт первая бронь");
            Assert.That(pallets.Space(pallet, "stone"), Is.Zero, "Другой ресурс сюда уже не несут");
            Assert.That(pallets.Store(pallet, new ItemDrop("stone", 1)), Is.False);
            Assert.That(pallets.Store(pallet, new ItemDrop("wood", 1)), Is.True);
            Assert.That(pallets.Store(pallet, new ItemDrop("wood", 1)), Is.True);
            Assert.That(pallets.Store(pallet, new ItemDrop("wood", 1)), Is.False, "Больше предела не кладётся");
            Assert.That(pallet.Count, Is.EqualTo(2));
            Assert.That(pallet.Reserved, Is.Zero);

            Assert.That(pallets.Take(pallet, 1, out var taken), Is.True);
            Assert.That(taken, Is.EqualTo(new ItemDrop("wood", 1)));
            Assert.That(pallets.Take(pallet, 5, out taken), Is.True);
            Assert.That(taken.Count, Is.EqualTo(1));
            Assert.That(pallet.ItemId, Is.Null, "Опустевший поддон принимает любой ресурс");
            Assert.That(pallets.Space(pallet, "stone"), Is.EqualTo(2));

            Assert.That(pallets.Reserve(pallet, "stone"), Is.True);
            pallets.Release(pallet);
            Assert.That(pallet.ItemId, Is.Null, "Отпущенная бронь тип не держит");
        }

        [Test]
        public void Label_ShowsItemCountAndCapacity()
        {
            capacity = 20;
            var pallet = pallets.Add(world, new Vector3Int(5, Ground, 5));
            Assert.That(PalletLabel.Text(pallet, 20), Is.EqualTo(PalletLabel.Empty));
            pallets.Store(pallet, new ItemDrop("wood", 2));
            Assert.That(PalletLabel.Text(pallet, pallets.Capacity("wood")), Is.EqualTo("бревно 2-20"));
        }

        [Test]
        public void Worker_HaulsLoosePileToPalletOneUnitPerTrip()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("wood", 3));
            var pallet = pallets.Add(world, new Vector3Int(14, Ground, 12));
            var worker = Spawn(new Vector2Int(9, 9));

            var most = 0;
            Run(60f, () =>
            {
                most = Mathf.Max(most, worker.Carrying?.Count ?? 0);
                return pallet.Count == 3;
            }, worker);

            Assert.That(pallet.Count, Is.EqualTo(3));
            Assert.That(pallet.ItemId, Is.EqualTo("wood"));
            Assert.That(items.Items, Is.Empty);
            Assert.That(most, Is.EqualTo(1), "За ходку одна единица");
            Assert.That(pallet.Reserved, Is.Zero);
        }

        [Test]
        public void Worker_StopsAtCapacityAndPrefersPalletWithSameItem()
        {
            capacity = 2;
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("wood", 3));
            // Пустой поддон ближе, но на дальнем уже лежит дерево: сначала несут туда.
            var empty = pallets.Add(world, new Vector3Int(7, Ground, 4));
            var withWood = pallets.Add(world, new Vector3Int(16, Ground, 4));
            pallets.Store(withWood, new ItemDrop("wood", 1));
            var worker = Spawn(new Vector2Int(5, 6));

            Run(80f, () => items.Items.Count == 0 && worker.State == WorkerState.Idle, worker);

            Assert.That(withWood.Count, Is.EqualTo(2), "Сначала дополнен поддон с деревом");
            Assert.That(empty.Count, Is.EqualTo(2), "Остальное — на пустой");
            Assert.That(empty.ItemId, Is.EqualTo("wood"));
            Assert.That(items.Items, Is.Empty);
        }

        [Test]
        public void TwoWorkers_DoNotOverfillPallet()
        {
            capacity = 1;
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 3));
            var pallet = pallets.Add(world, new Vector3Int(12, Ground, 12));
            var a = Spawn(new Vector2Int(9, 9));
            var b = Spawn(new Vector2Int(9, 10));

            Run(40f, () => pallet.Count == 1 && a.State == WorkerState.Idle && b.State == WorkerState.Idle, a, b);
            Run(5f, () => false, a, b);

            Assert.That(pallet.Count, Is.EqualTo(1));
            Assert.That(pallet.Reserved, Is.Zero);
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop.Count, Is.EqualTo(2), "Лишнее осталось лежать в кучке");
        }

        [Test]
        public void Worker_TakesStoneFromPalletForBlueprint()
        {
            var pallet = pallets.Add(world, new Vector3Int(4, Ground, 4));
            pallets.Store(pallet, new ItemDrop("stone", 1));
            var cell = new Vector3Int(14, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(9, 9));

            Run(30f, () => orders.Orders.Count == 0, worker);

            Assert.That(world.GetBlock(cell), Is.EqualTo(Block.Full(BlockMaterials.Stone)));
            Assert.That(pallet.Count, Is.Zero);
            Assert.That(pallet.ItemId, Is.Null);
        }

        [Test]
        public void Worker_BuildsBeforeHauling()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 2));
            var pallet = pallets.Add(world, new Vector3Int(14, Ground, 4));
            var cell = new Vector3Int(10, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var worker = Spawn(new Vector2Int(5, 6));

            var hauledBeforeBuilt = false;
            Run(60f, () =>
            {
                hauledBeforeBuilt |= orders.Orders.Count > 0 && worker.HaulTarget != null;
                return orders.Orders.Count == 0 && pallet.Count == 1;
            }, worker);

            Assert.That(hauledBeforeBuilt, Is.False, "Пока есть чертёж с ресурсом, на поддон не носят");
            Assert.That(world.GetBlock(cell).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(pallet.Count, Is.EqualTo(1));
        }

        [Test]
        public void Worker_DropsLoadWhenPalletRemoved()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("wood", 1));
            var pallet = pallets.Add(world, new Vector3Int(20, Ground, 20));
            var worker = Spawn(new Vector2Int(6, 6));

            Run(20f, () => worker.Carrying != null, worker);
            Assert.That(worker.Carrying, Is.Not.Null, "Несёт полено на поддон");
            pallets.Remove(pallet);
            Run(Step, () => false, worker);

            Assert.That(worker.Carrying, Is.Null);
            Assert.That(worker.HaulTarget, Is.Null);
            Assert.That(items.Items, Has.Count.EqualTo(1), "Полено упало у ног");
        }

        /// <summary>Рабочие одного отряда: общий контекст, как в сцене, — с ним общие и занятые под ношу кучки.</summary>
        private Worker Spawn(Vector2Int column)
        {
            context ??= new WorkerContext(world, orders, items, damage, new BlockStability(), settings, null, pallets: pallets);
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
