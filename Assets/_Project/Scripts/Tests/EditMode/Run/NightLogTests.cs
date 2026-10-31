using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Run;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Run
{
    public sealed class NightLogTests
    {
        [Test]
        public void FuelIncreases_SplitIntoDayAndNight()
        {
            var log = new NightLog(1, 5f);
            log.Sample(DayPhase.Day, 1f, 8f, true, false, 0f, 0);
            log.StartNight(8f);
            log.Sample(DayPhase.Night, 1f, 10f, true, false, 0f, 0);
            var record = log.Close(true, 10f);
            Assert.That(record.BroughtByDay, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(record.BroughtAtNight, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(record.FuelAtNightStart, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(record.FuelAtEnd, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(record.Survived, Is.True);
        }

        [Test]
        public void LossBeyondBurning_IsEatenByCrawlers()
        {
            var log = new NightLog(1, 10f);
            log.StartNight(10f);
            // За 10 с ночи при расходе 0,1 в секунду сгорит 1 полено, а убыло 4: 3 съели ползуны.
            log.Sample(DayPhase.Night, 10f, 6f, true, false, 0.1f, 3);
            var record = log.Close(true, 6f);
            Assert.That(record.BurnedByFire, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(record.EatenByCrawlers, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(record.MinFuelAtNight, Is.EqualTo(6f).Within(1e-4f));
            Assert.That(record.MaxCrawlersAlive, Is.EqualTo(3));
        }

        [Test]
        public void BurnedByFire_NeverExceedsWhatWasLost()
        {
            var log = new NightLog(1, 1f);
            log.StartNight(1f);
            log.Sample(DayPhase.Night, 100f, 0f, true, true, 1f, 0);
            var record = log.Close(false, 0f);
            Assert.That(record.BurnedByFire, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(record.EatenByCrawlers, Is.Zero);
            Assert.That(record.Survived, Is.False);
        }

        [Test]
        public void LowAndOutSeconds_AreCounted()
        {
            var log = new NightLog(1, 1f);
            log.StartNight(1f);
            log.Sample(DayPhase.Night, 2f, 1f, true, true, 0f, 0);
            log.Sample(DayPhase.Night, 3f, 0f, false, false, 0f, 0);
            var record = log.Close(false, 0f);
            Assert.That(record.LowSeconds, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(record.OutSeconds, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void Close_StartsNextNight_FromZero()
        {
            var log = new NightLog(1, 5f);
            log.OnCrawlerSpawned();
            log.OnCrawlerKilled();
            var first = log.Close(true, 5f);
            Assert.That(first.CrawlersSpawned, Is.EqualTo(1));
            Assert.That(first.CrawlersKilled, Is.EqualTo(1));
            Assert.That(log.Current.Night, Is.EqualTo(2));
            Assert.That(log.Current.CrawlersSpawned, Is.Zero);
        }

        [Test]
        public void Csv_HasOneFieldPerColumn()
        {
            var line = PlaytestLog.ToCsv(new NightRecord { Night = 3, Survived = true, FuelAtEnd = 1.5f });
            Assert.That(line.Split(',').Length, Is.EqualTo(PlaytestLog.CsvHeader.Split(',').Length));
            Assert.That(line, Does.StartWith("3,1,"));
            Assert.That(line, Does.Contain("1.5"));
        }
    }
}
