using System;
using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Рассадка леса по карте: в каждом столбце лес берётся у профиля, который там главный
    /// (зона с наибольшим весом или фон, как у рельефа в <see cref="TerrainGenerator"/>; на карте кампании — локация).
    /// Сначала расставляются рощи (<see cref="Grove"/>): центры случайно, с промежутком между рощами, у каждой свой
    /// размер, волнистый край и главный вид. Внутри рощи деревья густо (к краю реже), между рощами — по одному.
    /// Дерево растёт на траве (а у горного леса и на камне), вид выбирается по долям, в роще чаще главный.
    /// Соседние столбцы (и по диагонали) не заняты двумя деревьями сразу; ствол сдвинут от центра клетки на случайные
    /// ±<see cref="MaxOffset"/>, чтобы густая роща не стояла рядами. У каждого старта (<see cref="ForestStart"/>) роща
    /// стоит центром на краю круга света: её край заходит в круг, и первые дрова растут рядом с костром. Рощи, где выросло меньше
    /// <see cref="MinGroveTrees"/> деревьев (камень, вода, лагерь), не сохраняются. Одинаковые seed, мир и зоны дают
    /// одинаковый лес.
    /// </summary>
    public static class ForestScatter
    {
        /// <summary>Наибольший сдвиг ствола от центра клетки по X и по Z, в блоках.</summary>
        public const float MaxOffset = 0.35f;

        /// <summary>Роща сохраняется, если в ней выросло хотя бы столько деревьев.</summary>
        public const int MinGroveTrees = 3;

        // Глубже этой доли радиуса роща полной густоты, к краю густота падает до EdgeDensity от полной.
        private const float FullDepth = 0.6f;
        private const float EdgeDensity = 0.25f;
        // Рощи почти не сливаются: центры не ближе этой доли суммы средних радиусов.
        private const float GroveSpacing = 0.9f;
        // Сетка поиска рощи по столбцу, в блоках.
        private const int GridCell = 32;

        /// <summary>
        /// Посадить лес в forest. background — лес фонового профиля, zones — зоны со своим лесом,
        /// keepClear — столбцы, где сажать нельзя (место у костра). Возвращает, сколько посажено.
        /// </summary>
        public static int Scatter(VoxelWorld world, int seed, ForestMix background, IReadOnlyList<TerrainZoneArea> zones,
            Forest forest, Func<Vector2Int, bool> keepClear = null, IReadOnlyList<ForestStart> starts = null)
        {
            var weights = new float[(zones?.Count ?? 0) + 1];
            return Scatter(world, seed, column => MixAt(background, zones, new Vector2(column.x + 0.5f, column.y + 0.5f), weights),
                forest, keepClear, starts);
        }

        /// <summary>То же, но лес столбца даёт mixAt (например, карта кампании по локациям); null — без деревьев.</summary>
        public static int Scatter(VoxelWorld world, int seed, Func<Vector2Int, ForestMix> mixAt, Forest forest,
            Func<Vector2Int, bool> keepClear = null, IReadOnlyList<ForestStart> starts = null)
        {
            var size = world.SizeInBlocks;
            var groves = PlaceGroves(size, seed, mixAt, keepClear, starts);
            var grid = new Dictionary<Vector2Int, List<Grove>>();
            foreach (var grove in groves)
            {
                forest.AddGrove(grove);
                var reach = grove.MaxRadius;
                var min = Vector2Int.FloorToInt((grove.Center - Vector2.one * reach) / GridCell);
                var max = Vector2Int.FloorToInt((grove.Center + Vector2.one * reach) / GridCell);
                for (var gz = min.y; gz <= max.y; gz++)
                for (var gx = min.x; gx <= max.x; gx++)
                {
                    var key = new Vector2Int(gx, gz);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new List<Grove>();
                    list.Add(grove);
                }
            }

            var planted = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var column = new Vector2Int(x, z);
                var mix = mixAt(column);
                if (mix == null || mix.IsEmpty)
                    continue;
                // Роща действует только в лесу с рощами: на чужой локации её край — обычный луг.
                var depth = 0f;
                var grove = mix.HasGroves ? GroveAt(grid, column, out depth) : null;
                var chance = grove != null
                    ? mix.GroveDensity * (depth <= FullDepth ? 1f : Mathf.Lerp(1f, EdgeDensity, (depth - FullDepth) / (1f - FullDepth)))
                    : mix.SingleChance;
                if (Random01(seed, x, z, 0) >= chance)
                    continue;
                if (keepClear != null && keepClear(column) || HasNeighbour(forest, column))
                    continue;
                if (!TryGroundTop(world, x, z, mix.GrowsOnStone, out var cell))
                    continue;
                var main = grove?.MainSpecies;
                var species = main != null && mix.Contains(main) && Random01(seed, x, z, 3) < mix.GroveMainShare
                    ? main
                    : mix.Pick(Random01(seed, x, z, 1));
                if (species == null)
                    continue;
                var tree = forest.Plant(cell, species, grove);
                if (tree == null)
                    continue;
                tree.Offset = new Vector2(Random01(seed, x, z, 8) * 2f - 1f, Random01(seed, x, z, 9) * 2f - 1f) * MaxOffset;
                planted++;
            }

            foreach (var grove in groves)
            {
                if (grove.Trees.Count < MinGroveTrees)
                    forest.RemoveGrove(grove);
            }
            return planted;
        }

        // Центры рощ: сначала по роще у каждого старта (центр на краю круга света, направление по seed), потом
        // столбец становится центром с вероятностью GroveCenterChance своего леса, если рядом нет другой рощи
        // и это не место лагеря.
        private static List<Grove> PlaceGroves(Vector3Int size, int seed, Func<Vector2Int, ForestMix> mixAt,
            Func<Vector2Int, bool> keepClear, IReadOnlyList<ForestStart> starts)
        {
            var groves = new List<Grove>();
            if (starts != null)
            {
                foreach (var start in starts)
                {
                    var x = Mathf.FloorToInt(start.Center.x);
                    var z = Mathf.FloorToInt(start.Center.y);
                    var angle = Random01(seed, x, z, 10) * Mathf.PI * 2f;
                    var center = start.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * start.LightRadius;
                    var column = Vector2Int.FloorToInt(center);
                    if (column.x < 0 || column.y < 0 || column.x >= size.x || column.y >= size.z)
                        continue;
                    var mix = mixAt(column);
                    if (mix == null || mix.IsEmpty || !mix.HasGroves)
                        continue;
                    groves.Add(new Grove(center, mix.GroveRadius, mix.Pick(Random01(seed, x, z, 11)),
                        Random01(seed, x, z, 12) * Mathf.PI * 2f, Random01(seed, x, z, 13) * Mathf.PI * 2f));
                }
            }
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var roll = Random01(seed, x, z, 2);
                // Центров мало: лес столбца (он дорогой у зон) смотрим, только если бросок вообще может пройти.
                if (roll >= ForestMix.MaxGroveCenterChance)
                    continue;
                var column = new Vector2Int(x, z);
                var mix = mixAt(column);
                if (mix == null || mix.IsEmpty || roll >= mix.GroveCenterChance)
                    continue;
                if (keepClear != null && keepClear(column))
                    continue;
                var center = new Vector2(x + 0.5f, z + 0.5f);
                var radius = mix.GroveRadius * Mathf.Lerp(0.7f, 1.3f, Random01(seed, x, z, 4));
                var crowded = false;
                foreach (var other in groves)
                {
                    if ((other.Center - center).magnitude < (other.Radius + radius) * GroveSpacing)
                    {
                        crowded = true;
                        break;
                    }
                }
                if (crowded)
                    continue;
                groves.Add(new Grove(center, radius, mix.Pick(Random01(seed, x, z, 5)),
                    Random01(seed, x, z, 6) * Mathf.PI * 2f, Random01(seed, x, z, 7) * Mathf.PI * 2f));
            }
            return groves;
        }

        // Роща, в которой столбец глубже всего (depth — 0 в центре, 1 на краю); null — вне рощ.
        private static Grove GroveAt(Dictionary<Vector2Int, List<Grove>> grid, Vector2Int column, out float depth)
        {
            depth = float.MaxValue;
            Grove best = null;
            if (!grid.TryGetValue(new Vector2Int(Mathf.FloorToInt((column.x + 0.5f) / GridCell),
                    Mathf.FloorToInt((column.y + 0.5f) / GridCell)), out var list))
                return null;
            var point = new Vector2(column.x + 0.5f, column.y + 0.5f);
            foreach (var grove in list)
            {
                var d = grove.Depth(point);
                if (d <= 1f && d < depth)
                {
                    depth = d;
                    best = grove;
                }
            }
            return best;
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
                // На гладкой земле верх — кусок склона из травы и над камнем: каменное место узнаём по поверхности.
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
