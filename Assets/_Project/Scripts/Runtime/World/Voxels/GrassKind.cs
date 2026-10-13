using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Вид травы местности: у каждого своя текстура. Луговая — обычная трава; остальные три — у своих локаций.
    /// Смесь видов в точке хранится цветом (<see cref="GrassKinds.Weight"/>): r — топь, g — мёртвый город, b — горы,
    /// остаток до 1 — луговая.
    /// </summary>
    public enum GrassKind : byte
    {
        /// <summary>Обычная трава.</summary>
        Meadow = 0,
        /// <summary>Тёмная мокрая трава с мхом и маленькими лужицами (Топи).</summary>
        Swamp = 1,
        /// <summary>Сухая редкая трава, проплешины земли и пепел (Мёртвый город).</summary>
        Dead = 2,
        /// <summary>Короткая жёсткая трава с камешками и лишайником (Горы).</summary>
        Mountain = 3,
    }

    public static class GrassKinds
    {
        /// <summary>Вес вида для смешивания: единица в своём канале (у луговой — ноль во всех).</summary>
        public static Color Weight(GrassKind kind) => kind switch
        {
            GrassKind.Swamp => new Color(1f, 0f, 0f, 0f),
            GrassKind.Dead => new Color(0f, 1f, 0f, 0f),
            GrassKind.Mountain => new Color(0f, 0f, 1f, 0f),
            _ => new Color(0f, 0f, 0f, 0f),
        };
    }
}
