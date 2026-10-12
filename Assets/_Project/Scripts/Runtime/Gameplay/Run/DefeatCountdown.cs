using UnityEngine;

namespace Emberhold.Gameplay.Run
{
    /// <summary>
    /// Отсчёт до проигрыша: пока огонь не горит, идёт время; снова загорелся — отсчёт сбрасывается.
    /// Дошёл до нуля — проигрыш, и дальше огонь его уже не отменяет. Чистая логика без Unity-объектов,
    /// её проверяют EditMode-тесты.
    /// </summary>
    public sealed class DefeatCountdown
    {
        public DefeatCountdown(float seconds)
        {
            Seconds = Mathf.Max(0f, seconds);
            Remaining = Seconds;
        }

        /// <summary>Сколько секунд даётся, чтобы снова разжечь огонь.</summary>
        public float Seconds { get; }

        /// <summary>Сколько секунд осталось; полный запас, пока огонь горит.</summary>
        public float Remaining { get; private set; }

        /// <summary>Огонь погас, и время идёт.</summary>
        public bool IsCounting { get; private set; }

        /// <summary>Время вышло: проигрыш.</summary>
        public bool IsDefeated { get; private set; }

        /// <summary>Прожить dt секунд, зная, горит ли огонь. Возвращает true в тот шаг, когда наступил проигрыш.</summary>
        public bool Tick(bool fireLit, float dt)
        {
            if (IsDefeated)
                return false;
            if (fireLit)
            {
                IsCounting = false;
                Remaining = Seconds;
                return false;
            }

            IsCounting = true;
            Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, dt));
            if (Remaining > 0f)
                return false;
            IsCounting = false;
            IsDefeated = true;
            return true;
        }
    }
}
