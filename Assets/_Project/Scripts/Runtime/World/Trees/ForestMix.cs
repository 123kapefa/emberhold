using System;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Лес профиля местности: как густо растут деревья и какие виды в какой доле. Лежит в профиле
    /// (<see cref="Generation.TerrainProfile"/>), поэтому каждая зона местности приносит свой лес.
    /// </summary>
    [Serializable]
    public sealed class ForestMix
    {
        [Serializable]
        public struct Entry
        {
            public TreeSpecies species;
            [Tooltip("Доля вида среди деревьев профиля, относительно других видов.")]
            [Min(0f)]
            public float weight;
        }

        [Tooltip("Доля столбцов с травой, на которых растёт дерево: 0.02 — одно дерево на 50 столбцов.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float density;
        [Tooltip("Деревья растут и на каменной поверхности (горы), а не только на траве.")]
        [SerializeField] private bool growsOnStone;
        [SerializeField] private Entry[] species = Array.Empty<Entry>();

        public ForestMix()
        {
        }

        public ForestMix(float density, params Entry[] species) : this(density, false, species)
        {
        }

        public ForestMix(float density, bool growsOnStone, params Entry[] species)
        {
            this.density = density;
            this.growsOnStone = growsOnStone;
            this.species = species ?? Array.Empty<Entry>();
        }

        public float Density => density;
        public bool GrowsOnStone => growsOnStone;

        /// <summary>Есть ли что сажать: густота больше нуля и хотя бы один вид с весом.</summary>
        public bool IsEmpty => density <= 0f || TotalWeight() <= 0f;

        /// <summary>Вид по числу от 0 до 1 с учётом долей; null, если видов нет.</summary>
        public TreeSpecies Pick(float roll)
        {
            var total = TotalWeight();
            if (total <= 0f)
                return null;
            var target = Mathf.Clamp01(roll) * total;
            TreeSpecies last = null;
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
