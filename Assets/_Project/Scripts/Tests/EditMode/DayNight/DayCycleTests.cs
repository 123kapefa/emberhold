using Emberhold.Gameplay.DayNight;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.DayNight
{
    public sealed class DayCycleTests
    {
        // Сутки из дизайн-документа: день 6 минут, сумерки 1, ночь 4, рассвет 1.
        private static DayCycle DesignDay() => new(360f, 60f, 240f, 60f);

        [Test]
        public void DesignDay_Lasts12Minutes()
        {
            var cycle = DesignDay();
            Assert.That(cycle.Length, Is.EqualTo(720f));
            Assert.That(cycle.StartOf(DayPhase.Day), Is.EqualTo(0f));
            Assert.That(cycle.StartOf(DayPhase.Dusk), Is.EqualTo(360f));
            Assert.That(cycle.StartOf(DayPhase.Night), Is.EqualTo(420f));
            Assert.That(cycle.StartOf(DayPhase.Dawn), Is.EqualTo(660f));
        }

        [Test]
        public void PhaseAt_FollowsSchedule()
        {
            var cycle = DesignDay();
            Assert.That(cycle.PhaseAt(0f), Is.EqualTo(DayPhase.Day));
            Assert.That(cycle.PhaseAt(359.9f), Is.EqualTo(DayPhase.Day));
            Assert.That(cycle.PhaseAt(360f), Is.EqualTo(DayPhase.Dusk));
            Assert.That(cycle.PhaseAt(420f), Is.EqualTo(DayPhase.Night));
            Assert.That(cycle.PhaseAt(659f), Is.EqualTo(DayPhase.Night));
            Assert.That(cycle.PhaseAt(660f), Is.EqualTo(DayPhase.Dawn));

            cycle.PhaseAt(540f, out var progress);
            Assert.That(progress, Is.EqualTo(0.5f).Within(1e-5f), "Середина ночи");
        }

        [Test]
        public void PhaseAt_WrapsAroundDay()
        {
            var cycle = DesignDay();
            Assert.That(cycle.PhaseAt(720f), Is.EqualTo(DayPhase.Day));
            Assert.That(cycle.PhaseAt(720f + 430f), Is.EqualTo(DayPhase.Night));
            Assert.That(cycle.PhaseAt(-10f), Is.EqualTo(DayPhase.Dawn), "Отрицательное время — конец прошлых суток");
        }

        [Test]
        public void Darkness_RisesAtDuskAndFallsAtDawn()
        {
            var cycle = DesignDay();
            Assert.That(cycle.DarknessAt(100f), Is.Zero, "День");
            Assert.That(cycle.DarknessAt(500f), Is.EqualTo(1f), "Ночь");
            Assert.That(cycle.DarknessAt(390f), Is.EqualTo(0.5f).Within(1e-4f), "Середина сумерек");
            Assert.That(cycle.DarknessAt(690f), Is.EqualTo(0.5f).Within(1e-4f), "Середина рассвета");
            Assert.That(cycle.DarknessAt(370f), Is.LessThan(cycle.DarknessAt(400f)), "В сумерки темнеет");
            Assert.That(cycle.DarknessAt(670f), Is.GreaterThan(cycle.DarknessAt(700f)), "На рассвете светлеет");
        }

        [Test]
        public void ZeroLengthPhase_IsSkipped()
        {
            var cycle = new DayCycle(10f, 0f, 10f, 0f);
            Assert.That(cycle.Length, Is.EqualTo(20f));
            Assert.That(cycle.PhaseAt(10f), Is.EqualTo(DayPhase.Night), "Без сумерек день сразу сменяется ночью");
            Assert.That(cycle.PhaseAt(19.9f), Is.EqualTo(DayPhase.Night));
        }

        [Test]
        public void Next_GoesRound()
        {
            Assert.That(DayCycle.Next(DayPhase.Day), Is.EqualTo(DayPhase.Dusk));
            Assert.That(DayCycle.Next(DayPhase.Night), Is.EqualTo(DayPhase.Dawn));
            Assert.That(DayCycle.Next(DayPhase.Dawn), Is.EqualTo(DayPhase.Day));
        }
    }
}
