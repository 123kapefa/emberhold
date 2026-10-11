using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Правки мира игроком. Меняют только данные: <see cref="VoxelWorld.SetBlock"/> сам помечает чанк
    /// (и соседа на границе), а меш перестраивает <see cref="VoxelWorldView"/> в том же кадре.
    /// Каждая правка возвращает true, если блок изменился.
    /// </summary>
    public static class BlockEdits
    {
        /// <summary>
        /// Снять слой: опустить каждый верхний угол на step шестнадцатых. Углы опускаются одинаково,
        /// поэтому склон остаётся склоном, пока не упрётся в пол. Когда все углы дошли до 0, блок исчезает.
        /// </summary>
        public static bool Dig(VoxelWorld world, Vector3Int block, int step)
        {
            if (step <= 0 || !world.IsInsideBlocks(block))
                return false;
            var current = world.GetBlock(block);
            if (current.IsAir)
                return false;

            var s = current.Shape;
            var lowered = new BlockShape(s.C00 - step, s.C10 - step, s.C01 - step, s.C11 - step);
            world.SetBlock(block, new Block(current.Material, lowered));
            return true;
        }

        /// <summary>Убрать блок целиком.</summary>
        public static bool Remove(VoxelWorld world, Vector3Int block)
        {
            if (!world.IsInsideBlocks(block) || world.GetBlock(block).IsAir)
                return false;
            world.SetBlock(block, Block.Air);
            return true;
        }
    }
}
