using System;
using UnityEngine;

namespace Emberhold.World.Bushes
{
    /// <summary>
    /// Кусты профиля местности (#88): как густо растут и какие виды в какой доле. Лежит в лесу профиля
    /// (<see cref="Trees.ForestMix.Bushes"/>), поэтому каждая зона и локация приносит свои кусты: густо в речной пойме,
    /// редко в сосновых холмах.
    /// </summary>
    [Serializable]
    public sealed class BushMix
    {
        [Serializable]
        public struct Entry
        {
            public BushSpecies species;
            [Tooltip("Доля вида среди кустов профиля, относительно других видов.")]
            [Min(0f)]
            public float weight;
        }

        [Tooltip("Доля столбцов с травой, на которых растёт куст: 0.02 — один куст на 50 столбцов.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float density;
        [SerializeField] private Entry[] species = Array.Empty<Entry>();

        public BushMix()
        {
        }

        public BushMix(float density, params Entry[] species)
        {
            this.density = density;
            this.species = species ?? Array.Empty<Entry>();
        }

        public float Density => density;

        /// <summary>Есть ли что сажать: густота больше нуля и хотя бы один вид с весом.</summary>
        public bool IsEmpty => density <= 0f || TotalWeight() <= 0f;

        /// <summary>Вид по числу от 0 до 1 с учётом долей; null, если видов нет.</summary>
        public BushSpecies Pick(float roll)
        {
            var total = TotalWeight();
            if (total <= 0f)
                return null;
            var target = Mathf.Clamp01(roll) * total;
            BushSpecies last = null;
            foreach (var entry in species)
            {
                if (entry.species == null || entry.weight <= 0f)
                    continue;
                last = entry.species;
                if (target < entry.weight)
                    return entry.species;
                target -= entry.weight;
            }
            return last;
        }

        private float TotalWeight()
        {
            var total = 0f;
            if (species == null)
                return total;
            foreach (var entry in species)
            {
                if (entry.species != null && entry.weight > 0f)
                    total += entry.weight;
            }
            return total;
        }
    }
}
