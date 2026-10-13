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
            // Тела дерева (ствол, конусы, комки кроны) — замкнутые поверхности. Грани смотрят наружу, если
            // 1) обход согласован: каждое ребро соседние грани проходят в разные стороны, и
            // 2) у каждого тела объём по формуле Гаусса (сумма a · (b × c) / 6) положительный, а не вывернутый.
            var mesh = Build(Species(shape, 5f, 1.6f), 1);
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var points = new Dictionary<Vector3, int>();
            int Point(Vector3 p)
            {
                if (!points.TryGetValue(p, out var id))
                    points[p] = id = points.Count;
                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            var faces = new List<(int a, int b, int c)>();
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var face = (Point(vertices[triangles[i]]), Point(vertices[triangles[i + 1]]), Point(vertices[triangles[i + 2]]));
                faces.Add(face);
                foreach (var edge in new[] { (face.Item1, face.Item2), (face.Item2, face.Item3), (face.Item3, face.Item1) })
                    edges[edge] = edges.TryGetValue(edge, out var n) ? n + 1 : 1;
            }
            foreach (var (edge, count) in edges)
            {
                edges.TryGetValue((edge.Item2, edge.Item1), out var back);
                Assert.That(back, Is.EqualTo(count), "Ребро в обе стороны поровну: поверхность замкнута, обход согласован");
            }

            // Тела — связные по общим точкам группы граней.
            var parent = new int[points.Count];
            for (var i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
            foreach (var (a, b, c) in faces)
            {
                parent[Root(b)] = Root(a);
                parent[Root(c)] = Root(a);
            }
            var byId = new Vector3[points.Count];
            foreach (var (p, id) in points)
                byId[id] = p;
            var volumes = new Dictionary<int, float>();
            foreach (var (a, b, c) in faces)
            {
                var root = Root(a);
                volumes.TryGetValue(root, out var v);
                volumes[root] = v + Vector3.Dot(byId[a], Vector3.Cross(byId[b], byId[c])) / 6f;
            }
            Assert.That(volumes.Count, Is.GreaterThan(1));
            foreach (var volume in volumes.Values)
                Assert.That(volume, Is.GreaterThan(0f), "Тело не вывернуто наизнанку");
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
