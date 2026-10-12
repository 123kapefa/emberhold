using UnityEngine;

namespace Emberhold.Gameplay.DayNight
{
    /// <summary>Фаза суток. Порядок совпадает с порядком смены: день → сумерки → ночь → рассвет.</summary>
    public enum DayPhase
    {
        Day,
        Dusk,
        Night,
        Dawn,
    }

    /// <summary>
    /// Расписание суток: длительности фаз в секундах и что из них следует для момента времени —
    /// фаза, доля пройденной фазы и темнота. Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// Время суток отсчитывается от начала дня и заворачивается по длине суток.
    /// </summary>
    public sealed class DayCycle
    {
        private readonly float[] durations;

        public DayCycle(float day, float dusk, float night, float dawn)
        {
            durations = new[] { Mathf.Max(0f, day), Mathf.Max(0f, dusk), Mathf.Max(0f, night), Mathf.Max(0f, dawn) };
            Length = durations[0] + durations[1] + durations[2] + durations[3];
            if (Length <= 0f)
            {
                durations[0] = 1f;
                Length = 1f;
            }
        }

        /// <summary>Длина суток в секундах.</summary>
        public float Length { get; }

        public float Duration(DayPhase phase) => durations[(int)phase];

        /// <summary>Сколько секунд от начала дня до начала фазы.</summary>
        public float StartOf(DayPhase phase)
        {
            var start = 0f;
            for (var i = 0; i < (int)phase; i++)
                start += durations[i];
            return start;
        }

        /// <summary>Время, приведённое к [0, Length).</summary>
        public float Wrap(float time)
        {
            var wrapped = time % Length;
            return wrapped < 0f ? wrapped + Length : wrapped;
        }

        public DayPhase PhaseAt(float time) => PhaseAt(time, out _);

        /// <summary>Фаза в момент времени и доля от 0 до 1, сколько её уже прошло.</summary>
        public DayPhase PhaseAt(float time, out float progress)
        {
            var t = Wrap(time);
            for (var i = 0; i < durations.Length; i++)
            {
                if (t < durations[i])
                {
                    progress = t / durations[i];
                    return (DayPhase)i;
                }
                t -= durations[i];
            }
            // Сюда попадаем только из-за погрешности на самом конце суток.
            progress = 1f;
            return DayPhase.Dawn;
        }

        /// <summary>
        /// Темнота от 0 (день) до 1 (ночь). В сумерки плавно растёт, на рассвете плавно спадает.
        /// По ней смешиваются освещение и туман.
        /// </summary>
        public float DarknessAt(float time)
        {
            var phase = PhaseAt(time, out var progress);
            return Darkness(phase, progress);
        }

        /// <summary>Темнота внутри фазы при пройденной доле progress.</summary>
        public static float Darkness(DayPhase phase, float progress) => phase switch
        {
            DayPhase.Day => 0f,
            DayPhase.Dusk => Mathf.SmoothStep(0f, 1f, progress),
            DayPhase.Night => 1f,
            _ => Mathf.SmoothStep(1f, 0f, progress),
        };

        public static DayPhase Next(DayPhase phase) => (DayPhase)(((int)phase + 1) % 4);
    }
}
