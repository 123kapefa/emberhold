using System;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>
    /// Рассадка мелочи на земле по карте. Мелочь столбца берётся у того же профиля, что и лес (<see cref="ForestMix.GroundCover"/>).
    /// Растёт островками: плавный шум делит карту на сочные места и проплешины, густота профиля — доля площади
    /// под островками. В островке пучок стоит примерно в каждом втором столбце. У деревьев (до двух блоков от ствола)
    /// мелочь растёт и вне островков, и там свои виды (папоротник, грибы, мох). Пучок встаёт на траву (а у горной
    /// мелочи и на камень), не в столбце дерева. Лес сажается раньше. Одинаковые seed, мир и лес дают одинаковую мелочь.
    /// </summary>
    public static class GroundCoverScatter
    {
        /// <summary>Вероятность пучка в столбце внутри островка.</summary>
        public const float PatchChance = 0.55f;

        /// <summary>Наибольший сдвиг пучка от центра клетки по X и по Z, в блоках.</summary>
        public const float MaxOffset = 0.4f;

        /// <summary>Мелочь «под деревьями» растёт не дальше стольких столбцов от ствола.</summary>
        public const int TreeReach = 2;

        // Под деревьями берутся свои виды с этой вероятностью, иначе — виды открытого места.
        private const float UnderTreesShare = 0.7f;
        // Масштаб шума островков: островок и проплешина около 1 / NoiseScale блоков в поперечнике.
        private const float NoiseScale = 0.075f;

        /// <summary>
        /// Посадить мелочь в cover. mixAt даёт лес столбца (null — пусто), forest — уже посаженный лес,
        /// keepClear — столбцы, где не сажать (у самого огня). Возвращает, сколько посажено.
        /// </summary>
        public static int Scatter(VoxelWorld world, int seed, Func<Vector2Int, ForestMix> mixAt, Forest forest,
            GroundCover cover, Func<Vector2Int, bool> keepClear = null)
        {
            var size = world.SizeInBlocks;
            var nearTree = NearTrees(size, forest);
            var noise = new Vector2(ForestScatter.Random01(seed, 0, 0, 20), ForestScatter.Random01(seed, 0, 0, 21)) * 1000f;
            var planted = 0;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var column = new Vector2Int(x, z);
                var mix = mixAt(column)?.GroundCover;
                if (mix == null || mix.IsEmpty)
                    continue;
                var near = nearTree[x + z * size.x];
                var inPatch = Lushness(noise, x, z) <= mix.Density;
                if (!inPatch && !(near && ForestScatter.Random01(seed, x, z, 31) < mix.Density))
                    continue;
                if (ForestScatter.Random01(seed, x, z, 30) >= PatchChance)
                    continue;
                if (forest != null && forest.At(column) != null || keepClear != null && keepClear(column))
                    continue;
                if (!ForestScatter.TryGroundTop(world, x, z, mix.GrowsOnStone, out var cell))
                    continue;
                var roll = ForestScatter.Random01(seed, x, z, 33);
                var kind = near && mix.HasUnderTrees && ForestScatter.Random01(seed, x, z, 32) < UnderTreesShare
                    ? mix.PickUnderTrees(roll)
                    : mix.PickOpen(roll);
                if (kind == null)
                    continue;
                var offset = new Vector2(ForestScatter.Random01(seed, x, z, 34) * 2f - 1f, ForestScatter.Random01(seed, x, z, 35) * 2f - 1f) * MaxOffset;
                var variant = Mathf.Min(kind.Variants - 1, (int)(ForestScatter.Random01(seed, x, z, 38) * kind.Variants));
                var patch = new GroundCoverPatch(cell, kind, variant, offset, ForestScatter.Random01(seed, x, z, 36) * 360f,
                    Mathf.Lerp(0.85f, 1.2f, ForestScatter.Random01(seed, x, z, 37)));
                if (cover.Add(patch))
                    planted++;
            }
            return planted;
        }

        /// <summary>
        /// Сочность места, 0..1, примерно равномерно по карте: столбец в островке, если она не больше густоты.
        /// Две октавы шума Перлина, растянутые так, чтобы доля площади примерно совпадала с густотой.
        /// </summary>
        internal static float Lushness(Vector2 offset, int x, int z)
        {
            var n = 0.65f * Mathf.PerlinNoise(offset.x + x * NoiseScale, offset.y + z * NoiseScale)
                + 0.35f * Mathf.PerlinNoise(offset.y + x * NoiseScale * 2.3f, offset.x + z * NoiseScale * 2.3f);
            return Mathf.Clamp01((n - 0.3f) / 0.4f);
        }

        // Столбцы не дальше TreeReach от какого-нибудь дерева (по сторонам и диагоналям).
        private static bool[] NearTrees(Vector3Int size, Forest forest)
        {
            var near = new bool[size.x * size.z];
            if (forest == null)
                return near;
            foreach (var tree in forest.Trees)
            {
                for (var dz = -TreeReach; dz <= TreeReach; dz++)
                for (var dx = -TreeReach; dx <= TreeReach; dx++)
                {
                    var x = tree.Cell.x + dx;
                    var z = tree.Cell.z + dz;
                    if ((uint)x < size.x && (uint)z < size.z)
                        near[x + z * size.x] = true;
                }
            }
            return near;
        }
    }
}
