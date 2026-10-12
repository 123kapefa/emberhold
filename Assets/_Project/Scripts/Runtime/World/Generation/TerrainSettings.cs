using System.Collections.Generic;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Что сгенерировать при старте сцены: seed, размер мира и профиль местности или карта кампании.</summary>
    [CreateAssetMenu(fileName = "TerrainSettings", menuName = "Emberhold/World/Terrain Settings")]
    public sealed class TerrainSettings : ScriptableObject
    {
        [Tooltip("Одинаковый seed — одинаковый рельеф.")]
        [SerializeField] private int seed = 12345;
        [Tooltip("Размер мира в чанках 16×16×16 (X — ширина, Y — высота, Z — длина).")]
        [SerializeField] private Vector3Int sizeInChunks = new(8, 3, 8);
        [Tooltip("Фоновый профиль местности: там, где нет зон (TerrainZone). Пусто — долина по умолчанию.")]
        [SerializeField] private TerrainProfile profile;
        [Tooltip("Карта кампании: хребет, локации и ущелья. Если задана, рельеф строится по ней, а профиль и зоны сцены не действуют.")]
        [SerializeField] private CampaignMap map;

        public int Seed => seed;
        public Vector3Int SizeInChunks => sizeInChunks;
        public TerrainProfileParameters Profile => profile != null ? profile.Parameters : TerrainProfileParameters.Valley;

        /// <summary>Карта кампании; null — рельеф по профилю и зонам.</summary>
        public CampaignMap Map => map;

        /// <summary>Лес фонового профиля; null — без профиля деревьев нет.</summary>
        public ForestMix Forest => profile != null ? profile.Forest : null;

        /// <summary>
        /// Сгенерировать мир; zones — зоны местности поверх профиля (в координатах сетки блоков).
        /// С картой кампании мир строится по ней, зоны не действуют.
        /// </summary>
        public VoxelWorld Generate(IReadOnlyList<TerrainZoneArea> zones = null) =>
            map != null
                ? TerrainGenerator.GenerateCampaign(map.ToLayout(), seed, sizeInChunks, out _)
                : TerrainGenerator.Generate(Profile, seed, sizeInChunks, zones);

        private void OnValidate() => sizeInChunks = Vector3Int.Max(sizeInChunks, Vector3Int.one);
    }
}
