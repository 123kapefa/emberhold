using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Постановка блоков. Куда встанет блок: в соседнюю ячейку за гранью под курсором; клик по верху
    /// неполного блока того же материала вместо этого досыпает его до полного.
    /// </summary>
    public static class BlockPlacement
    {
        /// <summary>Что произойдёт при клике: в какую ячейку и какой блок встанет.</summary>
        public readonly struct Plan
        {
            public readonly Vector3Int Cell;
            public readonly Block Block;
            /// <summary>Досыпать существующий неполный блок, а не ставить новый.</summary>
            public readonly bool FillsExisting;

            public Plan(Vector3Int cell, Block block, bool fillsExisting)
            {
                Cell = cell;
                Block = block;
                FillsExisting = fillsExisting;
            }
        }

        /// <summary>
        /// План постановки блока block по попаданию hit. false — ставить некуда: ячейка за гранью
        /// вне мира или занята.
        /// </summary>
        public static bool TryPlan(VoxelWorld world, BlockHit hit, Block block, out Plan plan)
        {
            plan = default;
            if (block.IsAir)
                return false;

            var hovered = world.GetBlock(hit.Block);
            if (hit.Face == Vector3Int.up && CanTopUp(hovered, block.Material))
            {
                plan = new Plan(hit.Block, Block.Full(block.Material), fillsExisting: true);
                return true;
            }

            var cell = hit.Block + hit.Face;
            if (!world.IsInsideBlocks(cell) || !world.GetBlock(cell).IsAir)
                return false;
            plan = new Plan(cell, block, fillsExisting: false);
            return true;
        }

        /// <summary>Выполнить план. Меш перестроит <see cref="VoxelWorldView"/> в том же кадре.</summary>
        public static void Apply(VoxelWorld world, Plan plan) => world.SetBlock(plan.Cell, plan.Block);

        /// <summary>Неполный простой блок того же материала можно досыпать до полного.</summary>
        private static bool CanTopUp(Block hovered, byte material) =>
            !hovered.IsAir && hovered.Material == material && !hovered.Shape.IsCatalog && !hovered.Shape.IsFull;
    }
}
