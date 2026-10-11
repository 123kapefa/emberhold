using System;
using Emberhold.World.Voxels;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockStorageTests
    {
        [Test]
        public void BlockShape_PackUnpack_RoundTrips()
        {
            var shape = new BlockShape(0, 16, 7, 3);

            Assert.That(BlockShape.Unpack(shape.Pack()), Is.EqualTo(shape));
            Assert.That(shape.IsFlat, Is.False);
            Assert.That(shape.Top, Is.EqualTo(16));
        }

        [Test]
        public void BlockShape_ClampsHeightsTo0Through16()
        {
            var shape = new BlockShape(-3, 40, 5, 5);

            Assert.That(shape.C00, Is.EqualTo(0));
            Assert.That(shape.C10, Is.EqualTo(16));
        }

        [Test]
        public void Block_WithZeroHeightOrNoMaterial_IsAir()
        {
            Assert.That(Block.Flat(BlockMaterials.Dirt, 0).IsAir, Is.True);
            Assert.That(new Block(BlockMaterials.Air, BlockShape.Full), Is.EqualTo(Block.Air));
        }

        [TestCase(1)]
        [TestCase(8)]
        [TestCase(12)]
        [TestCase(16)]
        public void Chunk_StoresFlatPartialBlocks(int height)
        {
            var chunk = new ChunkData();
            var block = Block.Flat(BlockMaterials.Grass, height);

            chunk.Set(3, 4, 5, block);

            Assert.That(chunk.Get(3, 4, 5), Is.EqualTo(block));
            Assert.That(chunk.Get(4, 4, 5).IsAir, Is.True);
            Assert.That(chunk.SlopeShapeCount, Is.Zero);
        }

        [Test]
        public void Chunk_StoresSlopesInPaletteWithoutDuplicates()
        {
            var chunk = new ChunkData();
            var slope = new Block(BlockMaterials.Stone, new BlockShape(16, 8, 8, 0));

            chunk.Set(0, 0, 0, slope);
            chunk.Set(15, 15, 15, slope);

            Assert.That(chunk.Get(0, 0, 0), Is.EqualTo(slope));
            Assert.That(chunk.Get(15, 15, 15), Is.EqualTo(slope));
            Assert.That(chunk.SlopeShapeCount, Is.EqualTo(1));
        }

        [Test]
        public void Chunk_WhenPaletteFull_ReusesShapesNoLongerInUse()
        {
            var chunk = new ChunkData();
            var survivor = new Block(BlockMaterials.Grass, new BlockShape(16, 0, 0, 1));
            chunk.Set(0, 0, 0, survivor);
            // Перебираем разные склоны в одной ячейке: старые формы перестают использоваться.
            var placed = 0;
            for (var a = 0; a <= 16 && placed < ChunkData.MaxSlopeShapes + 10; a++)
            for (var b = 0; b <= 16 && placed < ChunkData.MaxSlopeShapes + 10; b++)
            for (var c = 0; c <= 16 && placed < ChunkData.MaxSlopeShapes + 10; c++)
            {
                var shape = new BlockShape(a, b, c, 16);
                if (shape.IsFlat)
                    continue;
                chunk.Set(1, 1, 1, new Block(BlockMaterials.Dirt, shape));
                placed++;
            }

            var kept = new Block(BlockMaterials.Stone, new BlockShape(1, 2, 3, 4));
            chunk.Set(2, 2, 2, kept);

            Assert.That(chunk.Get(2, 2, 2), Is.EqualTo(kept));
            Assert.That(chunk.Get(0, 0, 0), Is.EqualTo(survivor));
            Assert.That(chunk.SlopeShapeCount, Is.LessThan(ChunkData.MaxSlopeShapes));
        }

        [Test]
        public void Chunk_TooManyDifferentSlopes_StoresExtraAsFlatOfSameVolume()
        {
            var chunk = new ChunkData();
            var placed = 0;
            Block last = default;
            // Каждый склон в своей ячейке: сжать палитру нечем.
            for (var a = 0; a <= 16 && placed < ChunkData.MaxSlopeShapes + 1; a++)
            for (var b = 0; b <= 16 && placed < ChunkData.MaxSlopeShapes + 1; b++)
            for (var c = 0; c <= 16 && placed < ChunkData.MaxSlopeShapes + 1; c++)
            {
                var shape = new BlockShape(a, b, c, 16);
                if (shape.IsFlat)
                    continue;
                last = new Block(BlockMaterials.Dirt, shape);
                var pos = ChunkData.Position(placed++);
                Assert.DoesNotThrow(() => chunk.Set(pos.x, pos.y, pos.z, last));
            }

            var stored = chunk.Get(ChunkData.Position(placed - 1).x, ChunkData.Position(placed - 1).y, ChunkData.Position(placed - 1).z);
            Assert.That(stored.Material, Is.EqualTo(BlockMaterials.Dirt));
            Assert.That(stored.Shape.IsFlat, Is.True);
            Assert.That(stored.Shape.Fill, Is.EqualTo(last.Shape.Fill).Within(1f / 32));
        }

        [Test]
        public void Position_IsInverseOfIndex()
        {
            for (var i = 0; i < ChunkData.Volume; i += 37)
            {
                var p = ChunkData.Position(i);
                Assert.That(ChunkData.Index(p.x, p.y, p.z), Is.EqualTo(i));
            }
        }

        [Test]
        public void Chunk_RejectsMaterialAbove63()
        {
            var chunk = new ChunkData();

            Assert.Throws<ArgumentOutOfRangeException>(() => chunk.Set(0, 0, 0, Block.Full(64)));
        }
    }
}
