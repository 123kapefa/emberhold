using System;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Блоки одного чанка 16×16×16. Каждый блок — 2 байта: 6 бит материала и 10 бит номера формы.
    /// Номера 0..16 — плоские формы высотой 0..16, они есть всегда. Склоны получают номера 17+
    /// из палитры чанка: четыре независимых угла 0..16 в 16 бит не помещаются, а разных склонов
    /// в обычном рельефе в одном чанке намного меньше 1007 свободных номеров. Если склоны всё же
    /// закончатся, <see cref="Set"/> бросит исключение; что делать в этом случае, решаем в #14.
    /// Только данные, без ссылок на сцену, чтобы потом перевести на NativeArray и Burst.
    /// </summary>
    public sealed class ChunkData
    {
        public const int Size = 16;
        public const int Volume = Size * Size * Size;

        private const int ShapeBits = 10;
        private const int ShapeMask = (1 << ShapeBits) - 1;
        private const int FlatShapeCount = BlockShape.MaxHeight + 1;
        public const int MaxSlopeShapes = (1 << ShapeBits) - FlatShapeCount;

        private readonly ushort[] blocks = new ushort[Volume];
        // Упакованные формы склонов; номер формы = FlatShapeCount + индекс в массиве.
        private readonly uint[] slopeShapes = new uint[MaxSlopeShapes];
        private int slopeShapeCount;

        public static bool IsInside(int x, int y, int z) =>
            (uint)x < Size && (uint)y < Size && (uint)z < Size;

        /// <summary>Порядок x → z → y: горизонтальный слой лежит подряд.</summary>
        public static int Index(int x, int y, int z) => x + z * Size + y * Size * Size;

        public Block Get(int x, int y, int z)
        {
            var raw = blocks[Index(x, y, z)];
            var material = (byte)(raw >> ShapeBits);
            if (material == BlockMaterials.Air)
                return Block.Air;
            return new Block(material, ShapeFromIndex(raw & ShapeMask));
        }

        public void Set(int x, int y, int z, Block block)
        {
            if (block.Material > BlockMaterials.Max)
                throw new ArgumentOutOfRangeException(nameof(block), $"Материал {block.Material} больше {BlockMaterials.Max}");
            blocks[Index(x, y, z)] = block.IsAir
                ? (ushort)0
                : (ushort)(block.Material << ShapeBits | IndexFromShape(block.Shape));
        }

        /// <summary>Сколько разных склонов сейчас занято в палитре (для тестов и отладки).</summary>
        public int SlopeShapeCount => slopeShapeCount;

        private BlockShape ShapeFromIndex(int index) =>
            index < FlatShapeCount
                ? BlockShape.Flat(index)
                : BlockShape.Unpack(slopeShapes[index - FlatShapeCount]);

        private int IndexFromShape(BlockShape shape)
        {
            if (shape.IsFlat)
                return shape.C00;

            var packed = shape.Pack();
            for (var i = 0; i < slopeShapeCount; i++)
            {
                if (slopeShapes[i] == packed)
                    return FlatShapeCount + i;
            }

            if (slopeShapeCount == MaxSlopeShapes)
                CompactSlopeShapes();
            if (slopeShapeCount == MaxSlopeShapes)
                throw new InvalidOperationException("В чанке закончились номера форм склонов");

            slopeShapes[slopeShapeCount] = packed;
            return FlatShapeCount + slopeShapeCount++;
        }

        /// <summary>Выкинуть из палитры склоны, которых больше нет ни в одном блоке, и перенумеровать блоки.</summary>
        private void CompactSlopeShapes()
        {
            var remap = new int[MaxSlopeShapes];
            Array.Fill(remap, -1);
            var kept = new uint[MaxSlopeShapes];
            var keptCount = 0;

            for (var i = 0; i < Volume; i++)
            {
                var raw = blocks[i];
                var shape = raw & ShapeMask;
                if (raw >> ShapeBits == BlockMaterials.Air || shape < FlatShapeCount)
                    continue;
                var old = shape - FlatShapeCount;
                if (remap[old] < 0)
                {
                    remap[old] = keptCount;
                    kept[keptCount++] = slopeShapes[old];
                }
                blocks[i] = (ushort)(raw & ~ShapeMask | FlatShapeCount + remap[old]);
            }

            Array.Copy(kept, slopeShapes, keptCount);
            slopeShapeCount = keptCount;
        }
    }
}
