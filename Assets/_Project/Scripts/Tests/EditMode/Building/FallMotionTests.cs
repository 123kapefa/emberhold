using Emberhold.Gameplay.Building;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Building
{
    public sealed class FallMotionTests
    {
        private const float Gravity = 30f;

        [Test]
        public void Drop_AcceleratesAndStopsAtDistance()
        {
            var first = FallMotion.Drop(10f, 0.1f, Gravity);
            var second = FallMotion.Drop(10f, 0.2f, Gravity) - first;

            Assert.That(FallMotion.Drop(10f, 0f, Gravity), Is.Zero);
            Assert.That(second, Is.GreaterThan(first), "Вторая десятая секунды быстрее первой");
            Assert.That(FallMotion.Drop(10f, 100f, Gravity), Is.EqualTo(10f));
        }

        [Test]
        public void FallSeconds_IsWhenDropReachesDistance()
        {
            var seconds = FallMotion.FallSeconds(4f, Gravity);

            Assert.That(FallMotion.Drop(4f, seconds, Gravity), Is.EqualTo(4f).Within(1e-4f));
            Assert.That(FallMotion.Drop(4f, seconds * 0.99f, Gravity), Is.LessThan(4f));
            Assert.That(FallMotion.FallSeconds(0f, Gravity), Is.Zero);
        }

        [Test]
        public void Offset_BouncesUpAfterLandingAndSettles()
        {
            const float bounceSeconds = 0.12f;
            const float bounce = 0.06f;
            var landing = FallMotion.FallSeconds(3f, Gravity);

            Assert.That(FallMotion.Offset(3f, 0f, Gravity, bounceSeconds, bounce), Is.Zero);
            Assert.That(FallMotion.Offset(3f, landing, Gravity, bounceSeconds, bounce), Is.EqualTo(-3f).Within(1e-4f));
            Assert.That(FallMotion.Offset(3f, landing + bounceSeconds * 0.5f, Gravity, bounceSeconds, bounce),
                Is.EqualTo(-3f + bounce).Within(1e-4f), "Подскок в середине");
            Assert.That(FallMotion.Offset(3f, landing + bounceSeconds, Gravity, bounceSeconds, bounce), Is.EqualTo(-3f));
        }
    }
}
