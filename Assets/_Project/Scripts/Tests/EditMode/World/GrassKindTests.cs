using System.Linq;
using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Sandbox;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Свои виды травы у Топей, Мёртвого города и Гор: смешивание в генераторе, вершины, макет (#82).</summary>
    public sealed class GrassKindTests
    {
        private const int Size = 160;
        private const string MapPath = "Assets/_Project/Data/World/Campaign/CampaignMap.asset";

        private static CampaignLayout SwampAndDead()
        {
            var floor = TerrainProfileParameters.Valley;
            floor.Shape = TerrainShape.Flat;
            floor.BaseHeight = 0f;
            floor.TerraceHeight = 0f;
            floor.CliffAmount = 0f;
            var layout = new CampaignLayout { Ridge = CampaignRidge.Default };
            layout.Locations.Add(new CampaignLocationShape
            {
                Name = "Топи", Center = new Vector2(36f, 80f), Radius = new Vector2(24f, 18f), FloorHeight = 10f,
                Roughness = 1f, Profile = floor, GrassKind = GrassKind.Swamp,
            });
            layout.Locations.Add(new CampaignLocationShape
            {
                Name = "Город", Center = new Vector2(124f, 80f), Radius = new Vector2(24f, 18f), FloorHeight = 14f,
                Roughness = 1f, Profile = floor, GrassKind = GrassKind.Dead,
            });
            layout.Gorges.Add(new CampaignGorgeShape
            {
                Name = "Ущелье", From = 0, To = 1, Waypoints = new Vector2[0],
                WidthStart = 10f, WidthEnd = 10f, HeightStart = 10f, HeightEnd = 14f, Wiggle = 0f,
            });
            return layout;
        }

        [Test]
        public void Heights_OwnKindInside_MeadowOnRidge_SmoothAlongGorge()
        {
            var kinds = CampaignHeights.Build(SwampAndDead(), 12345, Size, Size, 46f).GrassKinds;

            Assert.That(kinds[36, 80], Is.EqualTo(GrassKinds.Weight(GrassKind.Swamp)));
            Assert.That(kinds[124, 80], Is.EqualTo(GrassKinds.Weight(GrassKind.Dead)));
            Assert.That(kinds[80, 150], Is.EqualTo(GrassKinds.Weight(GrassKind.Meadow)), "Хребет — обычная трава");
            // По дну от Топей к городу вид травы меняется плавно, без скачков.
            for (var x = 36; x < 124; x++)
            {
                var a = kinds[x, 80];
                var b = kinds[x + 1, 80];
                Assert.That(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)), Is.LessThan(0.05f),
                    $"Скачок у x = {x}");
                Assert.That(a.r + a.g + a.b, Is.LessThanOrEqualTo(1f + 1e-4f));
            }
        }

        [Test]
        public void MapAsset_SwampCityAndMountainsHaveOwnGrass_OthersMeadow()
        {
            var map = AssetDatabase.LoadAssetAtPath<CampaignMap>(MapPath);
            Assert.That(map, Is.Not.Null);
            foreach (var location in map.Locations)
            {
                var expected = location.id switch
                {
                    "swamp" => GrassKind.Swamp,
                    "city" => GrassKind.Dead,
                    "mount" => GrassKind.Mountain,
                    _ => GrassKind.Meadow,
                };
                Assert.That(location.GrassKind, Is.EqualTo(expected), location.name);
            }
        }

        [Test]
        public void MapLocation_OverrideOrProfileOrMeadow()
        {
            var profile = ScriptableObject.CreateInstance<TerrainProfile>();
            try
            {
                var location = new CampaignMap.Location { id = "a", name = "А" };
                Assert.That(location.GrassKind, Is.EqualTo(GrassKind.Meadow));
                location.profile = profile;
                Assert.That(location.GrassKind, Is.EqualTo(profile.GrassKind));
                location.overrideGrassKind = true;
                location.grassKind = GrassKind.Mountain;
                Assert.That(location.ToShape().GrassKind, Is.EqualTo(GrassKind.Mountain));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Mesher_PutsKindOfThePlaceIntoVertices()
        {
            var world = GroundTexturesShowcase.Build();
            var mesh = new ChunkMeshData();
            // Чанк (4, 0, 1): площадка травы Топей (X 66–94, Z 16–32).
            ChunkMesher.Build(world, new Vector3Int(4, 0, 1), mesh);

            Assert.That(mesh.Kinds, Has.Count.EqualTo(mesh.Vertices.Count));
            var origin = new Vector3(64f, 0f, 16f);
            for (var i = 0; i < mesh.Vertices.Count; i++)
            {
                var p = mesh.Vertices[i] + origin;
                Assert.That((Color)mesh.Kinds[i], Is.EqualTo(world.GrassTint.SampleKinds(p.x, p.z)));
            }
            Assert.That(mesh.Kinds, Has.Some.Matches<Vector4>(k => k.x > 0.99f), "Есть чистая трава Топей");
        }

        [Test]
        public void Showcase_EachKindPadHasItsGrass_TransitionsBetween()
        {
            var tint = GroundTexturesShowcase.Build().GrassTint;
            foreach (var (area, kind) in GroundTexturesShowcase.GrassKindAreas)
            {
                var c = area.Rect.center;
                Assert.That(tint.SampleKinds(c.x, c.y), Is.EqualTo(GrassKinds.Weight(kind)), area.Name);
            }
            // На границе Топей и Мёртвого города — поровну.
            var border = tint.SampleKinds(80f, GroundTexturesShowcase.DeadGrass.Rect.yMin);
            Assert.That(border.r, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(border.g, Is.EqualTo(0.5f).Within(1e-3f));
            // Остальной макет — обычная трава.
            Assert.That(tint.SampleKinds(10f, 10f), Is.EqualTo(GrassKinds.Weight(GrassKind.Meadow)));
        }

        [Test]
        public void KindTextures_AreAssignedToBlockMaterials()
        {
            foreach (var name in new[] { "BlockGrass", "BlockDirt", "BlockStone" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Art/Materials/World/{name}.mat");
                foreach (var property in new[] { "_GrassSwampMap", "_GrassDeadMap", "_GrassMountainMap" })
                    Assert.That(material.GetTexture(property), Is.Not.Null, $"{name}.{property}");
            }
            var names = new[] { "_GrassSwampMap", "_GrassDeadMap", "_GrassMountainMap" }
                .Select(p => AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/World/BlockGrass.mat").GetTexture(p).name);
            Assert.That(names, Is.EquivalentTo(new[] { "GroundGrassSwamp", "GroundGrassDead", "GroundGrassMountain" }));
        }
    }
}
