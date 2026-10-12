using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Движение падающего тела: свободное падение с нуля с ускорением gravity (блоков/с²),
    /// после удара — короткий подскок вверх и обратно. Расстояния — в блоках, время — в секундах.
    /// </summary>
    public static class FallMotion
    {
        /// <summary>Сколько секунд падать на distance блоков.</summary>
        public static float FallSeconds(float distance, float gravity) =>
            distance <= 0f ? 0f : Mathf.Sqrt(2f * distance / gravity);

        /// <summary>Сколько блоков пролетело тело за elapsed секунд, не больше distance.</summary>
        public static float Drop(float distance, float elapsed, float gravity) =>
            elapsed <= 0f ? 0f : Mathf.Min(distance, 0.5f * gravity * elapsed * elapsed);

        /// <summary>Высота подскока через sinceLanding секунд после удара: ноль в момент удара и в конце.</summary>
        public static float Bounce(float sinceLanding, float seconds, float height) =>
            sinceLanding <= 0f || sinceLanding >= seconds ? 0f : height * Mathf.Sin(Mathf.PI * sinceLanding / seconds);

        /// <summary>Смещение по высоте от исходного места через elapsed секунд: отрицательное, пока тело летит вниз.</summary>
        public static float Offset(float distance, float elapsed, float gravity, float bounceSeconds, float bounceHeight)
        {
            var fall = FallSeconds(distance, gravity);
            return -Drop(distance, elapsed, gravity) + Bounce(elapsed - fall, bounceSeconds, bounceHeight);
        }
    }
}
