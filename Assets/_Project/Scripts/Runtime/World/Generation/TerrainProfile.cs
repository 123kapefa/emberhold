using Emberhold.World.Trees;
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

        public TerrainProfileParameters Parameters => parameters;
        public ForestMix Forest => forest;

        private void OnValidate() => parameters.Materials.Validate();
    }
}
