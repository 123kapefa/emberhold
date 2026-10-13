using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Трава на краю соседних блоков: у верха блока земли или камня — маска из восьми соседних столбцов,
    /// чей открытый верх — трава почти на той же высоте, что наш край с ними. Маска идёт в UV3.y меша,
    /// положение вершины в ячейке — в UV3.zw; по ним шейдер земли рисует рваную бахрому травы, заходящую
    /// на блок со стороны травы, со скруглёнными внешними углами. Дно ямы от травы на бровке травы не получает.
    /// </summary>
    public static class GrassEdge
    {
        /// <summary>Насколько верх соседа может отличаться от нашего края, чтобы трава с него заходила, в шестнадцатых.</summary>
        public const int MaxStep = BlockShape.MaxHeight / 2;

        // Биты маски: соседи по сторонам, затем по диагоналям.
        public const int Left = 1;           // −X
        public const int Right = 2;          // +X
        public const int Back = 4;           // −Z
        public const int Forward = 8;        // +Z
        public const int BackLeft = 16;      // −X −Z
        public const int BackRight = 32;     // +X −Z
        public const int ForwardLeft = 64;   // −X +Z
        public const int ForwardRight = 128; // +X +Z

        /// <summary>Получает ли материал траву с соседей: земля и камень — да, трава и дерево — нет.</summary>
        public static bool TakesGrass(byte material) => material == BlockMaterials.Dirt || material == BlockMaterials.Stone;

        /// <summary>
        /// Маска соседей-травы для верха блока block формы s. Сосед по стороне — трава, если его верх трава
        /// около высоты нашего края хотя бы у одного из двух общих углов; по диагонали — у общего угла.
        /// </summary>
        public static int NeighbourMask(VoxelWorld world, Vector3Int block, BlockShape s)
        {
            var floor = block.y * BlockShape.MaxHeight;
            int x = block.x, z = block.z;
            int h00 = floor + s.C00, h10 = floor + s.C10, h01 = floor + s.C01, h11 = floor + s.C11;

            bool Side(int nx, int nz, int ax, int az, int ha, int bx, int bz, int hb) =>
                IsGrass(world, nx, nz, ax, az, ha) || IsGrass(world, nx, nz, bx, bz, hb);

            var mask = 0;
            if (Side(x - 1, z, x, z, h00, x, z + 1, h01)) mask |= Left;
            if (Side(x + 1, z, x + 1, z, h10, x + 1, z + 1, h11)) mask |= Right;
            if (Side(x, z - 1, x, z, h00, x + 1, z, h10)) mask |= Back;
            if (Side(x, z + 1, x, z + 1, h01, x + 1, z + 1, h11)) mask |= Forward;
            if (IsGrass(world, x - 1, z - 1, x, z, h00)) mask |= BackLeft;
            if (IsGrass(world, x + 1, z - 1, x + 1, z, h10)) mask |= BackRight;
            if (IsGrass(world, x - 1, z + 1, x, z + 1, h01)) mask |= ForwardLeft;
            if (IsGrass(world, x + 1, z + 1, x + 1, z + 1, h11)) mask |= ForwardRight;
            return mask;
        }

        /// <summary>
        /// Открыт ли сверху столбец (x, z) травой в своём углу (cx, cz) около высоты height (шестнадцатые от дна мира).
        /// Гладкая земля — трава, если угол скорее травяной, чем каменный.
        /// </summary>
        public static bool IsGrass(VoxelWorld world, int x, int z, int cx, int cz, int height)
        {
            var size = world.SizeInBlocks;
            if (x < 0 || z < 0 || x >= size.x || z >= size.z)
                return false;

            const int h = BlockShape.MaxHeight;
            var surface = world.Surface;
            if (surface != null && surface.IsNatural(x, z))
            {
                // Гладкая земля, если на ней ничего не стоит.
                var top = surface.TopY(x, z);
                if (top + 1 < size.y && !world.GetBlock(new Vector3Int(x, top + 1, z)).IsAir)
                    return false;
                return Mathf.Abs(surface.CornerIn(x, z, cx, cz) - height) <= MaxStep && surface.Rock(cx, cz) < 0.5f;
            }

            // Блоки: сверху вниз первый непустой блок около нужной высоты; если над ним не воздух,
            // верх столбца выше и закрыт.
            var high = Mathf.Min((height + MaxStep) / h, size.y - 1);
            var low = Mathf.Max((height - MaxStep) / h - 1, 0);
            for (var y = high; y >= low; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (block.IsAir)
                    continue;
                if (block.Material != BlockMaterials.Grass || block.Shape.IsCatalog)
                    return false;
                if (y + 1 < size.y && !world.GetBlock(new Vector3Int(x, y + 1, z)).IsAir)
                    return false;
                var s = block.Shape;
                var corner = (cx - x, cz - z) switch
                {
                    (0, 0) => s.C00,
                    (1, 0) => s.C10,
                    (0, 1) => s.C01,
                    _ => s.C11,
                };
                return Mathf.Abs(y * h + corner - height) <= MaxStep;
            }
            return false;
        }
    }
}
