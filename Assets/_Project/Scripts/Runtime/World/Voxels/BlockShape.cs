using System;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Форма блока. Простые формы (<see cref="BlockForm.Corners"/>) — высоты четырёх верхних углов
    /// в шестнадцатых долях блока (0..16): все углы равны — плоский блок (возможно неполный), разные — скат.
    /// Углы названы по осям: C00 — (x0, z0), C10 — (x1, z0), C01 — (x0, z1), C11 — (x1, z1).
    /// Фигурные формы берутся из каталога (<see cref="BlockForms"/>) и хранят номер формы и поворот;
    /// их углы — не геометрия, а нижняя оценка того, как высоко форма закрывает каждую сторону ячейки
    /// (по ним сосед решает, рисовать ли свою боковую грань).
    /// </summary>
    public readonly struct BlockShape : IEquatable<BlockShape>
    {
        public const int MaxHeight = 16;
        private const int BitsPerCorner = 5;
        private const uint CornerMask = (1u << BitsPerCorner) - 1;
        private const int FormShift = 4 * BitsPerCorner;
        private const int FormBits = 5;
        private const int RotationShift = FormShift + FormBits;

        /// <summary>Наибольший номер фигурной формы, который помещается в упаковку.</summary>
        public const int MaxForm = (1 << FormBits) - 1;

        public readonly byte C00;
        public readonly byte C10;
        public readonly byte C01;
        public readonly byte C11;
        public readonly BlockForm Form;
        /// <summary>Поворот фигурной формы: число четвертей оборота против часовой стрелки, если смотреть сверху (+X вправо, +Z вверх).</summary>
        public readonly byte Rotation;

        public BlockShape(int c00, int c10, int c01, int c11)
        {
            C00 = ClampHeight(c00);
            C10 = ClampHeight(c10);
            C01 = ClampHeight(c01);
            C11 = ClampHeight(c11);
            Form = BlockForm.Corners;
            Rotation = 0;
        }

        private BlockShape(BlockForm form, int rotation)
        {
            Form = form;
            Rotation = (byte)(rotation & 3);
            var cover = (byte)BlockForms.SideCover(form);
            C00 = C10 = C01 = C11 = cover;
        }

        public static BlockShape Full => Flat(MaxHeight);

        public static BlockShape Flat(int height) => new(height, height, height, height);

        /// <summary>Фигурная форма из каталога с поворотом 0..3.</summary>
        public static BlockShape Catalog(BlockForm form, int rotation)
        {
            if (form == BlockForm.Corners)
                throw new ArgumentException("Простые формы задаются углами", nameof(form));
            return new BlockShape(form, rotation);
        }

        public bool IsCatalog => Form != BlockForm.Corners;
        public bool IsFlat => !IsCatalog && C00 == C10 && C00 == C01 && C00 == C11;
        public bool IsFull => IsFlat && C00 == MaxHeight;
        public bool IsEmpty => !IsCatalog && C00 == 0 && C10 == 0 && C01 == 0 && C11 == 0;

        /// <summary>
        /// Закрывает ли форма весь пол ячейки. Нет — у фигурных форм уже ячейки (столб, балка, полуарка)
        /// и если у простой формы половина верха лежит на полу
        /// (внешний угол ската): там у формы нет объёма, и верх блока под ней должен быть виден.
        /// Половины верха — те же, что у меша: диагональ между углами с меньшим перепадом.
        /// </summary>
        public bool CoversFloor
        {
            get
            {
                if (IsCatalog)
                    return BlockForms.CoversFloor(Form);
                if (IsEmpty)
                    return false;
                if (C00 + C11 == C10 + C01)
                    return true;
                return Math.Abs(C00 - C11) <= Math.Abs(C10 - C01)
                    ? C00 + C01 + C11 > 0 && C00 + C11 + C10 > 0
                    : C01 + C11 + C10 > 0 && C01 + C10 + C00 > 0;
            }
        }

        /// <summary>Наибольшая высота формы, 0..16.</summary>
        public int Top => IsCatalog ? BlockForms.Top(Form) : Math.Max(Math.Max(C00, C10), Math.Max(C01, C11));

        /// <summary>
        /// Коробка в шестнадцатых, в которую помещается форма: у простой формы — вся ячейка до <see cref="Top"/>,
        /// у фигурной — с учётом поворота (столб уже ячейки, балка лежит поверху).
        /// </summary>
        public FormBox Extent => IsCatalog ? BlockForms.Extent(Form, Rotation) : new FormBox(0, 0, 0, MaxHeight, Top, MaxHeight);

        /// <summary>Заполнение ячейки 0..1: объём формы. У простой формы — средняя высота углов.</summary>
        public float Fill => IsCatalog ? BlockForms.Fill(Form) : (C00 + C10 + C01 + C11) / (4f * MaxHeight);

        /// <summary>
        /// Повёрнутая на четверть оборота против часовой стрелки (вид сверху: +X вправо, +Z вверх) форма: точка (x, z) переходит
        /// в (1 − z, x). У простой формы переставляются углы, у фигурной меняется поворот.
        /// </summary>
        public BlockShape RotatedQuarter() =>
            IsCatalog ? new BlockShape(Form, Rotation + 1) : new BlockShape(C01, C00, C11, C10);

        /// <summary>Упаковка в 27 бит (по 5 на угол, 5 на форму, 2 на поворот) — ключ для палитры форм чанка.</summary>
        public uint Pack() =>
            C00 | (uint)C10 << BitsPerCorner | (uint)C01 << (2 * BitsPerCorner) | (uint)C11 << (3 * BitsPerCorner)
            | (uint)Form << FormShift | (uint)Rotation << RotationShift;

        public static BlockShape Unpack(uint packed)
        {
            var form = (BlockForm)(packed >> FormShift & ((1u << FormBits) - 1));
            if (form != BlockForm.Corners)
                return new BlockShape(form, (int)(packed >> RotationShift & 3));
            return new BlockShape(
                (int)(packed & CornerMask),
                (int)(packed >> BitsPerCorner & CornerMask),
                (int)(packed >> (2 * BitsPerCorner) & CornerMask),
                (int)(packed >> (3 * BitsPerCorner) & CornerMask));
        }

        public bool Equals(BlockShape other) => Pack() == other.Pack();
        public override bool Equals(object obj) => obj is BlockShape other && Equals(other);
        public override int GetHashCode() => (int)Pack();
        public static bool operator ==(BlockShape a, BlockShape b) => a.Equals(b);
        public static bool operator !=(BlockShape a, BlockShape b) => !a.Equals(b);

        public override string ToString() =>
            IsCatalog ? $"{Form}(rot {Rotation * 90})"
            : IsFlat ? $"Flat({C00})" : $"Slope({C00},{C10},{C01},{C11})";

        private static byte ClampHeight(int h) => (byte)Math.Clamp(h, 0, MaxHeight);
    }
}
