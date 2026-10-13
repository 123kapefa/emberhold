using System;
using System.Collections.Generic;
using System.Linq;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Editing;
using Emberhold.World.Generation;
using Emberhold.World.Items;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>
    /// Куски склона над верхним блоком нетронутого столбца — настоящие блоки (#78): у каждого свой слой
    /// поверхности, их выделяют и копают по одному, а склон после копания остаётся гладким.
    /// </summary>
    public sealed class SlopeBlocksTests
    {
        private const int H = BlockShape.MaxHeight;
        private const int Base = 2 * H + 8;
        private const int Bump = 3 * H + 12;

        // Столбец (4, 4): углы 40, 40, 40 и 60 — блок камня слоя 2 и над ним кусок склона слоя 3.
        private static readonly Vector3Int Piece = new(4, 3, 4);
        private static readonly Vector3Int Stone = new(4, 2, 4);

        private static readonly TerrainMaterials Rock = new()
        {
            Surface = BlockMaterials.Stone,
            Subsurface = BlockMaterials.Stone,
            SubsurfaceDepth = 1,
            Deep = BlockMaterials.Stone,
            Bed = BlockMaterials.Stone,
        };

        /// <summary>Мир 1×2×1 чанк: ровный камень (или column) на высоте Base, угол (5, 5) поднят до Bump.</summary>
        private static VoxelWorld BumpWorld(int bedLevel = 0, TerrainMaterials? column = null)
        {
            var world = new VoxelWorld(new Vector3Int(1, 2, 1));
            var size = world.SizeInBlocks;
            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
                corners[x, z] = Base;
            corners[5, 5] = Bump;
            var materials = new TerrainMaterials[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                materials[x, z] = column ?? Rock;
            var beds = new int[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                beds[x, z] = bedLevel;
            CornerTerrain.FillSmooth(world, corners, materials, beds);
            world.TakeDirtyChunks(new List<Vector3Int>());
            return world;
        }

        [Test]
        public void PieceAboveStone_IsItsOwnBlock_OfStone()
        {
            var world = BumpWorld();

            Assert.That(world.GetBlock(Piece), Is.EqualTo(new Block(BlockMaterials.Stone, new BlockShape(0, 0, 0, 12))));
            Assert.That(world.GetBlock(Stone), Is.EqualTo(new Block(BlockMaterials.Stone, new BlockShape(8, 8, 8, 16))));
            Assert.That(world.GetBlock(Piece + Vector3Int.up).IsAir, Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(3));
        }

        [Test]
        public void PieceAboveGrass_IsGrass()
        {
            var world = BumpWorld(column: TerrainMaterials.Default);

            Assert.That(world.GetBlock(Piece).Material, Is.EqualTo(BlockMaterials.Grass));
            Assert.That(world.GetBlock(Stone).Material, Is.EqualTo(TerrainMaterials.Default.Surface));
        }

        [Test]
        public void PieceUnderWater_IsBedMaterial()
        {
            var world = BumpWorld(4 * H);

            Assert.That(world.GetBlock(Piece).Material, Is.EqualTo(Rock.Bed));
        }

        [Test]
        public void GroundHeight_FromBelowTheTopPiece_IsSurfaceHeight()
        {
            // Поиск сверху вниз с середины столбца натыкается на блок под куском — высота всё равно поверхности.
            var world = BumpWorld();
            var center = world.Surface.CenterHeight(4, 4);

            Assert.That(DroppedItems.RestHeight(world, Stone), Is.EqualTo(center).Within(1e-4f));
            // Ползун с высоты 1 и шагом 1,7 дотягивается до 2,7 — до поверхности 2,81 не достаёт.
            Assert.That(CrawlerFooting.TryStep(world, 4.5f, 4.5f, 1f, 1.7f, out var surface), Is.False);
            Assert.That(surface, Is.EqualTo(center).Within(1e-4f));
        }

        [Test]
        public void GrassEdge_OverDugTop_IsTheBlockMaterial()
        {
            // Выкопанный верх закрыт крышкой камня: бахрома травы с неё на соседей не идёт.
            var world = BumpWorld();
            Assert.That(GrassEdge.IsGrass(world, 4, 4, 5, 5, Bump), Is.True);

            world.SetBlock(Piece, Block.Air);

            Assert.That(GrassEdge.IsGrass(world, 4, 4, 5, 5, 3 * H), Is.False);
        }

        [Test]
        public void SurfaceLayers_BelongToTheirBlocks()
        {
            var world = BumpWorld();
            var mesh = Build(world);

            // Выпуклость над слоем 3 — кусок, низ склона в слое 2 — блок камня.
            var piece = Heights(mesh, Piece);
            var stone = Heights(mesh, Stone);
            Assert.That(piece, Is.Not.Empty);
            Assert.That(stone, Is.Not.Empty);
            Assert.That(piece.Min(), Is.GreaterThanOrEqualTo(3f - 1e-4f));
            Assert.That(piece.Max(), Is.EqualTo(Bump / (float)H).Within(1e-4f));
            Assert.That(stone.Max(), Is.LessThanOrEqualTo(3f + 1e-4f));
            Assert.That(UpwardArea(mesh), Is.EqualTo(16f * 16f).Within(1e-3f), "поверхность без дыр и наложений");
        }

        [Test]
        public void Picking_OnSmoothGround_KeepsTheBlockUnderTheRay()
        {
            // Над блоком камня кусок с открытым полом (внешний угол ската): на гладкой земле низ склона — сам камень.
            var world = BumpWorld();
            Assert.That(world.GetBlock(Piece).Shape.CoversFloor, Is.False);

            var hit = BlockPicking.PreferSlopeAbove(world, new BlockHit(Stone, Vector3Int.up));

            Assert.That(hit.Block, Is.EqualTo(Stone));
        }

        [Test]
        public void DiggingPieceThenStone_KeepsSlopeSmooth()
        {
            var world = BumpWorld();

            world.SetBlock(Piece, Block.Air);
            Assert.That(world.Surface.IsNatural(4, 4), Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(2));
            Assert.That(world.IsDirty(Vector3Int.zero), Is.True);
            var mesh = Build(world);
            Assert.That(Heights(mesh, Piece), Is.Empty);
            // Верх камня: склон до потолка слоя и ровная крышка камня там, где поверхность была выше.
            Assert.That(Heights(mesh, Stone).Max(), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(OwnedTriangles(mesh, BlockMaterials.Stone, Stone), Is.GreaterThan(0));
            Assert.That(UpwardArea(mesh), Is.EqualTo(16f * 16f).Within(1e-3f), "поверхность без дыр после куска");
            AssertWallsUnderSurface(world, mesh);

            world.SetBlock(Stone, Block.Air);
            Assert.That(world.Surface.IsNatural(4, 4), Is.True);
            Assert.That(world.Surface.TopY(4, 4), Is.EqualTo(1));
            mesh = Build(world);
            Assert.That(Heights(mesh, new Vector3Int(4, 1, 4)).Max(), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(UpwardArea(mesh), Is.EqualTo(16f * 16f).Within(1e-3f), "поверхность без дыр после блока");
            AssertWallsUnderSurface(world, mesh);
            // Соседи с выпуклостью смотрят стенками в выкопанный столбец.
            Assert.That(mesh.Normals, Has.Some.EqualTo(Vector3.left).And.Some.EqualTo(Vector3.back));
        }

        [Test]
        public void DiggingPieceAndStone_EachDropsStone()
        {
            var world = BumpWorld();
            var damage = new BlockDamage(new UniformBlockDurability(100f));
            var items = new DroppedItems(new StoneDrops());
            damage.Destroyed += (block, destroyed) => items.OnBlockDestroyed(world, block, destroyed);

            // Кусок склона на скале — камень: после копания в столбце не остаётся травы.
            Assert.That(damage.Hit(world, Piece, 100f), Is.True);
            Assert.That(items.Items.Sum(i => i.Drop.Count), Is.EqualTo(1));
            Assert.That(damage.Hit(world, Stone, 100f), Is.True);

            // Оба камня в одном столбце — одна кучка из двух.
            Assert.That(items.Items.All(i => i.Drop.ItemId == "stone"), Is.True);
            Assert.That(items.Items.Sum(i => i.Drop.Count), Is.EqualTo(2));
        }

        [Test]
        public void Worker_DigsPieceThenStone()
        {
            var world = BumpWorld();
            var orders = new WorkOrders();
            var items = new DroppedItems(new StoneDrops());
            var damage = new BlockDamage(new UniformBlockDurability(100f));
            damage.Destroyed += (block, destroyed) => items.OnBlockDestroyed(world, block, destroyed);
            var settings = ScriptableObject.CreateInstance<WorkerSettings>();
            try
            {
                var context = new WorkerContext(world, orders, items, damage, new BlockStability(), settings, null);
                var worker = new Worker(context, context.Surface.StandPoint(new Vector2Int(9, 9)));
                orders.AddDig(Piece);
                orders.AddDig(Stone);

                for (var t = 0f; t < 120f && orders.Orders.Count > 0; t += 0.1f)
                {
                    orders.Prune(world);
                    worker.Tick(0.1f);
                    items.Settle(world);
                }

                Assert.That(world.GetBlock(Piece).IsAir, Is.True);
                Assert.That(world.GetBlock(Stone).IsAir, Is.True);
                Assert.That(world.Surface.IsNatural(4, 4), Is.True);
                Assert.That(items.Items.Sum(i => i.Drop.Count), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CampaignMap_SlopeShapesFitTheChunkPalette()
        {
            // Куски склонов — неполные блоки со своими углами: в чанке их разных форм не больше, чем мест в палитре.
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(CampaignMapTests.TerrainPath);
            var world = TerrainGenerator.GenerateCampaign(settings.Map.ToLayout(), settings.Seed, settings.SizeInChunks, out _);
            var size = world.SizeInChunks;
            var most = 0;
            for (var y = 0; y < size.y; y++)
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                most = Mathf.Max(most, world.GetChunk(new Vector3Int(x, y, z)).SlopeShapeCount);

            Assert.That(most, Is.LessThan(ChunkData.MaxSlopeShapes));
        }

        private static ChunkMeshData Build(VoxelWorld world)
        {
            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);
            return mesh;
        }

        /// <summary>Высоты вершин всех треугольников, которые принадлежат блоку.</summary>
        private static List<float> Heights(ChunkMeshData mesh, Vector3Int block)
        {
            var index = ChunkData.Index(block.x, block.y, block.z);
            var heights = new List<float>();
            for (var m = 0; m < mesh.Triangles.Length; m++)
            for (var t = 0; t < mesh.TriangleBlocks[m].Count; t++)
            {
                if (mesh.TriangleBlocks[m][t] != index)
                    continue;
                for (var k = 0; k < 3; k++)
                    heights.Add(mesh.Vertices[mesh.Triangles[m][t * 3 + k]].y);
            }
            return heights;
        }

        private static int OwnedTriangles(ChunkMeshData mesh, byte material, Vector3Int block) =>
            mesh.TriangleBlocks[material].Count(i => i == ChunkData.Index(block.x, block.y, block.z));

        /// <summary>Площадь проекции на XZ всех треугольников, смотрящих вверх: у сплошной земли — площадь чанка.</summary>
        public static float UpwardArea(ChunkMeshData mesh)
        {
            var area = 0f;
            foreach (var list in mesh.Triangles)
            {
                for (var i = 0; i < list.Count; i += 3)
                {
                    Vector3 a = mesh.Vertices[list[i]], b = mesh.Vertices[list[i + 1]], c = mesh.Vertices[list[i + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    if (cross.y > 1e-6f)
                        area += cross.y * 0.5f;
                }
            }
            return area;
        }

        /// <summary>Стенки не торчат над краем поверхности (серые треугольники): каждая вершина стенки не выше него.</summary>
        private static void AssertWallsUnderSurface(VoxelWorld world, ChunkMeshData mesh)
        {
            var surface = world.Surface;
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                if (Mathf.Abs(mesh.Normals[i].y) > 1e-4f)
                    continue;
                var v = mesh.Vertices[i];
                float edge;
                if (Mathf.Abs(mesh.Normals[i].x) > 0.5f)
                {
                    int x = Mathf.RoundToInt(v.x), z = Mathf.Min(Mathf.FloorToInt(v.z), 15);
                    edge = Mathf.Lerp(surface.Corner(x, z), surface.Corner(x, z + 1), v.z - z);
                }
                else
                {
                    int z = Mathf.RoundToInt(v.z), x = Mathf.Min(Mathf.FloorToInt(v.x), 15);
                    edge = Mathf.Lerp(surface.Corner(x, z), surface.Corner(x + 1, z), v.x - x);
                }
                Assert.That(v.y, Is.LessThanOrEqualTo(edge / H + 1e-3f), $"вершина стенки {v}");
            }
        }

        private sealed class StoneDrops : IBlockDrops
        {
            public bool TryGetDrop(byte material, out ItemDrop drop)
            {
                drop = material == BlockMaterials.Stone ? new ItemDrop("stone", 1) : default;
                return material == BlockMaterials.Stone;
            }
        }
    }
}
