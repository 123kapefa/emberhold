using Emberhold.Gameplay.Lighting;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Lighting
{
    public sealed class LightFuelTests
    {
        [Test]
        public void New_ClampsStartAmountToCapacity()
        {
            Assert.That(new LightFuel(10f, 5f).Amount, Is.EqualTo(5f));
            Assert.That(new LightFuel(10f, 15f).Amount, Is.EqualTo(10f));
            Assert.That(new LightFuel(10f, -1f).IsEmpty, Is.True);
        }

        [Test]
        public void Burn_SpendsRateTimesSecondsUntilEmpty()
        {
            var fuel = new LightFuel(10f, 5f);
            Assert.That(fuel.Burn(30f, 0.1f), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(fuel.Amount, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(fuel.IsEmpty, Is.False);

            Assert.That(fuel.Burn(100f, 0.1f), Is.EqualTo(2f).Within(1e-5f), "Сгорает не больше, чем есть");
            Assert.That(fuel.Amount, Is.Zero);
            Assert.That(fuel.IsEmpty, Is.True);
            Assert.That(fuel.Burn(10f, 0.1f), Is.Zero);
        }

        [Test]
        public void Burn_IgnoresNegativeTime()
        {
            var fuel = new LightFuel(10f, 5f);
            Assert.That(fuel.Burn(-10f, 1f), Is.Zero);
            Assert.That(fuel.Amount, Is.EqualTo(5f));
        }

        [Test]
        public void Add_FillsUpToCapacity()
        {
            var fuel = new LightFuel(10f, 0f);
            Assert.That(fuel.Add(3f), Is.EqualTo(3f));
            Assert.That(fuel.Amount, Is.EqualTo(3f));
            Assert.That(fuel.Add(20f), Is.EqualTo(7f), "Лишнее не берётся");
            Assert.That(fuel.Amount, Is.EqualTo(10f));
            Assert.That(fuel.Add(1f), Is.Zero);
            Assert.That(fuel.Add(-2f), Is.Zero);
        }

        [Test]
        public void RadiusFactor_FullUntilLowThenShrinksThenOut()
        {
            Assert.That(LightFuel.RadiusFactor(5f, 2f, 0.4f), Is.EqualTo(1f));
            Assert.That(LightFuel.RadiusFactor(2f, 2f, 0.4f), Is.EqualTo(1f), "Ровно на пороге — ещё полный");
            Assert.That(LightFuel.RadiusFactor(1f, 2f, 0.4f), Is.EqualTo(0.7f).Within(1e-5f), "Полпути — между минимумом и полным");
            Assert.That(LightFuel.RadiusFactor(0.001f, 2f, 0.4f), Is.EqualTo(0.4f).Within(0.001f), "Последние крохи — минимум");
            Assert.That(LightFuel.RadiusFactor(0f, 2f, 0.4f), Is.Zero, "Без топлива огонь гаснет");
        }
    }
}
