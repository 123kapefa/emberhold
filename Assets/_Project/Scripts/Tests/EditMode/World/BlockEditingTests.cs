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

        private static BlockDamage NewDamage() => new(new UniformBlockDurability(100f));

        [Test]
        public void Hit_PartialBlock_BreaksWholeBlockAfterSameHitsAsFull()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var partial = Cell + Vector3Int.right;
            world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));
            world.SetBlock(partial, Block.Flat(BlockMaterials.Grass, 12));
            var damage = NewDamage();

            for (var hit = 0; hit < 3; hit++)
            {
                Assert.That(damage.Hit(world, Cell, 25f), Is.False);
                Assert.That(damage.Hit(world, partial, 25f), Is.False);
            }
            // До последнего удара блок не меняется: урон не снимает слои.
            Assert.That(world.GetBlock(Cell), Is.EqualTo(Block.Full(BlockMaterials.Dirt)));
            Assert.That(world.GetBlock(partial), Is.EqualTo(Block.Flat(BlockMaterials.Grass, 12)));
            Assert.That(damage.Remaining(world, Cell), Is.EqualTo(25f).Within(1e-4f));

            Assert.That(damage.Hit(world, Cell, 25f), Is.True);
            Assert.That(damage.Hit(world, partial, 25f), Is.True);
            Assert.That(world.GetBlock(Cell).IsAir, Is.True);
            Assert.That(world.GetBlock(partial).IsAir, Is.True);
            Assert.That(damage.Count, Is.Zero);
        }

        [Test]
        public void Hit_TwoHitsThenLeave_BlockKeepsHalfHitPoints()
        {
            // Рабочий ударил дважды и ушёл: блок остаётся с половиной ХП, и следующий добивает его за два удара.
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Stone));
            var damage = NewDamage();

            damage.Hit(world, Cell, 25f);
            damage.Hit(world, Cell, 25f);

            Assert.That(damage.Remaining(world, Cell), Is.EqualTo(50f).Within(1e-4f));
            Assert.That(damage.Fraction(world, Cell), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(damage.Hit(world, Cell, 25f), Is.False);
            Assert.That(damage.Hit(world, Cell, 25f), Is.True);
        }

        [Test]
        public void Hit_UsesDurabilityOfBlockMaterial()
        {
            var table = ScriptableObject.CreateInstance<BlockDurabilityTable>();
            try
            {
                var world = new VoxelWorld(Vector3Int.one);
                world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));
                var damage = new BlockDamage(table);

                Assert.That(table.HitPoints(BlockMaterials.Dirt), Is.EqualTo(100f));
                Assert.That(table.HitPoints(BlockMaterials.Max), Is.EqualTo(100f), "Запасная прочность");
                Assert.That(damage.Remaining(world, Cell), Is.EqualTo(100f));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void Hit_AirOrOutside_DoesNothing()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var damage = NewDamage();

            Assert.That(damage.Hit(world, Cell, 50f), Is.False);
            Assert.That(damage.Hit(world, new Vector3Int(-1, 0, 0), 50f), Is.False);
            Assert.That(damage.Count, Is.Zero);
            Assert.That(world.DirtyCount, Is.Zero);
        }

        [Test]
        public void Hit_BreakOnChunkBorder_MarksNeighbourChunk()
        {
            var world = new VoxelWorld(new Vector3Int(2, 1, 1));
            var border = new Vector3Int(15, 3, 3);
            world.SetBlock(border, Block.Full(BlockMaterials.Dirt));
            world.TakeDirtyChunks(new List<Vector3Int>());

            NewDamage().Hit(world, border, 100f);

            Assert.That(world.IsDirty(Vector3Int.zero), Is.True);
            Assert.That(world.IsDirty(Vector3Int.right), Is.True);
        }

        [Test]
        public void ForgetMissing_DropsRemovedBlocks()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(BlockMaterials.Dirt));
            var damage = NewDamage();
            damage.Hit(world, Cell, 50f);
            var version = damage.Version;

            BlockEdits.Remove(world, Cell);
            damage.ForgetMissing(world);

            Assert.That(damage.Count, Is.Zero);
            Assert.That(damage.Version, Is.Not.EqualTo(version));
        }

        [TestCase(0f, 0)]
        [TestCase(0.25f, 1)]
        [TestCase(0.5f, 2)]
        [TestCase(0.75f, 3)]
        [TestCase(0.99f, 3)]
        public void Stage_ThreeStages_GrowWithEachQuarter(float value, int expected)
        {
            Assert.That(BlockDamage.Stage(value, 3), Is.EqualTo(expected));
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
