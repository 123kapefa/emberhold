using System.Collections.Generic;
using Emberhold.World.Trees;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Trees
{
    public sealed class TreeMeshBuilderTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        private static IEnumerable<TreeShape> Shapes => new[] { TreeShape.Leafy, TreeShape.Conifer, TreeShape.Tiered };

        [TestCaseSource(nameof(Shapes))]
        public void Build_FitsSpeciesSize(TreeShape shape)
        {
            var species = Species(shape, 5f, 1.6f);
            var mesh = Build(species, 0);

            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            var bounds = mesh.bounds;
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(1e-3f), "Основание на земле");
            Assert.That(bounds.max.y, Is.InRange(species.Height * 0.85f, species.Height * 1.08f), "Макушка на высоте вида");
            var half = species.CrownWidth * 0.5f;
            Assert.That(Mathf.Max(-bounds.min.x, bounds.max.x, -bounds.min.z, bounds.max.z), Is.InRange(half * 0.6f, half * 1.25f),
                "Крона шириной с вид");
        }

        [TestCaseSource(nameof(Shapes))]
        public void Build_FacesPointOutward(TreeShape shape)
        {
            var mesh = Build(Species(shape, 5f, 1.6f), 1);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                // Лицевая сторона в Unity — обход по часовой стрелке: Cross(b − a, c − a) совпадает с нормалью грани.
                Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, normals[triangles[i]]), Is.GreaterThan(0.99f));
            }
        }

        [Test]
        public void Build_SameVariantSameMesh_OtherVariantDiffers()
        {
            var species = Species(TreeShape.Leafy, 3f, 1.3f);
            var first = Build(species, 0).vertices;
            var again = Build(species, 0).vertices;
            var other = Build(species, 1).vertices;

            Assert.That(again, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
        }

        [Test]
        public void Build_CrownMarkedInVertexAlpha()
        {
            var species = Species(TreeShape.Leafy, 3f, 1.3f);
            var mesh = Build(species, 0);
            var crown = 0;
            var wood = 0;
            var vertices = mesh.vertices;
            var colors = mesh.colors;
            for (var i = 0; i < colors.Length; i++)
            {
                if (colors[i].a > 0.5f)
                    crown++;
                else
                {
                    wood++;
                    Assert.That(new Vector2(vertices[i].x, vertices[i].z).magnitude, Is.LessThan(species.TrunkWidth),
                        "Ствол тонкий, всё широкое — крона");
                }
            }
            Assert.That(crown, Is.GreaterThan(0));
            Assert.That(wood, Is.GreaterThan(0));
        }

        [TestCaseSource(nameof(Shapes))]
        public void Build_StaysLowPoly(TreeShape shape)
        {
            var mesh = Build(Species(shape, 8f, 3f), 2);
            Assert.That(mesh.triangles.Length / 3, Is.LessThan(1500), "Лес из тысяч деревьев остаётся дешёвым");
        }

        [Test]
        public void CrownTint_FollowsGrassTint()
        {
            var crown = new Color(0.4f, 0.6f, 0.3f);
            var plain = TreeMeshBuilder.CrownTint(crown, Color.white, 7);
            Assert.That(plain.r, Is.InRange(0.85f, 1.15f));
            Assert.That(plain.g, Is.InRange(0.85f, 1.15f));
            Assert.That(plain.b, Is.InRange(0.85f, 1.15f));

            var blue = TreeMeshBuilder.CrownTint(crown, new Color(0.8f, 0.9f, 1.1f, 1f), 7);
            Assert.That(blue.b / blue.r, Is.GreaterThan(plain.b / plain.r * 1.2f), "Крона холоднее там, где трава холоднее");
            Assert.That(TreeMeshBuilder.CrownTint(crown, Color.white, 7), Is.EqualTo(plain), "Один seed — один оттенок");
        }

        private TreeSpecies Species(TreeShape shape, float height, float crownWidth)
        {
            var species = TreeSpecies.Create(shape.ToString(), 100f, 3, height, crownWidth, shape);
            created.Add(species);
            return species;
        }

        private Mesh Build(TreeSpecies species, int variant)
        {
            var mesh = TreeMeshBuilder.Build(species, variant);
            created.Add(mesh);
            return mesh;
        }
    }
}
