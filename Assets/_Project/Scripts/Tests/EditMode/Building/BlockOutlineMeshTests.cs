using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Building
{
    public sealed class BlockOutlineMeshTests
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();

        [Test]
        public void Build_BoxFollowsBlockHeight()
        {
            BlockOutlineMesh.Build(0.5f, Vector3Int.zero, 0f, vertices, indices);

            // 12 рёбер по 2 индекса, без метки грани.
            Assert.That(indices.Count, Is.EqualTo(24));
            Assert.That(vertices.Count, Is.EqualTo(8));
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices)
                bounds.Encapsulate(v);
            Assert.That(bounds.min, Is.EqualTo(Vector3.zero));
            Assert.That(bounds.max, Is.EqualTo(new Vector3(1f, 0.5f, 1f)));
        }

        [Test]
        public void Build_FaceMarkLiesOnHoveredFace()
        {
            BlockOutlineMesh.Build(0.75f, Vector3Int.up, 0f, vertices, indices);

            Assert.That(vertices.Count, Is.EqualTo(12));
            Assert.That(indices.Count, Is.EqualTo(32));
            for (var i = 8; i < 12; i++)
                Assert.That(vertices[i].y, Is.EqualTo(0.75f).Within(1e-5f));

            BlockOutlineMesh.Build(1f, Vector3Int.left, 0f, vertices, indices);
            for (var i = 8; i < 12; i++)
                Assert.That(vertices[i].x, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Build_EdgesAreAxisAligned()
        {
            BlockOutlineMesh.Build(1f, Vector3Int.forward, 0.01f, vertices, indices);

            for (var i = 0; i < indices.Count; i += 2)
            {
                var d = vertices[indices[i + 1]] - vertices[indices[i]];
                var changedAxes = (d.x != 0f ? 1 : 0) + (d.y != 0f ? 1 : 0) + (d.z != 0f ? 1 : 0);
                Assert.That(changedAxes, Is.EqualTo(1), $"Ребро {i / 2}: {d}");
            }
        }
    }
}
