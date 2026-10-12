using Emberhold.Gameplay.Enemies;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Enemies
{
    public sealed class CrawlerPathsTests
    {
        private const float MaxStep = 1.1f;
        private const int GroundHeight = 3;
        private static readonly Block Stone = Block.Full(BlockMaterials.Stone);
        private static readonly Vector2Int Fire = new(12, 8);
        private static readonly Vector2Int Start = new(2, 8);

        [Test]
        public void FlatGround_LeadsToFire()
        {
            var paths = new CrawlerPaths(Ground(), MaxStep);

            Assert.That(Walk(paths, Start), Is.EqualTo(10), "По ровному — прямо, по столбцу за шаг");
        }

        [Test]
        public void OneBlockLedge_ClimbsOver()
        {
            var world = Ground();
            for (var z = 0; z < ChunkData.Size; z++)
                Column(world, 7, z, 1);

            Assert.That(Walk(new CrawlerPaths(world, MaxStep), Start), Is.EqualTo(10), "Через уступ в блок — не сворачивая");
        }

        [Test]
        public void TwoBlockWallWithGap_GoesAroundThroughGap()
        {
            var world = Ground();
            var gap = 14;
            for (var z = 0; z < ChunkData.Size; z++)
                if (z != gap)
                    Column(world, 7, z, 2);
            var paths = new CrawlerPaths(world, MaxStep);

            var passedGap = false;
            var column = Start;
            for (var i = 0; i < 100 && column != Fire; i++)
            {
                Assert.That(paths.TryNext(Fire, column, out var next), Is.True);
                Assert.That((next - column).sqrMagnitude, Is.LessThanOrEqualTo(2), "Только в соседний столбец");
                passedGap |= next == new Vector2Int(7, gap);
                column = next;
            }
            Assert.That(column, Is.EqualTo(Fire), "Дошёл до огня");
            Assert.That(passedGap, Is.True, "Обошёл стену через проход");
        }

        [Test]
        public void TwoBlockRingAroundFire_NoPath()
        {
            var world = Ground();
            Ring(world, 2);
            var paths = new CrawlerPaths(world, MaxStep);

            Assert.That(paths.CanReach(Fire, Start), Is.False);
            Assert.That(paths.TryNext(Fire, Start, out _), Is.False);
            Assert.That(paths.CanReach(Fire, Fire + Vector2Int.left), Is.True, "Внутри кольца путь есть");
        }

        [Test]
        public void OneBlockRingAroundFire_ClimbsOver()
        {
            var world = Ground();
            Ring(world, 1);

            Assert.That(new CrawlerPaths(world, MaxStep).CanReach(Fire, Start), Is.True);
        }

        [Test]
        public void HighCliff_DescendsButCannotClimb()
        {
            var world = Ground();
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < 5; x++)
                Column(world, x, z, 3);
            var paths = new CrawlerPaths(world, MaxStep);

            Assert.That(paths.CanReach(Fire, Start), Is.True, "С обрыва к огню — спрыгнуть");
            Assert.That(paths.CanReach(Start, Fire), Is.False, "К огню на обрыве — не залезть");
        }

        [Test]
        public void WorldChanged_PathRecomputed()
        {
            var world = Ground();
            var paths = new CrawlerPaths(world, MaxStep);
            Assert.That(paths.CanReach(Fire, Start), Is.True);

            Ring(world, 2);
            Assert.That(paths.CanReach(Fire, Start), Is.False, "Стену построили — пути больше нет");
        }

        /// <summary>Сколько шагов по полю направлений из from до огня.</summary>
        private static int Walk(CrawlerPaths paths, Vector2Int from)
        {
            var column = from;
            var steps = 0;
            while (column != Fire)
            {
                Assert.That(paths.TryNext(Fire, column, out column), Is.True, "Путь к огню есть");
                Assert.That(++steps, Is.LessThan(100));
            }
            return steps;
        }

        /// <summary>Кольцо стены высотой height вокруг огня, на расстоянии 3 столбцов.</summary>
        private static void Ring(VoxelWorld world, int height)
        {
            for (var dz = -3; dz <= 3; dz++)
            for (var dx = -3; dx <= 3; dx++)
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == 3)
                    Column(world, Fire.x + dx, Fire.y + dz, height);
        }

        /// <summary>Надстроить столбец на height блоков над землёй.</summary>
        private static void Column(VoxelWorld world, int x, int z, int height)
        {
            for (var y = GroundHeight; y < GroundHeight + height; y++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
        }

        /// <summary>Один чанк, засыпанный камнем на GroundHeight блоков.</summary>
        private static VoxelWorld Ground()
        {
            var world = new VoxelWorld(Vector3Int.one);
            for (var y = 0; y < GroundHeight; y++)
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < ChunkData.Size; x++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
            return world;
        }
    }
}
