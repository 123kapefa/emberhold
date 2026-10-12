using Emberhold.Gameplay.DayNight;

namespace Emberhold.Gameplay.Run
{
    /// <summary>
    /// Счёт забега: сколько ночей пережито и сколько ползунов убито. Ночь — это волна: существа приходят
    /// только ночью, и она считается пережитой, когда наступает рассвет. После проигрыша счёт замирает.
    /// Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public sealed class RunScore
    {
        /// <summary>Пережито ночей.</summary>
        public int NightsSurvived { get; private set; }

        /// <summary>Убито ползунов (растворившиеся с рассветом не в счёт).</summary>
        public int CrawlersKilled { get; private set; }

        /// <summary>Забег окончен, счёт больше не растёт.</summary>
        public bool IsFinal { get; private set; }

        /// <summary>Сменилась фаза суток: рассвет закрывает ночь.</summary>
        public void OnPhaseChanged(DayPhase phase)
        {
            if (!IsFinal && phase == DayPhase.Dawn)
                NightsSurvived++;
        }

        public void OnCrawlerKilled()
        {
            if (!IsFinal)
                CrawlersKilled++;
        }

        /// <summary>Зафиксировать счёт (проигрыш).</summary>
        public void Finish() => IsFinal = true;
    }
}
