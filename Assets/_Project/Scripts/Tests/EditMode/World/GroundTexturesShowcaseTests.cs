using Emberhold.World.Sandbox;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class GroundTexturesShowcaseTests
    {
        private static Vector2Int Center(GroundTexturesShowcase.Area area) =>
            Vector2Int.FloorToInt(area.Rect.center);

        [Test]
        public void Areas_FitInWorld_AndDoNotOverlap()
        {
            var size = GroundTexturesShowcase.Build().SizeInBlocks;
            var areas = GroundTexturesShowcase.Areas;
            for (var i = 0; i < areas.Length; i++)
            {
                Assert.That(areas[i].Rect.xMax, Is.LessThanOrEqualTo(size.x), areas[i].Name);
                Assert.That(areas[i].Rect.yMax, Is.LessThanOrEqualTo(size.z), areas[i].Name);
                for (var j = i + 1; j < areas.Length; j++)
                    Assert.That(areas[i].Rect.Overlaps(areas[j].Rect), Is.False, $"{areas[i].Name} / {areas[j].Name}");
            }
        }

        [Test]
        public void FlatPads_GrassIsSmoothGrass_RockIsStone_DirtIsBlocks()
        {
            var world = GroundTexturesShowcase.Build();
            var surface = world.Surface;
            var top = GroundTexturesShowcase.BaseHeight - 1;

            var grass = Center(GroundTexturesShowcase.GrassPad);
            Assert.That(surface.IsNatural(grass.x, grass.y), Is.True);
            Assert.That(surface.CenterHeight(grass.x, grass.y), Is.EqualTo(GroundTexturesShowcase.BaseHeight));
            Assert.That(surface.Rock(grass.x, grass.y), Is.Zero);

            var rock = Center(GroundTexturesShowcase.RockPad);
            Assert.That(surface.IsNatural(rock.x, rock.y), Is.True);
            Assert.That(surface.Rock(rock.x, rock.y), Is.EqualTo(1f));
            Assert.That(world.GetBlock(new Vector3Int(rock.x, top, rock.y)).Material, Is.EqualTo(BlockMaterials.Stone));

            var dirt = Center(GroundTexturesShowcase.DirtPad);
            Assert.That(surface.IsNatural(dirt.x, dirt.y), Is.False, "Площадка земли рисуется блоками");
            Assert.That(world.GetBlock(new Vector3Int(dirt.x, top, dirt.y)).Material, Is.EqualTo(BlockMaterials.Dirt));
        }

        [Test]
        public void SteepSlope_TurnsToRock_GentleSlopeStaysGrass()
        {
            var surface = GroundTexturesShowcase.Build().Surface;
            var gentle = Center(GroundTexturesShowcase.GentleSlope);
            Assert.That(surface.Rock(gentle.x, gentle.y), Is.Zero);
            Assert.That(surface.CenterHeight(gentle.x, gentle.y), Is.GreaterThan(GroundTexturesShowcase.BaseHeight + 1));

            // У подножия крутого склона трава, у верха — камень.
            var steep = GroundTexturesShowcase.SteepSlope.Rect;
            var z = (int)steep.center.y;
            Assert.That(surface.Rock(steep.xMin, z), Is.LessThan(0.5f));
            Assert.That(surface.Rock(steep.xMax - 2, z), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Cliff_IsSheerAndRocky()
        {
            var surface = GroundTexturesShowcase.Build().Surface;
            var cliff = GroundTexturesShowcase.Cliff.Rect;
            var x = (int)cliff.center.x;
            var edge = cliff.yMin + 4;
            Assert.That(surface.Corner(x, edge) - surface.Corner(x, edge - 1), Is.EqualTo(8 * BlockShape.MaxHeight));
            Assert.That(surface.Rock(x, edge), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Pit_IsDugToFloor_WithBlockWalls()
        {
            var world = GroundTexturesShowcase.Build();
            var pit = Center(GroundTexturesShowcase.Pit);
            Assert.That(world.Surface.IsNatural(pit.x, pit.y), Is.False);
            Assert.That(world.GetBlock(new Vector3Int(pit.x, GroundTexturesShowcase.PitFloor, pit.y)).Material,
                Is.Not.EqualTo(BlockMaterials.Air));
            for (var y = GroundTexturesShowcase.PitFloor + 1; y < GroundTexturesShowcase.BaseHeight; y++)
                Assert.That(world.GetBlock(new Vector3Int(pit.x, y, pit.y)).Material, Is.EqualTo(BlockMaterials.Air));

            // Сразу за краем ямы земля нетронута.
            var rect = GroundTexturesShowcase.Pit.Rect;
            Assert.That(world.Surface.IsNatural(rect.xMin - 1, pit.y), Is.True);
        }

        [Test]
        public void PlacedBlocks_StandOnGrass_WithoutBreakingSurface()
        {
            var world = GroundTexturesShowcase.Build();
            foreach (var (block, material) in GroundTexturesShowcase.PlacedBlocks)
            {
                Assert.That(world.GetBlock(block).Material, Is.EqualTo(material));
                Assert.That(world.Surface.IsNatural(block.x, block.z), Is.True, block.ToString());
            }
        }
    }
}
