using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Оттенки травы по локациям: смешивание в генераторе, выборка по миру и цвет вершин (#77).</summary>
    public sealed class GrassTintTests
    {
        private const int Size = 160;

        private static readonly Color West = new(1.2f, 1f, 0.7f, 1f);
        private static readonly Color East = new(0.7f, 1f, 1.2f, 0.5f);
        private static readonly Color RidgeTint = new(0.9f, 0.9f, 0.9f, 1f);

        private static CampaignLayout TwoLocations()
        {
            var floor = TerrainProfileParameters.Valley;
            floor.Shape = TerrainShape.Flat;
            floor.BaseHeight = 0f;
            floor.TerraceHeight = 0f;
            floor.CliffAmount = 0f;
            var layout = new CampaignLayout();
            var ridge = CampaignRidge.Default;
            ridge.GrassTint = RidgeTint;
            layout.Ridge = ridge;
            layout.Locations.Add(new CampaignLocationShape
            {
                Name = "Запад", Center = new Vector2(36f, 80f), Radius = new Vector2(24f, 18f), FloorHeight = 10f,
                Roughness = 1f, Profile = floor, GrassTint = West,
            });
            layout.Locations.Add(new CampaignLocationShape
            {
                Name = "Восток", Center = new Vector2(124f, 80f), Radius = new Vector2(24f, 18f), FloorHeight = 14f,
                Roughness = 1f, Profile = floor, GrassTint = East,
            });
            layout.Gorges.Add(new CampaignGorgeShape
            {
                Name = "Ущелье", From = 0, To = 1, Waypoints = new Vector2[0],
                WidthStart = 10f, WidthEnd = 10f, HeightStart = 10f, HeightEnd = 14f, Wiggle = 0f,
            });
            return layout;
        }

        private static CampaignHeights Build(CampaignLayout layout) => CampaignHeights.Build(layout, 12345, Size, Size, 46f);

        private static void AssertColor(Color actual, Color expected, float tolerance, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), message + " r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), message + " g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), message + " b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), message + " a");
        }

        [Test]
        public void InsideLocation_OwnTint_FarOnRidge_RidgeTint()
        {
            var tints = Build(TwoLocations()).GrassTints;
            AssertColor(tints[36, 80], West, 1e-4f, "Центр запада");
            AssertColor(tints[124, 80], East, 1e-4f, "Центр востока");
            AssertColor(tints[80, 150], RidgeTint, 1e-4f, "Хребет вдали от локаций и ущелья");
        }

        [Test]
        public void Gorge_BlendsFromEndToEnd_Smoothly()
        {
            var tints = Build(TwoLocations()).GrassTints;

            // Середина ущелья — между оттенками концов.
            var middle = tints[80, 80];
            Assert.That(middle.r, Is.InRange(East.r + 0.05f, West.r - 0.05f));
            Assert.That(middle.b, Is.InRange(West.b + 0.05f, East.b - 0.05f));

            // От центра запада к центру востока по дну оттенок меняется плавно, без скачков.
            for (var x = 36; x < 124; x++)
            {
                var a = tints[x, 80];
                var b = tints[x + 1, 80];
                var step = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
                Assert.That(step, Is.LessThan(0.05f), $"Скачок оттенка у x = {x}");
            }
        }

        [Test]
        public void NoTints_AllNeutral()
        {
            var layout = TwoLocations();
            layout.Ridge = CampaignRidge.Default;
            for (var i = 0; i < layout.Locations.Count; i++)
            {
                var shape = layout.Locations[i];
                shape.GrassTint = null;
                layout.Locations[i] = shape;
            }
            foreach (var tint in Build(layout).GrassTints)
                AssertColor(tint, Color.white, 1e-4f, "Без оттенков");
        }

        [Test]
        public void MapLocation_OverrideOrProfileOrNeutral()
        {
            var profile = ScriptableObject.CreateInstance<TerrainProfile>();
            try
            {
                var location = new CampaignMap.Location { id = "a", name = "А" };
                Assert.That(location.GrassTint, Is.EqualTo(Color.white), "Без профиля и своего оттенка");

                location.profile = profile;
                Assert.That(location.GrassTint, Is.EqualTo(profile.GrassTint), "Оттенок профиля");

                location.overrideGrassTint = true;
                location.grassTint = West;
                Assert.That(location.GrassTint, Is.EqualTo(West), "Свой оттенок");
                Assert.That(location.ToShape().GrassTint, Is.EqualTo((Color?)West));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void GroundTint_SamplesBetweenCorners_ClampsOutside()
        {
            var corners = new Color[3, 2];
            corners[0, 0] = Color.black;
            corners[1, 0] = Color.white;
            corners[2, 0] = Color.white;
            corners[0, 1] = Color.black;
            corners[1, 1] = Color.white;
            corners[2, 1] = Color.white;
            var tint = new GroundTint(corners);

            AssertColor(tint.Sample(0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f, 1f), 1e-5f, "Середина клетки");
            AssertColor(tint.Sample(-3f, 0f), Color.black, 1e-5f, "За левым краем");
            AssertColor(tint.Sample(9f, 9f), Color.white, 1e-5f, "За дальним углом");
        }

        [Test]
        public void Mesher_ColorsVerticesByTint_MarksGrass()
        {
            var world = new VoxelWorld(Vector3Int.one);
            world.SetBlock(new Vector3Int(2, 0, 2), Block.Full(BlockMaterials.Grass));
            world.SetBlock(new Vector3Int(5, 0, 2), Block.Full(BlockMaterials.Dirt));
            var corners = new Color[ChunkData.Size + 1, ChunkData.Size + 1];
            for (var z = 0; z <= ChunkData.Size; z++)
            for (var x = 0; x <= ChunkData.Size; x++)
                corners[x, z] = new Color(x / 16f, z / 16f, 1f, 1f);
            world.GrassTint = new GroundTint(corners);

            var output = new ChunkMeshData();
            ChunkMesher.Build(world, Vector3Int.zero, output);

            Assert.That(output.Colors, Has.Count.EqualTo(output.Vertices.Count));
            for (var i = 0; i < output.Vertices.Count; i++)
            {
                var v = output.Vertices[i];
                AssertColor(output.Colors[i], new Color(v.x / 16f, v.z / 16f, 1f, 1f), 1e-5f, $"Вершина {v}");
                var grass = v.x <= 3f;
                Assert.That(output.Blends[i].y, Is.EqualTo(grass ? ChunkMeshData.GrassVertex : 0f), $"Вершина {v}");
            }
        }
    }
}
