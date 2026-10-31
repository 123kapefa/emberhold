using UnityEngine;

namespace Emberhold.Core
{
    /// <summary>
    /// Предел кадров в секунду для игры. Без него vSync выключен и targetFrameRate не задан, и машина рисует
    /// сотни кадров впустую, а ноутбук греется. Ставится до загрузки первой сцены. В batchmode (тесты на Маке)
    /// предел не ставим: там кадры не показываются, а PlayMode-тесты, ждущие кадры, шли бы медленнее.
    /// </summary>
    public static class FrameRateCap
    {
        /// <summary>Сколько кадров в секунду рисует игра.</summary>
        public const int Target = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Application.isBatchMode)
                return;
            // vSync перекрывает targetFrameRate, а на экране 120 Гц дал бы 120 кадров: держим его выключенным.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Target;
        }
    }
}
