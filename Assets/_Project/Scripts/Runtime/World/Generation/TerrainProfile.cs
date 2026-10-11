using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Тип местности ассетом: форма, шум, террасы и материалы. Образцы лежат в Data/World/TerrainProfiles.</summary>
    [CreateAssetMenu(fileName = "TerrainProfile", menuName = "Emberhold/World/Terrain Profile")]
    public sealed class TerrainProfile : ScriptableObject
    {
        [SerializeField] private TerrainProfileParameters parameters = TerrainProfileParameters.Valley;

        public TerrainProfileParameters Parameters => parameters;

        private void OnValidate() => parameters.Materials.Validate();
    }
}
