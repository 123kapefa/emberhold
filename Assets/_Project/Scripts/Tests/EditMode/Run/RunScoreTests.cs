using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Run;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Run
{
    public sealed class RunScoreTests
    {
        [Test]
        public void Night_CountsAsSurvived_AtDawn()
        {
            var score = new RunScore();
            score.OnPhaseChanged(DayPhase.Dusk);
            score.OnPhaseChanged(DayPhase.Night);
            Assert.That(score.NightsSurvived, Is.Zero, "Ночь ещё идёт");
            score.OnPhaseChanged(DayPhase.Dawn);
            Assert.That(score.NightsSurvived, Is.EqualTo(1));
            score.OnPhaseChanged(DayPhase.Day);
            score.OnPhaseChanged(DayPhase.Dusk);
            score.OnPhaseChanged(DayPhase.Night);
            score.OnPhaseChanged(DayPhase.Dawn);
            Assert.That(score.NightsSurvived, Is.EqualTo(2));
        }

        [Test]
        public void Kills_AreCounted()
        {
            var score = new RunScore();
            score.OnCrawlerKilled();
            score.OnCrawlerKilled();
            Assert.That(score.CrawlersKilled, Is.EqualTo(2));
        }

        [Test]
        public void AfterFinish_ScoreStops()
        {
            var score = new RunScore();
            score.OnCrawlerKilled();
            score.Finish();
            score.OnCrawlerKilled();
            score.OnPhaseChanged(DayPhase.Dawn);
            Assert.That(score.IsFinal, Is.True);
            Assert.That(score.CrawlersKilled, Is.EqualTo(1));
            Assert.That(score.NightsSurvived, Is.Zero);
        }
    }
}
