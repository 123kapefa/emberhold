using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class WorkerRoleLookTests
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
        public void EveryRole_StandsOnFeet_AndAddsGearToFreeBody()
        {
            var free = Build(Look(WorkerRole.Free));
            foreach (WorkerRole role in Enum.GetValues(typeof(WorkerRole)))
            {
                var mesh = Build(Look(role));
                Assert.That(mesh.bounds.min.y, Is.EqualTo(0f).Within(1e-3f), $"{role}: ноги на земле");
                if (role != WorkerRole.Free)
                    Assert.That(mesh.vertexCount, Is.GreaterThan(free.vertexCount), $"{role}: есть снаряжение");
                foreach (var n in mesh.normals)
                    Assert.That(n.magnitude, Is.EqualTo(1f).Within(1e-3f), $"{role}: нормали");
            }
        }

        [Test]
        public void HeldGear_StaysCloseToBody_ExceptSpear()
        {
            foreach (WorkerRole role in Enum.GetValues(typeof(WorkerRole)))
            {
                var bounds = Build(Look(role)).bounds;
                Assert.That(Mathf.Max(-bounds.min.x, bounds.max.x), Is.LessThan(0.55f), $"{role}: в ширину около клетки");
                Assert.That(Mathf.Max(-bounds.min.z, bounds.max.z), Is.LessThan(0.55f), $"{role}: в глубину около клетки");
                if (role == WorkerRole.Spearman)
                    Assert.That(bounds.max.y, Is.GreaterThan(1.9f * 1.1f), "Копьё выше головы");
                else
                    Assert.That(bounds.max.y, Is.LessThan(1.9f * 1.1f), $"{role}: не выше роста с шапкой");
            }
        }

        [Test]
        public void Settings_RoleChangesHoodAndKeepsFace()
        {
            var settings = ScriptableObject.CreateInstance<WorkerSettings>();
            created.Add(settings);
            var free = settings.LookFor(3);
            var digger = settings.LookFor(3, WorkerRole.Digger);

            Assert.That(digger.Role, Is.EqualTo(WorkerRole.Digger));
            Assert.That(digger.Skin, Is.EqualTo(free.Skin));
            Assert.That(digger.Hair, Is.EqualTo(free.Hair));
            Assert.That(digger.Tunic, Is.EqualTo(free.Tunic));
            Assert.That(digger.Beard, Is.EqualTo(free.Beard));
            Assert.That(digger.Hood, Is.Not.EqualTo(free.Hood), "У ролей свой цвет капюшона");
            Assert.That(digger.Hood, Is.EqualTo(settings.HoodFor(WorkerRole.Digger)));
        }

        private Mesh Build(WorkerLook look)
        {
            var mesh = WorkerMeshBuilder.Build(look, 1.9f);
            created.Add(mesh);
            return mesh;
        }

        private static WorkerLook Look(WorkerRole role) =>
            new(new Color(0.9f, 0.75f, 0.6f), new Color(0.23f, 0.16f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                new Color(0.5f, 0.15f, 0.15f), true, role);
    }
}
