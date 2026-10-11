using System.Linq;
using Emberhold.World.Editing;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockFormTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void Stairs_IsCatalogFormWithVolumeAsFill()
        {
            var stairs = BlockShape.Catalog(BlockForm.Stairs, 0);

            Assert.That(stairs.IsCatalog, Is.True);
            Assert.That(stairs.IsFlat, Is.False);
            Assert.That(stairs.IsFull, Is.False);
            Assert.That(stairs.IsEmpty, Is.False);
            Assert.That(stairs.Top, Is.EqualTo(16));
            // 4 ступени: 16, 12, 8 и 4 шестнадцатых глубины на высоту 4 → 160 / 256.
            Assert.That(stairs.Fill, Is.EqualTo(0.625f).Within(Eps));
            Assert.That(new Block(BlockMaterials.Stone, stairs).IsAir, Is.False);
        }

        [Test]
        public void PackUnpack_KeepsFormAndRotation()
        {
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var stairs = BlockShape.Catalog(BlockForm.Stairs, rotation);
                Assert.That(BlockShape.Unpack(stairs.Pack()), Is.EqualTo(stairs));
                Assert.That(BlockShape.Unpack(stairs.Pack()).Rotation, Is.EqualTo(rotation));
            }
            Assert.That(BlockShape.Catalog(BlockForm.Stairs, 1), Is.Not.EqualTo(BlockShape.Catalog(BlockForm.Stairs, 2)));
        }

        [Test]
        public void RotatedClockwise_FourTimes_IsIdentity()
        {
            var ramp = new BlockShape(0, 0, 16, 16);
            var stairs = BlockShape.Catalog(BlockForm.Stairs, 0);

            Assert.That(ramp.RotatedClockwise().RotatedClockwise().RotatedClockwise().RotatedClockwise(), Is.EqualTo(ramp));
            Assert.That(stairs.RotatedClockwise().RotatedClockwise().RotatedClockwise().RotatedClockwise(), Is.EqualTo(stairs));
        }

        [Test]
        public void RotatedClockwise_RampRisingToPlusZ_RisesToMinusX()
        {
            // (x, z) → (1 − z, x): высокий край z = 1 переходит на x = 0.
            var rotated = new BlockShape(0, 0, 16, 16).RotatedClockwise();

            Assert.That(rotated, Is.EqualTo(new BlockShape(16, 0, 16, 0)));
        }

        [Test]
        public void FormBox_RotatesAroundCellCentre()
        {
            var box = new FormBox(0, 0, 0, 16, 4, 4);

            Assert.That(Bounds(box.Rotated(1)), Is.EqualTo(Bounds(new FormBox(12, 0, 0, 16, 4, 16))));
            Assert.That(Bounds(box.Rotated(2)), Is.EqualTo(Bounds(new FormBox(0, 0, 12, 16, 4, 16))));
            Assert.That(Bounds(box.Rotated(4)), Is.EqualTo(Bounds(box)));
        }

        [Test]
        public void ChunkData_StoresStairsInEveryRotation()
        {
            var chunk = new ChunkData();
            for (var rotation = 0; rotation < 4; rotation++)
                chunk.Set(rotation, 0, 0, new Block(BlockMaterials.Wood, BlockShape.Catalog(BlockForm.Stairs, rotation)));

            for (var rotation = 0; rotation < 4; rotation++)
            {
                var block = chunk.Get(rotation, 0, 0);
                Assert.That(block.Material, Is.EqualTo(BlockMaterials.Wood));
                Assert.That(block.Shape, Is.EqualTo(BlockShape.Catalog(BlockForm.Stairs, rotation)));
            }
        }

        [Test]
        public void Damage_StairsBreakByTheirVolume()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(2, 2, 2);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.Stairs, 0)));
            var damage = new BlockDamage(new UniformBlockDurability(100f));

            // 62,5 ХП: два удара по 25 не хватает, третий ломает.
            Assert.That(damage.Hit(world, cell, 25f), Is.False);
            Assert.That(damage.Hit(world, cell, 25f), Is.False);
            Assert.That(damage.Hit(world, cell, 25f), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void StairsMesh_FacesOutwardAndStaysInsideCell(int rotation)
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(5, 5, 5);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.Stairs, rotation)));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            var tris = mesh.Triangles[BlockMaterials.Stone];
            Assert.That(tris, Is.Not.Empty);
            for (var i = 0; i < tris.Count; i += 3)
            {
                var a = mesh.Vertices[tris[i]];
                var b = mesh.Vertices[tris[i + 1]];
                var c = mesh.Vertices[tris[i + 2]];
                var faceNormal = Vector3.Cross(b - a, c - a).normalized;
                Assert.That(Vector3.Dot(faceNormal, mesh.Normals[tris[i]]), Is.EqualTo(1f).Within(1e-4f));
                foreach (var v in new[] { a, b, c })
                {
                    var local = v - (Vector3)cell;
                    Assert.That(local.x, Is.InRange(-Eps, 1f + Eps));
                    Assert.That(local.y, Is.InRange(-Eps, 1f + Eps));
                    Assert.That(local.z, Is.InRange(-Eps, 1f + Eps));
                }
            }
            // 4 проступи сверху и 4 подступёнка спереди.
            Assert.That(mesh.Normals.Count(n => n == Vector3.up), Is.EqualTo(16));
        }

        [Test]
        public void StairsMesh_BackAgainstFullBlock_HidesBackFace()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(5, 5, 5);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.Stairs, 0)));
            world.SetBlock(cell + Vector3Int.forward, Block.Full(BlockMaterials.Dirt));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            // Задняя стенка лестницы (+Z) закрыта полным блоком.
            var stoneNormals = mesh.Triangles[BlockMaterials.Stone].Select(i => mesh.Normals[i]);
            Assert.That(stoneNormals, Has.None.EqualTo(Vector3.forward));
            // Сосед рисует к лестнице только часть стены выше её нижней ступени (4/16).
            var dirtBack = Enumerable.Range(0, mesh.Vertices.Count)
                .Where(i => mesh.Normals[i] == Vector3.back && Mathf.Abs(mesh.Vertices[i].z - 6f) < Eps).ToList();
            Assert.That(dirtBack.Select(i => mesh.Vertices[i].y).Min(), Is.EqualTo(5.25f).Within(Eps));
        }

        [Test]
        public void TriangleBlocks_PointToStairsCell()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(3, 4, 7);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.Stairs, 2)));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            Assert.That(mesh.TriangleBlocks[BlockMaterials.Stone].Select(ChunkData.Position), Has.All.EqualTo(cell));
        }

        private static (int, int, int, int, int, int) Bounds(FormBox b) => (b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1);
    }
}
