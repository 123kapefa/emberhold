using System.Collections;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Run;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Run
{
    public sealed class RunPrototypeSceneTests
    {
        // Та же точка появления, что в тестах ползуна: оттуда он доходит до костра.
        private const int SpawnSeed = 0;

        private RunController run;
        private LightSource campfire;
        private DayNightCycle cycle;
        private CrawlerSpawner spawner;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            run = Object.FindAnyObjectByType<RunController>();
            cycle = Object.FindAnyObjectByType<DayNightCycle>();
            spawner = Object.FindAnyObjectByType<CrawlerSpawner>();
            Assert.That(run, Is.Not.Null, "В сцене есть ход забега");
            campfire = run.Campfire;
            Assert.That(campfire, Is.EqualTo(Object.FindAnyObjectByType<BuildZone>().Sources[0]), "Главный костёр найден");
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FireOut_CountsDown_RelightCancels_ThenDefeat()
        {
            var defeated = false;
            run.Defeated += () => defeated = true;

            campfire.DrainFuel(campfire.Fuel.Capacity);
            Assert.That(campfire.IsLit, Is.False);
            run.Step(run.Countdown.Seconds - 1f);
            Assert.That(run.Countdown.IsCounting, Is.True, "Костёр погас — идёт отсчёт");
            Assert.That(run.IsGameOver, Is.False);

            campfire.AddFuel(1f);
            run.Step(0.1f);
            Assert.That(run.Countdown.IsCounting, Is.False, "Снова разожгли — отсчёт пропал");

            campfire.DrainFuel(campfire.Fuel.Capacity);
            run.Step(run.Countdown.Seconds + 0.1f);
            Assert.That(run.IsGameOver, Is.True, "Время вышло — проигрыш");
            Assert.That(defeated, Is.True);
            Assert.That(Time.timeScale, Is.Zero, "Игра на паузе");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Score_CountsNightsAndKills_ButNotDissolved()
        {
            campfire.AddFuel(campfire.Fuel.Capacity);
            cycle.SkipToNextPhase();
            cycle.SkipToNextPhase();
            Assert.That(cycle.IsNight, Is.True);

            Random.InitState(SpawnSeed);
            var killed = spawner.TrySpawn();
            var survivor = spawner.TrySpawn();
            Assert.That(killed, Is.Not.Null);
            Assert.That(survivor, Is.Not.Null);
            for (var i = 0; i < 10 && !killed.Hit(1f); i++) { }
            Assert.That(run.Score.CrawlersKilled, Is.EqualTo(1), "Убитый в счёт");

            cycle.SkipToNextPhase();
            Assert.That(cycle.Phase, Is.EqualTo(DayPhase.Dawn));
            Assert.That(survivor.IsAlive, Is.False);
            Assert.That(run.Score.CrawlersKilled, Is.EqualTo(1), "Растворившийся с рассветом не в счёт");
            Assert.That(run.Score.NightsSurvived, Is.EqualTo(1), "Рассвет — ночь пережита");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Restart_ReloadsSceneWithFreshScore()
        {
            run.Score.OnCrawlerKilled();
            campfire.DrainFuel(campfire.Fuel.Capacity);
            run.Step(run.Countdown.Seconds + 0.1f);
            Assert.That(run.IsGameOver, Is.True);

            var old = run;
            run.Restart();
            yield return null;
            yield return null;

            Assert.That(old == null, Is.True, "Старая сцена выгружена");
            var fresh = Object.FindAnyObjectByType<RunController>();
            Assert.That(fresh, Is.Not.Null);
            Assert.That(fresh.IsGameOver, Is.False);
            Assert.That(fresh.Score.CrawlersKilled, Is.Zero);
            Assert.That(fresh.Campfire.IsLit, Is.True, "Костёр снова горит");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "Пауза снята");
        }
    }
}
