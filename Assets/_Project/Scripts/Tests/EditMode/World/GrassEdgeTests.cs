using System.Linq;
using Emberhold.World.Meshing;
using Emberhold.World.Sandbox;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class GrassEdgeTests
    {
        private const int H = BlockShape.MaxHeight;

        /// <summary>Мир блоками: столбцы с x меньше grassUntil — трава, верхний слой top; дальше земля, верхний слой dirtTop.</summary>
        private static VoxelWorld Columns(int grassUntil, int top, int dirtTop)
        {
            var world = new VoxelWorld(Vector3Int.one);
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < ChunkData.Size; x++)
            {
                var grass = x < grassUntil;
                var columnTop = grass ? top : dirtTop;
                for (var y = 0; y <= columnTop; y++)
                    world.SetBlock(new Vector3Int(x, y, z), Block.Full(y == columnTop && grass ? BlockMaterials.Grass : BlockMaterials.Dirt));
            }
            return world;
        }

        private static int Mask(VoxelWorld world, int x, int y, int z) =>
            GrassEdge.NeighbourMask(world, new Vector3Int(x, y, z), world.GetBlock(new Vector3Int(x, y, z)).Shape);

        private const int LeftSide = GrassEdge.Left | GrassEdge.BackLeft | GrassEdge.ForwardLeft;

        [Test]
        public void GrassOnOneSide_OnlyBorderBlockTakesIt()
        {
            var world = Columns(grassUntil: 4, top: 7, dirtTop: 7);
            Assert.That(Mask(world, 4, 7, 8), Is.EqualTo(LeftSide));
            Assert.That(Mask(world, 5, 7, 8), Is.Zero, "Блоком дальше от травы");
        }

        [Test]
        public void PitFloor_GetsNoGrassFromBrow()
        {
            // Земля на 4 блока ниже травы — дно ямы.
            var world = Columns(grassUntil: 4, top: 7, dirtTop: 3);
            Assert.That(Mask(world, 4, 3, 8), Is.Zero);
        }

        [Test]
        public void HalfBlockStep_StillTakesGrass_BlockStep_DoesNot()
        {
            var world = Columns(grassUntil: 4, top: 7, dirtTop: 7);
            for (var z = 0; z < ChunkData.Size; z++)
                world.SetBlock(new Vector3Int(4, 7, z), Block.Flat(BlockMaterials.Dirt, H - GrassEdge.MaxStep));
            Assert.That(Mask(world, 4, 7, 8), Is.EqualTo(LeftSide), "Скос на полблока ниже травы");

            var lower = Columns(grassUntil: 4, top: 7, dirtTop: 6);
            Assert.That(Mask(lower, 4, 6, 8), Is.Zero, "Блок ниже травы");
        }

        [Test]
        public void CoveredGrass_DoesNotCount()
        {
            var world = Columns(grassUntil: 4, top: 7, dirtTop: 7);
            for (var z = 0; z < ChunkData.Size; z++)
                world.SetBlock(new Vector3Int(3, 8, z), Block.Full(BlockMaterials.Stone));
            Assert.That(Mask(world, 4, 7, 8), Is.Zero);
        }

        [Test]
        public void Showcase_DirtPadEdgeAndCorner_PitFloor_PlacedBlocks()
        {
            var world = GroundTexturesShowcase.Build();
            var top = GroundTexturesShowcase.BaseHeight - 1;

            var pad = GroundTexturesShowcase.DirtPad.Rect;
            Assert.That(Mask(world, pad.xMin, top, pad.yMin + 3), Is.EqualTo(LeftSide), "Край площадки");
            Assert.That(Mask(world, pad.xMin, top, pad.yMin) & (GrassEdge.Left | GrassEdge.Back),
                Is.EqualTo(GrassEdge.Left | GrassEdge.Back), "Угол площадки — трава с двух сторон");
            Assert.That(Mask(world, pad.xMin + 1, top, pad.yMin + 3), Is.Zero, "Блоком дальше");

            var pit = GroundTexturesShowcase.Pit.Rect;
            Assert.That(Mask(world, pit.xMin, GroundTexturesShowcase.PitFloor, pit.yMin + 3), Is.Zero, "Дно ямы у бровки");

            // На поставленный на траву блок трава не заходит: его верх на блок выше травы.
            var (placed, _) = GroundTexturesShowcase.PlacedBlocks[1];
            Assert.That(Mask(world, placed.x, placed.y, placed.z), Is.Zero);
        }

        [Test]
        public void Showcase_SmallPatch_RingTakesGrass_CenterOnlyFromRing()
        {
            var world = GroundTexturesShowcase.Build();
            var patch = GroundTexturesShowcase.SmallDugPatch.Rect;
            var top = GroundTexturesShowcase.BaseHeight - 1;
            Assert.That(Mask(world, patch.xMin, top, patch.yMin) & (GrassEdge.Left | GrassEdge.Back),
                Is.EqualTo(GrassEdge.Left | GrassEdge.Back));
            Assert.That(Mask(world, patch.xMin + 1, top, patch.yMin + 1), Is.Zero, "Середина окружена землёй");
        }

        [Test]
        public void Showcase_RockSurface_IsNotGrass()
        {
            var world = GroundTexturesShowcase.Build();
            var rock = GroundTexturesShowcase.RockPad.Rect;
            var height = GroundTexturesShowcase.BaseHeight * H;
            Assert.That(GrassEdge.IsGrass(world, rock.xMin + 5, rock.yMin + 5, rock.xMin + 5, rock.yMin + 5, height), Is.False);
            var grass = GroundTexturesShowcase.GrassPad.Rect;
            Assert.That(GrassEdge.IsGrass(world, grass.xMin + 2, grass.yMin + 2, grass.xMin + 2, grass.yMin + 2, height), Is.True);
        }

        [Test]
        public void Mesher_WritesMask_OnlyOnDirtTopNearGrass()
        {
            var world = GroundTexturesShowcase.Build();
            var pad = GroundTexturesShowcase.DirtPad.Rect;
            var chunk = new Vector3Int(pad.xMin / ChunkData.Size, 0, 0);
            var output = new ChunkMeshData();
            ChunkMesher.Build(world, chunk, output);

            var edgeX = pad.xMin - chunk.x * ChunkData.Size;
            var top = GroundTexturesShowcase.BaseHeight;
            bool OnTop(int i) => Mathf.Approximately(output.Vertices[i].y, top) && output.Normals[i].y > 0.9f;
            var edge = Enumerable.Range(0, output.Vertices.Count)
                .Where(i => OnTop(i) && output.Vertices[i].x >= edgeX && output.Vertices[i].x <= edgeX + 1
                    && output.Vertices[i].z > pad.yMin + 1 && output.Vertices[i].z < pad.yMax - 1)
                .ToList();
            Assert.That(edge.Any(i => (int)output.Blends[i].y == LeftSide), Is.True, "Маска на краю площадки");
            // Положение в ячейке: у левого ребра u = 0, у правого 1.
            Assert.That(edge.Where(i => (int)output.Blends[i].y == LeftSide)
                .All(i => Mathf.Approximately(output.Blends[i].z, output.Vertices[i].x - edgeX)), Is.True);

            // Середина площадки — без травы поверх.
            var inner = Enumerable.Range(0, output.Vertices.Count)
                .Where(i => OnTop(i) && output.Vertices[i].x >= edgeX + 3 && output.Vertices[i].x <= edgeX + 4
                    && output.Vertices[i].z > pad.yMin + 2 && output.Vertices[i].z < pad.yMax - 2);
            Assert.That(inner.All(i => output.Blends[i].y == 0f), Is.True);
        }
    }
}
