using Emberhold.World.Editing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockPlacementTests
    {
        private static readonly Vector3Int Cell = new(4, 4, 4);

        private static VoxelWorld WorldWith(Block block)
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, block);
            return world;
        }

        [Test]
        public void SideFace_PlacesIntoNeighbourCell()
        {
            var world = WorldWith(Block.Full(BlockMaterials.Stone));
            var block = Block.Full(BlockMaterials.Wood);

            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.right), block, out var plan), Is.True);
            Assert.That(plan.Cell, Is.EqualTo(Cell + Vector3Int.right));
            Assert.That(plan.FillsExisting, Is.False);

            BlockPlacement.Apply(world, plan);
            Assert.That(world.GetBlock(Cell + Vector3Int.right), Is.EqualTo(block));
        }

        [Test]
        public void TopOfPartialBlockOfSameMaterial_FillsItToFull()
        {
            var world = WorldWith(Block.Flat(BlockMaterials.Stone, 8));

            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.up), Block.Full(BlockMaterials.Stone), out var plan), Is.True);
            Assert.That(plan.FillsExisting, Is.True);
            Assert.That(plan.Cell, Is.EqualTo(Cell));

            BlockPlacement.Apply(world, plan);
            Assert.That(world.GetBlock(Cell), Is.EqualTo(Block.Full(BlockMaterials.Stone)));
        }

        [Test]
        public void TopOfPartialBlockOfOtherMaterial_PlacesAbove()
        {
            var world = WorldWith(Block.Flat(BlockMaterials.Dirt, 8));

            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.up), Block.Full(BlockMaterials.Stone), out var plan), Is.True);
            Assert.That(plan.FillsExisting, Is.False);
            Assert.That(plan.Cell, Is.EqualTo(Cell + Vector3Int.up));
        }

        [Test]
        public void TopOfFullBlockOrStairs_PlacesAbove()
        {
            foreach (var hovered in new[] { Block.Full(BlockMaterials.Stone), new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.Stairs, 0)) })
            {
                var world = WorldWith(hovered);
                Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.up), Block.Full(BlockMaterials.Stone), out var plan), Is.True);
                Assert.That(plan.Cell, Is.EqualTo(Cell + Vector3Int.up), hovered.ToString());
            }
        }

        [Test]
        public void OccupiedOrOutsideCell_CannotPlace()
        {
            var world = WorldWith(Block.Full(BlockMaterials.Stone));
            world.SetBlock(Cell + Vector3Int.left, Block.Full(BlockMaterials.Stone));
            var edge = new Vector3Int(15, 4, 4);
            world.SetBlock(edge, Block.Full(BlockMaterials.Stone));

            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.left), Block.Full(BlockMaterials.Wood), out _), Is.False);
            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(edge, Vector3Int.right), Block.Full(BlockMaterials.Wood), out _), Is.False);
            Assert.That(BlockPlacement.TryPlan(world, new BlockHit(Cell, Vector3Int.up), Block.Air, out _), Is.False);
        }
    }
}
