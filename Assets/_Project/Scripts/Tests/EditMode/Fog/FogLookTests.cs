using Emberhold.Gameplay.Fog;
using Emberhold.Gameplay.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Fog
{
    public sealed class FogLookTests
    {
        [Test]
        public void Density_IsZeroInsideLightAndGrowsOutside()
        {
            var look = new FogLook(Color.gray, 0.8f, 10f);
            Assert.That(look.DensityAt(0f), Is.Zero, "Край света");
            Assert.That(look.DensityAt(-5f), Is.Zero, "Внутри");
            Assert.That(look.DensityAt(2f), Is.GreaterThan(0f).And.LessThan(look.DensityAt(10f)));
            Assert.That(look.DensityAt(10f), Is.EqualTo(0.8f * (1f - Mathf.Exp(-1f))).Within(1e-5f));
            Assert.That(look.DensityAt(1000f), Is.EqualTo(0.8f).Within(1e-4f), "Вдали — полная непрозрачность");
            Assert.That(look.DensityAt(float.PositiveInfinity), Is.EqualTo(0.8f), "Без источников — сплошной туман");
        }

        [Test]
        public void NightFog_IsDenserThanDayFog()
        {
            var settings = ScriptableObject.CreateInstance<FogSettings>();
            try
            {
                var day = settings.LookAt(0f);
                var night = settings.LookAt(1f);
                Assert.That(night.Opacity, Is.GreaterThan(day.Opacity));
                foreach (var distance in new[] { 1f, 3f, 10f, 30f })
                    Assert.That(night.DensityAt(distance), Is.GreaterThan(day.DensityAt(distance)), $"{distance} блоков за краем");
                Assert.That(night.Color.grayscale, Is.LessThan(day.Color.grayscale), "Ночной туман темнее");

                var dusk = settings.LookAt(0.5f);
                Assert.That(dusk.Opacity, Is.InRange(day.Opacity, night.Opacity));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DistanceOutside_MeasuresToNearestLightEdge()
        {
            var zone = new LightZone();
            Assert.That(zone.DistanceOutside(Vector3.zero), Is.EqualTo(float.PositiveInfinity), "Без источников");

            zone.Add(new LightCircle(Vector3.zero, 5f));
            zone.Add(new LightCircle(new Vector3(20f, 0f, 0f), 2f));
            Assert.That(zone.DistanceOutside(new Vector3(3f, 0f, 0f)), Is.Zero, "Внутри");
            Assert.That(zone.DistanceOutside(new Vector3(0f, 40f, 8f)), Is.EqualTo(3f).Within(1e-5f), "Высота не важна");
            Assert.That(zone.DistanceOutside(new Vector3(15f, 0f, 0f)), Is.EqualTo(3f).Within(1e-5f), "Ближе ко второму кругу");
        }
    }
}
