using System;

namespace Emberhold.World.Items
{
    /// <summary>Что выпадает: ресурс (по строковому id, например «stone») и сколько штук.</summary>
    public readonly struct ItemDrop : IEquatable<ItemDrop>
    {
        public readonly string ItemId;
        public readonly int Count;

        public ItemDrop(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("Нужен id ресурса", nameof(itemId));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Количество должно быть положительным");
            ItemId = itemId;
            Count = count;
        }

        public bool Equals(ItemDrop other) => ItemId == other.ItemId && Count == other.Count;
        public override bool Equals(object obj) => obj is ItemDrop other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ItemId, Count);

        public override string ToString() => $"{ItemId}×{Count}";
    }
}
