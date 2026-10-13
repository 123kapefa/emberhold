using System;
using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>
    /// Мелочь на земле профиля местности: как густо и какие виды на открытом месте и под деревьями.
    /// Лежит в лесу профиля (<see cref="Trees.ForestMix.GroundCover"/>), поэтому каждая зона и локация приносит свою.
    /// Густота — доля площади, покрытая островками мелочи: 1 — ковёр почти везде, 0,15 — редкие пятна.
    /// </summary>
    [Serializable]
    public sealed class GroundCoverMix
    {
        [Serializable]
        public struct Entry
        {
            public GroundCoverKind kind;
            [Tooltip("Доля вида относительно других видов списка.")]
            [Min(0f)]
            public float weight;
        }

        [Tooltip("Доля площади под мелочью: 1 — ковёр почти везде, 0,3 — островками, 0 — пусто.")]
        [Range(0f, 1f)]
        [SerializeField] private float density;
        [Tooltip("Мелочь растёт и на каменной поверхности (лишайник, горные цветы), а не только на траве.")]
        [SerializeField] private bool growsOnStone;
        [Tooltip("Виды на открытом месте.")]
        [SerializeField] private Entry[] open = Array.Empty<Entry>();
        [Tooltip("Виды под деревьями (до двух блоков от ствола); пусто — там как на открытом месте.")]
        [SerializeField] private Entry[] underTrees = Array.Empty<Entry>();

        public GroundCoverMix()
        {
        }

        public GroundCoverMix(float density, bool growsOnStone, Entry[] open, Entry[] underTrees = null)
        {
            this.density = density;
            this.growsOnStone = growsOnStone;
            this.open = open ?? Array.Empty<Entry>();
            this.underTrees = underTrees ?? Array.Empty<Entry>();
        }

        public float Density => density;
        public bool GrowsOnStone => growsOnStone;

        /// <summary>Нечего сажать: густота 0 или нет видов с весом.</summary>
        public bool IsEmpty => density <= 0f || Total(open) <= 0f && Total(underTrees) <= 0f;

        /// <summary>Есть ли свои виды под деревьями.</summary>
        public bool HasUnderTrees => Total(underTrees) > 0f;

        /// <summary>Вид на открытом месте по числу от 0 до 1; null — видов нет.</summary>
        public GroundCoverKind PickOpen(float roll) => Pick(open, roll) ?? Pick(underTrees, roll);

        /// <summary>Вид под деревьями по числу от 0 до 1; без своих видов — как на открытом месте.</summary>
        public GroundCoverKind PickUnderTrees(float roll) => Pick(underTrees, roll) ?? Pick(open, roll);

        private static GroundCoverKind Pick(Entry[] entries, float roll)
        {
            var total = Total(entries);
            if (total <= 0f)
                return null;
            var target = Mathf.Clamp01(roll) * total;
            GroundCoverKind last = null;
            foreach (var entry in entries)
            {
                if (entry.kind == null || entry.weight <= 0f)
                    continue;
                last = entry.kind;
                if (target < entry.weight)
                    return entry.kind;
                target -= entry.weight;
            }
            return last;
        }

        private static float Total(Entry[] entries)
        {
            var total = 0f;
            if (entries == null)
                return total;
            foreach (var entry in entries)
            {
                if (entry.kind != null && entry.weight > 0f)
                    total += entry.weight;
            }
            return total;
        }
    }
}
