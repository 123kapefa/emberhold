using System.Collections;
using System.Linq;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Fog;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Enemies
{
    public sealed class CrawlerPrototypeSceneTests
    {
        private const float StepSeconds = 0.1f;
        // Спаунер выбирает точку случайно, а из части точек на рельефе прототипа ползун застревает
        // в ложбине (#49). Тесты берут одну и ту же точку, откуда путь до костра есть.
        private const int SpawnSeed = 0;

        private DayNightCycle cycle;
        private CrawlerSpawner spawner;
        private LightSource campfire;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            cycle = Object.FindAnyObjectByType<DayNightCycle>();
            spawner = Object.FindAnyObjectByType<CrawlerSpawner>();
            campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            Assert.That(spawner, Is.Not.Null, "В сцене есть спаунер ползунов");
            Assert.That(Object.FindAnyObjectByType<CrawlerStrike>(), Is.Not.Null, "Игрок может бить ползунов");
            // Пусть костёр не догорит сам, пока идёт тест.
            campfire.AddFuel(campfire.Fuel.Capacity);
        }

        [UnityTest]
        public IEnumerator Crawlers_ComeAtNightFromFog_AndDissolveAtDawn()
        {
            var fog = Object.FindAnyObjectByType<LightFog>();
            var zone = Object.FindAnyObjectByType<BuildZone>();

            cycle.Advance(cycle.Cycle.Duration(DayPhase.Day) - 1f);
            Assert.That(spawner.Crawlers, Is.Empty, "Днём ползунов нет");
            cycle.Advance(1f + cycle.Cycle.Duration(DayPhase.Dusk));
            Assert.That(cycle.IsNight, Is.True);
            Assert.That(spawner.Crawlers, Is.Empty, "Не в первую же секунду ночи");

            Random.InitState(SpawnSeed);
            cycle.Advance(spawner.Settings.FirstSpawnDelay + 0.1f);
            Assert.That(spawner.Crawlers.Count, Is.EqualTo(1), "Первый ползун вышел");
            var crawler = spawner.Crawlers[0];
            yield return null; // Туман пересчитывает вид суток в LateUpdate.
            var position = crawler.transform.position;
            Assert.That(zone.Contains(position), Is.False, "Появляется вне света");
            Assert.That(fog.DensityAt(position), Is.GreaterThan(0.5f), "Появляется в тумане");
            Assert.That(crawler.IsAlive, Is.True);

            cycle.Advance(spawner.Settings.SpawnInterval(cycle.DayNumber) * 3f);
            Assert.That(spawner.Crawlers.Count, Is.EqualTo(4), "Ночью выходят ещё");

            cycle.SkipToNextPhase();
            Assert.That(cycle.Phase, Is.EqualTo(DayPhase.Dawn));
            Assert.That(spawner.Crawlers, Is.Empty, "С рассветом растворяются");
            Assert.That(crawler.IsAlive, Is.False);
            yield return new WaitForSeconds(spawner.Settings.DissolveSeconds + 0.2f);
            Assert.That(crawler == null, Is.True, "Растворившийся ползун исчез из сцены");
        }

        [UnityTest]
        public IEnumerator Crawler_ReachesCampfire_DrainsFuel_AndDiesFromStrikes()
        {
            ToNight();
            var crawler = SpawnCrawler();
            Assert.That(crawler, Is.Not.Null);
            Assert.That(crawler.Target, Is.Null.Or.EqualTo(campfire));

            for (var i = 0; i < 2000 && !crawler.AtFire; i++)
                crawler.Step(StepSeconds);
            Assert.That(crawler.AtFire, Is.True, "Ползун дополз до костра");
            Assert.That(crawler.Target, Is.EqualTo(campfire));

            var before = campfire.Fuel.Amount;
            crawler.Step(1f);
            Assert.That(before - campfire.Fuel.Amount, Is.EqualTo(spawner.Settings.DrainPerSecond).Within(0.001f), "У огня гасит топливо");

            // Удар щелчком: луч сверху попадает в ползуна раньше земли.
            Physics.SyncTransforms();
            var ray = new Ray(crawler.transform.position + Vector3.up * 20f, Vector3.down);
            Assert.That(Crawler.TryPick(ray, 100f, out var picked), Is.True);
            Assert.That(picked, Is.EqualTo(crawler));

            Assert.That(crawler.Hit(1f), Is.False, "Одного удара мало");
            Assert.That(crawler.Hit(1f), Is.True, "Второй добивает");
            Assert.That(crawler.IsAlive, Is.False);
            Assert.That(spawner.Crawlers.Contains(crawler), Is.False, "Убитый ушёл из списка");
            before = campfire.Fuel.Amount;
            crawler.Step(StepSeconds);
            Assert.That(campfire.Fuel.Amount, Is.EqualTo(before), "Мёртвый не гасит");
            yield return new WaitForSeconds(spawner.Settings.DissolveSeconds + 0.2f);
            Assert.That(crawler == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Crawler_PutsOutCampfire_WhenNotStopped()
        {
            ToNight();
            var crawler = SpawnCrawler();
            var lit = true;
            campfire.LitChanged += isLit => lit = isLit;
            var maxSteps = (int)((campfire.Fuel.Capacity / spawner.Settings.DrainPerSecond + 120f) / StepSeconds);
            for (var i = 0; i < maxSteps && campfire.IsLit; i++)
                crawler.Step(StepSeconds);
            Assert.That(campfire.IsLit, Is.False, "Ползун погасил костёр");
            Assert.That(lit, Is.False);
            Assert.That(crawler.AtFire, Is.True, "Сидит у погасшего костра");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Crawler_StoppedByWallAroundCampfire()
        {
            ToNight();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var crawler = SpawnCrawler();
            Wall(view, campfire.transform.position, 3f, 4.5f, 3);

            for (var i = 0; i < 1500; i++)
                crawler.Step(StepSeconds);
            Assert.That(crawler.AtFire, Is.False, "Через стену в три блока не пройти");
            var toFire = crawler.transform.position - campfire.transform.position;
            toFire.y = 0f;
            Assert.That(toFire.magnitude, Is.GreaterThan(3f), "Остался снаружи стены");
            yield return null;
        }

        private Crawler SpawnCrawler()
        {
            Random.InitState(SpawnSeed);
            var crawler = spawner.TrySpawn();
            Assert.That(crawler, Is.Not.Null);
            return crawler;
        }

        private void ToNight()
        {
            cycle.Advance(cycle.Cycle.Duration(DayPhase.Day) + cycle.Cycle.Duration(DayPhase.Dusk) + 0.1f);
            Assert.That(cycle.IsNight, Is.True);
        }

        /// <summary>Кольцо стены вокруг точки: в каждом столбце от земли на height блоков вверх.</summary>
        private static void Wall(VoxelWorldView view, Vector3 center, float inner, float outer, int height)
        {
            var world = view.World;
            var local = view.transform.InverseTransformPoint(center);
            var stone = Block.Full(BlockMaterials.Stone);
            var r = Mathf.CeilToInt(outer);
            for (var dz = -r; dz <= r; dz++)
            for (var dx = -r; dx <= r; dx++)
            {
                var x = Mathf.FloorToInt(local.x) + dx;
                var z = Mathf.FloorToInt(local.z) + dz;
                var d = new Vector2(x + 0.5f - local.x, z + 0.5f - local.z).magnitude;
                if (d < inner || d > outer || !CrawlerFooting.TryTopSurface(world, x + 0.5f, z + 0.5f, out var ground))
                    continue;
                var y0 = Mathf.CeilToInt(ground);
                for (var y = y0; y < y0 + height; y++)
                    world.SetBlock(new Vector3Int(x, y, z), stone);
            }
        }
    }
}
