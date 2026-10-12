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
        public void Density_ThinsOutWithHeight()
        {
            var look = new FogLook(Color.gray, 0.8f, 10f, 8f, 16f);
            var far = look.DensityAt(1000f);
            Assert.That(look.HeightFactor(0f), Is.EqualTo(1f), "Низина");
            Assert.That(look.HeightFactor(8f), Is.EqualTo(1f), "Граница густого тумана");
            Assert.That(look.HeightFactor(12f), Is.EqualTo(0.5f).Within(1e-5f), "Посередине — половина");
            Assert.That(look.HeightFactor(10f), Is.GreaterThan(look.HeightFactor(14f)), "Выше — реже");
            Assert.That(look.HeightFactor(16f), Is.Zero, "Верхняя граница");
            Assert.That(look.HeightFactor(30f), Is.Zero, "Плато над туманом");

            Assert.That(look.DensityAt(1000f, 5f), Is.EqualTo(far).Within(1e-6f), "В низине туман как раньше");
            Assert.That(look.DensityAt(1000f, 12f), Is.EqualTo(far * 0.5f).Within(1e-5f));
            Assert.That(look.DensityAt(1000f, 20f), Is.Zero);
            Assert.That(look.DensityAt(0f, 0f), Is.Zero, "У огня тумана нет даже в низине");
            Assert.That(look.DensityAt(-3f, 2f), Is.Zero);

            var flat = new FogLook(Color.gray, 0.8f, 10f);
            Assert.That(flat.DensityAt(1000f, 100f), Is.EqualTo(far), "Без границы по высоте туман везде");
        }

        [Test]
        public void FogRisesAtNightAndHigherOnSpecialNights()
        {
            var settings = ScriptableObject.CreateInstance<FogSettings>();
            try
            {
                var day = settings.LookAt(0f);
                Assert.That(day.DenseBelow, Is.EqualTo(8f), "Днём густой ниже 8");
                Assert.That(day.ClearAbove, Is.EqualTo(16f), "Днём туман ниже 16");
                Assert.That(settings.LookAt(0f, true).ClearAbove, Is.EqualTo(16f), "Особая ночь днём ничего не меняет");

                var night = settings.LookAt(1f);
                var special = settings.LookAt(1f, true);
                var dusk = settings.LookAt(0.5f);
                Assert.That(night.ClearAbove, Is.EqualTo(16f + settings.NightRise));
                Assert.That(night.DenseBelow, Is.EqualTo(8f + settings.NightRise));
                Assert.That(special.ClearAbove, Is.EqualTo(16f + settings.NightRise + settings.SpecialNightRise));
                Assert.That(dusk.ClearAbove, Is.InRange(day.ClearAbove, night.ClearAbove), "В сумерки граница ползёт вверх");

                const float height = 18f;
                Assert.That(day.DensityAt(1000f, height), Is.Zero, "Днём на 18 блоках тумана нет");
                Assert.That(night.DensityAt(1000f, height), Is.GreaterThan(0f), "Ночью туман доходит до 18");
                Assert.That(special.DensityAt(1000f, height), Is.GreaterThan(night.DensityAt(1000f, height)), "В особую ночь гуще");
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
