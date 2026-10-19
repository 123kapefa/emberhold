using UnityEngine;

namespace Emberhold.Gameplay.Items
{
    /// <summary>Место одного предмета в кучке: от середины кучки на земле, поворот и размер.</summary>
    public readonly struct ItemSlot
    {
        public ItemSlot(Vector3 position, Quaternion rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Scale { get; }
    }

    /// <summary>
    /// Как поленья лежат в кучке: сколько единиц, столько поленьев, поленницей вдоль одной оси: нижний ряд,
    /// на нём ряд на одно полено короче и так далее (3 полена — 2 + 1, 6 — 3 + 2 + 1, 12 — 5 + 4 + 3).
    /// Все размеры — в блоках; кучка помещается в свою клетку. Камни не укладываются, а ссыпаются (<see cref="StoneHeap"/>).
    /// Чистая математика: показывает <see cref="DroppedItemsView"/>.
    /// </summary>
    public static class ItemPileLayout
    {
        /// <summary>Длина полена в кучке.</summary>
        public const float LogLength = 0.62f;

        /// <summary>Радиус полена в кучке.</summary>
        public const float LogRadius = 0.075f;

        /// <summary>Поленья ли это (иначе камни и всё остальное ссыпается горкой).</summary>
        public static bool IsLog(string itemId) => itemId == "wood";

        /// <summary>Место полена index в поленнице из count поленьев.</summary>
        public static ItemSlot Slot(int index, int count)
        {
            count = Mathf.Max(1, count);
            index = Mathf.Clamp(index, 0, count - 1);
            return LogSlot(index, count);
        }

        /// <summary>Сколько поленьев в нижнем ряду поленницы из count.</summary>
        public static int LogBase(int count)
        {
            var size = 1;
            while (size * (size + 1) / 2 < count)
                size++;
            return size;
        }

        private static ItemSlot LogSlot(int index, int count)
        {
            var size = LogBase(count);
            // Ряды снизу вверх: в ряду row на row поленьев меньше, чем в нижнем.
            var row = 0;
            var inRow = index;
            while (inRow >= size - row)
            {
                inRow -= size - row;
                row++;
            }
            var rowCount = size - row;
            var spacing = LogRadius * 2.04f;
            var x = (inRow - (rowCount - 1) * 0.5f) * spacing;
            var y = LogRadius + row * LogRadius * 1.73f;
            // Поленья лежат неровно: чуть повёрнуты и сдвинуты вдоль.
            var yaw = (Hash(index, 1) - 0.5f) * 8f;
            var along = (Hash(index, 2) - 0.5f) * 0.08f;
            return new ItemSlot(new Vector3(x, y, along), Quaternion.Euler(0f, yaw, Hash(index, 3) * 360f), 1f);
        }

        /// <summary>Повторяемое случайное число 0..1 по номеру предмета.</summary>
        private static float Hash(int index, int salt)
        {
            unchecked
            {
                var h = (uint)(index * 374761393 + salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
