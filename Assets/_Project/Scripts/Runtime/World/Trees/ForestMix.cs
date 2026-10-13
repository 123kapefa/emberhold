using System;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Лес профиля местности: как густо растут деревья, какие виды в какой доле и сколько деревьев собрано в рощи.
    /// Лежит в профиле (<see cref="Generation.TerrainProfile"/>), поэтому каждая зона местности приносит свой лес.
    /// Густота — среднее по всей площади: рощи забирают свою долю деревьев, остальные стоят по одному между ними.
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

        [Header("Рощи")]
        [Tooltip("Доля деревьев, растущих в рощах; остальные стоят по одному между рощами. 0 — ровный лес без рощ.")]
        [Range(0f, 1f)]
        [SerializeField] private float groveShare;
        [Tooltip("Средний радиус рощи в блоках; у каждой рощи свой, от 0,7 до 1,3 этого.")]
        [Range(2f, 20f)]
        [SerializeField] private float groveRadius = 6f;
        [Tooltip("Густота внутри рощи: вероятность дерева в столбце. Соседние столбцы не заняты, поэтому деревьев выходит "
            + "около трети этого: 0,4 — дерево на 7 столбцов.")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float groveDensity = 0.4f;
        [Tooltip("Доля главного вида рощи среди её деревьев; остальные — по долям видов (берёзовая роща, сосновый бор).")]
        [Range(0f, 1f)]
        [SerializeField] private float groveMainShare = 0.75f;

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
        public float GroveShare => groveShare;
        public float GroveRadius => groveRadius;
        public float GroveDensity => groveDensity;
        public float GroveMainShare => groveMainShare;

        /// <summary>Есть ли у леса рощи.</summary>
        public bool HasGroves => groveShare > 0f && groveDensity > 0f && groveRadius > 0f;

        /// <summary>Вероятность столбца вне рощи получить одиночное дерево.</summary>
        public float SingleChance => density * (1f - (HasGroves ? groveShare : 0f));

        /// <summary>
        /// Вероятность столбца стать центром рощи: столько рощ, чтобы в них росла доля GroveShare от всех деревьев.
        /// В роще в среднем около 0,35·π·r²·густота деревьев: к краю реже, и соседние столбцы не заняты.
        /// </summary>
        public float GroveCenterChance => HasGroves
            ? Mathf.Min(MaxGroveCenterChance, density * groveShare / (Mathf.PI * groveRadius * groveRadius * groveDensity * 0.35f))
            : 0f;

        /// <summary>Верхняя граница <see cref="GroveCenterChance"/>: рощ не больше одной на 20 столбцов.</summary>
        public const float MaxGroveCenterChance = 0.05f;

        /// <summary>Рощи для тестов и настройки из кода; возвращает этот же лес.</summary>
        public ForestMix WithGroves(float share, float radius, float groveDensity = 0.4f, float mainShare = 0.75f)
        {
            groveShare = Mathf.Clamp01(share);
            groveRadius = radius;
            this.groveDensity = groveDensity;
            groveMainShare = Mathf.Clamp01(mainShare);
            return this;
        }

        /// <summary>Растёт ли вид в этом лесу (есть с долей больше нуля).</summary>
        public bool Contains(TreeSpecies tree)
        {
            if (tree == null || species == null)
                return false;
            foreach (var entry in species)
            {
                if (entry.species == tree && entry.weight > 0f)
                    return true;
            }
            return false;
        }

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
