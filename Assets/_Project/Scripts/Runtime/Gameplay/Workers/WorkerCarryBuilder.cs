using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Как выглядит ноша.</summary>
    public enum WorkerCarry
    {
        None,
        /// <summary>Бревно на плече.</summary>
        Log,
        /// <summary>Заплечная плетёная корзина с камнями: на спине на лямках, руки свободны.</summary>
        StoneBasket,
        /// <summary>Такая же корзина с травой и прочим сыпучим.</summary>
        GrassBasket,
    }

    /// <summary>
    /// Сетка ноши и где она бывает: на плече или спине (<see cref="ShoulderAnchor"/>, в системе корпуса), путь оттуда
    /// на землю (<see cref="PathDown"/>, в системе рабочего), за что её держит левая рука (<see cref="Grip"/>, в системе ноши).
    /// Бревно лежит на левом плече вдоль хода, рука обхватывает его сбоку сверху. Корзина прижата к спине ниже капюшона
    /// и висит на двух лямках: от края корзины через плечи, по груди и под мышки, снизу короткие ремни от дна к бокам.
    /// Руки при этом свободны. Снимают корзину через левое плечо: левая рука берёт её на спине, ведёт к левому боку
    /// и вперёд к груди, правая подхватывает, и обеими руками корзину ставят на землю перед собой; поднимают в обратном
    /// порядке. Носят только свободные рабочие (без роли). Единицы — блоки при росте height.
    /// </summary>
    public static class WorkerCarryBuilder
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.7f, 0.54f, 0.34f);
        private static readonly Color Wicker = new(0.62f, 0.47f, 0.27f);
        private static readonly Color Stone = new(0.52f, 0.52f, 0.5f);
        private static readonly Color Grass = new(0.42f, 0.56f, 0.24f);
        private static readonly Color Leather = new(0.3f, 0.21f, 0.14f);

        private const float LogRadius = 0.05f;
        private const float LogLength = 0.46f;

        /// <summary>Пояс — точка вращения корпуса, доли роста: от неё считаются места ноши на плече.</summary>
        private const float Waist = 0.515f;

        /// <summary>Половина высоты корзины (от середины до дна), доли роста. Начало координат корзины — её середина, верх — +Y.</summary>
        private const float BasketHalfHeight = 0.12f;

        /// <summary>Сечения стенки корзины снизу вверх: высота, полуширина, полуглубина (доли роста).</summary>
        private static readonly Vector3[] BasketWall =
        {
            new(-BasketHalfHeight, 0.084f, 0.064f), new(-0.1f, 0.09f, 0.069f), new(0f, 0.1f, 0.077f), new(0.09f, 0.112f, 0.086f),
            new(0.12f, 0.116f, 0.089f),
        };

        /// <summary>
        /// Середина корзины на спине и её наклон, система рабочего: передняя стенка почти отвесно прилегает к спине
        /// и подолу, верх — ниже края пелерины и мешка капюшона.
        /// </summary>
        private static readonly Vector3 BasketOnBack = new(0f, 0.469f, -0.183f);

        private static readonly Quaternion BasketTilt = Quaternion.Euler(-8f, 0f, 0f);

        /// <summary>
        /// Верхняя часть левой лямки (правая — зеркально по X), система рабочего: от края корзины вверх по спине поверх
        /// пелерины, через плечо у шеи, вниз по груди под край пелерины и по телу под мышку.
        /// </summary>
        private static readonly Vector3[] StrapOver =
        {
            new(-0.08f, 0.6f, -0.118f), new(-0.09f, 0.63f, -0.126f), new(-0.1f, 0.665f, -0.128f), new(-0.1f, 0.72f, -0.124f),
            new(-0.1f, 0.758f, -0.084f), new(-0.1f, 0.779f, -0.02f), new(-0.1f, 0.768f, 0.042f), new(-0.1f, 0.738f, 0.083f),
            new(-0.1f, 0.702f, 0.108f), new(-0.105f, 0.645f, 0.084f), new(-0.11f, 0.6f, 0.072f), new(-0.112f, 0.56f, 0.054f),
            new(-0.126f, 0.53f, 0.014f),
        };

        /// <summary>Нижний ремень левой лямки: от дна корзины к боку под рукой, система рабочего.</summary>
        private static readonly Vector3[] StrapUnder =
        {
            new(-0.07f, 0.375f, -0.105f), new(-0.1f, 0.42f, -0.085f), new(-0.12f, 0.47f, -0.055f), new(-0.124f, 0.5f, -0.03f),
        };

        /// <summary>Точка рабочего (корзина на спине) в системе корзины, доли роста.</summary>
        private static Vector3 OnBasket(Vector3 worker) => Quaternion.Inverse(BasketTilt) * (worker - BasketOnBack);

        /// <summary>
        /// Ноша по ресурсу: поленья — бревно, камень — корзина с камнями, остальное — корзина с травой. Носят только
        /// свободные рабочие: у рабочего с ролью ноши не бывает.
        /// </summary>
        public static WorkerCarry CarryFor(string itemId, WorkerRole role) => itemId switch
        {
            null or "" => WorkerCarry.None,
            _ when role != WorkerRole.Free => WorkerCarry.None,
            "wood" => WorkerCarry.Log,
            "stone" => WorkerCarry.StoneBasket,
            _ => WorkerCarry.GrassBasket,
        };

        /// <summary>Держит ли рука ношу, пока её несут: бревно — да, корзина висит на лямках сама.</summary>
        public static bool HeldWhileCarried(WorkerCarry carry) => carry == WorkerCarry.Log;

        /// <summary>Ноша на плече или спине: матрица в системе корпуса (начало — его точка вращения).</summary>
        public static Matrix4x4 ShoulderAnchor(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            // Бревно на вершине левого плеча, передний конец чуть выше.
            ? Matrix4x4.TRS(new Vector3(-0.138f, 0.284f, 0.02f) * height, Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f)), Vector3.one)
            : Matrix4x4.TRS((BasketOnBack - Vector3.up * Waist) * height, BasketTilt, Vector3.one);

        /// <summary>
        /// Путь ноши с плеча или спины на землю (без начальной точки): система рабочего, ноги в нуле. Бревно сначала уходит
        /// вперёд и наружу с плеча, мимо поднятой руки, потом опускается вдоль левого бока. Корзина — через левое плечо
        /// к левому боку, вперёд к груди и вниз перед рабочим. Последняя точка — на земле. Поднять — тот же путь обратно.
        /// </summary>
        public static Matrix4x4[] PathDown(WorkerCarry carry, float height)
        {
            Matrix4x4 At(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position * height, rotation, Vector3.one);
            if (carry != WorkerCarry.Log)
            {
                // Корзину снимают через левое плечо: она уходит к левому боку, оттуда вперёд к груди (лямками к рабочему)
                // и опускается на землю перед ним.
                return new[]
                {
                    At(new Vector3(-0.33f, 0.49f, -0.18f), Quaternion.Euler(0f, 90f, 0f)),
                    At(new Vector3(-0.35f, 0.5f, 0.14f), Quaternion.Euler(0f, 135f, 0f)),
                    At(new Vector3(0f, 0.45f, 0.33f), Quaternion.Euler(0f, 180f, 0f)),
                    At(new Vector3(0f, BasketHalfHeight + 0.004f, 0.4f), Quaternion.Euler(0f, 180f, 0f)),
                };
            }
            var along = Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f));
            var level = Quaternion.LookRotation(new Vector3(0.05f, 0f, 1f));
            return new[]
            {
                At(new Vector3(-0.3f, 0.8f, 0.22f), along),
                At(new Vector3(-0.25f, 0.52f, 0.2f), level),
                At(new Vector3(-0.25f, LogRadius, 0.22f), level),
            };
        }

        /// <summary>Где ноша, положенная на землю перед рабочим: система рабочего, ноги в нуле.</summary>
        public static Matrix4x4 Ground(WorkerCarry carry, float height) => PathDown(carry, height)[^1];

        /// <summary>
        /// Где руки держат корзину: x — насколько в стороны от её середины (по сторонам рабочего), y — насколько выше
        /// середины по её оси; блоки.
        /// </summary>
        public static Vector2 BasketSideGrip(float height) => new Vector2(0.135f, 0.09f) * height;

        /// <summary>За что держит левая рука, в системе ноши: бревно — сбоку снаружи и чуть сверху, ближе к переднему концу.</summary>
        public static Vector3 Grip(WorkerCarry carry, float height) => new Vector3(-LogRadius - 0.022f, 0.022f, 0.1f) * height;

        /// <summary>Куда отводить локоть, когда рука держит ношу: в системе рабочего, относительно точки хвата.</summary>
        public static Vector3 ElbowHint(float height) => new Vector3(-0.25f, -0.35f, -0.05f) * height;

        public static Mesh Build(WorkerCarry carry, float height, string name = "Carry")
        {
            var scale = Matrix4x4.Scale(Vector3.one * height);
            var mesh = new FacetedMesh { Transform = scale };
            switch (carry)
            {
                case WorkerCarry.Log:
                    Log(mesh);
                    break;
                case WorkerCarry.StoneBasket:
                    Basket(mesh);
                    Stones(mesh);
                    break;
                case WorkerCarry.GrassBasket:
                    Basket(mesh);
                    GrassHeap(mesh, scale);
                    break;
            }
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static void Log(FacetedMesh mesh)
        {
            // Бревно вдоль +Z, светлые торцы, сучок сверху.
            var back = Vector3.back * (LogLength * 0.5f);
            var front = Vector3.forward * (LogLength * 0.5f);
            mesh.AddTube(new[] { back, front }, new[] { Vector2.one * LogRadius, Vector2.one * (LogRadius * 0.93f) }, 8, Paint(Bark));
            mesh.AddTube(new[] { back + Vector3.back * 0.003f, back + Vector3.forward * 0.004f }, new[] { Vector2.one * (LogRadius * 0.86f), Vector2.one * (LogRadius * 0.86f) },
                8, Paint(WoodLight));
            mesh.AddTube(new[] { front + Vector3.back * 0.004f, front + Vector3.forward * 0.003f }, new[] { Vector2.one * (LogRadius * 0.8f), Vector2.one * (LogRadius * 0.8f) },
                8, Paint(WoodLight));
            mesh.AddTube(new[] { new Vector3(0.01f, LogRadius * 0.8f, -0.09f), new Vector3(0.03f, LogRadius + 0.03f, -0.11f) },
                new[] { new Vector2(0.013f, 0.013f), new Vector2(0.006f, 0.006f) }, 5, Paint(Bark * 0.85f));
        }

        /// <summary>Верх насыпи в корзине: ниже края, чтобы изнутри была видна стенка и кромка, доли роста.</summary>
        private const float FillTop = 0.075f;

        /// <summary>Пустая корзина: тонкие плетёные стенки (внутри темнее), полосы плетения, тонкий обод по краю, лямки.</summary>
        private static void Basket(FacetedMesh mesh)
        {
            var rings = new LoftRing[BasketWall.Length];
            for (var i = 0; i < rings.Length; i++)
                rings[i] = new LoftRing(BasketWall[i].x, BasketWall[i].y, BasketWall[i].z);
            mesh.AddLoft(rings, 12, Paint(Wicker), capStart: true, capEnd: false, twoSided: true, innerColor: Paint(Wicker * 0.6f));
            foreach (var (y, r) in new[] { (-0.085f, 0.093f), (-0.04f, 0.097f), (0.005f, 0.101f), (0.05f, 0.106f) })
                mesh.AddLoft(new[] { new LoftRing(y, r + 0.004f, r * 0.77f + 0.004f), new LoftRing(y + 0.012f, r + 0.004f, r * 0.77f + 0.004f) }, 12,
                    Paint(Wicker * 0.78f), capStart: false, capEnd: false);
            // Обод по краю — тонкая полоса снаружи и изнутри, без крышки: корзина открыта сверху.
            var top = BasketWall[^1];
            mesh.AddLoft(new[] { new LoftRing(top.x - 0.014f, top.y + 0.004f, top.z + 0.004f), new LoftRing(top.x, top.y + 0.004f, top.z + 0.004f) }, 12,
                Paint(Wicker * 0.85f), capStart: false, capEnd: false, twoSided: true, innerColor: Paint(Wicker * 0.5f));
            mesh.AddLoft(new[] { new LoftRing(top.x - 0.014f, top.y - 0.005f, top.z - 0.005f), new LoftRing(top.x, top.y - 0.005f, top.z - 0.005f) }, 12,
                Paint(Wicker * 0.5f), capStart: false, capEnd: false, twoSided: true, innerColor: Paint(Wicker * 0.8f));
            foreach (var side in new[] { -1f, 1f })
            {
                Strap(mesh, StrapOver, side);
                Strap(mesh, StrapUnder, side);
            }
        }

        private static void Strap(FacetedMesh mesh, Vector3[] left, float side)
        {
            var path = new Vector3[left.Length];
            var radii = new Vector2[path.Length];
            for (var i = 0; i < path.Length; i++)
            {
                path[i] = OnBasket(Vector3.Scale(left[i], new Vector3(-side, 1f, 1f)));
                radii[i] = new Vector2(0.012f, 0.005f);
            }
            mesh.AddTube(path, radii, 6, Paint(Leather));
        }

        /// <summary>Корзина полна камней: насыпь чуть ниже края и сверху горка камней.</summary>
        private static void Stones(FacetedMesh mesh)
        {
            Fill(mesh, Stone * 0.7f);
            var stones = new[]
            {
                new Vector3(-0.05f, 0.082f, 0.025f), new Vector3(0.05f, 0.084f, 0.02f), new Vector3(0f, 0.086f, -0.035f),
                new Vector3(-0.045f, 0.084f, -0.03f), new Vector3(0.045f, 0.085f, -0.03f), new Vector3(0f, 0.082f, 0.04f),
                new Vector3(0.004f, 0.11f, 0.003f),
            };
            for (var i = 0; i < stones.Length; i++)
                mesh.AddBlob(stones[i], Vector3.one * (0.036f - 0.003f * (i % 3)), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.82f + 0.05f * (i % 4))));
        }

        /// <summary>Корзина полна травы: насыпь чуть ниже края и пучки, торчащие вверх и наружу.</summary>
        private static void GrassHeap(FacetedMesh mesh, Matrix4x4 scale)
        {
            Fill(mesh, Grass * 0.75f);
            // Пучки: плоские листья кольцами, у края наклонены наружу, в середине стоят торчком.
            var random = new System.Random(5);
            for (var i = 0; i < 26; i++)
            {
                var ring = i < 8 ? 0.02f : i < 16 ? 0.05f : 0.08f;
                var a = i * 2.4f;
                var tilt = 34f * ring / 0.08f;
                mesh.Transform = scale * Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * ring, FillTop - 0.005f, Mathf.Sin(a) * ring * 0.75f),
                    Quaternion.Euler(Mathf.Sin(a) * tilt, (float)random.NextDouble() * 180f, -Mathf.Cos(a) * tilt), new Vector3(1f, 1f, 0.3f));
                mesh.AddCone(3, Vector3.zero, 0.011f, 0.07f + 0.035f * (float)random.NextDouble(), 0f, null, Paint(Grass * (0.78f + 0.25f * (float)random.NextDouble())));
            }
            mesh.Transform = scale;
        }

        /// <summary>Насыпь в корзине: сплошной слой от дна до <see cref="FillTop"/>, сверху виден как поверхность содержимого.</summary>
        private static void Fill(FacetedMesh mesh, Color color)
        {
            mesh.AddLoft(new[]
            {
                new LoftRing(-BasketHalfHeight + 0.01f, BasketWall[0].y - 0.006f, BasketWall[0].z - 0.006f),
                new LoftRing(FillTop, 0.106f, 0.081f),
            }, 12, Paint(color));
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);

        /// <summary>
        /// Лямки корзины: на спине (flatten = 1) — через плечи и под мышки; пока корзину снимают, каждая втягивается вдоль
        /// своего же пути к месту, где пришита (путь идёт снаружи тела, поэтому лямка ни через что не проходит), и у снятой
        /// корзины от лямок остаются узелки у края и у дна. Бревно не меняется. Вершины меняются в сетке ноши.
        /// </summary>
        public sealed class Shape
        {
            private readonly Mesh mesh;
            private readonly Vector3[] restVertices;
            private readonly Vector3[] restNormals;
            private readonly Vector3[] vertices;
            private readonly bool straps;

            /// <summary>Лямки в системе корзины (блоки): левые и правые, верхние и нижние.</summary>
            private readonly Vector3[][] lines;

            /// <summary>У вершины лямки — её лямка, иначе −1; где она вдоль лямки (длина от места, где пришита) и сдвиг от оси.</summary>
            private readonly int[] line;
            private readonly float[] along;
            private readonly Vector3[] offset;

            public Shape(Mesh mesh, WorkerCarry carry, float height)
            {
                this.mesh = mesh;
                straps = carry is WorkerCarry.StoneBasket or WorkerCarry.GrassBasket;
                if (!straps)
                    return;
                mesh.MarkDynamic();
                restVertices = mesh.vertices;
                restNormals = mesh.normals;
                vertices = new Vector3[restVertices.Length];
                lines = new Vector3[4][];
                var index = 0;
                foreach (var side in new[] { -1f, 1f })
                foreach (var left in new[] { StrapOver, StrapUnder })
                {
                    var points = new Vector3[left.Length];
                    for (var i = 0; i < points.Length; i++)
                        points[i] = OnBasket(Vector3.Scale(left[i], new Vector3(-side, 1f, 1f))) * height;
                    lines[index++] = points;
                }
                line = new int[restVertices.Length];
                along = new float[restVertices.Length];
                offset = new Vector3[restVertices.Length];
                for (var v = 0; v < restVertices.Length; v++)
                {
                    line[v] = -1;
                    var best = 0.02f * height;
                    for (var l = 0; l < lines.Length; l++)
                    {
                        var length = 0f;
                        for (var k = 1; k < lines[l].Length; k++)
                        {
                            var a = lines[l][k - 1];
                            var ab = lines[l][k] - a;
                            var t = Mathf.Clamp01(Vector3.Dot(restVertices[v] - a, ab) / ab.sqrMagnitude);
                            var on = a + ab * t;
                            var distance = Vector3.Distance(restVertices[v], on);
                            if (distance < best)
                            {
                                best = distance;
                                line[v] = l;
                                along[v] = length + ab.magnitude * t;
                                offset[v] = restVertices[v] - on;
                            }
                            length += ab.magnitude;
                        }
                    }
                }
            }

            public Mesh Mesh => mesh;

            /// <summary>Точка лямки l на расстоянии s от места, где она пришита.</summary>
            private Vector3 PointAlong(int l, float s)
            {
                var points = lines[l];
                for (var k = 1; k < points.Length; k++)
                {
                    var length = Vector3.Distance(points[k - 1], points[k]);
                    if (s <= length)
                        return Vector3.Lerp(points[k - 1], points[k], s / length);
                    s -= length;
                }
                return points[^1];
            }

            /// <summary>
            /// Обновить форму. loadToWorld — где ноша в мире (для единого вызова, лямкам не нужно); flatten — насколько
            /// корзина на спине (1 — лямки на плечах, 0 — снята); deltaTime — шаг времени.
            /// </summary>
            public void Apply(Matrix4x4 loadToWorld, float flatten, float deltaTime)
            {
                if (!straps)
                    return;
                var left = Mathf.Clamp01(flatten);
                for (var v = 0; v < restVertices.Length; v++)
                    vertices[v] = line[v] < 0 ? restVertices[v] : PointAlong(line[v], along[v] * left) + offset[v];
                mesh.vertices = vertices;
                mesh.normals = restNormals;
                mesh.RecalculateBounds();
            }
        }
    }
}
