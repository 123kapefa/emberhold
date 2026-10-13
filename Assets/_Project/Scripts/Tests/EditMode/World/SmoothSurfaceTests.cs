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
    /// <summary>Гладкая поверхность нетронутой земли (#72): блоки под ней, отрисовка, высота для ходьбы.</summary>
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
        public void UnderSurface_LieWholeBlocks_UpToItsMiddle()
        {
            // Столбец (4, 4): углы 10 и 20 шестнадцатых, середина 15 — под ней один целый блок, без скосов.
            var world = Flat(10, (5, 4, 20), (5, 5, 20));

            Assert.That(world.GetBlock(new Vector3Int(4, 0, 4)).Shape, Is.EqualTo(BlockShape.Full));
            Assert.That(world.GetBlock(new Vector3Int(4, 1, 4)).IsAir, Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(0));
        }

        [Test]
        public void DiggingTopBlock_RemovesItWhole()
        {
            // Склон через границу слоя: раньше после копания оставался косой блок, теперь — целый блок ниже.
            var world = Flat(2 * H + 10, (5, 4, 2 * H + 20), (5, 5, 2 * H + 20));
            var top = world.Surface.TopY(4, 4);
            world.SetBlock(new Vector3Int(4, top, 4), Block.Air);

            Assert.That(world.GetBlock(new Vector3Int(4, top - 1, 4)).Shape, Is.EqualTo(BlockShape.Full));
            Assert.That(world.Surface.IsNatural(4, 4), Is.False);
        }

        [Test]
        public void SteepSurface_KeepsItsSteepness_BlocksAreWholeAtTheMiddle()
        {
            // Угол на 4 блока выше соседних: поверхность хранит крутизну, блоки — целые до её середины.
            var world = Flat(2 * H, (5, 5, 6 * H));

            Assert.That(world.GetBlock(new Vector3Int(4, 2, 4)).Shape, Is.EqualTo(BlockShape.Flat(H)));
            Assert.That(world.Surface.Corner(5, 5), Is.EqualTo(6 * H), "поверхность хранит крутизну как есть");
        }

        [Test]
        public void NaturalColumn_IsDrawnAsSurface_NotAsBlockFaces()
        {
            var world = Flat(10, (5, 4, 20), (5, 5, 20));
            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            // На каждый столбец ровно два треугольника поверхности и ни одной боковой грани.
            Assert.That(mesh.TriangleCount, Is.EqualTo(16 * 16 * 2));
            Assert.That(mesh.Vertices.Max(v => v.y), Is.EqualTo(20f / H).Within(1e-4f));
            Assert.That(mesh.Normals, Has.None.EqualTo(Vector3.right).And.None.EqualTo(Vector3.down));
        }

        [Test]
        public void DiggingTerrain_MakesColumnBlocky_AndNeighboursGetWalls()
        {
            var world = Flat(3 * H + 8);
            world.SetBlock(new Vector3Int(4, 3, 4), Block.Air);

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
            // Под склоном целый блок с верхом на 1, а земля для ходьбы — середина поверхности.
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
                var surface = world.Surface.CenterHeight(x, z);
                var top = world.Surface.TopY(x, z);
                var block = world.GetBlock(new Vector3Int(x, top, z));
                Assert.That(block.IsAir, Is.False, $"столбец {x},{z}");
                Assert.That(world.GetBlock(new Vector3Int(x, top + 1, z)).IsAir, Is.True, $"столбец {x},{z}");
                // Под поверхностью целые блоки до её середины: верх блока не дальше полублока от неё.
                Assert.That(block.Shape, Is.EqualTo(BlockShape.Full), $"столбец {x},{z}");
                Assert.That(top + 1f, Is.EqualTo(Mathf.Max(surface, 1f)).Within(0.51f), $"столбец {x},{z}");
            }
        }
    }
}
