using Emberhold.Gameplay.Workers;
using Emberhold.World.Editing;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Скала гор и коренная порода дна не копаются (#97).</summary>
    public sealed class UnbreakableBlocksTests
    {
        private static readonly Vector3Int Cell = new(3, 4, 3);

        private static VoxelWorld WorldWith(byte material)
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(Cell, Block.Full(material));
            return world;
        }

        [TestCase(BlockMaterials.Cliff)]
        [TestCase(BlockMaterials.Bedrock)]
        public void Hit_CliffAndBedrock_TakeNoDamage(byte material)
        {
            var world = WorldWith(material);
            var damage = new BlockDamage(new UniformBlockDurability(100f));

            for (var i = 0; i < 10; i++)
                Assert.That(damage.Hit(world, Cell, 50f), Is.False);

            Assert.That(world.GetBlock(Cell), Is.EqualTo(Block.Full(material)));
            Assert.That(damage.Taken(Cell), Is.Zero, "трещин нет");
        }

        [Test]
        public void Hit_Stone_StillBreaks()
        {
            var world = WorldWith(BlockMaterials.Stone);
            var damage = new BlockDamage(new UniformBlockDurability(100f));

            damage.Hit(world, Cell, 50f);

            Assert.That(damage.Hit(world, Cell, 50f), Is.True);
            Assert.That(world.GetBlock(Cell).IsAir, Is.True);
        }

        [TestCase(BlockMaterials.Cliff)]
        [TestCase(BlockMaterials.Bedrock)]
        public void Remove_CliffAndBedrock_Refused(byte material)
        {
            var world = WorldWith(material);

            Assert.That(BlockEdits.Remove(world, Cell), Is.False);
            Assert.That(world.GetBlock(Cell).Material, Is.EqualTo(material));
        }

        [Test]
        public void DigOrder_OnCliff_IsNotValid()
        {
            var world = WorldWith(BlockMaterials.Cliff);
            var order = new WorkOrders().AddDig(Cell);

            Assert.That(WorkOrders.IsValid(world, order), Is.False);
        }

        [Test]
        public void IsBreakable_OnlyCliffAndBedrockAreNot()
        {
            for (var material = 1; material < BlockMaterials.Count; material++)
            {
                var expected = material != BlockMaterials.Cliff && material != BlockMaterials.Bedrock;
                Assert.That(BlockMaterials.IsBreakable((byte)material), Is.EqualTo(expected), $"материал {material}");
            }
        }

        [Test]
        public void Mountain_StoneBecomesCliff_DeepIsCliff_GrassAndDirtStay()
        {
            var m = TerrainMaterials.Default.Mountain();

            Assert.That(m.Surface, Is.EqualTo(BlockMaterials.Grass));
            Assert.That(m.Subsurface, Is.EqualTo(BlockMaterials.Dirt));
            Assert.That(m.Deep, Is.EqualTo(BlockMaterials.Cliff));

            var rock = new TerrainMaterials
            {
                Surface = BlockMaterials.Stone, Subsurface = BlockMaterials.Stone, SubsurfaceDepth = 1,
                Deep = BlockMaterials.Stone, Bed = BlockMaterials.Stone,
            }.Mountain();
            Assert.That(new[] { rock.Surface, rock.Subsurface, rock.Deep, rock.Bed }, Is.All.EqualTo(BlockMaterials.Cliff));
        }

        private static VoxelWorld SolidWorld(int height)
        {
            var world = new VoxelWorld(new Vector3Int(2, 1, 2));
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var y = 0; y < height; y++)
            for (var x = 0; x < size.x; x++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(BlockMaterials.Stone));
            return world;
        }

        private static int CountLayer(VoxelWorld world, int y, byte material)
        {
            var count = 0;
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                if (world.GetBlock(new Vector3Int(x, y, z)).Material == material)
                    count++;
            return count;
        }

        [Test]
        public void Bedrock_BottomLayerSolid_ThinsOutUpward_NoneAboveDepth()
        {
            var world = SolidWorld(8);
            var area = world.SizeInBlocks.x * world.SizeInBlocks.z;

            Bedrock.Lay(world, 4, 7);

            Assert.That(CountLayer(world, 0, BlockMaterials.Bedrock), Is.EqualTo(area));
            var previous = area;
            for (var y = 1; y < 4; y++)
            {
                var count = CountLayer(world, y, BlockMaterials.Bedrock);
                Assert.That(count, Is.GreaterThan(0).And.LessThan(previous), $"слой {y} реже нижнего");
                previous = count;
            }
            for (var y = 4; y < 8; y++)
                Assert.That(CountLayer(world, y, BlockMaterials.Bedrock), Is.Zero, $"слой {y}");
        }

        [Test]
        public void Bedrock_SameSeed_SameLayout_AirStaysAir()
        {
            var a = SolidWorld(2);
            var b = SolidWorld(2);

            Bedrock.Lay(a, 4, 7);
            Bedrock.Lay(b, 4, 7);

            var size = a.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < size.x; x++)
            {
                var cell = new Vector3Int(x, y, z);
                Assert.That(b.GetBlock(cell), Is.EqualTo(a.GetBlock(cell)), $"{cell}");
            }
            Assert.That(CountLayer(a, 2, BlockMaterials.Bedrock), Is.Zero, "воздух не становится породой");
        }

        [Test]
        public void Bedrock_UnderSmoothSurface_KeepsColumnsNatural()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var size = world.SizeInBlocks;
            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
                corners[x, z] = 6 * BlockShape.MaxHeight;
            var materials = new TerrainMaterials[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                materials[x, z] = TerrainMaterials.Default;
            var surface = CornerTerrain.FillSmooth(world, corners, materials, new int[size.x, size.z]);

            Bedrock.Lay(world, 4, 7);

            Assert.That(world.Surface, Is.SameAs(surface));
            Assert.That(surface.IsNatural(4, 4), Is.True);
            Assert.That(world.GetBlock(new Vector3Int(4, 0, 4)).Material, Is.EqualTo(BlockMaterials.Bedrock));
        }
    }
}
