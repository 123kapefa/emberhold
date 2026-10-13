using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class VoxelWorldTests
    {
        [Test]
        public void GetBlock_OutsideWorld_IsAir()
        {
            var world = new VoxelWorld(Vector3Int.one);

            Assert.That(world.GetBlock(new Vector3Int(-1, 0, 0)).IsAir, Is.True);
            Assert.That(world.GetBlock(new Vector3Int(0, 16, 0)).IsAir, Is.True);
        }

        [Test]
        public void SetBlock_AcrossChunks_ReadsBack()
        {
            var world = new VoxelWorld(new Vector3Int(2, 2, 2));
            var pos = new Vector3Int(17, 20, 31);
            var block = Block.Flat(BlockMaterials.Dirt, 12);

            world.SetBlock(pos, block);

            Assert.That(world.GetBlock(pos), Is.EqualTo(block));
            Assert.That(world.GetChunk(new Vector3Int(1, 1, 1)).Get(1, 4, 15), Is.EqualTo(block));
        }

        [Test]
        public void SetBlock_InsideChunk_MarksOnlyThatChunk()
        {
            var world = new VoxelWorld(new Vector3Int(3, 1, 3));

            world.SetBlock(new Vector3Int(20, 5, 20), Block.Full(BlockMaterials.Stone));

            Assert.That(TakeDirty(world), Is.EquivalentTo(new[] { new Vector3Int(1, 0, 1) }));
        }

        [Test]
        public void SetBlock_OnChunkBorder_AlsoMarksNeighbour()
        {
            var world = new VoxelWorld(new Vector3Int(3, 1, 3));

            world.SetBlock(new Vector3Int(16, 5, 31), Block.Full(BlockMaterials.Stone));

            // Угол чанка по X и Z: соседи по сторонам и по диагонали (там меняется бахрома травы).
            Assert.That(TakeDirty(world), Is.EquivalentTo(new[]
            {
                new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 2), new Vector3Int(0, 0, 2),
            }));
        }

        [Test]
        public void SetBlock_OnWorldEdge_DoesNotMarkChunksOutsideWorld()
        {
            var world = new VoxelWorld(Vector3Int.one);

            world.SetBlock(Vector3Int.zero, Block.Full(BlockMaterials.Stone));

            Assert.That(TakeDirty(world), Is.EquivalentTo(new[] { Vector3Int.zero }));
        }

        [Test]
        public void SetBlock_SameValue_DoesNotMarkDirty()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone));
            TakeDirty(world);

            world.SetBlock(new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone));

            Assert.That(world.DirtyCount, Is.Zero);
        }

        [Test]
        public void GroundLayerConstant_MatchesProjectSettings()
        {
            Assert.That(LayerMask.NameToLayer(Layers.GroundName), Is.EqualTo(Layers.Ground));
        }

        private static List<Vector3Int> TakeDirty(VoxelWorld world)
        {
            var list = new List<Vector3Int>();
            world.TakeDirtyChunks(list);
            return list;
        }
    }
}
