using System.Linq;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class ChunkMesherTests
    {
        private const float Eps = 1e-5f;

        private static ChunkMeshData Mesh(VoxelWorld world, Vector3Int chunk)
        {
            var data = new ChunkMeshData();
            ChunkMesher.Build(world, chunk, data);
            return data;
        }

        private static VoxelWorld WorldWith(params (Vector3Int pos, Block block)[] blocks)
        {
            var world = new VoxelWorld(new Vector3Int(2, 2, 2));
            foreach (var (pos, block) in blocks)
                world.SetBlock(pos, block);
            return world;
        }

        [Test]
        public void EmptyChunk_HasNoFaces()
        {
            var world = new VoxelWorld(Vector3Int.one);

            Assert.That(Mesh(world, Vector3Int.zero).IsEmpty, Is.True);
        }

        [Test]
        public void SingleFullBlockInAir_HasSixFaces()
        {
            var world = WorldWith((new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone)));

            var mesh = Mesh(world, Vector3Int.zero);

            Assert.That(mesh.TriangleCount, Is.EqualTo(12));
            Assert.That(mesh.Triangles[BlockMaterials.Stone].Count, Is.EqualTo(36));
        }

        [Test]
        public void BlockOnWorldFloor_HasNoBottomFace()
        {
            var world = WorldWith((new Vector3Int(5, 0, 5), Block.Full(BlockMaterials.Stone)));

            var mesh = Mesh(world, Vector3Int.zero);

            Assert.That(mesh.TriangleCount, Is.EqualTo(10));
            Assert.That(mesh.Normals, Has.None.EqualTo(Vector3.down));
        }

        [Test]
        public void TwoFullBlocksSideBySide_HideSharedFaces()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(6, 5, 5), Block.Full(BlockMaterials.Stone)));

            Assert.That(Mesh(world, Vector3Int.zero).TriangleCount, Is.EqualTo(20));
        }

        [Test]
        public void FullBlockUnderPartialBlock_HidesTopFace()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(5, 6, 5), Block.Flat(BlockMaterials.Grass, 4)));

            var mesh = Mesh(world, Vector3Int.zero);

            // Верх полного блока и низ неполного закрыты: 5 + 5 граней.
            Assert.That(mesh.TriangleCount, Is.EqualTo(20));
        }

        [Test]
        public void PartialBlockUnderBlock_KeepsTopFaceAndNeighbourBottom()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Flat(BlockMaterials.Dirt, 8)),
                (new Vector3Int(5, 6, 5), Block.Full(BlockMaterials.Stone)));

            // Между ними щель: обе грани видны.
            Assert.That(Mesh(world, Vector3Int.zero).TriangleCount, Is.EqualTo(24));
        }

        [Test]
        public void PartialFlatBlock_TopFaceAtItsHeight()
        {
            var world = WorldWith((new Vector3Int(2, 3, 4), Block.Flat(BlockMaterials.Grass, 12)));

            var mesh = Mesh(world, Vector3Int.zero);

            var topVertices = mesh.Vertices.Where((_, i) => mesh.Normals[i] == Vector3.up).ToList();
            Assert.That(topVertices, Has.Count.EqualTo(4));
            Assert.That(topVertices.Select(v => v.y), Has.All.EqualTo(3.75f).Within(Eps));
            Assert.That(mesh.Vertices.Max(v => v.y), Is.EqualTo(3.75f).Within(Eps));
        }

        [Test]
        public void LowerNeighbour_ShowsOnlyExposedPartOfSide()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Flat(BlockMaterials.Grass, 12)),
                (new Vector3Int(6, 5, 5), Block.Flat(BlockMaterials.Grass, 4)));

            var mesh = Mesh(world, Vector3Int.zero);

            var shared = mesh.Vertices.Where((v, i) => mesh.Normals[i] == Vector3.right && Mathf.Abs(v.x - 6f) < Eps).ToList();
            Assert.That(shared, Has.Count.EqualTo(4));
            Assert.That(shared.Min(v => v.y), Is.EqualTo(5.25f).Within(Eps));
            Assert.That(shared.Max(v => v.y), Is.EqualTo(5.75f).Within(Eps));
            // Низкий сосед к высокому блоку грань не рисует.
            Assert.That(mesh.Normals.Count(n => n == Vector3.left), Is.EqualTo(4));
        }

        [Test]
        public void BlockOnChunkBorder_UsesNeighbourChunk()
        {
            var world = WorldWith(
                (new Vector3Int(15, 5, 5), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(16, 5, 5), Block.Full(BlockMaterials.Stone)));

            Assert.That(Mesh(world, Vector3Int.zero).TriangleCount, Is.EqualTo(10));
            Assert.That(Mesh(world, Vector3Int.right).TriangleCount, Is.EqualTo(10));
        }

        [Test]
        public void VerticesAreInChunkLocalCoordinates()
        {
            var world = WorldWith((new Vector3Int(17, 18, 19), Block.Full(BlockMaterials.Stone)));

            var mesh = Mesh(world, Vector3Int.one);

            Assert.That(mesh.Vertices.Min(v => v.x), Is.EqualTo(1f).Within(Eps));
            Assert.That(mesh.Vertices.Max(v => v.y), Is.EqualTo(3f).Within(Eps));
        }

        [Test]
        public void AllTriangles_FaceOutwardAlongTheirNormal()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Flat(BlockMaterials.Grass, 12)),
                (new Vector3Int(6, 5, 5), Block.Flat(BlockMaterials.Dirt, 4)),
                (new Vector3Int(5, 6, 5), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(9, 5, 5), Slope(16, 16, 16, 0)),
                (new Vector3Int(10, 5, 5), Slope(0, 16, 4, 8)),
                (new Vector3Int(9, 5, 6), Slope(12, 0, 16, 4)));

            var mesh = Mesh(world, Vector3Int.zero);

            foreach (var tris in mesh.Triangles)
            {
                for (var i = 0; i < tris.Count; i += 3)
                {
                    var a = mesh.Vertices[tris[i]];
                    var b = mesh.Vertices[tris[i + 1]];
                    var c = mesh.Vertices[tris[i + 2]];
                    // В Unity лицевая сторона — обход по часовой, и тогда Cross(b - a, c - a) смотрит наружу.
                    var faceNormal = Vector3.Cross(b - a, c - a).normalized;
                    Assert.That(Vector3.Dot(faceNormal, mesh.Normals[tris[i]]), Is.EqualTo(1f).Within(1e-4f));
                }
            }
        }

        [Test]
        public void MaterialsGoToSeparateTriangleLists()
        {
            var world = WorldWith(
                (new Vector3Int(1, 1, 1), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(8, 8, 8), Block.Full(BlockMaterials.Grass)));

            var mesh = Mesh(world, Vector3Int.zero);

            Assert.That(mesh.Triangles[BlockMaterials.Stone].Count, Is.EqualTo(36));
            Assert.That(mesh.Triangles[BlockMaterials.Grass].Count, Is.EqualTo(36));
            Assert.That(mesh.Triangles[BlockMaterials.Dirt], Is.Empty);
        }
        private static Block Slope(int c00, int c10, int c01, int c11) =>
            new(BlockMaterials.Dirt, new BlockShape(c00, c10, c01, c11));

        private static Vector3[] TopNormals(ChunkMeshData mesh) =>
            mesh.Normals.Where(n => n.y > 0.01f).Distinct().ToArray();

        [Test]
        public void PlanarSlope_TopIsOneTiltedQuad()
        {
            // Высокий край на z = 0, низкий на z = 1: склон смотрит вверх и вперёд.
            var world = WorldWith((new Vector3Int(5, 5, 5), Slope(16, 16, 0, 0)));

            var mesh = Mesh(world, Vector3Int.zero);

            var normals = TopNormals(mesh);
            Assert.That(normals, Has.Length.EqualTo(1));
            Assert.That(normals[0].z, Is.GreaterThan(0.5f));
            Assert.That(mesh.Normals.Count(n => n == normals[0]), Is.EqualTo(4));
        }

        [Test]
        public void ThreeCornersUp_SplitsAlongDiagonalWithoutFold()
        {
            // Низкий только угол (1,1): одна половина ровная, вторая наклонная.
            var world = WorldWith((new Vector3Int(5, 5, 5), Slope(16, 16, 16, 0)));

            var mesh = Mesh(world, Vector3Int.zero);

            var normals = TopNormals(mesh);
            Assert.That(normals, Has.Length.EqualTo(2));
            Assert.That(normals, Has.Some.EqualTo(Vector3.up));
            var top = mesh.Vertices.Where((_, i) => mesh.Normals[i] == Vector3.up).ToList();
            Assert.That(top.Select(v => v.y), Has.All.EqualTo(6f).Within(Eps));
        }

        [Test]
        public void SlopeInAir_SideUnderLowEdgeIsTriangleAndZeroEdgeHasNoSide()
        {
            var world = WorldWith((new Vector3Int(5, 5, 5), Slope(16, 16, 0, 0)));

            var mesh = Mesh(world, Vector3Int.zero);

            // Сторона +Z: оба угла на высоте 0 — грани нет. Стороны ±X: треугольники. Сторона −Z: полная.
            Assert.That(mesh.Normals.Count(n => n == Vector3.forward), Is.Zero);
            Assert.That(mesh.Normals.Count(n => n == Vector3.right), Is.EqualTo(3));
            Assert.That(mesh.Normals.Count(n => n == Vector3.left), Is.EqualTo(3));
            Assert.That(mesh.Normals.Count(n => n == Vector3.back), Is.EqualTo(4));
        }

        [Test]
        public void MatchingSlopesSideBySide_HaveNoFacesBetweenThem()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Slope(16, 16, 0, 0)),
                (new Vector3Int(6, 5, 5), Slope(16, 16, 0, 0)));

            var mesh = Mesh(world, Vector3Int.zero);

            var between = mesh.Vertices.Where((v, i) => Mathf.Abs(v.x - 6f) < Eps
                && (mesh.Normals[i] == Vector3.right || mesh.Normals[i] == Vector3.left));
            Assert.That(between, Is.Empty);
        }

        [Test]
        public void CrossingEdges_EachSideShowsItsUpperTriangle()
        {
            // На общем ребре x = 6 наш край идёт 16 → 0, край соседа 0 → 16: каждый виден до середины.
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Slope(16, 16, 0, 0)),
                (new Vector3Int(6, 5, 5), Slope(0, 16, 16, 16)));

            var mesh = Mesh(world, Vector3Int.zero);

            var ours = mesh.Vertices.Where((v, i) => mesh.Normals[i] == Vector3.right && Mathf.Abs(v.x - 6f) < Eps).ToList();
            var theirs = mesh.Vertices.Where((v, i) => mesh.Normals[i] == Vector3.left && Mathf.Abs(v.x - 6f) < Eps).ToList();
            Assert.That(ours, Has.Count.EqualTo(3));
            Assert.That(theirs, Has.Count.EqualTo(3));
            Assert.That(ours.Max(v => v.z), Is.EqualTo(5.5f).Within(Eps));
            Assert.That(theirs.Min(v => v.z), Is.EqualTo(5.5f).Within(Eps));
        }

        [Test]
        public void SlopeWithFullCorner_UnderBlock_KeepsTheGapVisible()
        {
            // У склона верх 16, но он не полный: низ блока сверху виден в щели.
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Slope(16, 16, 16, 8)),
                (new Vector3Int(5, 6, 5), Block.Full(BlockMaterials.Stone)));

            var mesh = Mesh(world, Vector3Int.zero);

            // Низ склона — два треугольника (6 вершин) под его разбиением, низ блока над ним — четырёхугольник.
            Assert.That(mesh.Normals.Count(n => n == Vector3.down), Is.EqualTo(10), "Низ склона и низ блока над ним");
            // Ровная половина склона и верх блока смотрят вверх, вторая половина склона наклонная.
            Assert.That(TopNormals(mesh), Has.Length.EqualTo(2));
        }

        [Test]
        public void TriangleBlocks_PointToTheirBlocks()
        {
            var a = new Vector3Int(2, 3, 4);
            var b = new Vector3Int(9, 3, 4);
            var world = WorldWith((a, Block.Full(BlockMaterials.Stone)), (b, Slope(16, 16, 16, 0)));

            var mesh = Mesh(world, Vector3Int.zero);

            for (var material = 0; material < mesh.Triangles.Length; material++)
            {
                var tris = mesh.Triangles[material];
                var blocks = mesh.TriangleBlocks[material];
                Assert.That(blocks, Has.Count.EqualTo(tris.Count / 3));
                for (var t = 0; t < blocks.Count; t++)
                {
                    var expected = material == BlockMaterials.Stone ? a : b;
                    Assert.That(ChunkData.Position(blocks[t]), Is.EqualTo(expected));
                    // Все вершины треугольника лежат в ячейке своего блока.
                    for (var k = 0; k < 3; k++)
                    {
                        var v = mesh.Vertices[tris[t * 3 + k]] - (Vector3)expected;
                        Assert.That(v.x, Is.InRange(-Eps, 1f + Eps));
                        Assert.That(v.y, Is.InRange(-Eps, 1f + Eps));
                        Assert.That(v.z, Is.InRange(-Eps, 1f + Eps));
                    }
                }
            }
        }

        [Test]
        public void SmoothNormals_OnEvenSlope_MatchFaceAndOnFlatPointUp()
        {
            // Ровный склон: 4 столбца подряд, каждый поднимается на 4/16 по X.
            var world = new VoxelWorld(new Vector3Int(1, 1, 1));
            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                var h = 4 + x / 4 * 4;
                var rise = x % 4 == 3 ? 4 : 0;
                world.SetBlock(new Vector3Int(x, 0, z), new Block(BlockMaterials.Grass, new BlockShape(h, h + rise, h, h + rise)));
            }

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh, new SurfaceNormals());

            // Внутри площадки нормали верха наклонены вдоль −X только там, где есть подъём рядом, и все смотрят вверх.
            var tops = mesh.Normals.Where(n => n.y > 0.1f).ToList();
            Assert.That(tops, Is.Not.Empty);
            Assert.That(tops.Select(n => n.magnitude), Has.All.EqualTo(1f).Within(1e-4f));
            Assert.That(tops.Select(n => n.x), Has.All.LessThanOrEqualTo(1e-4f));
            Assert.That(tops.Select(n => Mathf.Abs(n.z)), Has.All.LessThan(1e-4f));
            Assert.That(tops.Count(n => n.x < -1e-3f), Is.GreaterThan(0));
        }

        [Test]
        public void SmoothNormals_SharedCornerGetsSameNormalInBothBlocks()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Slope(8, 12, 8, 12)),
                (new Vector3Int(6, 5, 5), Slope(12, 16, 12, 16)));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh, new SurfaceNormals());

            // Угол (6, 5.75, 5) общий у двух блоков: нормаль в нём одна и та же.
            var shared = Enumerable.Range(0, mesh.Vertices.Count)
                .Where(i => (mesh.Vertices[i] - new Vector3(6f, 5.75f, 5f)).sqrMagnitude < 1e-6f && mesh.Normals[i].y > 0.1f)
                .Select(i => mesh.Normals[i]).Distinct().ToList();
            Assert.That(shared, Has.Count.EqualTo(1));
        }

        [Test]
        public void OuterCorner_DoesNotDrawFlatHalfOnTheFloor()
        {
            // Внешний угол ската: высокий только угол (1, 1). Половина с нулевыми углами — без объёма.
            var world = WorldWith((new Vector3Int(5, 5, 5), Slope(0, 0, 0, 16)));

            var mesh = Mesh(world, Vector3Int.zero);

            // Ни одной грани на полу ячейки, кроме низа под настоящим объёмом: один треугольник снизу.
            var flatOnFloor = 0;
            foreach (var tris in mesh.Triangles)
                for (var i = 0; i < tris.Count; i += 3)
                    if (mesh.Normals[tris[i]].y > 0.5f && Enumerable.Range(0, 3).All(k => Mathf.Abs(mesh.Vertices[tris[i + k]].y - 5f) < Eps))
                        flatOnFloor++;
            Assert.That(flatOnFloor, Is.Zero, "Плоская половина верха на полу");
            Assert.That(mesh.Normals.Count(n => n == Vector3.down), Is.EqualTo(3), "Низ — один треугольник под скатом");
            foreach (var tris in mesh.Triangles)
            {
                for (var i = 0; i < tris.Count; i += 3)
                {
                    var a = mesh.Vertices[tris[i]];
                    var b = mesh.Vertices[tris[i + 1]];
                    var c = mesh.Vertices[tris[i + 2]];
                    var faceNormal = Vector3.Cross(b - a, c - a).normalized;
                    Assert.That(Vector3.Dot(faceNormal, mesh.Normals[tris[i]]), Is.EqualTo(1f).Within(1e-4f));
                }
            }
        }

        [Test]
        public void FullBlockUnderOuterCorner_KeepsItsTop()
        {
            var world = WorldWith(
                (new Vector3Int(5, 5, 5), Block.Full(BlockMaterials.Stone)),
                (new Vector3Int(5, 6, 5), Slope(0, 0, 0, 16)));

            var mesh = Mesh(world, Vector3Int.zero);

            // Верх камня нужен: под открытой половиной внешнего угла его видно.
            var stoneTop = mesh.Triangles[BlockMaterials.Stone].Count(i => mesh.Normals[i] == Vector3.up);
            Assert.That(stoneTop, Is.EqualTo(6));
            Assert.That(Slope(0, 0, 0, 16).Shape.CoversFloor, Is.False);
            Assert.That(Slope(0, 16, 16, 16).Shape.CoversFloor, Is.True);
            Assert.That(Slope(0, 0, 16, 16).Shape.CoversFloor, Is.True);
        }
    }
}
