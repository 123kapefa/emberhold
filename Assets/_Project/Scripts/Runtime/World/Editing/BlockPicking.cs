using System;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>Блок под курсором и грань, в которую попал луч (единичный вектор по оси).</summary>
    public readonly struct BlockHit : IEquatable<BlockHit>
    {
        public readonly Vector3Int Block;
        public readonly Vector3Int Face;

        public BlockHit(Vector3Int block, Vector3Int face)
        {
            Block = block;
            Face = face;
        }

        public bool Equals(BlockHit other) => Block == other.Block && Face == other.Face;
        public override bool Equals(object obj) => obj is BlockHit other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Block, Face);

        public override string ToString() => $"{Block} face {Face}";
    }

    /// <summary>
    /// Переводит попадание луча в меш мира в координату блока. Точка и нормаль — в координатах мира блоков
    /// (локальных для <see cref="VoxelWorldView"/>), где блок (x, y, z) занимает куб от (x, y, z) до (x+1, y+1, z+1).
    /// </summary>
    public static class BlockPicking
    {
        /// <summary>Насколько отступить от поверхности внутрь блока, чтобы точка на грани не попала в соседа.</summary>
        public const float SurfaceEpsilon = 0.01f;

        /// <summary>
        /// Блок, на поверхность которого попал луч. Верх неполного блока лежит внутри его ячейки,
        /// поэтому отступ против нормали всегда оставляет точку в самом блоке.
        /// </summary>
        public static BlockHit FromSurface(Vector3 point, Vector3 normal)
        {
            var face = DominantAxis(normal);
            var inside = point - (Vector3)face * SurfaceEpsilon;
            return new BlockHit(Vector3Int.FloorToInt(inside), face);
        }

        /// <summary>То же, но только если там действительно непустой блок внутри мира.</summary>
        public static bool TryPick(VoxelWorld world, Vector3 point, Vector3 normal, out BlockHit hit)
        {
            hit = FromSurface(point, normal);
            if (!world.IsInsideBlocks(hit.Block) || world.GetBlock(hit.Block).IsAir)
                return false;
            hit = PreferSlopeAbove(world, hit);
            return true;
        }

        /// <summary>
        /// Попали в верх блока, а над ним скос, у которого часть верха лежит на полу ячейки (внешний угол ската):
        /// там виден верх нижнего блока, но глазом это пол самого скоса — выбираем скос. Фигурные формы
        /// (столб, балка) не трогаем: пол вокруг них — верх нижнего блока. На нетронутой гладкой земле
        /// (<see cref="TerrainSurface"/>) каждый слой поверхности и так принадлежит своему блоку — её не поправляем.
        /// </summary>
        public static BlockHit PreferSlopeAbove(VoxelWorld world, BlockHit hit)
        {
            if (hit.Face != Vector3Int.up || IsSmoothGround(world, hit.Block))
                return hit;
            // Верх неполного блока лежит внутри его ячейки — это он сам, а не пол ячейки сверху.
            var above = hit.Block + Vector3Int.up;
            if (!world.IsInsideBlocks(above) || !world.GetBlock(hit.Block).Shape.IsFull)
                return hit;
            var shape = world.GetBlock(above).Shape;
            if (world.GetBlock(above).IsAir || shape.IsCatalog || shape.CoversFloor)
                return hit;
            return new BlockHit(above, Vector3Int.up);
        }

        /// <summary>Блок — земля нетронутого столбца, которая рисуется гладкой поверхностью.</summary>
        private static bool IsSmoothGround(VoxelWorld world, Vector3Int block)
        {
            var surface = world.Surface;
            return surface != null && surface.IsNatural(block.x, block.z) && block.y <= surface.TopY(block.x, block.z);
        }

        /// <summary>
        /// Ближайшая к нормали ось. Склоны (#14) дадут наклонные нормали; их верх считаем гранью «вверх».
        /// Для склонов выбор по точке неточен (у низкого края точка лежит на полу ячейки), поэтому в игре
        /// блок берётся по треугольнику меша (<see cref="VoxelWorldView.TryGetBlock"/>), а это — запасной путь.
        /// </summary>
        public static Vector3Int DominantAxis(Vector3 normal)
        {
            var ax = Mathf.Abs(normal.x);
            var ay = Mathf.Abs(normal.y);
            var az = Mathf.Abs(normal.z);
            if (ay >= ax && ay >= az)
                return normal.y >= 0f ? Vector3Int.up : Vector3Int.down;
            if (ax >= az)
                return normal.x >= 0f ? Vector3Int.right : Vector3Int.left;
            return normal.z >= 0f ? Vector3Int.forward : Vector3Int.back;
        }
    }
}
