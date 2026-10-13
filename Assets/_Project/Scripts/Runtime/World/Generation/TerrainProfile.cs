using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Тип местности ассетом: форма, шум, террасы, материалы и лес. Образцы лежат в Data/World/TerrainProfiles.</summary>
    [CreateAssetMenu(fileName = "TerrainProfile", menuName = "Emberhold/World/Terrain Profile")]
    public sealed class TerrainProfile : ScriptableObject
    {
        [SerializeField] private TerrainProfileParameters parameters = TerrainProfileParameters.Valley;

        [Tooltip("Какие деревья и как густо растут на этой местности.")]
        [SerializeField] private ForestMix forest = new();

        [Tooltip("Оттенок травы местности (умножается на текстуру травы, можно больше 1 — ярче); альфа — насыщенность: 1 — как есть, меньше — блёклее. "
                 + "Локация карты может задать свой.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color grassTint = Color.white;

        [Tooltip("Вид травы местности: своя текстура травы. Локация карты может задать свой.")]
        [SerializeField] private GrassKind grassKind = GrassKind.Meadow;

        public TerrainProfileParameters Parameters => parameters;
        public ForestMix Forest => forest;
        /// <summary>Оттенок травы: rgb умножается на траву, a — насыщенность (1 — как есть).</summary>
        public Color GrassTint => grassTint;
        /// <summary>Вид травы: какая текстура травы на этой местности.</summary>
        public GrassKind GrassKind => grassKind;

        private void OnValidate() => parameters.Materials.Validate();
    }
}
