using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Что сгенерировать при старте сцены: seed, размер мира и профиль местности.</summary>
    [CreateAssetMenu(fileName = "TerrainSettings", menuName = "Emberhold/World/Terrain Settings")]
    public sealed class TerrainSettings : ScriptableObject
    {
        [Tooltip("Одинаковый seed — одинаковый рельеф.")]
        [SerializeField] private int seed = 12345;
        [Tooltip("Размер мира в чанках 16×16×16 (X — ширина, Y — высота, Z — длина).")]
        [SerializeField] private Vector3Int sizeInChunks = new(8, 3, 8);
        [Tooltip("Профиль местности. Пусто — долина по умолчанию.")]
        [SerializeField] private TerrainProfile profile;

        public int Seed => seed;
        public Vector3Int SizeInChunks => sizeInChunks;
        public TerrainProfileParameters Profile => profile != null ? profile.Parameters : TerrainProfileParameters.Valley;

        public VoxelWorld Generate() => TerrainGenerator.Generate(Profile, seed, sizeInChunks);

        private void OnValidate() => sizeInChunks = Vector3Int.Max(sizeInChunks, Vector3Int.one);
    }
}
