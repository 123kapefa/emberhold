using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Правки мира игроком. Меняют только данные: <see cref="VoxelWorld.SetBlock"/> сам помечает чанк
    /// (и соседа на границе), а меш перестраивает <see cref="VoxelWorldView"/> в том же кадре.
    /// Постепенное копание — <see cref="BlockDamage"/>.
    /// </summary>
    public static class BlockEdits
    {
        /// <summary>Убрать блок целиком. Возвращает true, если блок был. Скалу и коренную породу не убрать.</summary>
        public static bool Remove(VoxelWorld world, Vector3Int block)
        {
            if (!world.IsInsideBlocks(block) || world.GetBlock(block).IsAir
                || !BlockMaterials.IsBreakable(world.GetBlock(block).Material))
                return false;
            world.SetBlock(block, Block.Air);
            return true;
        }
    }
}
