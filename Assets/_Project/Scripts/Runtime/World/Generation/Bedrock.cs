using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Коренная порода на дне мира: нижний слой сплошь, выше — всё реже, вразброс по блокам, как в Майнкрафте.
    /// Кладётся поверх готового рельефа: меняет материал уже стоящих блоков, форма остаётся.
    /// </summary>
    public static class Bedrock
    {
        /// <summary>
        /// Положить породу в нижние depth слоёв: в слое y блок становится породой с вероятностью 1 − y / depth
        /// (нижний слой — всегда). Воздух не трогается. Тот же seed — та же раскладка.
        /// Гладкая поверхность (<see cref="VoxelWorld.Surface"/>) остаётся нетронутой.
        /// </summary>
        public static void Lay(VoxelWorld world, int depth, int seed)
        {
            var size = world.SizeInBlocks;
            depth = Mathf.Min(depth, size.y);
            if (depth <= 0)
                return;
            // Смена материала под поверхностью — не копание: на время снимаем поверхность, чтобы столбцы остались нетронутыми.
            var surface = world.Surface;
            world.Surface = null;
            for (var y = 0; y < depth; y++)
            {
                var chance = 1f - (float)y / depth;
                for (var z = 0; z < size.z; z++)
                for (var x = 0; x < size.x; x++)
                {
                    var cell = new Vector3Int(x, y, z);
                    var block = world.GetBlock(cell);
                    if (block.IsAir || block.Material == BlockMaterials.Bedrock || Random01(seed, x, y, z) >= chance)
                        continue;
                    world.SetBlock(cell, new Block(BlockMaterials.Bedrock, block.Shape));
                }
            }
            world.Surface = surface;
        }

        /// <summary>Псевдослучайное число 0..1 для блока: зависит только от seed и координат.</summary>
        internal static float Random01(int seed, int x, int y, int z)
        {
            unchecked
            {
                var h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE3Du;
                h = (h << 11) | (h >> 21);
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
