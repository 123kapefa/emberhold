using System.Collections.Generic;
using Emberhold.World.Editing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockEditingTests
    {
        private static readonly Vector3Int Cell = new(3, 4, 2);

        [Test]
        public void FromSurface_TopOfFullBlock_PicksThatBlock()
        {
            var hit = BlockPicking.FromSurface(new Vector3(3.5f, 5f, 2.5f), Vector3.up);

            Assert.That(hit.Block, Is.EqualTo(Cell));
            Assert.That(hit.Face, Is.EqualTo(Vector3Int.up));
        }

        [Test]
        public void FromSurface_TopOfPartialBlock_PicksThatBlock()
        {
            var hit = BlockPicking.FromSurface(new Vector3(3.5f, 4.5f, 2.5f), Vector3.up);

            Assert.That(hit.Block, Is.EqualTo(Cell));
        }

        [Test]
        public void FromSurface_SideFaces_PickBlockBehindFace()
        {
            Assert.That(BlockPicking.FromSurface(new Vector3(4f, 4.5f, 2.5f), Vector3.right),
                Is.EqualTo(new BlockHit(Cell, Vector3Int.right)));
            Assert.That(BlockPicking.FromSurface(new Vector3(3f, 4.5f, 2.5f), Vector3.left),
                Is.EqualTo(new BlockHit(Cell, Vector3Int.left)));
            Assert.That(BlockPicking.FromSurface(new Vector3(3.5f, 4.5f, 3f), Vector3.forward),
                Is.EqualTo(new BlockHit(Cell, Vector3Int.forward)));
            Assert.That(BlockPicking.FromSurface(new Vector3(3.5f, 4.5f, 2f), Vector3.back),
                Is.EqualTo(new BlockHit(Cell, Vector3Int.back)));
            Assert.That(BlockPicking.FromSurface(new Vector3(3.5f, 4f, 2.5f), Vector3.down),
                Is.EqualTo(new BlockHit(Cell, Vector3Int.down)));
        }

        [Test]
        public void DominantAxis_TiltedNormal_SnapsToNearestAxis()
        {
            Assert.That(BlockPicking.DominantAxis(new Vector3(0.3f, 0.9f, 0.1f)), Is.EqualTo(Vector3Int.up));
            Assert.That(BlockPicking.DominantAxis(new Vector3(-0.8f, 0.5f, 0.2f)), Is.EqualTo(Vector3Int.left));
            Assert.That(BlockPicking.DominantAxis(new Vector3(0.1f, -0.2f, 0.95f)), Is.EqualTo(Vector3Int.forward));
        }

        [Test]
        public void TryPick_AirOrOutsideWorld_ReturnsFalse()
        {
            var world = new VoxelWorld(Vector3Int.one);

            Assert.That(BlockPicking.TryPick(world, new Vector3(3.5f, 5f, 2.5f), Vector3.up, out _), Is.False);
            Assert.That(BlockPicking.TryPick(world, new Vector3(-0.5f, 1f, 0.5f), Vector3.up, out _), Is.False);
        }

        [Test]
        public void TryPick_SolidBlock_ReturnsTrue()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));

            Assert.That(BlockPicking.TryPick(world, new Vector3(3.5f, 5f, 2.5f), Vector3.up, out var hit), Is.True);
            Assert.That(hit.Block, Is.EqualTo(Cell));
        }

        [Test]
        public void Dig_FullBlock_LowersTopByStep()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));

            Assert.That(BlockEdits.Dig(world, Cell, 4), Is.True);

            Assert.That(world.GetBlock(Cell), Is.EqualTo(Block.Flat(BlockMaterials.Dirt, 12)));
        }

        [Test]
        public void Dig_RepeatedUntilEmpty_LeavesAir()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Stone));

            for (var i = 0; i < 4; i++)
                Assert.That(BlockEdits.Dig(world, Cell, 4), Is.True);

            Assert.That(world.GetBlock(Cell).IsAir, Is.True);
            Assert.That(BlockEdits.Dig(world, Cell, 4), Is.False);
        }

        [Test]
        public void Dig_ThinnerThanStep_RemovesBlock()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Flat(BlockMaterials.Grass, 3));

            BlockEdits.Dig(world, Cell, 4);

            Assert.That(world.GetBlock(Cell).IsAir, Is.True);
        }

        [Test]
        public void Dig_Slope_LowersEveryCornerAndClampsAtFloor()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, new Block(BlockMaterials.Dirt, new BlockShape(16, 12, 4, 2)));

            BlockEdits.Dig(world, Cell, 4);

            Assert.That(world.GetBlock(Cell).Shape, Is.EqualTo(new BlockShape(12, 8, 0, 0)));
        }

        [Test]
        public void Dig_OnChunkBorder_MarksNeighbourChunk()
        {
            var world = new VoxelWorld(new Vector3Int(2, 1, 1));
            var border = new Vector3Int(15, 3, 3);
            world.SetBlock(border, Block.Full(BlockMaterials.Dirt));
            world.TakeDirtyChunks(new List<Vector3Int>());

            BlockEdits.Dig(world, border, 4);

            Assert.That(world.IsDirty(Vector3Int.zero), Is.True);
            Assert.That(world.IsDirty(Vector3Int.right), Is.True);
        }

        [Test]
        public void Dig_NonPositiveStep_DoesNothing()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));

            Assert.That(BlockEdits.Dig(world, Cell, 0), Is.False);
            Assert.That(world.GetBlock(Cell), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
        }

        [Test]
        public void Remove_SolidBlock_LeavesAir()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Flat(BlockMaterials.Dirt, 9));

            Assert.That(BlockEdits.Remove(world, Cell), Is.True);
            Assert.That(world.GetBlock(Cell).IsAir, Is.True);
        }

        [Test]
        public void Remove_AirOrOutsideWorld_ReturnsFalse()
        {
            var world = new VoxelWorld(Vector3Int.one);

            Assert.That(BlockEdits.Remove(world, Cell), Is.False);
            Assert.That(BlockEdits.Remove(world, new Vector3Int(-1, 0, 0)), Is.False);
            Assert.That(world.DirtyCount, Is.Zero);
        }
    }
}
