using Emberhold.World.Trees;
using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>
    /// Мелочь при отдалении камеры: до начала редения видна вся, дальше редеет ступенями (<see cref="Steps"/>),
    /// за порогом скрывается целиком. Первыми пропадают самые мелкие пучки (мох, лишайник, грибы, цветы), трава
    /// и папоротник держатся дольше, но редеет каждый вид. Ступени, а не плавный спад: участки перестраиваются
    /// только при смене ступени, а не на каждом движении колёсика.
    /// </summary>
    public static class GroundCoverThinning
    {
        /// <summary>Ступеней редения: на ступени n видна доля n / Steps пучков, на 0 мелочь скрыта.</summary>
        public const int Steps = 4;

        /// <summary>
        /// Ступень для расстояния от камеры до земли: Steps до thinFrom, 0 дальше hideBeyond, между ними —
        /// Steps−1 … 1. current — нынешняя ступень: её меняет только переход за границу ступени больше чем
        /// на margin блоков, чтобы мелочь не мигала, когда камера стоит на самой границе.
        /// </summary>
        public static int Level(float distance, float thinFrom, float hideBeyond, int current, float margin = 2f)
        {
            var level = RawLevel(distance, thinFrom, hideBeyond);
            if (level < current)
                return Mathf.Max(level, RawLevel(distance - margin, thinFrom, hideBeyond));
            if (level > current)
                return Mathf.Min(level, RawLevel(distance + margin, thinFrom, hideBeyond));
            return level;
        }

        /// <summary>Виден ли пучок на ступени level.</summary>
        public static bool Keeps(GroundCoverPatch patch, int level)
        {
            if (level >= Steps)
                return true;
            if (level <= 0)
                return false;
            return Importance(patch) >= 1f - (float)level / Steps;
        }

        /// <summary>
        /// Насколько пучок нужен вдали, 0..1: чем меньше, тем раньше он пропадает. Случайно по клетке
        /// (одинаково при каждой перестройке), со сдвигом вверх у крупных форм.
        /// </summary>
        public static float Importance(GroundCoverPatch patch)
        {
            var roll = ForestScatter.Random01(0, patch.Cell.x, patch.Cell.z, 39);
            return Tier(patch.Kind.Shape) * 0.2f + roll * 0.8f;
        }

        private static int RawLevel(float distance, float thinFrom, float hideBeyond)
        {
            if (distance <= thinFrom)
                return Steps;
            if (distance > hideBeyond || hideBeyond <= thinFrom)
                return 0;
            var t = (distance - thinFrom) / (hideBeyond - thinFrom);
            return Mathf.Clamp(Steps - 1 - Mathf.FloorToInt(t * (Steps - 1)), 1, Steps - 1);
        }

        // Мелкие формы — 0, трава — половина, крупные (папоротник, камыш) — 1.
        private static float Tier(GroundCoverShape shape) => shape switch
        {
            GroundCoverShape.Fern or GroundCoverShape.Reeds => 1f,
            GroundCoverShape.Grass => 0.5f,
            _ => 0f,
        };
    }
}
