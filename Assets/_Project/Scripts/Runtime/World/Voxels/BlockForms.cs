using System;
using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>Вид формы блока. Номер хранится в 3 битах формы (см. <see cref="BlockShape.Pack"/>), поэтому не больше 7.</summary>
    public enum BlockForm : byte
    {
        /// <summary>Простая форма по высотам четырёх углов: полный блок, полублок, скат, углы ската.</summary>
        Corners = 0,
        /// <summary>Лестница на 4 ступени; без поворота поднимается к +Z.</summary>
        Stairs = 1,
    }

    /// <summary>Коробка внутри ячейки в шестнадцатых: от (X0, Y0, Z0) до (X1, Y1, Z1).</summary>
    public readonly struct FormBox
    {
        public readonly int X0, Y0, Z0, X1, Y1, Z1;

        public FormBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }

        public int Volume => (X1 - X0) * (Y1 - Y0) * (Z1 - Z0);

        /// <summary>Поворот на четверть против часовой стрелки (вид сверху: +X вправо, +Z вверх): (x, z) → (16 − z, x).</summary>
        public FormBox RotatedQuarter()
        {
            const int s = BlockShape.MaxHeight;
            return new FormBox(s - Z1, Y0, X0, s - Z0, Y1, X1);
        }

        public FormBox Rotated(int quarters)
        {
            var box = this;
            for (var i = 0; i < (quarters & 3); i++)
                box = box.RotatedQuarter();
            return box;
        }
    }

    /// <summary>
    /// Каталог фигурных форм: каждая собрана из коробок на сетке 16 × 16 × 16, поэтому меш, клик мышью
    /// и прочность (заполнение = объём) считаются одинаково для всех форм. Столб, балка, полуарка — #27.
    /// </summary>
    public static class BlockForms
    {
        private const int S = BlockShape.MaxHeight;

        private static readonly FormBox[] Stairs =
        {
            new(0, 0, 0, S, 4, S),
            new(0, 4, 4, S, 8, S),
            new(0, 8, 8, S, 12, S),
            new(0, 12, 12, S, 16, S),
        };

        /// <summary>Коробки формы без поворота.</summary>
        public static ReadOnlySpan<FormBox> Boxes(BlockForm form) => form switch
        {
            BlockForm.Stairs => Stairs,
            _ => throw new ArgumentOutOfRangeException(nameof(form), $"У формы {form} нет коробок"),
        };

        /// <summary>Объём формы, доля ячейки 0..1.</summary>
        public static float Fill(BlockForm form)
        {
            var volume = 0;
            foreach (var box in Boxes(form))
                volume += box.Volume;
            return volume / (float)(S * S * S);
        }

        /// <summary>Наибольшая высота формы в шестнадцатых.</summary>
        public static int Top(BlockForm form)
        {
            var top = 0;
            foreach (var box in Boxes(form))
                top = Mathf.Max(top, box.Y1);
            return top;
        }

        /// <summary>
        /// До какой высоты форма гарантированно закрывает любую сторону ячейки при любом повороте.
        /// Сосед сравнивает с ней свои углы и рисует боковую грань выше неё — лишняя грань внутри
        /// формы не видна, а дыр не бывает.
        /// </summary>
        public static int SideCover(BlockForm form)
        {
            // Нижняя коробка лестницы — сплошная плита на всю ячейку.
            var cover = 0;
            foreach (var box in Boxes(form))
            {
                if (box.Y0 == 0 && box.X0 == 0 && box.Z0 == 0 && box.X1 == S && box.Z1 == S)
                    cover = Mathf.Max(cover, box.Y1);
            }
            return cover;
        }
    }
}
