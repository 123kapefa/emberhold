using System;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Форма блока: высоты четырёх верхних углов в шестнадцатых долях блока (0..16).
    /// Все углы равны — плоский блок (возможно неполный), разные — склон.
    /// Углы названы по осям: C00 — (x0, z0), C10 — (x1, z0), C01 — (x0, z1), C11 — (x1, z1).
    /// </summary>
    public readonly struct BlockShape : IEquatable<BlockShape>
    {
        public const int MaxHeight = 16;
        private const int BitsPerCorner = 5;
        private const uint CornerMask = (1u << BitsPerCorner) - 1;

        public readonly byte C00;
        public readonly byte C10;
        public readonly byte C01;
        public readonly byte C11;

        public BlockShape(int c00, int c10, int c01, int c11)
        {
            C00 = ClampHeight(c00);
            C10 = ClampHeight(c10);
            C01 = ClampHeight(c01);
            C11 = ClampHeight(c11);
        }

        public static BlockShape Full => Flat(MaxHeight);

        public static BlockShape Flat(int height) => new(height, height, height, height);

        public bool IsFlat => C00 == C10 && C00 == C01 && C00 == C11;
        public bool IsFull => IsFlat && C00 == MaxHeight;
        public bool IsEmpty => C00 == 0 && C10 == 0 && C01 == 0 && C11 == 0;

        /// <summary>Заполнение ячейки 0..1: средняя высота углов. У склона — объём под его верхом.</summary>
        public float Fill => (C00 + C10 + C01 + C11) / (4f * MaxHeight);

        /// <summary>Наибольшая высота угла, 0..16.</summary>
        public int Top => Math.Max(Math.Max(C00, C10), Math.Max(C01, C11));

        /// <summary>Упаковка в 20 бит (по 5 на угол) — ключ для палитры форм чанка.</summary>
        public uint Pack() =>
            C00 | (uint)C10 << BitsPerCorner | (uint)C01 << (2 * BitsPerCorner) | (uint)C11 << (3 * BitsPerCorner);

        public static BlockShape Unpack(uint packed) => new(
            (int)(packed & CornerMask),
            (int)(packed >> BitsPerCorner & CornerMask),
            (int)(packed >> (2 * BitsPerCorner) & CornerMask),
            (int)(packed >> (3 * BitsPerCorner) & CornerMask));

        public bool Equals(BlockShape other) => Pack() == other.Pack();
        public override bool Equals(object obj) => obj is BlockShape other && Equals(other);
        public override int GetHashCode() => (int)Pack();
        public static bool operator ==(BlockShape a, BlockShape b) => a.Equals(b);
        public static bool operator !=(BlockShape a, BlockShape b) => !a.Equals(b);

        public override string ToString() => IsFlat ? $"Flat({C00})" : $"Slope({C00},{C10},{C01},{C11})";

        private static byte ClampHeight(int h) => (byte)Math.Clamp(h, 0, MaxHeight);
    }
}
