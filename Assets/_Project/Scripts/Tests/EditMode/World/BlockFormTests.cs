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
        public void RotatedQuarter_FourTimes_IsIdentity()
        {
            var ramp = new BlockShape(0, 0, 16, 16);
            var stairs = BlockShape.Catalog(BlockForm.Stairs, 0);

            Assert.That(ramp.RotatedQuarter().RotatedQuarter().RotatedQuarter().RotatedQuarter(), Is.EqualTo(ramp));
            Assert.That(stairs.RotatedQuarter().RotatedQuarter().RotatedQuarter().RotatedQuarter(), Is.EqualTo(stairs));
        }

        [Test]
        public void RotatedQuarter_RampRisingToPlusZ_RisesToMinusX()
        {
            // (x, z) → (1 − z, x): высокий край z = 1 переходит на x = 0.
            var rotated = new BlockShape(0, 0, 16, 16).RotatedQuarter();

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

        private static readonly BlockForm[] StoneForms =
        {
            BlockForm.SquarePillar, BlockForm.RoundPillar, BlockForm.Beam, BlockForm.Bracket, BlockForm.HalfArch,
        };

        [Test]
        public void StoneForms_KeepFormAndRotationInPackingAndChunk()
        {
            var chunk = new ChunkData();
            for (var f = 0; f < StoneForms.Length; f++)
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var shape = BlockShape.Catalog(StoneForms[f], rotation);
                Assert.That(BlockShape.Unpack(shape.Pack()), Is.EqualTo(shape));
                Assert.That(shape.RotatedQuarter().RotatedQuarter().RotatedQuarter().RotatedQuarter(), Is.EqualTo(shape));
                chunk.Set(f, 0, rotation, new Block(BlockMaterials.Stone, shape));
            }

            for (var f = 0; f < StoneForms.Length; f++)
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var stored = chunk.Get(f, 0, rotation).Shape;
                Assert.That(stored.Form, Is.EqualTo(StoneForms[f]));
                Assert.That(stored.Rotation, Is.EqualTo(rotation));
            }
        }

        [Test]
        public void EveryForm_FitsIntoPacking()
        {
            foreach (BlockForm form in System.Enum.GetValues(typeof(BlockForm)))
                Assert.That((int)form, Is.LessThanOrEqualTo(BlockShape.MaxForm));
        }

        [TestCase(BlockForm.SquarePillar, 8f * 8 * 16)]
        [TestCase(BlockForm.Beam, 8f * 8 * 16)]
        // Круг радиусом 5 — 24-угольник: 12 · 25 · sin 15°, на высоту 16.
        [TestCase(BlockForm.RoundPillar, 77.6457f * 16)]
        // Трапеция в профиле: высота 16 у стены и 4 у губы, длина 16, ширина 8.
        [TestCase(BlockForm.Bracket, (16f + 4f) / 2 * 16 * 8)]
        // Клетка без четверти круга радиусом 12 (12 отрезков: 6 · 144 · sin 7,5°), на ширину 16.
        [TestCase(BlockForm.HalfArch, (256f - 112.7745f) * 16)]
        public void StoneForm_FillIsItsVolume(BlockForm form, float volume)
        {
            var shape = BlockShape.Catalog(form, 1);

            Assert.That(shape.Fill, Is.EqualTo(volume / 4096f).Within(1e-3f));
            Assert.That(shape.Top, Is.EqualTo(16));
            Assert.That(shape.CoversFloor, Is.False);
        }

        [Test]
        public void Damage_PillarBreaksByItsVolume()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(2, 2, 2);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.SquarePillar, 0)));
            var damage = new BlockDamage(new UniformBlockDurability(100f));

            // Четверть ячейки — 25 ХП, ломается с одного удара.
            Assert.That(damage.Hit(world, cell, 25f), Is.True);
        }

        [Test]
        public void Extent_FollowsRotation()
        {
            Assert.That(Bounds(BlockShape.Catalog(BlockForm.Beam, 0).Extent), Is.EqualTo((4, 8, 0, 12, 16, 16)));
            Assert.That(Bounds(BlockShape.Catalog(BlockForm.Beam, 1).Extent), Is.EqualTo((0, 8, 4, 16, 16, 12)));
            Assert.That(Bounds(BlockShape.Catalog(BlockForm.RoundPillar, 3).Extent), Is.EqualTo((3, 0, 3, 13, 16, 13)));
            Assert.That(Bounds(BlockShape.Catalog(BlockForm.Bracket, 2).Extent), Is.EqualTo((4, 0, 0, 12, 16, 16)));
            Assert.That(Bounds(BlockShape.Flat(6).Extent), Is.EqualTo((0, 0, 0, 16, 6, 16)));
        }

        [Test]
        public void StoneFormMeshes_FaceOutwardAndStayInsideCell(
            [ValueSource(nameof(StoneForms))] BlockForm form, [Values(0, 1, 2, 3)] int rotation)
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(5, 5, 5);
            world.SetBlock(cell, new Block(BlockMaterials.Stone, BlockShape.Catalog(form, rotation)));

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
                // Гладкие стенки получают общую нормаль в вершине, но она смотрит в ту же сторону, что и грань.
                for (var k = 0; k < 3; k++)
                    Assert.That(Vector3.Dot(faceNormal, mesh.Normals[tris[i + k]]), Is.GreaterThan(0.9f));
                foreach (var v in new[] { a, b, c })
                {
                    var local = v - (Vector3)cell;
                    Assert.That(local.x, Is.InRange(-Eps, 1f + Eps));
                    Assert.That(local.y, Is.InRange(-Eps, 1f + Eps));
                    Assert.That(local.z, Is.InRange(-Eps, 1f + Eps));
                }
            }
            Assert.That(mesh.TriangleBlocks[BlockMaterials.Stone].Select(ChunkData.Position), Has.All.EqualTo(cell));
        }

        [Test]
        public void StoneFormMesh_EnclosesItsVolume(
            [ValueSource(nameof(StoneForms))] BlockForm form, [Values(0, 1, 2, 3)] int rotation)
        {
            var world = new VoxelWorld(Vector3Int.one);
            var shape = BlockShape.Catalog(form, rotation);
            world.SetBlock(new Vector3Int(5, 5, 5), new Block(BlockMaterials.Stone, shape));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            // Объём замкнутой поверхности по теореме о дивергенции: сумма (a · (b × c)) / 6 по треугольникам.
            // Обход Unity — по часовой стрелке снаружи, поэтому знак обратный.
            var tris = mesh.Triangles[BlockMaterials.Stone];
            var volume = 0f;
            for (var i = 0; i < tris.Count; i += 3)
            {
                var a = mesh.Vertices[tris[i]];
                var b = mesh.Vertices[tris[i + 1]];
                var c = mesh.Vertices[tris[i + 2]];
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
            }
            Assert.That(Mathf.Abs(volume), Is.EqualTo(shape.Fill).Within(1e-3f));
        }

        [Test]
        public void FullBlockUnderPillar_KeepsItsTop()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var cell = new Vector3Int(5, 5, 5);
            world.SetBlock(cell, Block.Full(BlockMaterials.Dirt));
            world.SetBlock(cell + Vector3Int.up, new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.RoundPillar, 0)));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            var dirtTop = mesh.Triangles[BlockMaterials.Dirt].Where(i => mesh.Normals[i] == Vector3.up).ToList();
            Assert.That(dirtTop, Is.Not.Empty);
            // А низ столба на полном блоке не рисуется.
            var stoneBottom = mesh.Triangles[BlockMaterials.Stone].Where(i => mesh.Normals[i] == Vector3.down);
            Assert.That(stoneBottom, Is.Empty);
        }

        [Test]
        public void TwoHalfArchesFacingEachOther_MakeArchTwoBlocksWide()
        {
            var world = new VoxelWorld(Vector3Int.one);
            // Нога первой полуарки на +Z (поворот 0), второй — на −Z (поворот 2): замки встречаются на z = 5.
            world.SetBlock(new Vector3Int(5, 5, 5), new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.HalfArch, 0)));
            world.SetBlock(new Vector3Int(5, 5, 4), new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.HalfArch, 2)));

            var mesh = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, mesh);

            var stone = mesh.Triangles[BlockMaterials.Stone].Select(i => mesh.Vertices[i]).ToList();
            float Lowest(float z) => stone.Where(v => Mathf.Abs(v.z - z) < Eps).Min(v => v.y);
            // Ноги опускаются до пола по краям арки, посередине над проёмом остаётся замок.
            Assert.That(Lowest(6f), Is.EqualTo(5f).Within(Eps));
            Assert.That(Lowest(4f), Is.EqualTo(5f).Within(Eps));
            Assert.That(Lowest(5f), Is.EqualTo(5f + BlockForms.ArchRadius / 16f).Within(Eps));
            // Изгиб гладкий: на дуге есть наклонные нормали.
            Assert.That(mesh.Normals.Count(n => Mathf.Abs(n.y) > 0.3f && Mathf.Abs(n.z) > 0.3f), Is.GreaterThan(0));
        }

        [Test]
        public void PillarHoldsFloorAndDropsItWhenRemoved()
        {
            var world = new VoxelWorld(Vector3Int.one);
            var pillar = new Block(BlockMaterials.Stone, BlockShape.Catalog(BlockForm.RoundPillar, 0));
            var beam = new Block(BlockMaterials.Wood, BlockShape.Catalog(BlockForm.Beam, 1));
            for (var y = 0; y < 3; y++)
                world.SetBlock(new Vector3Int(2, y, 2), pillar);
            // Пол на балке поверх столба и вынос за его край.
            world.SetBlock(new Vector3Int(2, 3, 2), beam);
            world.SetBlock(new Vector3Int(3, 3, 2), beam);
            world.SetBlock(new Vector3Int(3, 4, 2), Block.Full(BlockMaterials.Stone));
            var stability = new BlockStability();

            Assert.That(stability.Settle(world, new Vector3Int(3, 4, 2)), Is.Zero);

            world.SetBlock(new Vector3Int(2, 1, 2), Block.Air);
            Assert.That(stability.Settle(world, new Vector3Int(2, 1, 2)), Is.GreaterThan(0));
            Assert.That(world.GetBlock(new Vector3Int(3, 4, 2)).IsAir, Is.True);
        }

        private static (int, int, int, int, int, int) Bounds(FormBox b) => (b.X0, b.Y0, b.Z0, b.X1, b.Y1, b.Z1);
    }
}
