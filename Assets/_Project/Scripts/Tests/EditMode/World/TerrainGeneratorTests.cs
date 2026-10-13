using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class TerrainGeneratorTests
    {
        private const int DefaultSeed = 12345;
        private static readonly Vector3Int DefaultSize = new(8, 3, 8);
        private static readonly Vector3Int SmallSize = new(2, 2, 2);

        /// <summary>
        /// Отпечаток всех блоков мира: материал и углы формы. Обновлён в #20: под внешним углом ската
        /// блок ниже теперь из материала поверхности (его верх виден в открытой половине).
        /// </summary>
        private const int DefaultValleyHash = -2000301201;

        /// <summary>Маленькая долина 2×2×2 чанка: дно на 4 блоках, борта на 20.</summary>
        private static TerrainProfileParameters SmallValley()
        {
            var p = TerrainProfileParameters.Valley;
            p.BaseHeight = 4f;
            p.ShapeHeight = 16f;
            return p;
        }

        private static VoxelWorld Small(TerrainProfileParameters p, int seed = 7) => TerrainGenerator.Generate(p, seed, SmallSize);

        private static VoxelWorld Full(TerrainProfileParameters p, int seed = DefaultSeed) =>
            TerrainGenerator.Generate(p, seed, DefaultSize);

        /// <summary>Долина без террас и скал — проверяем форму рельефа и гладкость склонов.</summary>
        private static TerrainProfileParameters PlainValley(TerrainProfileParameters p)
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
            var a = Small(SmallValley());
            var b = Small(SmallValley());

            for (var z = 0; z < a.SizeInBlocks.z; z++)
            for (var x = 0; x < a.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(a, x, z), Is.EqualTo(ColumnHeight(b, x, z)), $"Столбец {x},{z}");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentWorlds()
        {
            var a = Small(SmallValley(), 1);
            var b = Small(SmallValley(), 2);

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
            var world = Small(SmallValley());

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
            {
                var height = ColumnHeight(world, x, z);
                Assert.That(height, Is.GreaterThan(0));
                var topY = (height - 1) / BlockShape.MaxHeight;
                var surface = world.GetBlock(new Vector3Int(x, topY, z));
                Assert.That(surface.Material, Is.EqualTo(BlockMaterials.Grass));
                // Под внешним углом ската пол наполовину открыт, поэтому блок под ним тоже трава.
                var grassBelow = surface.Shape.CoversFloor ? 0 : 1;
                for (var y = 0; y < topY; y++)
                {
                    var block = world.GetBlock(new Vector3Int(x, y, z));
                    Assert.That(block.Shape.IsFull, Is.True, $"Под поверхностью неполный блок {x},{y},{z}");
                    Assert.That(block.Material == BlockMaterials.Grass, Is.EqualTo(topY - y <= grassBelow), $"{x},{y},{z}");
                }
            }
        }

        [Test]
        public void GentleSlope_NeighbourColumnsShareCornerHeights()
        {
            // Пологая долина: склон не круче шага высоты на блок, уступов быть не должно.
            var p = PlainValley(SmallValley());
            p.ShapeHeight = 1f;
            p.NoiseAmplitude = 0f;
            p.MeanderAmplitude = 0f;
            var world = Small(p);
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
            var p = PlainValley(TerrainProfileParameters.Valley);
            p.NoiseAmplitude = 2.5f;
            p.NoiseScale = 24f;
            p.ShapeWidth = 0.12f;
            var world = Full(p);
            var (ledges, edges) = CountLedges(world);

            TestContext.WriteLine($"Уступов: {ledges} из {edges}");
            Assert.That(ledges, Is.LessThan(edges / 20));
        }

        [Test]
        public void TerracedValley_HasFlatLevelsSlopesAndCleanCliffs()
        {
            var world = Full(TerrainProfileParameters.Valley);
            var size = world.SizeInBlocks;

            // «Ровная» — пригодная для стройки: перепад углов не больше четверти блока
            // (пологий купол у края плато тоже сюда попадает).
            int flat = 0, slopes = 0, columns = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var s = world.GetBlock(new Vector3Int(x, (ColumnHeight(world, x, z) - 1) / BlockShape.MaxHeight, z)).Shape;
                var spread = Mathf.Max(Mathf.Max(s.C00, s.C10), Mathf.Max(s.C01, s.C11))
                             - Mathf.Min(Mathf.Min(s.C00, s.C10), Mathf.Min(s.C01, s.C11));
                columns++;
                if (spread <= 4) flat++;
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
            Assert.That(TerrainGenerator.Terrace(3.2f, 3f, 0.5f, cliff: false), Is.EqualTo(3f).Within(1e-4f), "Ровная площадка");
            Assert.That(TerrainGenerator.Terrace(4.4f, 3f, 0.5f, cliff: false), Is.EqualTo(3f).Within(1e-4f), "Ещё ровная");
            Assert.That(TerrainGenerator.Terrace(5.25f, 3f, 0.5f, cliff: false), Is.EqualTo(4.5f).Within(1e-4f), "Середина склона");
            Assert.That(TerrainGenerator.Terrace(5.999f, 3f, 0.5f, cliff: false), Is.EqualTo(6f).Within(1e-2f), "Склон выходит на уровень");
            Assert.That(TerrainGenerator.Terrace(4.4f, 3f, 0.5f, cliff: true), Is.EqualTo(3f).Within(1e-4f), "Скала: нижний уровень");
            Assert.That(TerrainGenerator.Terrace(4.6f, 3f, 0.5f, cliff: true), Is.EqualTo(6f).Within(1e-4f), "Скала: сразу верхний");
            Assert.That(TerrainGenerator.Terrace(4.6f, 0f, 0.5f, cliff: true), Is.EqualTo(4.6f), "Без террас");
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

        [Test]
        public void CornerTerrain_RoundsCliffTopEdgeAndFoot()
        {
            // Левая половина на 6 блоках, правая на 2: между ними обрыв.
            var world = new VoxelWorld(Vector3Int.one);
            var corners = new int[17, 17];
            for (var z = 0; z <= 16; z++)
            for (var x = 0; x <= 16; x++)
                corners[x, z] = x <= 8 ? 96 : 32;

            CornerTerrain.Fill(world, corners, dirtDepth: 1, edgeRounding: 8, footRounding: 4);

            // Столбец 8 (крутой) выровнен по верхнему уровню, его кромка над обрывом опущена на 8/16.
            Assert.That(CornerHeight(world, 8, 5, 0), Is.EqualTo(96));
            Assert.That(CornerHeight(world, 8, 5, 1), Is.EqualTo(88));
            Assert.That(CornerHeight(world, 8, 5, 3), Is.EqualTo(88));
            // У подножия земля поднята на 4/16, дальше ровно.
            Assert.That(CornerHeight(world, 9, 5, 0), Is.EqualTo(36));
            Assert.That(CornerHeight(world, 9, 5, 1), Is.EqualTo(32));
            // Вдали от обрыва ничего не меняется.
            Assert.That(CornerHeight(world, 3, 5, 1), Is.EqualTo(96));
        }

        [Test]
        public void CornerTerrain_CapOverCliffSlopesDownFromMiddleToEdge()
        {
            // Плато шириной 9 блоков на 6 блоках, вокруг земля на 2 блоках.
            var world = new VoxelWorld(Vector3Int.one);
            var corners = new int[17, 17];
            for (var z = 0; z <= 16; z++)
            for (var x = 0; x <= 16; x++)
                corners[x, z] = x <= 8 ? 96 : 32;

            CornerTerrain.RoundCaps(corners, capDepth: 16, capWidth: 6f, heightStep: 1);
            CornerTerrain.Fill(world, corners, dirtDepth: 1);

            // У кромки (x = 9) шапка опущена на блок, к середине плато поднимается, дальше ровно.
            var edge = CornerHeight(world, 8, 8, 1);
            var nearEdge = CornerHeight(world, 6, 8, 1);
            var inside = CornerHeight(world, 1, 8, 1);
            Assert.That(edge, Is.EqualTo(80));
            Assert.That(nearEdge, Is.GreaterThan(edge).And.LessThan(96));
            Assert.That(inside, Is.EqualTo(96));
            // Шапка гладкая: внутри плато соседние столбцы делят углы.
            for (var x = 0; x < 8; x++)
                Assert.That(CornerHeight(world, x, 8, 1), Is.EqualTo(CornerHeight(world, x + 1, 8, 0)), $"x = {x}");
            // Земля у подножия не тронута.
            Assert.That(CornerHeight(world, 10, 8, 0), Is.EqualTo(32));
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
            var world = Small(PlainValley(SmallValley()));

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
            var world = Small(p);

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
                Assert.That(ColumnHeight(world, x, z) % 4, Is.Zero);
        }

        [Test]
        public void ValleyWiderThanHalfMap_IsAllFloor_NotUpsideDown()
        {
            // Дно шире половины карты занимает всю карту; раньше InverseLerp переворачивался и долина вставала стеной.
            var p = PlainValley(TerrainProfileParameters.Valley);
            p.MeanderAmplitude = 0f;
            for (var width = 0.5f; width <= 1f; width += 0.1f)
            {
                p.ShapeWidth = width;
                for (var u = 0f; u <= 1f; u += 0.125f)
                    Assert.That(TerrainGenerator.ShapeHeight(p, u, 0.5f), Is.EqualTo(p.BaseHeight), $"Ширина {width}, u = {u}");
            }
        }

        [Test]
        public void ValleyFloor_IsLowerThanRims()
        {
            var p = PlainValley(SmallValley());
            p.MeanderAmplitude = 0f;
            p.NoiseAmplitude = 0f;
            var world = Small(p);
            var size = world.SizeInBlocks;

            var middle = ColumnHeight(world, size.x / 2, size.z / 2);
            var edge = ColumnHeight(world, 0, size.z / 2);

            // Борт маленькой долины круче 45°: склон у подножия поднимается, и дно может стать чуть выше заданного.
            Assert.That(middle, Is.GreaterThanOrEqualTo(4 * BlockShape.MaxHeight));
            Assert.That(edge, Is.GreaterThan(middle));
        }

        [Test]
        public void Fill_OverExistingWorld_ClearsBlocksAboveSurface()
        {
            var p = SmallValley();
            var world = new VoxelWorld(SmallSize);
            var top = new Vector3Int(3, world.SizeInBlocks.y - 1, 3);
            world.SetBlock(top, Block.Full(BlockMaterials.Stone));

            TerrainGenerator.Fill(world, p, 7);

            Assert.That(world.GetBlock(top).IsAir, Is.True);
            Assert.That(ColumnHeight(world, 3, 3), Is.EqualTo(ColumnHeight(Small(p), 3, 3)));
        }

        [Test]
        public void Generate_MarksChunksForMeshing()
        {
            var world = Small(SmallValley());

            Assert.That(world.DirtyCount, Is.GreaterThan(0));
        }

        /// <summary>Отпечаток всех блоков: материал и четыре угла формы каждого блока.</summary>
        private static int WorldHash(VoxelWorld world)
        {
            var size = world.SizeInBlocks;
            unchecked
            {
                var hash = 17;
                for (var y = 0; y < size.y; y++)
                for (var z = 0; z < size.z; z++)
                for (var x = 0; x < size.x; x++)
                {
                    var block = world.GetBlock(new Vector3Int(x, y, z));
                    hash = hash * 31 + block.Material;
                    hash = hash * 31 + block.Shape.C00;
                    hash = hash * 31 + block.Shape.C10;
                    hash = hash * 31 + block.Shape.C01;
                    hash = hash * 31 + block.Shape.C11;
                }
                return hash;
            }
        }

        [Test]
        public void DefaultValleyProfile_GivesTheSameWorldAsBeforeProfiles()
        {
            // Отпечаток долины из сцены Prototype до перехода на профили (seed 12345, 8×3×8 чанков).
            Assert.That(WorldHash(Full(TerrainProfileParameters.Valley)), Is.EqualTo(DefaultValleyHash));
        }

        [Test]
        public void TerrainSettingsAsset_GeneratesTheDefaultValley()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>("Assets/_Project/Data/World/TerrainSettings.asset");
            Assert.That(settings, Is.Not.Null);
            // Рельеф — долина по умолчанию, поднятая на 4 блока, чтобы коренная порода на дне не выходила
            // на поверхность (#97); поверх неё на дне коренная порода.
            var raised = TerrainProfileParameters.Valley;
            raised.BaseHeight += 4f;
            var valley = Full(raised);
            settings.LayBedrock(valley);
            Assert.That(WorldHash(settings.Generate()), Is.EqualTo(WorldHash(valley)));
        }

        [Test]
        public void TerrainSettingsAsset_BedrockNeverOnTheSurface()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>("Assets/_Project/Data/World/TerrainSettings.asset");
            var world = settings.Generate();
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            for (var y = size.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (block.IsAir)
                    continue;
                Assert.That(block.Material, Is.Not.EqualTo(BlockMaterials.Bedrock), $"Порода сверху столбца {x},{z}");
                break;
            }
        }

        private const string ProfilesFolder = "Assets/_Project/Data/World/TerrainProfiles";
        private static readonly string[] SampleProfiles = { "Valley", "Plain", "Hills", "Mountain", "Lake" };

        private static TerrainProfileParameters LoadProfile(string name)
        {
            var profile = AssetDatabase.LoadAssetAtPath<TerrainProfile>($"{ProfilesFolder}/{name}.asset");
            Assert.That(profile, Is.Not.Null, name);
            return profile.Parameters;
        }

        [Test]
        public void SampleProfiles_SameSeed_GiveSameWorld([ValueSource(nameof(SampleProfiles))] string name)
        {
            var p = LoadProfile(name);
            Assert.That(WorldHash(Small(p, 3)), Is.EqualTo(WorldHash(Small(p, 3))));
            Assert.That(WorldHash(Small(p, 3)), Is.Not.EqualTo(WorldHash(Small(p, 4))), "Другой seed — другой мир");
        }

        private static float AverageHeight(VoxelWorld world)
        {
            var total = 0L;
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
                total += ColumnHeight(world, x, z);
            return total / (float)(size.x * size.z) / BlockShape.MaxHeight;
        }

        [Test]
        public void SampleProfiles_GiveDifferentHeights()
        {
            var plain = Full(LoadProfile("Plain"));
            var hills = Full(LoadProfile("Hills"));
            var mountain = Full(LoadProfile("Mountain"));
            var lake = Full(LoadProfile("Lake"));
            var size = plain.SizeInBlocks;
            var (cx, cz) = (size.x / 2, size.z / 2);
            TestContext.WriteLine($"Средняя высота: равнина {AverageHeight(plain):F1}, холмы {AverageHeight(hills):F1}, " +
                                  $"гора {AverageHeight(mountain):F1}, озеро {AverageHeight(lake):F1}");

            // Гора в центре выше всех, дно озера ниже равнины, холмы в среднем выше равнины.
            Assert.That(ColumnHeight(mountain, cx, cz), Is.GreaterThan(ColumnHeight(hills, cx, cz) + 8 * BlockShape.MaxHeight));
            Assert.That(ColumnHeight(lake, cx, cz), Is.LessThan(ColumnHeight(lake, 0, 0)));
            Assert.That(AverageHeight(hills), Is.GreaterThan(AverageHeight(plain) + 1f));
            Assert.That(HeightRange(plain), Is.LessThan(HeightRange(hills)), "Равнина ровнее холмов");
        }

        private static int HeightRange(VoxelWorld world)
        {
            int min = int.MaxValue, max = int.MinValue;
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var h = ColumnHeight(world, x, z);
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            return max - min;
        }

        [Test]
        public void Lake_BedBelowWaterLevelUsesBedMaterial()
        {
            var p = LoadProfile("Lake");
            var world = Full(p);
            var size = world.SizeInBlocks;
            var (cx, cz) = (size.x / 2, size.z / 2);
            var bottom = ColumnHeight(world, cx, cz);

            Assert.That(bottom, Is.LessThan(p.WaterLevel * BlockShape.MaxHeight), "Дно ниже уровня воды");
            Assert.That(world.GetBlock(new Vector3Int(cx, (bottom - 1) / BlockShape.MaxHeight, cz)).Material, Is.EqualTo(p.Materials.Bed));
            Assert.That(world.GetBlock(new Vector3Int(0, (ColumnHeight(world, 0, 0) - 1) / BlockShape.MaxHeight, 0)).Material,
                Is.EqualTo(p.Materials.Surface), "Берег выше воды");
        }

        [Test]
        public void ProfileMaterials_GoToSurfaceAndBelow()
        {
            var p = SmallValley();
            p.Materials = new TerrainMaterials
            {
                Surface = BlockMaterials.Stone, Subsurface = BlockMaterials.Grass, SubsurfaceDepth = 2,
                Deep = BlockMaterials.Dirt, Bed = BlockMaterials.Dirt,
            };
            var world = Small(p);

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
            {
                var topY = (ColumnHeight(world, x, z) - 1) / BlockShape.MaxHeight;
                var surface = world.GetBlock(new Vector3Int(x, topY, z));
                Assert.That(surface.Material, Is.EqualTo(BlockMaterials.Stone));
                // Под внешним углом ската ещё один блок из материала поверхности.
                var surfaceBelow = surface.Shape.CoversFloor ? 0 : 1;
                for (var y = 0; y < topY; y++)
                {
                    var depth = topY - y;
                    var expected = depth <= surfaceBelow ? BlockMaterials.Stone
                        : depth - surfaceBelow <= 2 ? BlockMaterials.Grass : BlockMaterials.Dirt;
                    Assert.That(world.GetBlock(new Vector3Int(x, y, z)).Material, Is.EqualTo(expected), $"{x},{y},{z}");
                }
            }
        }

        [Test]
        public void SampleValleyAsset_MatchesDefaultValley()
        {
            // Образец — долина по умолчанию, поднятая на 4 блока над коренной породой (#97).
            var raised = TerrainProfileParameters.Valley;
            raised.BaseHeight += 4f;
            Assert.That(WorldHash(Full(LoadProfile("Valley"))), Is.EqualTo(WorldHash(Full(raised))));
        }
    }
}
