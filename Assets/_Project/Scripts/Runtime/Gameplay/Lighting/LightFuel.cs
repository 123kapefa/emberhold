using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Запас топлива источника света в поленьях. Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// Ночью топливо сгорает; когда его мало, радиус света сжимается, без топлива огонь гаснет.
    /// </summary>
    public sealed class LightFuel
    {
        public LightFuel(float capacity, float amount)
        {
            Capacity = Mathf.Max(0f, capacity);
            Amount = Mathf.Clamp(amount, 0f, Capacity);
        }

        /// <summary>Сколько топлива помещается, в поленьях.</summary>
        public float Capacity { get; }

        /// <summary>Сколько топлива сейчас, в поленьях.</summary>
        public float Amount { get; private set; }

        public bool IsEmpty => Amount <= 0f;

        /// <summary>Сколько ещё можно подбросить.</summary>
        public float Space => Capacity - Amount;

        /// <summary>Сжечь топливо за seconds секунд при расходе perSecond поленьев в секунду. Возвращает, сколько сгорело.</summary>
        public float Burn(float seconds, float perSecond)
        {
            var burned = Mathf.Min(Amount, Mathf.Max(0f, seconds) * Mathf.Max(0f, perSecond));
            Amount -= burned;
            return burned;
        }

        /// <summary>Подбросить топливо. Лишнее сверх запаса не берётся; возвращает, сколько принято.</summary>
        public float Add(float amount)
        {
            var accepted = Mathf.Clamp(amount, 0f, Space);
            Amount += accepted;
            return accepted;
        }

        /// <summary>
        /// Во сколько раз расход в ночь номер night (с 1) больше, чем в первую: растёт на growthPerNight за каждую ночь.
        /// </summary>
        public static float NightBurnFactor(int night, float growthPerNight) =>
            1f + Mathf.Max(0f, growthPerNight) * Mathf.Max(0, night - 1);

        /// <summary>
        /// Доля полного радиуса при запасе amount: 1, пока топлива не меньше lowFuel, дальше сжимается
        /// до minFactor, без топлива 0 — огонь погас.
        /// </summary>
        public static float RadiusFactor(float amount, float lowFuel, float minFactor)
        {
            if (amount <= 0f)
                return 0f;
            if (amount >= lowFuel)
                return 1f;
            return Mathf.Lerp(Mathf.Clamp01(minFactor), 1f, amount / lowFuel);
        }
    }
}
