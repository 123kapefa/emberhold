using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class ValleyGeneratorTests
    {
        private static ValleyParameters SmallValley(int seed = 7)
        {
            var p = ValleyParameters.Default;
            p.Seed = seed;
            p.SizeInChunks = new Vector3Int(2, 2, 2);
            p.FloorHeight = 4f;
            p.RimHeight = 20f;
            return p;
        }

        /// <summary>Долина без террас и скал — проверяем форму рельефа и гладкость склонов.</summary>
        private static ValleyParameters PlainValley(ValleyParameters p)
        {
            p.TerraceHeight = 0f;
            p.CliffAmount = 0f;
            return p;
        }

        /// <summary>Высота поверхности столбца в шестнадцатых.</summary>
        private static int ColumnHeight(VoxelWorld world, int x, int z)
        {
            for (var y = world.SizeInBlocks.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (!block.IsAir)
                    return y * BlockShape.MaxHeight + block.Shape.Top;
            }
            return 0;
        }

        [Test]
        public void SameSeed_GivesSameWorld()
        {
            var a = ValleyGenerator.Generate(SmallValley());
            var b = ValleyGenerator.Generate(SmallValley());

            for (var z = 0; z < a.SizeInBlocks.z; z++)
            for (var x = 0; x < a.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(a, x, z), Is.EqualTo(ColumnHeight(b, x, z)), $"Столбец {x},{z}");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentWorlds()
        {
            var a = ValleyGenerator.Generate(SmallValley(1));
            var b = ValleyGenerator.Generate(SmallValley(2));

            var differ = 0;
            for (var z = 0; z < a.SizeInBlocks.z; z++)
            for (var x = 0; x < a.SizeInBlocks.x; x++)
                if (ColumnHeight(a, x, z) != ColumnHeight(b, x, z))
                    differ++;

            Assert.That(differ, Is.GreaterThan(0));
        }

        /// <summary>Абсолютная высота угла столбца: слои складываются, у каждого угол обрезан до своей ячейки.</summary>
        private static int CornerHeight(VoxelWorld world, int x, int z, int corner)
        {
            var total = 0;
            for (var y = 0; y < world.SizeInBlocks.y; y++)
            {
                var s = world.GetBlock(new Vector3Int(x, y, z)).Shape;
                total += corner switch { 0 => s.C00, 1 => s.C10, 2 => s.C01, _ => s.C11 };
            }
            return total;
        }

        [Test]
        public void Columns_AreFullUnderOneGrassSurfaceBlock()
        {
            var world = ValleyGenerator.Generate(SmallValley());

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
            {
                var height = ColumnHeight(world, x, z);
                Assert.That(height, Is.GreaterThan(0));
                var topY = (height - 1) / BlockShape.MaxHeight;
                Assert.That(world.GetBlock(new Vector3Int(x, topY, z)).Material, Is.EqualTo(BlockMaterials.Grass));
                for (var y = 0; y < topY; y++)
                {
                    var block = world.GetBlock(new Vector3Int(x, y, z));
                    Assert.That(block.Shape.IsFull, Is.True, $"Под поверхностью неполный блок {x},{y},{z}");
                    Assert.That(block.Material, Is.Not.EqualTo(BlockMaterials.Grass));
                }
            }
        }

        [Test]
        public void GentleSlope_NeighbourColumnsShareCornerHeights()
        {
            // Пологая долина: склон не круче шага высоты на блок, уступов быть не должно.
            var p = PlainValley(SmallValley());
            p.RimHeight = p.FloorHeight + 1f;
            p.NoiseAmplitude = 0f;
            p.MeanderAmplitude = 0f;
            var world = ValleyGenerator.Generate(p);
            var size = world.SizeInBlocks;

            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x - 1; x++)
            {
                Assert.That(CornerHeight(world, x, z, 1), Is.EqualTo(CornerHeight(world, x + 1, z, 0)), $"{x},{z} по X");
                Assert.That(CornerHeight(world, x, z, 3), Is.EqualTo(CornerHeight(world, x + 1, z, 2)), $"{x},{z} по X");
            }
            for (var z = 0; z < size.z - 1; z++)
            for (var x = 0; x < size.x; x++)
            {
                Assert.That(CornerHeight(world, x, z, 2), Is.EqualTo(CornerHeight(world, x, z + 1, 0)), $"{x},{z} по Z");
                Assert.That(CornerHeight(world, x, z, 3), Is.EqualTo(CornerHeight(world, x, z + 1, 1)), $"{x},{z} по Z");
            }
        }

        [Test]
        public void PlainValley_HasAlmostNoLedgesBetweenColumns()
        {
            // Без террас склоны не круче блока на блок укладываются в один слой без уступов.
            // Долина как в сцене Prototype: в маленькой тестовой склоны круче блока на блок.
            // Мягкий рельеф: прежние значения шума и ширины дна, без террас.
            var p = PlainValley(ValleyParameters.Default);
            p.NoiseAmplitude = 2.5f;
            p.NoiseScale = 24f;
            p.FloorHalfWidth = 0.12f;
            var world = ValleyGenerator.Generate(p);
            var (ledges, edges) = CountLedges(world);

            TestContext.WriteLine($"Уступов: {ledges} из {edges}");
            Assert.That(ledges, Is.LessThan(edges / 20));
        }

        [Test]
        public void TerracedValley_HasFlatLevelsSlopesAndCleanCliffs()
        {
            var world = ValleyGenerator.Generate(ValleyParameters.Default);
            var size = world.SizeInBlocks;

            int flat = 0, slopes = 0, columns = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var top = world.GetBlock(new Vector3Int(x, (ColumnHeight(world, x, z) - 1) / BlockShape.MaxHeight, z));
                columns++;
                if (top.Shape.IsFlat) flat++;
                else slopes++;
            }
            var (ledges, edges) = CountLedges(world);
            TestContext.WriteLine($"Ровных: {flat}, склонов: {slopes} из {columns}; стыков-обрывов: {ledges} из {edges}");

            Assert.That(flat, Is.GreaterThan(columns / 2), "Больше половины поверхности — ровные площадки");
            Assert.That(slopes, Is.GreaterThan(columns / 20), "Между уровнями есть склоны");
            Assert.That(ledges, Is.GreaterThan(0), "Есть скалы");
        }

        [Test]
        public void Terrace_FlatsThenSmoothSlope_AndCliffIsAStep()
        {
            // Уровни через 3 блока, склон занимает верхнюю половину перепада.
            Assert.That(ValleyGenerator.Terrace(3.2f, 3f, 0.5f, cliff: false), Is.EqualTo(3f).Within(1e-4f), "Ровная площадка");
            Assert.That(ValleyGenerator.Terrace(4.4f, 3f, 0.5f, cliff: false), Is.EqualTo(3f).Within(1e-4f), "Ещё ровная");
            Assert.That(ValleyGenerator.Terrace(5.25f, 3f, 0.5f, cliff: false), Is.EqualTo(4.5f).Within(1e-4f), "Середина склона");
            Assert.That(ValleyGenerator.Terrace(5.999f, 3f, 0.5f, cliff: false), Is.EqualTo(6f).Within(1e-2f), "Склон выходит на уровень");
            Assert.That(ValleyGenerator.Terrace(4.4f, 3f, 0.5f, cliff: true), Is.EqualTo(3f).Within(1e-4f), "Скала: нижний уровень");
            Assert.That(ValleyGenerator.Terrace(4.6f, 3f, 0.5f, cliff: true), Is.EqualTo(6f).Within(1e-4f), "Скала: сразу верхний");
            Assert.That(ValleyGenerator.Terrace(4.6f, 0f, 0.5f, cliff: true), Is.EqualTo(4.6f), "Без террас");
        }

        [Test]
        public void SnapSteepToLevels_TurnsSteepColumnIntoStepBetweenLevels()
        {
            // Столбец (0, 0) поднимается на 2,5 блока: такой склон в блок не уложить. Уровни через 3 блока.
            var corners = new[,] { { 48, 48, 48 }, { 88, 88, 56 }, { 88, 88, 56 } };

            CornerTerrain.SnapSteepToLevels(corners, levelSixteenths: 48);

            Assert.That(corners[1, 0], Is.EqualTo(96));
            Assert.That(corners[0, 0], Is.EqualTo(48));
            // Каждый столбец теперь либо укладывается в блок, либо все его углы на уровнях (станет ступенькой).
            for (var z = 0; z < 2; z++)
            for (var x = 0; x < 2; x++)
            {
                var c = new[] { corners[x, z], corners[x + 1, z], corners[x, z + 1], corners[x + 1, z + 1] };
                var steep = Mathf.Max(c) - Mathf.Min(c) > BlockShape.MaxHeight;
                if (steep)
                    Assert.That(c, Has.All.Matches<int>(v => v % 48 == 0), $"Столбец {x},{z}");
            }
        }

        private static (int ledges, int edges) CountLedges(VoxelWorld world)
        {
            var size = world.SizeInBlocks;
            int edges = 0, ledges = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x - 1; x++)
            {
                edges++;
                if (CornerHeight(world, x, z, 1) != CornerHeight(world, x + 1, z, 0)
                    || CornerHeight(world, x, z, 3) != CornerHeight(world, x + 1, z, 2))
                    ledges++;
            }
            return (ledges, edges);
        }

        [Test]
        public void ValleySides_AreMadeOfSlopes()
        {
            var world = ValleyGenerator.Generate(PlainValley(SmallValley()));

            var slopes = 0;
            for (var y = 0; y < world.SizeInBlocks.y; y++)
            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
                if (!world.GetBlock(new Vector3Int(x, y, z)).Shape.IsFlat)
                    slopes++;

            Assert.That(slopes, Is.GreaterThan(world.SizeInBlocks.x));
        }

        [Test]
        public void SurfaceHeights_AreQuantizedToStep()
        {
            var p = SmallValley();
            p.HeightStep = 4;
            var world = ValleyGenerator.Generate(p);

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(world, x, z) % 4, Is.Zero);
        }

        [Test]
        public void ValleyFloor_IsLowerThanRims()
        {
            var p = PlainValley(SmallValley());
            p.MeanderAmplitude = 0f;
            p.NoiseAmplitude = 0f;
            var world = ValleyGenerator.Generate(p);
            var size = world.SizeInBlocks;

            var middle = ColumnHeight(world, size.x / 2, size.z / 2);
            var edge = ColumnHeight(world, 0, size.z / 2);

            Assert.That(middle, Is.EqualTo(4 * BlockShape.MaxHeight));
            Assert.That(edge, Is.GreaterThan(middle));
        }

        [Test]
        public void Fill_OverExistingWorld_ClearsBlocksAboveSurface()
        {
            var p = SmallValley();
            var world = new VoxelWorld(p.SizeInChunks);
            var top = new Vector3Int(3, world.SizeInBlocks.y - 1, 3);
            world.SetBlock(top, Block.Full(BlockMaterials.Stone));

            ValleyGenerator.Fill(world, p);

            Assert.That(world.GetBlock(top).IsAir, Is.True);
            Assert.That(ColumnHeight(world, 3, 3), Is.EqualTo(ColumnHeight(ValleyGenerator.Generate(p), 3, 3)));
        }

        [Test]
        public void Generate_MarksChunksForMeshing()
        {
            var world = ValleyGenerator.Generate(SmallValley());

            Assert.That(world.DirtyCount, Is.GreaterThan(0));
        }
    }
}
