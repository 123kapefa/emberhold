using System;
using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Вид формы блока. Номер хранится в битах формы (см. <see cref="BlockShape.Pack"/>), поэтому не больше
    /// <see cref="BlockShape.MaxForm"/>. Без поворота главная сторона формы смотрит к +Z: туда поднимается
    /// лестница, к стене на +Z прилегает кронштейн, на +Z опускается до пола нога полуарки, вдоль Z лежит балка.
    /// </summary>
    public enum BlockForm : byte
    {
        /// <summary>Простая форма по высотам четырёх углов: полный блок, полублок, скат, углы ската.</summary>
        Corners = 0,
        /// <summary>Лестница на 4 ступени; без поворота поднимается к +Z.</summary>
        Stairs = 1,
        /// <summary>Квадратный столб 8 × 8 шестнадцатых по центру клетки, на всю высоту.</summary>
        SquarePillar = 2,
        /// <summary>Круглый столб диаметром 10 шестнадцатых по центру клетки, на всю высоту.</summary>
        RoundPillar = 3,
        /// <summary>Балка 8 × 8 шестнадцатых поверху клетки; без поворота лежит вдоль Z.</summary>
        Beam = 4,
        /// <summary>
        /// Кронштейн под выносом, шириной 8 шестнадцатых: у стены (+Z) на всю высоту, сверху ровный,
        /// снизу скошен к губе высотой 4 у −Z.
        /// </summary>
        Bracket = 5,
        /// <summary>
        /// Полуарка: клетка без четверти круга радиусом <see cref="BlockForms.ArchRadius"/> у нижнего ребра −Z.
        /// Нога (+Z) доходит до пола, у −Z остаётся замок. Две полуарки ногами наружу дают арку шириной 2 блока.
        /// </summary>
        HalfArch = 6,
    }

    /// <summary>Коробка внутри ячейки в шестнадцатых: от (X0, Y0, Z0) до (X1, Y1, Z1).</summary>
    public readonly struct FormBox : IEquatable<FormBox>
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

        /// <summary>Наименьшая коробка, вмещающая обе.</summary>
        public FormBox Union(FormBox other) => new(
            Math.Min(X0, other.X0), Math.Min(Y0, other.Y0), Math.Min(Z0, other.Z0),
            Math.Max(X1, other.X1), Math.Max(Y1, other.Y1), Math.Max(Z1, other.Z1));

        public bool Equals(FormBox o) => X0 == o.X0 && Y0 == o.Y0 && Z0 == o.Z0 && X1 == o.X1 && Y1 == o.Y1 && Z1 == o.Z1;
        public override bool Equals(object obj) => obj is FormBox other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X0, Y0, Z0, X1, Y1, Z1);

        public override string ToString() => $"({X0},{Y0},{Z0})–({X1},{Y1},{Z1})";
    }

    /// <summary>
    /// Призма внутри ячейки: плоский многоугольник, вытянутый вдоль оси <see cref="Axis"/> (0 — X, 1 — Y, 2 — Z)
    /// от <see cref="From"/> до <see cref="To"/>. Так задаются гладкие формы: круглый столб, изгиб полуарки,
    /// скос кронштейна. Точки профиля — в шестнадцатых, в плоскости двух других осей по порядку: для оси X — (y, z),
    /// для Y — (x, z), для Z — (x, y). Профиль звёздный относительно первой точки: торцы режутся веером из неё.
    /// </summary>
    public sealed class FormPrism
    {
        public readonly int Axis;
        public readonly int From;
        public readonly int To;
        public readonly Vector2[] Profile;

        public FormPrism(int axis, int from, int to, Vector2[] profile)
        {
            Axis = axis;
            From = from;
            To = to;
            Profile = profile;
        }

        /// <summary>
        /// Площадь профиля в квадратных шестнадцатых со знаком (формула шнурования):
        /// больше нуля — точки идут против часовой стрелки в осях профиля.
        /// </summary>
        public float SignedArea
        {
            get
            {
                var twice = 0f;
                for (var i = 0; i < Profile.Length; i++)
                {
                    var a = Profile[i];
                    var b = Profile[(i + 1) % Profile.Length];
                    twice += a.x * b.y - b.x * a.y;
                }
                return twice * 0.5f;
            }
        }

        public float Area => Mathf.Abs(SignedArea);

        public float Volume => Area * (To - From);

        /// <summary>Точка профиля p на отметке t вдоль оси — в координатах ячейки (шестнадцатые, без поворота).</summary>
        public Vector3 Point(Vector2 p, float t) => Axis switch
        {
            0 => new Vector3(t, p.x, p.y),
            1 => new Vector3(p.x, t, p.y),
            _ => new Vector3(p.x, p.y, t),
        };

        /// <summary>Ограничивающая коробка, округлённая наружу до целых шестнадцатых.</summary>
        public FormBox Bounds
        {
            get
            {
                Vector3 min = Point(Profile[0], From), max = min;
                foreach (var p in Profile)
                {
                    min = Vector3.Min(min, Vector3.Min(Point(p, From), Point(p, To)));
                    max = Vector3.Max(max, Vector3.Max(Point(p, From), Point(p, To)));
                }
                const float e = 1e-3f;
                return new FormBox(
                    Mathf.FloorToInt(min.x + e), Mathf.FloorToInt(min.y + e), Mathf.FloorToInt(min.z + e),
                    Mathf.CeilToInt(max.x - e), Mathf.CeilToInt(max.y - e), Mathf.CeilToInt(max.z - e));
            }
        }
    }

    /// <summary>
    /// Каталог фигурных форм: каждая собрана из коробок и призм на сетке 16 × 16 × 16, поэтому меш, клик мышью
    /// и прочность (заполнение = объём) считаются одинаково для всех форм. Детали на несколько клеток,
    /// деревянные формы и дверь — #44–#46.
    /// </summary>
    public static class BlockForms
    {
        private const int S = BlockShape.MaxHeight;
        private const int CircleSegments = 24;
        private const int ArchSegments = 12;

        /// <summary>Радиус изгиба полуарки в шестнадцатых: над проёмом у −Z остаётся замок высотой 16 − ArchRadius.</summary>
        public const int ArchRadius = 12;

        private static readonly FormBox[] NoBoxes = Array.Empty<FormBox>();
        private static readonly FormPrism[] NoPrisms = Array.Empty<FormPrism>();

        private static readonly FormBox[] Stairs =
        {
            new(0, 0, 0, S, 4, S),
            new(0, 4, 4, S, 8, S),
            new(0, 8, 8, S, 12, S),
            new(0, 12, 12, S, 16, S),
        };

        private static readonly FormBox[] SquarePillar = { new(4, 0, 4, 12, S, 12) };

        private static readonly FormBox[] Beam = { new(4, 8, 0, 12, S, S) };

        private static readonly FormPrism[] RoundPillar = { new(1, 0, S, Circle(new Vector2(8, 8), 5, CircleSegments)) };

        // Профиль (y, z): верх стены, низ стены, низ губы, верх губы.
        private static readonly FormPrism[] Bracket =
        {
            new(0, 4, 12, new Vector2[] { new(S, S), new(0, S), new(S - 4, 0), new(S, 0) }),
        };

        private static readonly FormPrism[] HalfArch = { new(0, 0, S, ArchProfile()) };

        /// <summary>Коробки формы без поворота.</summary>
        public static ReadOnlySpan<FormBox> Boxes(BlockForm form) => form switch
        {
            BlockForm.Stairs => Stairs,
            BlockForm.SquarePillar => SquarePillar,
            BlockForm.Beam => Beam,
            BlockForm.RoundPillar or BlockForm.Bracket or BlockForm.HalfArch => NoBoxes,
            _ => throw new ArgumentOutOfRangeException(nameof(form), $"Формы {form} нет в каталоге"),
        };

        /// <summary>Призмы формы без поворота: гладкие части, которые коробками не собрать.</summary>
        public static ReadOnlySpan<FormPrism> Prisms(BlockForm form) => form switch
        {
            BlockForm.RoundPillar => RoundPillar,
            BlockForm.Bracket => Bracket,
            BlockForm.HalfArch => HalfArch,
            BlockForm.Stairs or BlockForm.SquarePillar or BlockForm.Beam => NoPrisms,
            _ => throw new ArgumentOutOfRangeException(nameof(form), $"Формы {form} нет в каталоге"),
        };

        /// <summary>Объём формы, доля ячейки 0..1.</summary>
        public static float Fill(BlockForm form)
        {
            var volume = 0f;
            foreach (var box in Boxes(form))
                volume += box.Volume;
            foreach (var prism in Prisms(form))
                volume += prism.Volume;
            return volume / (S * S * S);
        }

        /// <summary>Наибольшая высота формы в шестнадцатых.</summary>
        public static int Top(BlockForm form) => Extent(form, 0).Y1;

        /// <summary>Коробка, в которую помещается форма с поворотом: по ней рисуются рамка и трещины.</summary>
        public static FormBox Extent(BlockForm form, int rotation)
        {
            FormBox? extent = null;
            foreach (var box in Boxes(form))
                extent = extent?.Union(box) ?? box;
            foreach (var prism in Prisms(form))
                extent = extent?.Union(prism.Bounds) ?? prism.Bounds;
            return extent?.Rotated(rotation) ?? default;
        }

        /// <summary>
        /// До какой высоты форма гарантированно закрывает любую сторону ячейки при любом повороте.
        /// Сосед сравнивает с ней свои углы и рисует боковую грань выше неё — лишняя грань внутри
        /// формы не видна, а дыр не бывает. Сторону закрывает только сплошная плита снизу на всю ячейку
        /// (нижняя ступень лестницы); у столбов, балки, кронштейна и полуарки — 0.
        /// </summary>
        public static int SideCover(BlockForm form)
        {
            var cover = 0;
            foreach (var box in Boxes(form))
            {
                if (box.Y0 == 0 && box.X0 == 0 && box.Z0 == 0 && box.X1 == S && box.Z1 == S)
                    cover = Mathf.Max(cover, box.Y1);
            }
            return cover;
        }

        /// <summary>
        /// Закрывает ли форма весь пол ячейки. Нет — у столбов, балки, кронштейна и полуарки: вокруг них
        /// виден верх блока снизу, и его нужно рисовать.
        /// </summary>
        public static bool CoversFloor(BlockForm form) => SideCover(form) > 0;

        /// <summary>Правильный многоугольник вокруг центра, против часовой стрелки.</summary>
        private static Vector2[] Circle(Vector2 center, float radius, int segments)
        {
            var points = new Vector2[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = 2f * Mathf.PI * i / segments;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        /// <summary>
        /// Профиль полуарки (y, z): верх ноги (16, 16), пол ноги (0, 16), по полу к началу дуги, по дуге вокруг
        /// ребра (y 0, z 0) до низа замка, верх замка (16, 0). Из верха ноги видна вся дуга, поэтому веер торца цел.
        /// </summary>
        private static Vector2[] ArchProfile()
        {
            var points = new Vector2[ArchSegments + 4];
            var n = 0;
            points[n++] = new Vector2(S, S);
            points[n++] = new Vector2(0, S);
            for (var i = 0; i <= ArchSegments; i++)
            {
                var angle = 0.5f * Mathf.PI * i / ArchSegments;
                points[n++] = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * ArchRadius;
            }
            points[n] = new Vector2(S, 0);
            return points;
        }
    }
}
