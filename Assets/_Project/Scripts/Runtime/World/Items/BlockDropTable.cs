using System;
using UnityEngine;

namespace Emberhold.World.Items
{
    /// <summary>
    /// Таблица выпадения: какой ресурс и сколько даёт разрушенный блок каждого материала.
    /// Материалов, которых нет в таблице, разрушение ничего не даёт (трава и земля в прототипе).
    /// </summary>
    [CreateAssetMenu(fileName = "BlockDrops", menuName = "Emberhold/Block Drops")]
    public sealed class BlockDropTable : ScriptableObject, IBlockDrops
    {
        [Serializable]
        private struct Entry
        {
            [Tooltip("Номер материала блока: 1 — трава, 2 — земля, 3 — камень.")]
            public int material;
            [Tooltip("Id выпадающего ресурса, например stone.")]
            public string itemId;
            [Min(1)]
            public int count;
        }

        [SerializeField] private Entry[] drops = { new() { material = 3, itemId = "stone", count = 1 } };

        public bool TryGetDrop(byte material, out ItemDrop drop)
        {
            if (drops != null)
            {
                foreach (var entry in drops)
                {
                    if (entry.material == material && !string.IsNullOrEmpty(entry.itemId) && entry.count > 0)
                    {
                        drop = new ItemDrop(entry.itemId, entry.count);
                        return true;
                    }
                }
            }
            drop = default;
            return false;
        }
    }
}
