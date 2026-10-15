using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Материалы столбца рельефа по глубине. Номера — из <see cref="BlockMaterials"/>:
    /// 1 — трава, 2 — земля, 3 — камень.
    /// </summary>
    [Serializable]
    public struct TerrainMaterials
    {
        [Tooltip("Материал блока поверхности (номер: 1 — трава, 2 — земля, 3 — камень).")]
        public byte Surface;

        [Tooltip("Материал под поверхностью.")]
        public byte Subsurface;

        [Tooltip("Толщина слоя под поверхностью в блоках, глубже — материал глубины.")]
        [Min(0)]
        public int SubsurfaceDepth;

        [Tooltip("Материал глубже слоя под поверхностью.")]
        public byte Deep;

        [Tooltip("Материал поверхности ниже уровня воды (дно озера).")]
        public byte Bed;

        /// <summary>Трава, блок земли, глубже камень; дно — земля.</summary>
        public static TerrainMaterials Default => new()
        {
            Surface = BlockMaterials.Grass,
            Subsurface = BlockMaterials.Dirt,
            SubsurfaceDepth = 1,
            Deep = BlockMaterials.Stone,
            Bed = BlockMaterials.Dirt,
        };

        /// <summary>
        /// Те же материалы для гор хребта: глубже поверхности — скала до самого дна, камень везде становится скалой.
        /// Трава и земля сверху (проплешины на склонах) остаются: их можно снять, а под ними скала.
        /// </summary>
        public TerrainMaterials Mountain() => new()
        {
            Surface = ToCliff(Surface),
            Subsurface = ToCliff(Subsurface),
            SubsurfaceDepth = SubsurfaceDepth,
            Deep = BlockMaterials.Cliff,
            Bed = ToCliff(Bed),
        };

        private static byte ToCliff(byte material) => material == BlockMaterials.Stone ? BlockMaterials.Cliff : material;

        /// <summary>Материал сплошного блока на глубине depth под поверхностью (1 — сразу под ней).</summary>
        public byte Below(int depth) => depth <= SubsurfaceDepth ? Subsurface : Deep;

        /// <summary>Не воздух и в пределах номеров хранилища: из пустого номера получился бы невидимый столбец.</summary>
        public void Validate()
        {
            Surface = Clamp(Surface);
            Subsurface = Clamp(Subsurface);
            Deep = Clamp(Deep);
            Bed = Clamp(Bed);
            SubsurfaceDepth = Mathf.Max(0, SubsurfaceDepth);
        }

        private static byte Clamp(byte material) => (byte)Mathf.Clamp(material, 1, BlockMaterials.Max);
    }
}
