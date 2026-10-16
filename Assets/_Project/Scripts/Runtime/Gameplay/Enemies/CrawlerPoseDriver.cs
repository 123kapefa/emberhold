using Emberhold.Gameplay.Enemies.Bestiary;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Из состояния ползуна — в позу рига: присосался (latch) и жар (glow), удар (recoil и вспышка), смерть (fold и fade).
    /// Отдельно от компонента, чтобы проверять без сцены.
    /// </summary>
    public sealed class CrawlerPoseDriver
    {
        /// <summary>Быстрее ног не перебираем: тесты двигают ползуна рывком за один кадр.</summary>
        public const float MaxSpeed = 4f;

        private const float HitSeconds = 0.5f;
        private const float FoldSeconds = 0.6f;

        private float health;
        private float hitTime = -1f;
        private float deathTime = -1f;

        public void Reset(float startHealth)
        {
            health = startHealth;
            hitTime = -1f;
            deathTime = -1f;
        }

        public void Update(float dt, bool alive, bool latched, float currentHealth, CreaturePose pose)
        {
            // Удар: здоровье убыло (и добивающий тоже — смерть начинается с отшатывания).
            if (currentHealth < health)
                hitTime = 0f;
            health = currentHealth;
            if (!alive && deathTime < 0f)
                deathTime = 0f;

            if (deathTime >= 0f)
            {
                deathTime += dt;
                var t = deathTime;
                pose.Latch = Mathf.Max(0f, pose.Latch - dt * 3f);
                pose.Glow = Mathf.Max(0f, pose.Glow - dt * 2f);
                pose.Recoil = Mathf.Max(0f, 0.6f - t * 2f);
                pose.Flash = Mathf.Max(0f, 1f - t * 3f);
                pose.Fold = Mathf.Min(1f, t / FoldSeconds);
                pose.Fade = Mathf.Min(1f, t / FoldSeconds) * 0.6f;
                return;
            }

            pose.Latch = latched ? Mathf.Min(1f, pose.Latch + dt * 2.5f) : Mathf.Max(0f, pose.Latch - dt * 3f);
            pose.Glow = latched ? Mathf.Min(1f, pose.Glow + dt * 0.4f) : Mathf.Max(0f, pose.Glow - dt * 0.5f);
            if (hitTime >= 0f)
            {
                hitTime += dt;
                var k = hitTime / HitSeconds;
                pose.Recoil = Mathf.Max(0f, Mathf.Sin(Mathf.Min(1f, k) * Mathf.PI));
                pose.Flash = Mathf.Max(0f, 1f - k * 2.2f);
                if (k >= 1f)
                {
                    hitTime = -1f;
                    pose.Recoil = 0f;
                    pose.Flash = 0f;
                }
            }
        }
    }
}
