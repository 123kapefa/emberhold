using Emberhold.Gameplay.Run;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Run
{
    public sealed class DefeatCountdownTests
    {
        [Test]
        public void WhileFireBurns_NothingCounts()
        {
            var countdown = new DefeatCountdown(10f);
            Assert.That(countdown.Tick(true, 100f), Is.False);
            Assert.That(countdown.IsCounting, Is.False);
            Assert.That(countdown.IsDefeated, Is.False);
            Assert.That(countdown.Remaining, Is.EqualTo(10f));
        }

        [Test]
        public void FireOut_CountsDown_AndDefeatsWhenTimeRunsOut()
        {
            var countdown = new DefeatCountdown(10f);
            Assert.That(countdown.Tick(false, 4f), Is.False);
            Assert.That(countdown.IsCounting, Is.True);
            Assert.That(countdown.Remaining, Is.EqualTo(6f).Within(1e-5f));

            Assert.That(countdown.Tick(false, 5.9f), Is.False, "Ещё есть доля секунды");
            Assert.That(countdown.Tick(false, 0.2f), Is.True, "Время вышло — проигрыш в этот шаг");
            Assert.That(countdown.IsDefeated, Is.True);
            Assert.That(countdown.IsCounting, Is.False);
            Assert.That(countdown.Remaining, Is.Zero);
        }

        [Test]
        public void Relit_ResetsCountdown()
        {
            var countdown = new DefeatCountdown(10f);
            countdown.Tick(false, 8f);
            countdown.Tick(true, 0.1f);
            Assert.That(countdown.IsCounting, Is.False);
            Assert.That(countdown.Remaining, Is.EqualTo(10f), "Снова разожгли — время полностью вернулось");

            Assert.That(countdown.Tick(false, 8f), Is.False, "Новый отсчёт с начала");
            Assert.That(countdown.IsDefeated, Is.False);
        }

        [Test]
        public void Defeat_IsFinal_EvenIfFireIsRelit()
        {
            var countdown = new DefeatCountdown(1f);
            countdown.Tick(false, 2f);
            Assert.That(countdown.Tick(true, 1f), Is.False);
            Assert.That(countdown.Tick(false, 1f), Is.False, "Проигрыш сообщается один раз");
            Assert.That(countdown.IsDefeated, Is.True);
        }

        [Test]
        public void ZeroSeconds_DefeatsOnFirstStepWithoutFire()
        {
            var countdown = new DefeatCountdown(0f);
            Assert.That(countdown.Tick(false, 0f), Is.True);
        }
    }
}
