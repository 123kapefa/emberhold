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
    public sealed class WorkerStatusTests
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

        [TestCase(WorkerState.Idle, null, false, null, null, false, "без дела")]
        [TestCase(WorkerState.Fleeing, null, false, null, null, false, "бежит к свету")]
        [TestCase(WorkerState.ToPile, WorkKind.Fuel, false, "wood", null, false, "идёт за бревном")]
        [TestCase(WorkerState.ToPile, WorkKind.Build, false, "grass", null, false, "идёт за травой")]
        [TestCase(WorkerState.PickingUp, WorkKind.Build, false, "stone", null, false, "берёт камень")]
        [TestCase(WorkerState.ToSite, WorkKind.Build, false, "stone", "stone", false, "несёт камень к чертежу")]
        [TestCase(WorkerState.ToSite, WorkKind.Fuel, false, "wood", "wood", false, "несёт бревно к костру")]
        [TestCase(WorkerState.ToSite, WorkKind.Campfire, false, "wood", "wood", false, "несёт бревно к чертежу костра")]
        [TestCase(WorkerState.ToSite, null, false, "grass", "grass", true, "несёт траву на поддон")]
        [TestCase(WorkerState.ToSite, WorkKind.Dig, false, null, null, false, "идёт копать")]
        [TestCase(WorkerState.ToSite, WorkKind.Chop, true, null, null, false, "идёт корчевать")]
        [TestCase(WorkerState.Working, WorkKind.Dig, false, null, null, false, "копает")]
        [TestCase(WorkerState.Working, WorkKind.Chop, false, null, null, false, "рубит")]
        [TestCase(WorkerState.Working, WorkKind.Build, false, null, null, false, "строит")]
        [TestCase(WorkerState.Putting, WorkKind.Fuel, false, "wood", "wood", false, "кладёт в костёр")]
        [TestCase(WorkerState.Putting, null, false, "wood", "wood", true, "кладёт на поддон")]
        public void Text_SaysWhatWorkerDoes(WorkerState state, WorkKind? kind, bool stump, string wanted, string carrying, bool toPallet,
            string expected)
        {
            Assert.That(WorkerStatus.Text(state, kind, stump, wanted, carrying, toPallet), Is.EqualTo(expected));
        }

        [TestCase(WorkerState.Idle, null, false, WorkerActivity.Idle)]
        [TestCase(WorkerState.Fleeing, WorkKind.Dig, false, WorkerActivity.Flee)]
        [TestCase(WorkerState.ToSite, null, true, WorkerActivity.Haul)]
        [TestCase(WorkerState.Working, WorkKind.Chop, false, WorkerActivity.Chop)]
        [TestCase(WorkerState.ToPile, WorkKind.Fuel, false, WorkerActivity.Fuel)]
        public void Activity_ByStateAndOrder(WorkerState state, WorkKind? kind, bool toPallet, WorkerActivity expected)
        {
            Assert.That(WorkerStatus.Activity(state, kind, toPallet), Is.EqualTo(expected));
        }

        [Test]
        public void Worker_StatusAndRouteFollowHisWork()
        {
            items.Drop(world, new Vector3Int(4, Ground, 4), new ItemDrop("stone", 1));
            var cell = new Vector3Int(14, Ground, 12);
            orders.AddBuild(cell, Block.Full(BlockMaterials.Stone), false, new ItemDrop("stone", 1));
            var context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, null);
            var worker = new Worker(context, context.Surface.StandPoint(new Vector2Int(9, 9)));
            var route = new List<Vector3>();

            Assert.That(WorkerStatus.Text(worker), Is.EqualTo("без дела"));
            worker.RemainingRoute(route);
            Assert.That(route, Is.Empty, "Стоит — пути нет");

            Run(5f, () => worker.State == WorkerState.ToPile, worker);
            Assert.That(WorkerStatus.Text(worker), Is.EqualTo("идёт за камнем"));
            Assert.That(WorkerStatus.Activity(worker), Is.EqualTo(WorkerActivity.Build));
            worker.RemainingRoute(route);
            Assert.That(route.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(route[0], Is.EqualTo(worker.Position), "Линия начинается у рабочего");
            Assert.That(route[^1], Is.EqualTo(context.Surface.StandPoint(worker.Path[^1])), "и кончается в конце пути");

            Run(1f, () => false, worker);
            var shorter = new List<Vector3>();
            worker.RemainingRoute(shorter);
            Assert.That(shorter.Count, Is.LessThan(route.Count), "Пройденные столбцы из линии уходят");

            Run(30f, () => worker.State == WorkerState.ToSite, worker);
            Assert.That(WorkerStatus.Text(worker), Is.EqualTo("несёт камень к чертежу"));
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
    }
}
