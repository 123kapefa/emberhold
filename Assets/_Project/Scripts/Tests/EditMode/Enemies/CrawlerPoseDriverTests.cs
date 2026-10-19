using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Enemies.Bestiary;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Enemies
{
    /// <summary>Поза рига игрового ползуна по его состоянию: присосался к огню, удар, смерть.</summary>
    public sealed class CrawlerPoseDriverTests
    {
        private const float Dt = 1f / 30f;

        [Test]
        public void AtLitFire_LatchesAndGlows_ThenLetsGo()
        {
            var driver = new CrawlerPoseDriver();
            var pose = new CreaturePose();
            driver.Reset(2f);
            Run(driver, pose, 1f, alive: true, latched: true, health: 2f);
            Assert.That(pose.Latch, Is.EqualTo(1f));
            Assert.That(pose.Glow, Is.GreaterThan(0.3f));
            Run(driver, pose, 1f, alive: true, latched: false, health: 2f);
            Assert.That(pose.Latch, Is.EqualTo(0f), "Огонь погас — отпустил");
        }

        [Test]
        public void Strike_RecoilsWithFlash_ThenRecovers()
        {
            var driver = new CrawlerPoseDriver();
            var pose = new CreaturePose();
            driver.Reset(2f);
            Run(driver, pose, 0.2f, alive: true, latched: false, health: 1f);
            Assert.That(pose.Recoil, Is.GreaterThan(0.5f), "Отшатнулся");
            Run(driver, pose, 0.5f, alive: true, latched: false, health: 1f);
            Assert.That(pose.Recoil, Is.EqualTo(0f));
            Assert.That(pose.Flash, Is.EqualTo(0f));
            Assert.That(pose.Fold, Is.EqualTo(0f), "Жив — не складывается");
        }

        [Test]
        public void Death_FoldsAndFades()
        {
            var driver = new CrawlerPoseDriver();
            var pose = new CreaturePose();
            driver.Reset(2f);
            Run(driver, pose, 1f, alive: true, latched: true, health: 2f);
            Run(driver, pose, 1f, alive: false, latched: false, health: 0f);
            Assert.That(pose.Fold, Is.EqualTo(1f));
            Assert.That(pose.Fade, Is.GreaterThan(0.5f));
            Assert.That(pose.Latch, Is.EqualTo(0f));
        }

        private static void Run(CrawlerPoseDriver driver, CreaturePose pose, float seconds, bool alive, bool latched, float health)
        {
            for (var t = 0f; t < seconds; t += Dt)
                driver.Update(Dt, alive, latched, health, pose);
        }
    }
}
