using Emberhold.Gameplay.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Lighting
{
    public sealed class LightZoneTests
    {
        [Test]
        public void EmptyZone_ContainsNothing()
        {
            var zone = new LightZone();
            Assert.That(zone.Contains(Vector3.zero), Is.False);
        }

        [Test]
        public void Circle_ContainsPointsUpToRadius()
        {
            var circle = new LightCircle(new Vector3(10f, 5f, -3f), 4f);
            Assert.That(circle.Contains(new Vector3(10f, 5f, -3f)), Is.True, "Центр");
            Assert.That(circle.Contains(new Vector3(14f, 5f, -3f)), Is.True, "Ровно на границе");
            Assert.That(circle.Contains(new Vector3(12.8f, 5f, -0.2f)), Is.True, "Внутри по диагонали");
            Assert.That(circle.Contains(new Vector3(14.01f, 5f, -3f)), Is.False, "Чуть дальше радиуса");
            Assert.That(circle.Contains(new Vector3(13f, 5f, 0f)), Is.False, "Снаружи по диагонали");
        }

        [Test]
        public void Circle_IgnoresHeight()
        {
            var circle = new LightCircle(Vector3.zero, 3f);
            Assert.That(circle.Contains(new Vector3(1f, 50f, 1f)), Is.True);
            Assert.That(circle.Contains(new Vector3(1f, -20f, 1f)), Is.True);
        }

        [Test]
        public void Zone_IsUnionOfCircles()
        {
            var zone = new LightZone();
            zone.Add(new LightCircle(Vector3.zero, 2f));
            zone.Add(new LightCircle(new Vector3(10f, 0f, 0f), 2f));

            Assert.That(zone.Contains(new Vector3(1f, 0f, 0f)), Is.True);
            Assert.That(zone.Contains(new Vector3(11f, 0f, 0f)), Is.True);
            Assert.That(zone.Contains(new Vector3(5f, 0f, 0f)), Is.False, "Между кругами темно");

            zone.Clear();
            Assert.That(zone.Contains(new Vector3(1f, 0f, 0f)), Is.False);
        }
    }
}
