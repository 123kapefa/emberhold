using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>
    /// Гранёная low-poly сетка пучка мелочи по форме вида (<see cref="GroundCoverKind.Shape"/>), в стиле деревьев.
    /// Основание в (0, 0, 0), ось Y вверх, единица — блок. У каждого треугольника свои вершины и нормаль грани;
    /// плоские части (травинки, листья, пятна) двусторонние — по треугольнику на каждую сторону. Цвет вершин линейный:
    /// rgb — цвет части (у земли темнее), a — 1 у частей, которые подстраиваются под оттенок травы (<see cref="Tint"/>),
    /// и 0 у остальных. Вид и номер формы всегда дают одну и ту же сетку.
    /// </summary>
    public static class GroundCoverMeshBuilder
    {
        private static readonly Color StemColor = new(0.435f, 0.541f, 0.227f);

        /// <summary>Вершины, нормали и цвета формы variant вида kind.</summary>
        public sealed class Shape
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Color> Colors = new();
        }

        /// <summary>Форма variant (0..Variants−1) вида kind.</summary>
        public static Shape Build(GroundCoverKind kind, int variant)
        {
            var random = new System.Random(Seed(kind, variant));
            var shape = new Shape();
            var builder = new Builder(shape);
            switch (kind.Shape)
            {
                case GroundCoverShape.Flowers:
                    BuildFlowers(builder, kind, random);
                    break;
                case GroundCoverShape.Fern:
                    BuildFern(builder, kind, random);
                    break;
                case GroundCoverShape.Mushrooms:
                    BuildMushrooms(builder, kind, random);
                    break;
                case GroundCoverShape.Reeds:
                    BuildReeds(builder, kind, random);
                    break;
                case GroundCoverShape.Moss:
                    BuildMoss(builder, kind, random);
                    break;
                case GroundCoverShape.Lichen:
                    BuildLichen(builder, kind, random);
                    break;
                default:
                    BuildGrass(builder, kind, random, Vector3.zero, 1f);
                    break;
            }
            return shape;
        }

        /// <summary>
        /// Цвет части, которая подстраивается под траву: как у крон, rgb оттенка — множитель, a — насыщенность
        /// (1 — как есть, меньше — блёклее). Белый оттенок оставляет цвет как есть.
        /// </summary>
        public static Color Tint(Color color, Color grassTint)
        {
            var luma = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            var tinted = Color.LerpUnclamped(new Color(luma, luma, luma), color, grassTint.a) * grassTint;
            tinted.a = color.a;
            return tinted;
        }

        // Пучок изогнутых травинок, темнее у корня; scale — для травы под цветами.
        private static void BuildGrass(Builder b, GroundCoverKind kind, System.Random random, Vector3 origin, float scale)
        {
            var color = Pick(kind.Colors, random, StemColor).linear;
            var alpha = kind.TintedByGrass ? 1f : 0f;
            var tall = kind.Height > 0.6f;
            var count = (tall ? 9 : 6) + random.Next(4);
            for (var i = 0; i < count; i++)
            {
                var angle = Range(random, 0f, Mathf.PI * 2f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-direction.z, 0f, direction.x) * (Range(random, 0.04f, 0.07f) * scale);
                var root = origin + direction * (Range(random, 0f, 0.12f) * scale);
                var height = kind.Height * Range(random, 0.55f, 1f) * scale;
                var lean = Range(random, 0.15f, 0.4f) * height;
                var middle = root + direction * (lean * 0.4f) + Vector3.up * (height * 0.55f);
                var tip = root + direction * lean + Vector3.up * height;
                var low = (color * 0.7f).WithAlpha(alpha);
                var high = (color * Range(random, 1f, 1.15f)).WithAlpha(alpha);
                b.Quad(root - side, root + side, middle + side * 0.6f, middle - side * 0.6f, low);
                b.Flat(middle - side * 0.6f, middle + side * 0.6f, tip, high);
            }
        }

        // Несколько стеблей с гранёными головками и пучок травы вокруг; низкие (горные) — без травы.
        private static void BuildFlowers(Builder b, GroundCoverKind kind, System.Random random)
        {
            var head = Pick(kind.Accents, random, Color.white).linear.WithAlpha(0f);
            var stem = StemColor.linear.WithAlpha(1f);
            var small = kind.Height < 0.2f;
            var count = 3 + random.Next(4);
            for (var i = 0; i < count; i++)
            {
                var root = new Vector3(Range(random, -0.25f, 0.25f), 0f, Range(random, -0.25f, 0.25f));
                var height = kind.Height * Range(random, 0.55f, 1f);
                AddStem(b, root, height, 0.012f, stem);
                var size = small ? 0.045f : 0.075f;
                b.Blob(root + Vector3.up * height, new Vector3(size, size * 0.67f, size), random, 0.1f,
                    y => head * (y > root.y + height ? 1.08f : 0.85f));
                b.Flat(root + Vector3.up * 0.05f, root + new Vector3(0.08f, 0.11f, 0.024f), root + new Vector3(0.032f, 0.04f, 0.08f), stem);
            }
            if (!small)
                BuildGrass(b, kind, random, Vector3.zero, 0.7f);
        }

        // Розетка листьев: каждый лист дугой от земли, по бокам зубчатые листочки.
        private static void BuildFern(Builder b, GroundCoverKind kind, System.Random random)
        {
            var color = Pick(kind.Colors, random, StemColor).linear;
            var count = 6 + random.Next(3);
            const int segments = 5;
            for (var i = 0; i < count; i++)
            {
                var angle = (float)i / count * Mathf.PI * 2f + Range(random, -0.3f, 0.3f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-direction.z, 0f, direction.x);
                var length = Range(random, 0.4f, 0.55f) * kind.Height / 0.35f;
                var rise = Range(random, 0.8f, 1.1f) * kind.Height;
                Vector3 At(float t) => direction * (length * t) + Vector3.up * (rise * Mathf.Sin(Mathf.PI * 0.85f * t) + 0.02f);
                for (var s = 0; s < segments; s++)
                {
                    var t0 = (float)s / segments;
                    var t1 = (float)(s + 1) / segments;
                    var p0 = At(t0);
                    var p1 = At(t1);
                    var w0 = 0.11f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t0 * 1.1f + 0.08f));
                    var w1 = 0.11f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t1 * 1.1f + 0.08f));
                    for (var sign = -1; sign <= 1; sign += 2)
                    {
                        var tip = p1 + side * (sign * w1 * 1.2f) - direction * 0.02f + Vector3.down * 0.04f;
                        b.Flat(p0, p1, tip, (color * (0.8f + 0.3f * t1)).WithAlpha(1f));
                        var middle = (p0 + p1) * 0.5f + side * (sign * w0) + Vector3.down * 0.02f;
                        b.Flat(p0, middle, p1, (color * (0.75f + 0.3f * t0)).WithAlpha(1f));
                    }
                }
            }
        }

        // Кучка грибов: ножки-призмы и шляпки-конусы, у мухомора крапинки.
        private static void BuildMushrooms(Builder b, GroundCoverKind kind, System.Random random)
        {
            var cap = Pick(kind.Accents, random, new Color(0.541f, 0.392f, 0.251f)).linear.WithAlpha(0f);
            var stem = (Pick(kind.Colors, random, new Color(0.847f, 0.812f, 0.722f)).linear * 0.95f).WithAlpha(0f);
            var spots = kind.SpotColor.a > 0f && cap.r > cap.g * 1.6f;
            var count = 2 + random.Next(3);
            for (var i = 0; i < count; i++)
            {
                var root = new Vector3(Range(random, -0.15f, 0.15f), 0f, Range(random, -0.15f, 0.15f));
                var height = kind.Height * Range(random, 0.45f, 1f);
                var radius = Range(random, 0.07f, 0.12f) * kind.Height / 0.22f;
                b.Prism(5, root, radius * 0.38f, radius * 0.3f, height, _ => stem);
                var capBase = root + Vector3.up * (height - radius * 0.1f);
                b.Cone(6, capBase, radius, radius * 0.8f, y => cap * (y > capBase.y + radius * 0.15f ? 1f : 0.6f));
                if (!spots)
                    continue;
                for (var k = 0; k < 3; k++)
                {
                    var a = Range(random, 0f, Mathf.PI * 2f);
                    var p = capBase + new Vector3(Mathf.Cos(a) * radius * 0.45f, radius * 0.4f, Mathf.Sin(a) * radius * 0.45f);
                    b.Blob(p, Vector3.one * (radius * 0.12f), random, 0f, _ => kind.SpotColor.linear.WithAlpha(0f));
                }
            }
        }

        // Рогоз: высокие стебли накрест, часть с бурой головкой.
        private static void BuildReeds(Builder b, GroundCoverKind kind, System.Random random)
        {
            var color = Pick(kind.Colors, random, StemColor).linear;
            var headColor = Pick(kind.Accents, random, new Color(0.416f, 0.29f, 0.188f)).linear.WithAlpha(0f);
            var count = 7 + random.Next(5);
            for (var i = 0; i < count; i++)
            {
                var root = new Vector3(Range(random, -0.2f, 0.2f), 0f, Range(random, -0.2f, 0.2f));
                var height = kind.Height * Range(random, 0.62f, 1f);
                var lean = new Vector3(Range(random, -0.08f, 0.08f), 0f, Range(random, -0.08f, 0.08f));
                var top = root + lean + Vector3.up * height;
                var col = (color * Range(random, 0.9f, 1.05f)).WithAlpha(1f);
                const float w = 0.025f;
                b.Quad(root - Vector3.right * w, root + Vector3.right * w, top + Vector3.right * (w * 0.4f), top - Vector3.right * (w * 0.4f), col);
                b.Quad(root - Vector3.forward * w, root + Vector3.forward * w, top + Vector3.forward * (w * 0.4f), top - Vector3.forward * (w * 0.4f), col);
                if (random.NextDouble() < 0.45)
                    b.Prism(5, root + lean * 0.85f + Vector3.up * (height * 0.78f), 0.035f, 0.035f, 0.18f, _ => headColor);
            }
        }

        // Мох: несколько приплюснутых гранёных подушек.
        private static void BuildMoss(Builder b, GroundCoverKind kind, System.Random random)
        {
            var color = Pick(kind.Colors, random, StemColor).linear;
            var count = 3 + random.Next(4);
            for (var i = 0; i < count; i++)
            {
                var size = Range(random, 0.12f, 0.28f);
                var center = new Vector3(Range(random, -0.3f, 0.3f), 0f, Range(random, -0.3f, 0.3f));
                var shade = Range(random, 0.95f, 1.12f);
                b.Blob(center, new Vector3(size, Mathf.Max(0.02f, kind.Height) * size / 0.2f, size * Range(random, 0.8f, 1.2f)), random, 0.15f,
                    y => (color * (y > center.y + 0.02f ? shade : 0.7f)).WithAlpha(1f));
            }
        }

        // Лишайник: плоские неровные пятна у самой земли; с цветами вида — горные цветы среди пятен.
        private static void BuildLichen(Builder b, GroundCoverKind kind, System.Random random)
        {
            var count = 4 + random.Next(5);
            for (var i = 0; i < count; i++)
            {
                var center = new Vector3(Range(random, -0.35f, 0.35f), 0.012f + Range(random, 0f, 0.01f), Range(random, -0.35f, 0.35f));
                var radius = Range(random, 0.06f, 0.16f);
                var color = (Pick(kind.Colors, random, new Color(0.604f, 0.647f, 0.541f)).linear * Range(random, 0.9f, 1.05f)).WithAlpha(0f);
                const int sides = 6;
                var r0 = radius * Range(random, 0.8f, 1.2f);
                var first = r0;
                for (var j = 0; j < sides; j++)
                {
                    var a0 = (float)j / sides * Mathf.PI * 2f;
                    var a1 = (float)(j + 1) / sides * Mathf.PI * 2f;
                    var r1 = j == sides - 1 ? first : radius * Range(random, 0.8f, 1.2f);
                    b.Flat(center, center + new Vector3(Mathf.Cos(a1) * r1, 0f, Mathf.Sin(a1) * r1),
                        center + new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0), color);
                    r0 = r1;
                }
            }
            if (kind.Accents.Length == 0)
                return;
            var flowers = 3 + random.Next(4);
            var stem = StemColor.linear.WithAlpha(0f);
            for (var i = 0; i < flowers; i++)
            {
                var root = new Vector3(Range(random, -0.3f, 0.3f), 0f, Range(random, -0.3f, 0.3f));
                var height = kind.Height * Range(random, 0.5f, 1f);
                AddStem(b, root, height, 0.01f, stem);
                var head = Pick(kind.Accents, random, Color.white).linear.WithAlpha(0f);
                b.Blob(root + Vector3.up * height, new Vector3(0.045f, 0.03f, 0.045f), random, 0.1f, _ => head);
            }
        }

        private static void AddStem(Builder b, Vector3 root, float height, float half, Color color)
        {
            var top = root + Vector3.up * height;
            b.Quad(root - Vector3.right * half, root + Vector3.right * half, top + Vector3.right * half, top - Vector3.right * half, color);
            b.Quad(root - Vector3.forward * half, root + Vector3.forward * half, top + Vector3.forward * half, top - Vector3.forward * half, color);
        }

        private static Color Pick(Color[] colors, System.Random random, Color fallback) =>
            colors.Length == 0 ? fallback : colors[random.Next(colors.Length)];

        private static Color WithAlpha(this Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // Seed по имени вида (FNV-1a), а не string.GetHashCode: тот может меняться между запусками.
        private static int Seed(GroundCoverKind kind, int variant)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in kind.name)
                    hash = (hash ^ c) * 16777619u;
                return (int)(hash ^ (uint)variant * 0x9E3779B1u);
            }
        }

        private sealed class Builder
        {
            private static readonly List<Vector3> Points = new();
            private static readonly List<int> Faces = new();
            private readonly Shape shape;

            public Builder(Shape shape)
            {
                this.shape = shape;
            }

            /// <summary>Плоский двусторонний треугольник.</summary>
            public void Flat(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f)
                    return;
                normal.Normalize();
                Add(a, b, c, normal, color);
                Add(a, c, b, -normal, color);
            }

            /// <summary>Плоский двусторонний четырёхугольник a-b-c-d.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                Flat(a, b, c, color);
                Flat(a, c, d, color);
            }

            /// <summary>Гранёный комок: икосаэдр, растянутый по radii; цвет грани по её высоте.</summary>
            public void Blob(Vector3 center, Vector3 radii, System.Random random, float jitter, System.Func<float, Color> color)
            {
                Points.Clear();
                Faces.Clear();
                var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
                foreach (var p in new[]
                         {
                             new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                             new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                             new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                         })
                {
                    var j = new Vector3(Range(random, -0.5f, 0.5f), Range(random, -0.5f, 0.5f), Range(random, -0.5f, 0.5f)) * jitter;
                    Points.Add(center + Vector3.Scale(p.normalized + j, radii));
                }
                Faces.AddRange(new[]
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                    1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                    4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                });
                Solid(center, color);
            }

            /// <summary>Призма (ножка гриба, головка рогоза) от base вверх на height.</summary>
            public void Prism(int sides, Vector3 basePoint, float bottomRadius, float topRadius, float height, System.Func<float, Color> color)
            {
                Points.Clear();
                Faces.Clear();
                for (var i = 0; i < sides; i++)
                {
                    var angle = (float)i / sides * Mathf.PI * 2f;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Points.Add(basePoint + direction * bottomRadius);
                    Points.Add(basePoint + direction * topRadius + Vector3.up * height);
                }
                var top = Points.Count;
                Points.Add(basePoint + Vector3.up * height);
                for (var i = 0; i < sides; i++)
                {
                    int b0 = i * 2, t0 = b0 + 1, b1 = (i + 1) % sides * 2, t1 = b1 + 1;
                    Faces.AddRange(new[] { b0, t0, t1, b0, t1, b1, top, t0, t1 });
                }
                Solid(basePoint + Vector3.up * (height * 0.5f), color);
            }

            /// <summary>Конус (шляпка) с основанием в basePoint, вершиной выше на height, с дном.</summary>
            public void Cone(int sides, Vector3 basePoint, float radius, float height, System.Func<float, Color> color)
            {
                Points.Clear();
                Faces.Clear();
                for (var i = 0; i < sides; i++)
                {
                    var angle = (float)i / sides * Mathf.PI * 2f;
                    Points.Add(basePoint + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                }
                var apex = Points.Count;
                Points.Add(basePoint + Vector3.up * height);
                Points.Add(basePoint);
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    Faces.AddRange(new[] { i, next, apex, i, next, apex + 1 });
                }
                Solid(basePoint + Vector3.up * (height * 0.3f), color);
            }

            // Грани тела из Points и Faces: нормаль каждой смотрит от inside наружу, цвет — по высоте середины грани.
            private void Solid(Vector3 inside, System.Func<float, Color> color)
            {
                for (var i = 0; i < Faces.Count; i += 3)
                {
                    var a = Points[Faces[i]];
                    var b = Points[Faces[i + 1]];
                    var c = Points[Faces[i + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-12f)
                        continue;
                    normal.Normalize();
                    // В Unity лицевая сторона — обход по часовой стрелке, её нормаль Cross(b − a, c − a).
                    if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f)
                    {
                        (b, c) = (c, b);
                        normal = -normal;
                    }
                    Add(a, b, c, normal, color((a.y + b.y + c.y) / 3f));
                }
            }

            private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color color)
            {
                shape.Vertices.Add(a);
                shape.Vertices.Add(b);
                shape.Vertices.Add(c);
                for (var i = 0; i < 3; i++)
                {
                    shape.Normals.Add(normal);
                    shape.Colors.Add(color);
                }
            }
        }
    }
}
