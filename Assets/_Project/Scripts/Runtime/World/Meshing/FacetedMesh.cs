using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Гранёная low-poly сетка из простых тел (усечённый конус, конус, коробка, комок): тела из общих точек,
    /// каждая грань — свои три вершины с нормалью грани, поэтому грани читаются при любом свете. Цвет вершины задаёт
    /// функция от её положения. Общая для деревьев (<see cref="Trees.TreeMeshBuilder"/>) и рабочих.
    /// </summary>
    public sealed class FacetedMesh
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Color> colors = new();
        private readonly List<int> triangles = new();
        private readonly List<Vector3> points = new();
        private readonly List<int> faces = new();

        /// <summary>
        /// Куда ставить следующие тела: их точки умножаются на эту матрицу (наклонить руку, повернуть нос).
        /// Функция цвета получает уже перенесённую точку.
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.identity;

        /// <summary>Усечённый конус (ствол, полоска) от base вверх на height; jitter двигает точки только по X и Z.</summary>
        public void AddPrism(int sides, Vector3 basePoint, float bottomRadius, float topRadius, float height, float jitter,
            System.Random random, System.Func<Vector3, Color> color)
        {
            points.Clear();
            faces.Clear();
            for (var i = 0; i < sides; i++)
            {
                var angle = (float)i / sides * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                points.Add(basePoint + direction * bottomRadius + FlatJitter(random, jitter));
                points.Add(basePoint + direction * topRadius + Vector3.up * height + FlatJitter(random, jitter));
            }
            var bottomCenter = points.Count;
            points.Add(basePoint);
            points.Add(basePoint + Vector3.up * height);
            for (var i = 0; i < sides; i++)
            {
                var b0 = i * 2;
                var t0 = b0 + 1;
                var b1 = (i + 1) % sides * 2;
                var t1 = b1 + 1;
                faces.AddRange(new[] { b0, t0, t1, b0, t1, b1, bottomCenter, b1, b0, bottomCenter + 1, t0, t1 });
            }
            Emit(basePoint + Vector3.up * (height * 0.5f), color);
        }

        /// <summary>Гранёный конус с кольцом посередине: основание в basePoint, вершина выше на height.</summary>
        public void AddCone(int sides, Vector3 basePoint, float radius, float height, float jitter, System.Random random,
            System.Func<Vector3, Color> color)
        {
            points.Clear();
            faces.Clear();
            for (var i = 0; i < sides; i++)
            {
                var angle = (float)i / sides * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                points.Add(basePoint + direction * radius + Jitter(random, jitter));
                var middle = (i + 0.5f) / sides * Mathf.PI * 2f;
                points.Add(basePoint + new Vector3(Mathf.Cos(middle), 0f, Mathf.Sin(middle)) * (radius * 0.5f)
                    + Vector3.up * (height * 0.5f) + Jitter(random, jitter));
            }
            var apex = points.Count;
            points.Add(basePoint + Vector3.up * height + Jitter(random, jitter * 0.5f));
            points.Add(basePoint + Vector3.up * (height * 0.05f));
            // Точки через одну: основание b(i) = 2i, кольцо посередине m(i) = 2i + 1 (повёрнуто на полшага).
            for (var i = 0; i < sides; i++)
            {
                var b0 = i * 2;
                var b1 = (i + 1) % sides * 2;
                var m0 = b0 + 1;
                var m1 = b1 + 1;
                var mPrev = (i + sides - 1) % sides * 2 + 1;
                faces.AddRange(new[] { b0, b1, m0, mPrev, m0, b0, m0, m1, apex, apex + 1, b0, b1 });
            }
            Emit(basePoint + Vector3.up * (height * 0.3f), color);
        }

        /// <summary>Коробка с центром center и размерами size.</summary>
        public void AddBox(Vector3 center, Vector3 size, System.Func<Vector3, Color> color)
        {
            points.Clear();
            faces.Clear();
            var half = size * 0.5f;
            for (var i = 0; i < 8; i++)
                points.Add(center + Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
            // Обход граней поправит Emit: нормаль смотрит от центра наружу.
            faces.AddRange(new[]
            {
                0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, // −z, +z
                0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, // −y, +y
                0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3, // −x, +x
            });
            Emit(center, color);
        }

        /// <summary>Гранёный комок: икосаэдр (subdivisions раз поделённый), неровный, растянутый по radii.</summary>
        public void AddBlob(Vector3 center, Vector3 radii, int subdivisions, float jitter, System.Random random,
            System.Func<Vector3, Color> color)
        {
            Icosphere.Build(subdivisions, points, faces);
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i] + Jitter(random, jitter);
                points[i] = center + Vector3.Scale(p, radii);
            }
            Emit(center, color);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Грани тела из points и faces; нормаль каждой грани смотрит от inside наружу.</summary>
        private void Emit(Vector3 inside, System.Func<Vector3, Color> color)
        {
            var transform = Transform;
            inside = transform.MultiplyPoint3x4(inside);
            for (var i = 0; i < faces.Count; i += 3)
            {
                var a = transform.MultiplyPoint3x4(points[faces[i]]);
                var b = transform.MultiplyPoint3x4(points[faces[i + 1]]);
                var c = transform.MultiplyPoint3x4(points[faces[i + 2]]);
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
                foreach (var p in new[] { a, b, c })
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(p);
                    normals.Add(normal);
                    colors.Add(color(p));
                }
            }
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // Без random (тела без неровности) точки не сдвигаются.
        private static Vector3 Jitter(System.Random random, float amount) => random == null ? Vector3.zero
            : new Vector3(Range(random, -0.5f, 0.5f), Range(random, -0.5f, 0.5f), Range(random, -0.5f, 0.5f)) * amount;

        private static Vector3 FlatJitter(System.Random random, float amount) => random == null ? Vector3.zero
            : new Vector3(Range(random, -0.5f, 0.5f), 0f, Range(random, -0.5f, 0.5f)) * amount;

        /// <summary>Икосаэдр единичного радиуса, каждое деление разбивает грань на четыре.</summary>
        private static class Icosphere
        {
            public static void Build(int subdivisions, List<Vector3> points, List<int> faces)
            {
                points.Clear();
                faces.Clear();
                var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
                foreach (var p in new[]
                         {
                             new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                             new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                             new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                         })
                    points.Add(p.normalized);
                faces.AddRange(new[]
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                    1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                    4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                });
                for (var s = 0; s < subdivisions; s++)
                {
                    var middles = new Dictionary<long, int>();
                    var next = new List<int>(faces.Count * 4);
                    for (var i = 0; i < faces.Count; i += 3)
                    {
                        int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                        var ab = Middle(points, middles, a, b);
                        var bc = Middle(points, middles, b, c);
                        var ca = Middle(points, middles, c, a);
                        next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    faces.Clear();
                    faces.AddRange(next);
                }
            }

            private static int Middle(List<Vector3> points, Dictionary<long, int> middles, int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (middles.TryGetValue(key, out var index))
                    return index;
                index = points.Count;
                points.Add(((points[a] + points[b]) * 0.5f).normalized);
                middles[key] = index;
                return index;
            }
        }
    }
}
