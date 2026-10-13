using System.Linq;
using Emberhold.Gameplay.Enemies;
using Emberhold.World.Generation;
using Emberhold.World.Items;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Гладкая поверхность нетронутой земли (#72, #78): блоки под ней, отрисовка, высота для ходьбы.</summary>
    public sealed class SmoothSurfaceTests
    {
        private const int H = BlockShape.MaxHeight;

        /// <summary>Мир 1×2×1 чанк с ровной поверхностью на высоте level (в шестнадцатых), кроме заданных углов.</summary>
        private static VoxelWorld Flat(int level, params (int x, int z, int height)[] overrides)
        {
            var world = new VoxelWorld(new Vector3Int(1, 2, 1));
            var size = world.SizeInBlocks;
            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
                corners[x, z] = level;
            foreach (var (x, z, height) in overrides)
                corners[x, z] = height;
            var materials = new TerrainMaterials[size.x, size.z];
            var beds = new int[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                materials[x, z] = TerrainMaterials.Default;
            CornerTerrain.FillSmooth(world, corners, materials, beds);
            world.TakeDirtyChunks(new System.Collections.Generic.List<Vector3Int>());
            return world;
        }

        [Test]
        public void UnderSurface_BlocksReachHighestCorner_EachCutByItsLayer()
        {
            // Столбец (4, 4): углы 10 и 20 шестнадцатых — блок слоя 0 и над ним кусок склона слоя 1.
            var world = Flat(10, (5, 4, 20), (5, 5, 20));

            Assert.That(world.GetBlock(new Vector3Int(4, 0, 4)).Shape, Is.EqualTo(new BlockShape(10, 16, 10, 16)));
            Assert.That(world.GetBlock(new Vector3Int(4, 1, 4)).Shape, Is.EqualTo(new BlockShape(0, 4, 0, 4)));
            Assert.That(world.GetBlock(new Vector3Int(4, 2, 4)).IsAir, Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(1));
        }

        [Test]
        public void DiggingTopBlock_LowersTop_ColumnStaysSmooth()
        {
            var world = Flat(2 * H + 10, (5, 4, 2 * H + 20), (5, 5, 2 * H + 20));
            var top = world.Surface.TopY(4, 4);
            world.SetBlock(new Vector3Int(4, top, 4), Block.Air);

            Assert.That(world.GetBlock(new Vector3Int(4, top - 1, 4)).Shape, Is.EqualTo(new BlockShape(10, 16, 10, 16)));
            Assert.That(world.Surface.IsNatural(4, 4), Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(top - 1));
        }

        [Test]
        public void SteepSurface_KeepsItsSteepness_BlocksGoUpToTheHighCorner()
        {
            // Угол на 4 блока выше соседних: поверхность хранит крутизну, блоки идут до него, у каждого свой слой.
            var world = Flat(2 * H, (5, 5, 6 * H));

            Assert.That(world.GetBlock(new Vector3Int(4, 1, 4)).Shape, Is.EqualTo(BlockShape.Full));
            for (var y = 2; y < 6; y++)
                Assert.That(world.GetBlock(new Vector3Int(4, y, 4)).Shape, Is.EqualTo(new BlockShape(0, 0, 0, H)), $"слой {y}");
            Assert.That(world.GetBlock(new Vector3Int(4, 6, 4)).IsAir, Is.True);
            Assert.That(world.Surface.Corner(5, 5), Is.EqualTo(6 * H), "поверхность хранит крутизну как есть");
        }

        [Test]
        public void NaturalColumn_IsDrawnAsSurface_NotAsBlockFaces()
        {
            var world = Flat(10, (5, 4, 20), (5, 5, 20));
            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            // Только поверхность, без боковых граней: вся она в подмеше травы и закрывает каждый столбец ровно раз.
            Assert.That(mesh.TriangleCount, Is.EqualTo(mesh.Triangles[BlockMaterials.Grass].Count / 3));
            Assert.That(SlopeBlocksTests.UpwardArea(mesh), Is.EqualTo(16f * 16f).Within(1e-3f));
            Assert.That(mesh.Vertices.Max(v => v.y), Is.EqualTo(20f / H).Within(1e-4f));
            Assert.That(mesh.Normals, Has.None.EqualTo(Vector3.right).And.None.EqualTo(Vector3.down));
        }

        [Test]
        public void DiggingBelowTop_MakesColumnBlocky_AndNeighboursGetWalls()
        {
            var world = Flat(3 * H + 8);
            world.SetBlock(new Vector3Int(4, 2, 4), Block.Air);

            Assert.That(world.Surface.IsNatural(4, 4), Is.False);
            Assert.That(world.Surface.IsNatural(5, 4), Is.True);
            Assert.That(world.IsDirty(Vector3Int.zero), Is.True);

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);
            // Стенка соседа справа смотрит в яму (−X) и опускается до дна мира.
            Assert.That(mesh.Normals, Has.Some.EqualTo(Vector3.left));
            Assert.That(mesh.Vertices.Min(v => v.y), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void PitWall_EachLayerBelongsToItsBlock()
        {
            // Клик по стенке ямы у дна должен попадать в блок этого слоя, а не в верхний блок соседа.
            var world = Flat(3 * H + 8);
            for (var y = 0; y <= 3; y++)
                world.SetBlock(new Vector3Int(4, y, 4), Block.Air);
            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            var blocks = mesh.TriangleBlocks.SelectMany(list => list).ToHashSet();
            for (var y = 0; y <= 3; y++)
                Assert.That(blocks, Does.Contain(ChunkData.Index(5, y, 4)), $"слой {y}");
        }

        [Test]
        public void BlockOnSmoothGround_HasNoGapsBelow()
        {
            // Верх нетронутой земли не прячет грани: под постройкой видно её дно, щели не остаётся.
            var world = Flat(3 * H + 8);
            world.SetBlock(new Vector3Int(4, 4, 4), Block.Full(BlockMaterials.Wood));
            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            Assert.That(mesh.Triangles[BlockMaterials.Wood].Count / 3, Is.EqualTo(12), "все шесть граней");
        }

        [Test]
        public void BuildingInsideTheSurface_MakesColumnBlocky()
        {
            // Угол столбца поднят выше слоя 4: блок на слое 4 частично под поверхностью — это уже земля.
            var world = Flat(3 * H + 8, (5, 5, 4 * H + 14));
            world.SetBlock(new Vector3Int(4, 4, 4), Block.Full(BlockMaterials.Wood));

            Assert.That(world.Surface.IsNatural(4, 4), Is.False);
        }

        [Test]
        public void BuildingAboveGround_KeepsColumnSmooth()
        {
            var world = Flat(3 * H + 8);
            world.SetBlock(new Vector3Int(4, 4, 4), Block.Full(BlockMaterials.Wood));

            Assert.That(world.Surface.IsNatural(4, 4), Is.True);
        }

        [Test]
        public void GroundHeight_OnSmoothSurface_IsSurfaceHeight()
        {
            // Под склоном блок и кусок над ним, а земля для ходьбы — середина поверхности.
            var world = Flat(10, (5, 4, 20), (5, 5, 20));
            var expected = (10 + 20 + 10 + 20) / 4f / H;

            Assert.That(DroppedItems.RestHeight(world, new Vector3Int(4, 5, 4)), Is.EqualTo(expected).Within(1e-4f));
            Assert.That(CrawlerFooting.TryTopSurface(world, 4.5f, 4.5f, out var crawler), Is.True);
            Assert.That(crawler, Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void CampaignSmooth_BlocksFollowTheSurface()
        {
            var layout = new CampaignLayout { SmoothSurface = true };
            layout.Locations.Add(new CampaignLocationShape
            {
                Name = "Котловина",
                Center = new Vector2(40f, 40f),
                Radius = new Vector2(20f, 16f),
                FloorHeight = 10f,
                Roughness = 1f,
                Profile = TerrainProfileParameters.Valley,
            });

            var world = TerrainGenerator.GenerateCampaign(layout, 7, new Vector3Int(5, 3, 5), out _);

            Assert.That(world.Surface, Is.Not.Null);
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z += 3)
            for (var x = 0; x < size.x; x += 3)
            {
                var s = world.Surface;
                var top = s.TopY(x, z);
                int c00 = s.Corner(x, z), c10 = s.Corner(x + 1, z), c01 = s.Corner(x, z + 1), c11 = s.Corner(x + 1, z + 1);
                var highest = Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11));
                // Блоки идут до самого высокого угла, каждый — углы поверхности, обрезанные по своему слою.
                Assert.That(top * H, Is.LessThan(highest), $"столбец {x},{z}");
                Assert.That((top + 1) * H, Is.GreaterThanOrEqualTo(Mathf.Min(highest, size.y * H)), $"столбец {x},{z}");
                Assert.That(world.GetBlock(new Vector3Int(x, top + 1, z)).IsAir, Is.True, $"столбец {x},{z}");
                for (var y = 0; y <= top; y++)
                    Assert.That(world.GetBlock(new Vector3Int(x, y, z)).Shape,
                        Is.EqualTo(new BlockShape(c00 - y * H, c10 - y * H, c01 - y * H, c11 - y * H)), $"блок {x},{y},{z}");
            }
        }
    }
}
