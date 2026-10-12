using System;
using Emberhold.World.Items;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Сколько стоит блок по чертежу: какой ресурс и сколько единиц рабочие должны принести.
    /// Материалы, которых нет в таблице, строятся бесплатно (в прототипе трава и земля).
    /// </summary>
    [CreateAssetMenu(fileName = "BuildCosts", menuName = "Emberhold/Build Costs")]
    public sealed class BuildCostTable : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            [Tooltip("Номер материала блока: 1 — трава, 2 — земля, 3 — камень, 4 — дерево.")]
            public int material;
            [Tooltip("Id ресурса, например stone.")]
            public string itemId;
            [Min(1)]
            public int count;
        }

        [SerializeField] private Entry[] costs = { new() { material = 3, itemId = "stone", count = 1 } };

        /// <summary>Что стоит блок из материала; null — бесплатно.</summary>
        public ItemDrop? CostOf(byte material)
        {
            if (costs == null)
                return null;
            foreach (var entry in costs)
            {
                if (entry.material == material && !string.IsNullOrEmpty(entry.itemId) && entry.count > 0)
                    return new ItemDrop(entry.itemId, entry.count);
            }
            return null;
        }
    }
}
