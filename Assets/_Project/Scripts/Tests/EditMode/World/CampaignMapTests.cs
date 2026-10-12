using System.Collections.Generic;
using System.Diagnostics;
using Emberhold.World.Generation;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Карта кампании: хребет, котловины локаций неровной формы и извилистые ущелья (#60).</summary>
    public sealed class CampaignMapTests
    {
        private const string MapPath = "Assets/_Project/Data/World/Campaign/CampaignMap.asset";
        private const string TerrainPath = "Assets/_Project/Data/World/Campaign/CampaignTerrain.asset";
        private const int Size = 160;
        private const int Seed = 12345;

        private static TerrainProfileParameters Floor(float noise = 0.5f)
        {
            var p = TerrainProfileParameters.Valley;
            p.Shape = TerrainShape.Flat;
            p.BaseHeight = 0f;
            p.NoiseAmplitude = noise;
            p.TerraceHeight = 0f;
            p.CliffAmount = 0f;
            return p;
        }

        private static CampaignLocationShape Location(string name, Vector2 center, float floor, float roughness = 1f) => new()
        {
            Name = name,
            Center = center,
            Radius = new Vector2(24f, 18f),
            FloorHeight = floor,
            Roughness = roughness,
            Profile = Floor(),
        };

        /// <summary>Две котловины на западе и востоке, между ними ущелье без промежуточных точек.</summary>
        private static CampaignLayout TwoLocations(float wiggle = 18f, float roughness = 1f)
        {
            var layout = new CampaignLayout();
            layout.Locations.Add(Location("Запад", new Vector2(36f, 80f), 10f, roughness));
            layout.Locations.Add(Location("Восток", new Vector2(124f, 80f), 14f, roughness));
            layout.Gorges.Add(new CampaignGorgeShape
            {
                Name = "Ущелье",
                From = 0,
                To = 1,
                Waypoints = new Vector2[0],
                WidthStart = 10f,
                WidthEnd = 10f,
                HeightStart = 10f,
                HeightEnd = 14f,
                Wiggle = wiggle,
            });
            return layout;
        }

        private static CampaignHeights Build(CampaignLayout layout, int seed = Seed) =>
            CampaignHeights.Build(layout, seed, Size, Size, 46f);

        [Test]
        public void SameSeed_GivesSameHeights_DifferentSeed_Differs()
        {
            var a = Build(TwoLocations());
            var b = Build(TwoLocations());
            var c = Build(TwoLocations(), Seed + 1);

            var differ = 0;
            for (var z = 0; z <= Size; z++)
            for (var x = 0; x <= Size; x++)
            {
                Assert.That(b.Heights[x, z], Is.EqualTo(a.Heights[x, z]), $"Угол {x},{z}");
                if (!Mathf.Approximately(a.Heights[x, z], c.Heights[x, z]))
                    differ++;
            }
            Assert.That(differ, Is.GreaterThan(Size * Size / 2));
        }

        [Test]
        public void Ridge_AwayFromLocationsAndGorges_IsBetween36And46()
        {
            var heights = Build(new CampaignLayout());

            float min = float.MaxValue, max = float.MinValue;
            foreach (var h in heights.Heights)
            {
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            Assert.That(min, Is.GreaterThanOrEqualTo(36f));
            Assert.That(max, Is.LessThanOrEqualTo(46f));
            Assert.That(max - min, Is.GreaterThan(4f), "хребет должен быть с гребнями, а не плоским");
        }

        [Test]
        public void Location_FloorIsAtItsHeight_AndBelongsToIt()
        {
            var heights = Build(TwoLocations());

            Assert.That(heights.Heights[36, 80], Is.EqualTo(10f).Within(1.5f));
            Assert.That(heights.Heights[124, 80], Is.EqualTo(14f).Within(1.5f));
            Assert.That(heights.Owners[36, 80], Is.EqualTo(1));
            Assert.That(heights.Owners[124, 80], Is.EqualTo(2));
            // Угол карты далеко от всего — хребет.
            Assert.That(heights.Owners[2, 2], Is.EqualTo(CampaignHeights.Ridge));
            Assert.That(heights.Heights[2, 2], Is.GreaterThanOrEqualTo(36f));
        }

        [Test]
        public void Location_EdgeIsIrregular_NotAnEllipse()
        {
            // Насколько граница дна отходит от эллипса: отношение расстояния до края к радиусу эллипса по лучу.
            var rough = EdgeSpread(Build(TwoLocations(roughness: 1f)));
            var smooth = EdgeSpread(Build(TwoLocations(roughness: 0f)));

            Assert.That(rough, Is.GreaterThan(0.25f), "края неровные");
            Assert.That(smooth, Is.LessThan(rough), "без неровности края ровнее");
        }

        /// <summary>Разброс (макс − мин) по 24 лучам: где из центра западной котловины высота поднимается на 4 блока над дном.</summary>
        private static float EdgeSpread(CampaignHeights heights)
        {
            var center = new Vector2(36f, 80f);
            var radius = new Vector2(24f, 18f);
            float min = float.MaxValue, max = float.MinValue;
            for (var i = 0; i < 24; i++)
            {
                var angle = i * Mathf.PI * 2f / 24f;
                // Лучи на запад, север и юг: на восток уходит ущелье.
                if (Mathf.Cos(angle) > 0.3f)
                    continue;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var ellipse = 1f / Mathf.Sqrt(dir.x * dir.x / (radius.x * radius.x) + dir.y * dir.y / (radius.y * radius.y));
                var edge = 0f;
                for (var r = 0f; r < 70f; r += 0.5f)
                {
                    var p = center + dir * r;
                    var x = Mathf.Clamp(Mathf.RoundToInt(p.x), 0, Size);
                    var z = Mathf.Clamp(Mathf.RoundToInt(p.y), 0, Size);
                    edge = r;
                    if (heights.Heights[x, z] > 14f)
                        break;
                }
                var ratio = edge / ellipse;
                min = Mathf.Min(min, ratio);
                max = Mathf.Max(max, ratio);
            }
            return max - min;
        }

        [Test]
        public void Gorge_ConnectsLocations_BelowTheRidge()
        {
            var heights = Build(TwoLocations());

            // По линии между котловинами есть низкий проход: в каждом сечении дно не выше 18 блоков.
            for (var x = 64; x <= 96; x += 4)
            {
                var lowest = float.MaxValue;
                for (var z = 40; z <= 120; z++)
                    lowest = Mathf.Min(lowest, heights.Heights[x, z]);
                Assert.That(lowest, Is.LessThan(18f), $"Сечение x = {x}");
            }
        }

        [Test]
        public void Gorge_WindsAndChangesWidth()
        {
            var straight = GorgeSections(Build(TwoLocations(wiggle: 0f)));
            var winding = GorgeSections(Build(TwoLocations(wiggle: 18f)));

            // Ширина дна меняется по пути, а не постоянна.
            float minWidth = float.MaxValue, maxWidth = 0f;
            foreach (var (_, width) in winding)
            {
                minWidth = Mathf.Min(minWidth, width);
                maxWidth = Mathf.Max(maxWidth, width);
            }
            Assert.That(maxWidth, Is.GreaterThan(minWidth * 1.4f), $"ширина {minWidth}..{maxWidth}");

            // Середина дна уходит от прямой между центрами (z = 80) дальше, чем без извилистости.
            Assert.That(MaxOffset(winding), Is.GreaterThan(MaxOffset(straight) + 2f));
        }

        /// <summary>Сечения ущелья поперёк по X между котловинами: середина дна по Z и ширина дна.</summary>
        private static List<(float middle, float width)> GorgeSections(CampaignHeights heights)
        {
            var result = new List<(float, float)>();
            for (var x = 66; x <= 94; x += 2)
            {
                var lowest = float.MaxValue;
                var lowestZ = 0;
                for (var z = 30; z <= 130; z++)
                {
                    if (heights.Heights[x, z] < lowest)
                    {
                        lowest = heights.Heights[x, z];
                        lowestZ = z;
                    }
                }
                int from = lowestZ, to = lowestZ;
                while (from > 0 && heights.Heights[x, from - 1] < lowest + 1.5f)
                    from--;
                while (to < Size && heights.Heights[x, to + 1] < lowest + 1.5f)
                    to++;
                result.Add(((from + to) * 0.5f, to - from + 1));
            }
            return result;
        }

        private static float MaxOffset(List<(float middle, float width)> sections)
        {
            var max = 0f;
            foreach (var (middle, _) in sections)
                max = Mathf.Max(max, Mathf.Abs(middle - 80f));
            return max;
        }

        [Test]
        public void Generate_TakesMaterialsAndForestFromTheOwner()
        {
            var layout = TwoLocations();
            var stone = Floor();
            stone.Materials.Surface = BlockMaterials.Stone;
            var west = layout.Locations[0];
            west.Profile = stone;
            west.Forest = new ForestMix(0.5f);
            layout.Locations[0] = west;
            var ridge = layout.Ridge;
            ridge.Profile.Materials = TerrainMaterials.Default;
            layout.Ridge = ridge;

            var world = TerrainGenerator.GenerateCampaign(layout, Seed, new Vector3Int(10, 3, 10), out var heights);

            Assert.That(TopBlock(world, 36, 80).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(TopBlock(world, 2, 2).Material, Is.EqualTo(BlockMaterials.Grass));
            Assert.That(heights.ColumnOwner(36, 80), Is.EqualTo(1));
        }

        private static Block TopBlock(VoxelWorld world, int x, int z)
        {
            for (var y = world.SizeInBlocks.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (!block.IsAir)
                    return block;
            }
            return Block.Air;
        }

        [Test]
        public void MapAsset_HasAllLocationsAndPathsOfTheSketch()
        {
            var map = AssetDatabase.LoadAssetAtPath<CampaignMap>(MapPath);
            Assert.That(map, Is.Not.Null);
            var layout = map.ToLayout();

            Assert.That(layout.Locations, Has.Count.EqualTo(13));
            Assert.That(layout.Gorges, Has.Count.EqualTo(14), "все ущелья ссылаются на существующие локации");
            foreach (var location in layout.Locations)
                Assert.That(location.Name, Is.Not.Empty);
        }

        [Test]
        public void MapAsset_Generates512World_WithLocationsAtTheirHeights()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainPath);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.SizeInChunks * ChunkData.Size, Is.EqualTo(new Vector3Int(512, 48, 512)));
            var layout = settings.Map.ToLayout();

            var timer = Stopwatch.StartNew();
            var world = TerrainGenerator.GenerateCampaign(layout, settings.Seed, settings.SizeInChunks, out var heights);
            timer.Stop();
            Debug.Log($"[CampaignMap] Мир 512×512 сгенерирован за {timer.ElapsedMilliseconds} мс.");

            Assert.That(world.SizeInBlocks, Is.EqualTo(new Vector3Int(512, 48, 512)));
            Assert.That(timer.ElapsedMilliseconds, Is.LessThan(15000), "генерация не должна подвешивать редактор");
            for (var i = 0; i < layout.Locations.Count; i++)
            {
                var location = layout.Locations[i];
                var x = Mathf.RoundToInt(location.Center.x);
                var z = Mathf.RoundToInt(location.Center.y);
                Assert.That(heights.Owners[x, z], Is.EqualTo(i + 1), location.Name);
                // У гор в центре пик, у холмов подъём: сверяем с дном и самым высоким местом формы.
                var top = location.FloorHeight + location.Rise + location.Profile.ShapeHeight;
                Assert.That(heights.Heights[x, z], Is.InRange(location.FloorHeight - 3f, top + 4f), location.Name);
            }
        }
    }
}
