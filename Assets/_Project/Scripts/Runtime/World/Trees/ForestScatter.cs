using System;
using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Рассадка леса по карте: в каждом столбце лес берётся у профиля, который там главный
    /// (зона с наибольшим весом или фон, как у рельефа в <see cref="TerrainGenerator"/>; на карте кампании — локация). Дерево растёт на траве
    /// (а у горного леса и на камне) с вероятностью густоты этого леса, вид выбирается по долям. Соседние столбцы (и по диагонали) не заняты
    /// двумя деревьями сразу. Одинаковые seed, мир и зоны дают одинаковый лес.
    /// </summary>
    public static class ForestScatter
    {
        /// <summary>
        /// Посадить лес в forest. background — лес фонового профиля, zones — зоны со своим лесом,
        /// keepClear — столбцы, где сажать нельзя (место у костра). Возвращает, сколько посажено.
        /// </summary>
        public static int Scatter(VoxelWorld world, int seed, ForestMix background, IReadOnlyList<TerrainZoneArea> zones,
            Forest forest, Func<Vector2Int, bool> keepClear = null)
        {
            var weights = new float[(zones?.Count ?? 0) + 1];
            return Scatter(world, seed, column => MixAt(background, zones, new Vector2(column.x + 0.5f, column.y + 0.5f), weights),
                forest, keepClear);
        }

        /// <summary>То же, но лес столбца даёт mixAt (например, карта кампании по локациям); null — без деревьев.</summary>
        public static int Scatter(VoxelWorld world, int seed, Func<Vector2Int, ForestMix> mixAt, Forest forest,
            Func<Vector2Int, bool> keepClear = null)
        {
            var size = world.SizeInBlocks;
            var planted = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var column = new Vector2Int(x, z);
                var mix = mixAt(column);
                if (mix == null || mix.IsEmpty || Random01(seed, x, z, 0) >= mix.Density)
                    continue;
                if (keepClear != null && keepClear(column) || HasNeighbour(forest, column))
                    continue;
                if (!TryGroundTop(world, x, z, mix.GrowsOnStone, out var cell))
                    continue;
                var species = mix.Pick(Random01(seed, x, z, 1));
                if (species != null && forest.Plant(cell, species) != null)
                    planted++;
            }
            return planted;
        }

        /// <summary>Лес профиля, главного в точке карты: зоны с наибольшим весом, иначе фона.</summary>
        public static ForestMix MixAt(ForestMix background, IReadOnlyList<TerrainZoneArea> zones, Vector2 point, float[] weights)
        {
            if (zones == null || zones.Count == 0)
                return background;
            TerrainGenerator.ProfileWeights(zones, point, weights);
            var dominant = 0;
            for (var i = 1; i < weights.Length; i++)
            {
                if (weights[i] > weights[dominant])
                    dominant = i;
            }
            return dominant == 0 ? background : zones[dominant - 1].Forest;
        }

        private static bool HasNeighbour(Forest forest, Vector2Int column)
        {
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (forest.At(column + new Vector2Int(dx, dz)) != null)
                    return true;
            }
            return false;
        }

        /// <summary>Верхний блок столбца — трава (или камень, если можно), а над ним есть пустая клетка: туда и встанет дерево.</summary>
        private static bool TryGroundTop(VoxelWorld world, int x, int z, bool onStone, out Vector3Int cell)
        {
            cell = default;
            for (var y = world.SizeInBlocks.y - 2; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (block.IsAir)
                    continue;
                // На гладкой земле травяной столбец бывает с каменными углами: каменное место узнаём по поверхности.
                var material = block.Material;
                var surface = world.Surface;
                if (surface != null && surface.IsNatural(x, z) && y <= surface.TopY(x, z) && surface.IsRocky(x, z))
                    material = BlockMaterials.Stone;
                if (material != BlockMaterials.Grass && !(onStone && material == BlockMaterials.Stone))
                    return false;
                cell = new Vector3Int(x, y + 1, z);
                return true;
            }
            return false;
        }

        /// <summary>Псевдослучайное число от 0 до 1 для столбца: зависит только от seed, координат и номера броска.</summary>
        private static float Random01(int seed, int x, int z, int roll)
        {
            unchecked
            {
                var h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)z * 0xC2B2AE3Du;
                h = (h << 17) | (h >> 15);
                h ^= (uint)roll * 0x27D4EB2Fu;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
