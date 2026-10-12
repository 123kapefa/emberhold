using Emberhold.Gameplay.Enemies;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Enemies
{
    public sealed class CrawlerFootingTests
    {
        private const float MaxStep = 1.1f;
        private static readonly Block Stone = Block.Full(BlockMaterials.Stone);

        [Test]
        public void FlatGround_WalksAtSameHeight()
        {
            var world = Ground(3);

            Assert.That(CrawlerFooting.TryStep(world, 5.5f, 5.5f, 3f, MaxStep, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(3f));
        }

        [Test]
        public void OneBlockLedge_ClimbsOnTop()
        {
            var world = Ground(3);
            world.SetBlock(new Vector3Int(6, 3, 5), Stone);

            Assert.That(CrawlerFooting.TryStep(world, 6.5f, 5.5f, 3f, MaxStep, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(4f));
        }

        [Test]
        public void TwoBlockWall_Blocks()
        {
            var world = Ground(3);
            world.SetBlock(new Vector3Int(6, 3, 5), Stone);
            world.SetBlock(new Vector3Int(6, 4, 5), Stone);

            Assert.That(CrawlerFooting.TryStep(world, 6.5f, 5.5f, 3f, MaxStep, out _), Is.False);
            // С уступа в один блок на ту же стену уже можно.
            Assert.That(CrawlerFooting.TryStep(world, 6.5f, 5.5f, 4f, MaxStep, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(5f));
        }

        [Test]
        public void Pit_DescendsAnyDepth()
        {
            var world = Ground(5);
            for (var y = 1; y < 5; y++)
                world.SetBlock(new Vector3Int(4, y, 4), Block.Air);

            Assert.That(CrawlerFooting.TryStep(world, 4.5f, 4.5f, 5f, MaxStep, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(1f));
        }

        [Test]
        public void HalfSlab_SurfaceInMiddleOfBlock()
        {
            var world = Ground(3);
            world.SetBlock(new Vector3Int(2, 3, 2), Block.Flat(BlockMaterials.Stone, BlockShape.MaxHeight / 2));

            Assert.That(CrawlerFooting.TryStep(world, 2.5f, 2.5f, 3f, MaxStep, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(3.5f));
        }

        [Test]
        public void OutsideWorldOrEmptyColumn_CannotStand()
        {
            var world = Ground(3);
            world.SetBlock(new Vector3Int(1, 0, 1), Block.Air);
            world.SetBlock(new Vector3Int(1, 1, 1), Block.Air);
            world.SetBlock(new Vector3Int(1, 2, 1), Block.Air);

            Assert.That(CrawlerFooting.TryStep(world, -0.5f, 3f, 3f, MaxStep, out _), Is.False);
            Assert.That(CrawlerFooting.TryStep(world, ChunkData.Size + 0.5f, 3f, 3f, MaxStep, out _), Is.False);
            Assert.That(CrawlerFooting.TryStep(world, 1.5f, 1.5f, 3f, MaxStep, out _), Is.False);
        }

        [Test]
        public void TopSurface_FindsHighestBlock()
        {
            var world = Ground(3);
            world.SetBlock(new Vector3Int(7, 10, 7), Stone);

            Assert.That(CrawlerFooting.TryTopSurface(world, 7.5f, 7.5f, out var surface), Is.True);
            Assert.That(surface, Is.EqualTo(11f));
            Assert.That(CrawlerFooting.TryTopSurface(world, 8.5f, 7.5f, out surface), Is.True);
            Assert.That(surface, Is.EqualTo(3f));
        }

        [Test]
        public void SpawnInterval_ShrinksEachNight()
        {
            var settings = ScriptableObject.CreateInstance<CrawlerSettings>();
            try
            {
                Assert.That(settings.SpawnInterval(2), Is.LessThan(settings.SpawnInterval(1)));
                Assert.That(settings.SpawnInterval(3), Is.LessThan(settings.SpawnInterval(2)));
                Assert.That(settings.SpawnInterval(0), Is.EqualTo(settings.SpawnInterval(1)));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        /// <summary>Один чанк, засыпанный камнем на height блоков.</summary>
        private static VoxelWorld Ground(int height)
        {
            var world = new VoxelWorld(Vector3Int.one);
            for (var y = 0; y < height; y++)
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < ChunkData.Size; x++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
            return world;
        }
    }
}
