using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class ValleyGeneratorTests
    {
        private static ValleyParameters SmallValley(int seed = 7)
        {
            var p = ValleyParameters.Default;
            p.Seed = seed;
            p.SizeInChunks = new Vector3Int(2, 2, 2);
            p.FloorHeight = 4f;
            p.RimHeight = 20f;
            return p;
        }

        /// <summary>Высота поверхности столбца в шестнадцатых.</summary>
        private static int ColumnHeight(VoxelWorld world, int x, int z)
        {
            for (var y = world.SizeInBlocks.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (!block.IsAir)
                    return y * BlockShape.MaxHeight + block.Shape.Top;
            }
            return 0;
        }

        [Test]
        public void SameSeed_GivesSameWorld()
        {
            var a = ValleyGenerator.Generate(SmallValley());
            var b = ValleyGenerator.Generate(SmallValley());

            for (var z = 0; z < a.SizeInBlocks.z; z++)
            for (var x = 0; x < a.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(a, x, z), Is.EqualTo(ColumnHeight(b, x, z)), $"Столбец {x},{z}");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentWorlds()
        {
            var a = ValleyGenerator.Generate(SmallValley(1));
            var b = ValleyGenerator.Generate(SmallValley(2));

            var differ = 0;
            for (var z = 0; z < a.SizeInBlocks.z; z++)
            for (var x = 0; x < a.SizeInBlocks.x; x++)
                if (ColumnHeight(a, x, z) != ColumnHeight(b, x, z))
                    differ++;

            Assert.That(differ, Is.GreaterThan(0));
        }

        [Test]
        public void Columns_AreSolidUpToSurface_WithGrassOnTop()
        {
            var world = ValleyGenerator.Generate(SmallValley());

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
            {
                var height = ColumnHeight(world, x, z);
                Assert.That(height, Is.GreaterThan(0));
                var surfaceY = (height - 1) / BlockShape.MaxHeight;
                Assert.That(world.GetBlock(new Vector3Int(x, surfaceY, z)).Material, Is.EqualTo(BlockMaterials.Grass));
                for (var y = 0; y < surfaceY; y++)
                {
                    var block = world.GetBlock(new Vector3Int(x, y, z));
                    Assert.That(block.Shape.IsFull, Is.True, $"Под поверхностью неполный блок {x},{y},{z}");
                    Assert.That(block.Material, Is.Not.EqualTo(BlockMaterials.Grass));
                }
            }
        }

        [Test]
        public void SurfaceHeights_AreQuantizedToStep()
        {
            var p = SmallValley();
            p.HeightStep = 4;
            var world = ValleyGenerator.Generate(p);

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(world, x, z) % 4, Is.Zero);
        }

        [Test]
        public void ValleyFloor_IsLowerThanRims()
        {
            var p = SmallValley();
            p.MeanderAmplitude = 0f;
            p.NoiseAmplitude = 0f;
            var world = ValleyGenerator.Generate(p);
            var size = world.SizeInBlocks;

            var middle = ColumnHeight(world, size.x / 2, size.z / 2);
            var edge = ColumnHeight(world, 0, size.z / 2);

            Assert.That(middle, Is.EqualTo(4 * BlockShape.MaxHeight));
            Assert.That(edge, Is.GreaterThan(middle));
        }

        [Test]
        public void Generate_MarksChunksForMeshing()
        {
            var world = ValleyGenerator.Generate(SmallValley());

            Assert.That(world.DirtyCount, Is.GreaterThan(0));
        }
    }
}
