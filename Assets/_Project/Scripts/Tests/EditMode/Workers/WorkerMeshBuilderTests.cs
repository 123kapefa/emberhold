using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Meshing;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class WorkerMeshBuilderTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void Build_StandsOnFeetAtWorkerHeight()
        {
            var mesh = Build(Look(beard: true), 1.9f);

            var bounds = mesh.bounds;
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(1e-3f), "Ноги на земле");
            Assert.That(bounds.max.y, Is.InRange(1.9f * 0.95f, 1.9f), "Макушка капюшона на высоте роста, не выше");
            Assert.That(Mathf.Max(-bounds.min.x, bounds.max.x), Is.LessThan(0.5f), "В ширину помещается в клетку");
            Assert.That(Mathf.Max(-bounds.min.z, bounds.max.z), Is.LessThan(0.5f), "В глубину помещается в клетку");
        }

        [Test]
        public void Build_FaceLooksForward()
        {
            // Нос — самая выступающая вперёд часть головы: лицо смотрит в +Z.
            var mesh = Build(Look(beard: false), 1.9f);
            var head = 0.8f * 1.9f;
            var front = float.MinValue;
            var back = float.MaxValue;
            foreach (var v in mesh.vertices)
            {
                if (v.y < head)
                    continue;
                front = Mathf.Max(front, v.z);
                back = Mathf.Min(back, v.z);
            }
            Assert.That(front, Is.GreaterThan(0.15f), "Нос впереди");
            Assert.That(back, Is.LessThan(-0.1f), "Затылок сзади");
            Assert.That(front, Is.GreaterThan(-back), "Нос выступает дальше затылка");
        }

        [Test]
        public void Build_HoodLiesOnBack()
        {
            // Капюшон откинут: всё цвета капюшона ниже макушки и в среднем за спиной.
            var look = Look(beard: false);
            var mesh = Build(look, 1.9f);
            var hood = Linear(look.Hood);
            var colors = mesh.colors;
            var vertices = mesh.vertices;
            var sumZ = 0f;
            var count = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                if (colors[i] != hood)
                    continue;
                Assert.That(vertices[i].y, Is.LessThan(0.85f * 1.9f), "Капюшон не на голове");
                sumZ += vertices[i].z;
                count++;
            }
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(sumZ / count, Is.LessThan(0f), "Капюшон за спиной");
        }

        [Test]
        public void Build_ColorsComeFromLook()
        {
            var look = Look(beard: false);
            var colors = new HashSet<Color>(Build(look, 1.9f).colors);

            Assert.That(colors, Does.Contain(Linear(look.Skin)), "Кожа");
            Assert.That(colors, Does.Contain(Linear(look.Tunic)), "Рубаха");
            Assert.That(colors, Does.Contain(Linear(look.Hood)), "Капюшон");
            Assert.That(colors, Does.Contain(Linear(look.Hair)), "Брови цвета волос");
            foreach (var color in colors)
                Assert.That(color.a, Is.Zero, "Шейдер деревьев не красит части оттенком материала");
        }

        [Test]
        public void Build_BeardAddsFaces()
        {
            var plain = Build(Look(beard: false), 1.9f);
            var bearded = Build(Look(beard: true), 1.9f);

            Assert.That(bearded.vertexCount, Is.GreaterThan(plain.vertexCount));
        }

        [Test]
        public void Pick_SameSeedSameLook_DifferentSeedsVary()
        {
            var skins = new[] { Color.red, Color.green, Color.blue, Color.white };
            var hair = new[] { Color.black, Color.gray, Color.yellow };
            var tunics = new[] { Color.cyan, Color.magenta, Color.grey };
            var hood = new Color(0.7f, 0.6f, 0.5f);

            var a = WorkerLook.Pick(3, skins, hair, tunics, hood, 0.5f);
            var b = WorkerLook.Pick(3, skins, hair, tunics, hood, 0.5f);
            Assert.That(b.Skin, Is.EqualTo(a.Skin));
            Assert.That(b.Hair, Is.EqualTo(a.Hair));
            Assert.That(b.Tunic, Is.EqualTo(a.Tunic));
            Assert.That(b.Beard, Is.EqualTo(a.Beard));

            var looks = new HashSet<(Color, Color, Color, bool)>();
            for (var i = 0; i < 8; i++)
            {
                var look = WorkerLook.Pick(i, skins, hair, tunics, hood, 0.5f);
                Assert.That(look.Hood, Is.EqualTo(hood), "Капюшон у всех одного цвета");
                looks.Add((look.Skin, look.Hair, look.Tunic, look.Beard));
            }
            Assert.That(looks.Count, Is.GreaterThan(4), "У восьми рабочих облик разный");
        }

        [Test]
        public void Pick_EmptyPaletteGivesWhite()
        {
            var look = WorkerLook.Pick(1, null, new Color[0], new[] { Color.red }, Color.blue, 0f);

            Assert.That(look.Skin, Is.EqualTo(Color.white));
            Assert.That(look.Hair, Is.EqualTo(Color.white));
            Assert.That(look.Tunic, Is.EqualTo(Color.red));
            Assert.That(look.Beard, Is.False);
        }

        [Test]
        public void FacetedMesh_TransformMovesAndTurnsBodies()
        {
            var mesh = new FacetedMesh
            {
                Transform = Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one),
            };
            // Конус вверх на 1, повёрнутый на 90° вокруг X, смотрит вершиной в +Z.
            mesh.AddCone(4, Vector3.zero, 0.2f, 1f, 0f, null, _ => Color.white);
            var result = mesh.ToMesh("Cone");
            created.Add(result);

            var bounds = result.bounds;
            Assert.That(bounds.center.x, Is.EqualTo(5f).Within(1e-3f));
            Assert.That(bounds.max.z, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(bounds.size.y, Is.LessThan(0.5f));
            foreach (var n in result.normals)
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void FacetedMesh_LoftNormalsPointOutward()
        {
            var mesh = new FacetedMesh();
            mesh.AddLoft(new[] { new LoftRing(0f, 1f, 0.5f), new LoftRing(1f, 0.5f, 0.25f) }, 8, _ => Color.white);
            var result = mesh.ToMesh("Loft");
            created.Add(result);

            var bounds = result.bounds;
            Assert.That(bounds.size.x, Is.EqualTo(2f).Within(1e-3f), "Полуось по X");
            Assert.That(bounds.size.z, Is.EqualTo(1f).Within(1e-3f), "Полуось по Z");
            var vertices = result.vertices;
            var normals = result.normals;
            for (var i = 0; i < vertices.Length; i++)
            {
                // Крышки смотрят вниз и вверх, бока — от оси.
                var outward = new Vector3(vertices[i].x, vertices[i].y - 0.5f, vertices[i].z);
                Assert.That(Vector3.Dot(normals[i], outward), Is.GreaterThan(0f));
            }
        }

        private Mesh Build(WorkerLook look, float height)
        {
            var mesh = WorkerMeshBuilder.Build(look, height);
            created.Add(mesh);
            return mesh;
        }

        private static WorkerLook Look(bool beard) =>
            new(new Color(0.9f, 0.75f, 0.6f), new Color(0.23f, 0.16f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                new Color(0.73f, 0.66f, 0.53f), beard);

        private static Color Linear(Color color)
        {
            var linear = color.linear;
            linear.a = 0f;
            return linear;
        }
    }
}
