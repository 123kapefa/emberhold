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
                (new Vector3Int(5, 6, 5), Block.Full(BlockMaterials.Stone)));

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
    }
}
