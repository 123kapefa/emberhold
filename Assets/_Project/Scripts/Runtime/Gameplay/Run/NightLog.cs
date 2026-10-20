using Emberhold.Gameplay.DayNight;
using UnityEngine;

namespace Emberhold.Gameplay.Run
{
    /// <summary>Итог одних суток забега для плейтеста: что было с костром и ползунами от прошлого рассвета до этого.</summary>
    public sealed class NightRecord
    {
        /// <summary>Номер ночи, с 1.</summary>
        public int Night;
        /// <summary>Ночь пережита (рассвет); false — забег кончился проигрышем в эти сутки.</summary>
        public bool Survived;
        /// <summary>Топливо в начале ночи, в поленьях.</summary>
        public float FuelAtNightStart;
        /// <summary>Топливо в конце (рассвет или проигрыш).</summary>
        public float FuelAtEnd;
        /// <summary>Меньше всего топлива за ночь.</summary>
        public float MinFuelAtNight;
        /// <summary>Подброшено днём и в сумерки (рабочими и клавишей L).</summary>
        public float BroughtByDay;
        /// <summary>Подброшено ночью.</summary>
        public float BroughtAtNight;
        /// <summary>Сгорело в костре само.</summary>
        public float BurnedByFire;
        /// <summary>Съели ползуны: всё, что убыло сверх горения.</summary>
        public float EatenByCrawlers;
        /// <summary>Секунд суток, когда огонь горел, но радиус был сжат (топлива мало).</summary>
        public float LowSeconds;
        /// <summary>Секунд суток, когда огонь не горел.</summary>
        public float OutSeconds;
        public int CrawlersSpawned;
        public int CrawlersKilled;
        /// <summary>Больше всего живых ползунов сразу.</summary>
        public int MaxCrawlersAlive;
    }

    /// <summary>
    /// Журнал плейтеста: копит по одним суткам, сколько дров принесли, сколько сгорело и сколько съели ползуны,
    /// сколько огонь был слабым или погасшим, сколько ползунов вышло и убито. Сутки закрываются на рассвете
    /// или проигрышем. Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public sealed class NightLog
    {
        private NightRecord current;
        private float lastFuel;
        private float consumed;
        private bool nightStarted;

        public NightLog(int night, float fuel)
        {
            Begin(night, fuel);
        }

        /// <summary>Сутки, которые сейчас копятся.</summary>
        public NightRecord Current => current;

        /// <summary>
        /// Прошло seconds секунд суток в фазе phase. fuel — топливо костра сейчас; прибавка с прошлого раза считается
        /// принесённой, убыль — потраченной. burnPerSecond — расход горения в эту ночь; alive — живых ползунов.
        /// </summary>
        public void Sample(DayPhase phase, float seconds, float fuel, bool lit, bool low, float burnPerSecond, int alive)
        {
            var night = phase == DayPhase.Night;
            if (night && !nightStarted)
                StartNight(lastFuel);

            var change = fuel - lastFuel;
            if (change > 0f)
            {
                if (night)
                    current.BroughtAtNight += change;
                else
                    current.BroughtByDay += change;
            }
            else
            {
                consumed -= change;
            }
            lastFuel = fuel;

            // Огонь горит только ночью; ровно сколько сгорело, не знаем (порядок подписчиков), поэтому берём расход.
            if (night && lit)
                current.BurnedByFire += burnPerSecond * Mathf.Max(0f, seconds);
            if (!lit)
                current.OutSeconds += Mathf.Max(0f, seconds);
            else if (low)
                current.LowSeconds += Mathf.Max(0f, seconds);

            if (nightStarted)
                current.MinFuelAtNight = Mathf.Min(current.MinFuelAtNight, fuel);
            current.MaxCrawlersAlive = Mathf.Max(current.MaxCrawlersAlive, alive);
        }

        /// <summary>Наступила ночь: запомнить топливо на её начале.</summary>
        public void StartNight(float fuel)
        {
            if (nightStarted)
                return;
            nightStarted = true;
            current.FuelAtNightStart = fuel;
            current.MinFuelAtNight = fuel;
        }

        public void OnCrawlerSpawned() => current.CrawlersSpawned++;

        public void OnCrawlerKilled() => current.CrawlersKilled++;

        /// <summary>Закрыть сутки (рассвет — survived, проигрыш — нет) и начать следующие. Возвращает итог закрытых.</summary>
        public NightRecord Close(bool survived, float fuel)
        {
            var record = current;
            record.Survived = survived;
            record.FuelAtEnd = fuel;
            // Сгорело не больше, чем всего убыло; остальное съели ползуны.
            record.BurnedByFire = Mathf.Min(record.BurnedByFire, consumed);
            record.EatenByCrawlers = Mathf.Max(0f, consumed - record.BurnedByFire);
            if (!nightStarted)
                record.MinFuelAtNight = record.FuelAtNightStart = fuel;
            Begin(record.Night + 1, fuel);
            return record;
        }

        private void Begin(int night, float fuel)
        {
            current = new NightRecord { Night = Mathf.Max(1, night) };
            lastFuel = fuel;
            consumed = 0f;
            nightStarted = false;
        }
    }
}
