using System;
using System.Collections.Generic;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Bushes
{
    /// <summary>
    /// Рассадка кустов по карте (#88): кусты столбца берутся у того же профиля, что и лес (<see cref="ForestMix.Bushes"/>), поэтому
    /// густота своя у каждой зоны и локации. Куст растёт только на траве и не в столбце дерева; сдвинут от центра клетки на
    /// случайные ±<see cref="ForestScatter.MaxOffset"/>. Рассаживать после леса. У каждого старта (<see cref="ForestStart"/>)
    /// несколько кустов (<see cref="StartBushes"/>) растут внутри круга света, чтобы первую траву можно было собрать сразу.
    /// Одинаковые seed, мир и лес дают одинаковые кусты.
    /// </summary>
    public static class BushScatter
    {
        /// <summary>Сколько кустов растёт в свете у каждого старта, если есть место.</summary>
        public const int StartBushes = 4;

        /// <summary>Кусты у старта — на таком расстоянии от огня, доли радиуса света: в свету, но не в самом лагере.</summary>
        public const float StartNear = 0.45f;
        public const float StartFar = 0.75f;

        /// <summary>Ближе стольких блоков к огню куст у старта не растёт: место у самого огня.</summary>
        public const float StartMinDistance = 2f;

        /// <summary>
        /// Посадить кусты в forest.Thicket. mixAt даёт лес столбца (null — пусто), keepClear — столбцы, где сажать нельзя,
        /// starts — огни, у которых кусты растут в свете. Возвращает, сколько посажено.
        /// </summary>
        public static int Scatter(VoxelWorld world, int seed, Func<Vector2Int, ForestMix> mixAt, Forest forest,
            Func<Vector2Int, bool> keepClear = null, IReadOnlyList<ForestStart> starts = null)
        {
            var thicket = forest.Thicket;
            var planted = 0;
            if (starts != null)
            {
                foreach (var start in starts)
                    planted += PlantNearStart(world, seed, mixAt, forest, start);
            }
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var column = new Vector2Int(x, z);
                var mix = mixAt(column)?.Bushes;
                if (mix == null || mix.IsEmpty || ForestScatter.Random01(seed, x, z, 20) >= mix.Density)
                    continue;
                if (keepClear != null && keepClear(column) || forest.At(column) != null || thicket.At(column) != null)
                    continue;
                if (Plant(world, seed, forest, column, mix.Pick(ForestScatter.Random01(seed, x, z, 21))))
                    planted++;
            }
            return planted;
        }

        // Кусты у старта: по кругу от огня на случайных направлениях в кольце [StartNear, StartFar] радиуса света.
        private static int PlantNearStart(VoxelWorld world, int seed, Func<Vector2Int, ForestMix> mixAt, Forest forest, ForestStart start)
        {
            var planted = 0;
            var cx = Mathf.FloorToInt(start.Center.x);
            var cz = Mathf.FloorToInt(start.Center.y);
            var near = Mathf.Max(StartMinDistance, start.LightRadius * StartNear);
            var far = Mathf.Max(near, start.LightRadius * StartFar);
            var first = ForestScatter.Random01(seed, cx, cz, 22) * Mathf.PI * 2f;
            const int tries = StartBushes * 4;
            for (var i = 0; i < tries && planted < StartBushes; i++)
            {
                var angle = first + i * 2.39996f;
                var distance = Mathf.Lerp(near, far, ForestScatter.Random01(seed, cx + i, cz, 23));
                var column = Vector2Int.FloorToInt(start.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
                if (column.x < 0 || column.y < 0 || column.x >= world.SizeInBlocks.x || column.y >= world.SizeInBlocks.z)
                    continue;
                if (forest.At(column) != null || forest.Thicket.At(column) != null)
                    continue;
                var mix = mixAt(column)?.Bushes;
                if (mix == null || mix.IsEmpty)
                    continue;
                if (Plant(world, seed, forest, column, mix.Pick(ForestScatter.Random01(seed, column.x, column.y, 21))))
                    planted++;
            }
            return planted;
        }

        private static bool Plant(VoxelWorld world, int seed, Forest forest, Vector2Int column, BushSpecies species)
        {
            if (species == null || !ForestScatter.TryGroundTop(world, column.x, column.y, false, out var cell))
                return false;
            var bush = forest.Thicket.Plant(cell, species);
            if (bush == null)
                return false;
            bush.Offset = new Vector2(ForestScatter.Random01(seed, column.x, column.y, 24) * 2f - 1f,
                ForestScatter.Random01(seed, column.x, column.y, 25) * 2f - 1f) * ForestScatter.MaxOffset;
            return true;
        }
    }
}
