using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Building
{
    public sealed class BlockCrackMeshTests
    {
        [Test]
        public void Append_MoreStages_MoreCracksAndEarlierOnesStay()
        {
            var (v1, i1) = Build(stage: 1);
            var (v3, i3) = Build(stage: 3);

            Assert.That(i1.Count, Is.GreaterThan(0));
            Assert.That(i3.Count, Is.EqualTo(i1.Count * 3));
            // Трещины первого этапа — первые на каждой грани и на тех же местах.
            Assert.That(v3, Does.Contain(v1[0]));
            Assert.That(v3, Does.Contain(v1[v1.Count - 1]));
        }

        [Test]
        public void Append_StripsFaceOutward()
        {
            var (vertices, indices) = Build(stage: 2);

            for (var i = 0; i < indices.Count; i += 3)
            {
                var a = vertices[indices[i]];
                var normal = Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a);
                // Наружу — значит от центра блока.
                Assert.That(Vector3.Dot(normal, a - new Vector3(0.5f, 0.5f, 0.5f)), Is.GreaterThan(0f), $"Треугольник {i / 3}");
            }
        }

        [Test]
        public void Append_StageZero_AddsNothing()
        {
            var (vertices, indices) = Build(stage: 0);

            Assert.That(vertices, Is.Empty);
            Assert.That(indices, Is.Empty);
        }

        [Test]
        public void Append_CracksStayOnPartialBlockSurface()
        {
            var origin = new Vector3(3f, 4f, 2f);
            var (vertices, _) = Build(stage: BlockCrackMesh.MaxStages, height: 0.5f, origin: origin);

            foreach (var v in vertices)
            {
                var local = v - origin;
                Assert.That(local.x, Is.InRange(-0.011f, 1.011f));
                Assert.That(local.y, Is.InRange(0f, 0.511f));
                Assert.That(local.z, Is.InRange(-0.011f, 1.011f));
            }
        }

        [Test]
        public void Append_CracksStayOnFormExtent()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var min = new Vector3(0.25f, 0f, 0.25f);
            var max = new Vector3(0.75f, 1f, 0.75f);
            BlockCrackMesh.Append(Vector3.zero, min, max, BlockCrackMesh.MaxStages, seed: 7, offset: 0.01f, vertices, indices);

            Assert.That(vertices, Is.Not.Empty);
            foreach (var v in vertices)
            {
                Assert.That(v.x, Is.InRange(0.239f, 0.761f));
                Assert.That(v.y, Is.InRange(0f, 1.011f));
                Assert.That(v.z, Is.InRange(0.239f, 0.761f));
            }
        }

        private static (List<Vector3>, List<int>) Build(int stage, float height = 1f, Vector3 origin = default)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            BlockCrackMesh.Append(origin, height, stage, seed: 42, offset: 0.01f, vertices, indices);
            return (vertices, indices);
        }
    }
}
