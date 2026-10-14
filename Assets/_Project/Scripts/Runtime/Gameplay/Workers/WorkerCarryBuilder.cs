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
        /// <summary>Мешок с камнями, перекинутый через плечо на спину.</summary>
        StoneSack,
        /// <summary>Мешок с травой и прочим сыпучим.</summary>
        GrassSack,
    }

    /// <summary>
    /// Сетка ноши и где она бывает: на плече (<see cref="ShoulderAnchor"/>, в системе корпуса), путь с плеча на землю
    /// (<see cref="PathDown"/>, в системе рабочего), за что её держит левая рука (<see cref="Grip"/>, в системе ноши).
    /// Бревно лежит на левом плече вдоль хода, рука обхватывает его сбоку сверху. Мешок висит на спине, его горловина
    /// перекинута через левое плечо и спускается по груди, рука держит её у груди, локоть опущен. Единицы — блоки при росте height.
    /// </summary>
    public static class WorkerCarryBuilder
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.7f, 0.54f, 0.34f);
        private static readonly Color Burlap = new(0.6f, 0.5f, 0.34f);
        private static readonly Color Stone = new(0.52f, 0.52f, 0.5f);
        private static readonly Color Grass = new(0.42f, 0.56f, 0.24f);
        private static readonly Color Twine = new(0.4f, 0.3f, 0.18f);

        private const float LogRadius = 0.05f;
        private const float LogLength = 0.46f;

        /// <summary>Тело мешка: полуоси (доли роста). Начало координат мешка — середина тела, горловина — вдоль +Y.</summary>
        private static readonly Vector3 SackBody = new(0.115f, 0.15f, 0.09f);

        /// <summary>Середина мешка на спине, в системе рабочего (доли роста).</summary>
        private static readonly Vector3 SackOnBack = new(-0.08f, 0.6f, -0.21f);

        /// <summary>Куда идёт горловина от мешка — на вершину левого плеча: по этой оси стоит мешок; система рабочего.</summary>
        private static readonly Vector3 SackNeckOnShoulder = new(-0.125f, 0.81f, -0.02f);

        /// <summary>
        /// Горловина за плечом, система рабочего: лежит на вершине плеча поверх пелерины, спускается по груди,
        /// за <see cref="SackGripIndex"/> её держит рука, конец с устьем чуть выходит из кулака вперёд.
        /// </summary>
        private static readonly Vector3[] SackNeckOverShoulder =
        {
            new(-0.12f, 0.795f, 0f), new(-0.12f, 0.775f, 0.064f), new(-0.118f, 0.725f, 0.108f), new(-0.116f, 0.69f, 0.128f),
            new(-0.114f, 0.672f, 0.168f),
        };

        private const int SackGripIndex = 3;

        /// <summary>Поворот мешка на спине в системе рабочего: горловина (+Y) — к плечу.</summary>
        private static Quaternion SackRotation
        {
            get
            {
                var up = (SackNeckOnShoulder - SackOnBack).normalized;
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up), up);
            }
        }

        /// <summary>Точка рабочего (мешок на спине) в системе мешка, доли роста.</summary>
        private static Vector3 OnSack(Vector3 worker) => Quaternion.Inverse(SackRotation) * (worker - SackOnBack);

        /// <summary>Пояс — точка вращения корпуса, доли роста: от неё считаются места ноши на плече.</summary>
        private const float Waist = 0.515f;


        /// <summary>
        /// Ноша по ресурсу: поленья — бревно, камень — мешок с камнями, остальное — мешок с травой.
        /// Истопник носит поленья вязанкой на спине, которая и так на нём, поэтому на плече у него ничего.
        /// </summary>
        public static WorkerCarry CarryFor(string itemId, WorkerRole role) => itemId switch
        {
            null or "" => WorkerCarry.None,
            "wood" when role == WorkerRole.Stoker => WorkerCarry.None,
            "wood" => WorkerCarry.Log,
            "stone" => WorkerCarry.StoneSack,
            _ => WorkerCarry.GrassSack,
        };

        /// <summary>Ноша на плече: матрица в системе корпуса (начало — его точка вращения).</summary>
        public static Matrix4x4 ShoulderAnchor(WorkerCarry carry, float height)
        {
            if (carry == WorkerCarry.Log)
            {
                // Бревно на вершине левого плеча, передний конец чуть выше.
                return Matrix4x4.TRS(new Vector3(-0.138f, 0.284f, 0.02f) * height, Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f)), Vector3.one);
            }
            // Мешок висит на спине, горловина — через плечо к руке у груди.
            return Matrix4x4.TRS((SackOnBack - Vector3.up * Waist) * height, SackRotation, Vector3.one);
        }

        /// <summary>
        /// Путь ноши с плеча на землю (без начальной точки — плеча): система рабочего, ноги в нуле. Путь обходит тело:
        /// бревно сначала сдвигается вперёд с плеча, потом опускается вдоль левого бока; мешок снимается через плечо
        /// влево, потом вперёд и вниз. Последняя точка — на земле. Поднять — тот же путь в обратную сторону.
        /// </summary>
        public static Matrix4x4[] PathDown(WorkerCarry carry, float height)
        {
            Matrix4x4 At(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position * height, rotation, Vector3.one);
            if (carry == WorkerCarry.Log)
            {
                // С плеча бревно сначала уходит вперёд и наружу, дальше от плеча: поднятая рука под ним не проходит сквозь него.
                var along = Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f));
                var level = Quaternion.LookRotation(new Vector3(0.05f, 0f, 1f));
                return new[]
                {
                    At(new Vector3(-0.3f, 0.8f, 0.22f), along),
                    At(new Vector3(-0.25f, 0.52f, 0.2f), level),
                    At(new Vector3(-0.25f, LogRadius, 0.22f), level),
                };
            }
            var upright = Quaternion.identity;
            return new[]
            {
                At(new Vector3(-0.32f, 0.62f, -0.16f), Quaternion.Euler(20f, 0f, 10f)),
                At(new Vector3(-0.3f, 0.5f, 0.06f), upright),
                At(new Vector3(-0.24f, 0.3f, 0.22f), upright),
                At(new Vector3(-0.22f, SackBody.y + 0.004f, 0.25f), upright),
            };
        }

        /// <summary>Где ноша, положенная на землю перед рабочим: система рабочего, ноги в нуле.</summary>
        public static Matrix4x4 Ground(WorkerCarry carry, float height) => PathDown(carry, height)[^1];

        /// <summary>
        /// За что держит левая рука, в системе ноши. bend — изгиб мешка (<see cref="WorkerPose.LoadBend"/>): на плече
        /// горловина перекинута и рука держит её у груди, снятый с плеча мешок висит на распрямлённой горловине.
        /// </summary>
        public static Vector3 Grip(WorkerCarry carry, float height, float bend = 1f) => carry == WorkerCarry.Log
            // Сбоку снаружи и чуть сверху, ближе к переднему концу.
            ? new Vector3(-LogRadius - 0.022f, 0.022f, 0.1f) * height
            : Vector3.Lerp(NeckStraight(NeckGripIndex), NeckLine[NeckGripIndex], Mathf.Clamp01(bend)) * height;

        /// <summary>
        /// Ось горловины в системе мешка (доли роста), когда мешок на плече: от тела по оси мешка, через плечо, вниз по груди
        /// к руке и устью.
        /// </summary>
        private static readonly Vector3[] NeckLine = BuildNeckLine();

        private const int NeckGripIndex = SackGripIndex + 2;

        private static Vector3[] BuildNeckLine()
        {
            var line = new Vector3[SackNeckOverShoulder.Length + 2];
            line[0] = new Vector3(0f, 0.19f, 0f);
            line[1] = new Vector3(0f, 0.25f, 0f);
            for (var i = 0; i < SackNeckOverShoulder.Length; i++)
                line[i + 2] = OnSack(SackNeckOverShoulder[i]);
            return line;
        }

        /// <summary>Точка i оси горловины, когда она распрямлена вдоль оси мешка.</summary>
        private static Vector3 NeckStraight(int i)
        {
            var length = NeckLine[0].y;
            for (var k = 1; k <= i; k++)
                length += Vector3.Distance(NeckLine[k - 1], NeckLine[k]);
            return new Vector3(0f, length, 0f);
        }

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
                case WorkerCarry.StoneSack:
                    Sack(mesh, Burlap);
                    // Из открытой горловины выглядывают камни.
                    for (var i = 0; i < 4; i++)
                    {
                        var a = i * 1.7f;
                        mesh.AddBlob(Mouth + MouthTurn * new Vector3(Mathf.Cos(a) * 0.014f, -0.004f + 0.006f * (i % 2), Mathf.Sin(a) * 0.012f),
                            Vector3.one * (0.024f - 0.002f * i), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.85f + 0.05f * i)));
                    }
                    break;
                case WorkerCarry.GrassSack:
                    Sack(mesh, Burlap * 0.9f);
                    // Из горловины торчат пучки травы.
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i * 1.05f;
                        mesh.Transform = scale * Matrix4x4.TRS(Mouth + MouthTurn * new Vector3(Mathf.Cos(a) * 0.012f, -0.01f, Mathf.Sin(a) * 0.01f),
                            MouthTurn * Quaternion.Euler(Mathf.Sin(a) * 25f, 0f, Mathf.Cos(a) * 25f), new Vector3(1f, 1f, 0.35f));
                        mesh.AddCone(4, Vector3.zero, 0.013f, 0.08f + 0.012f * (i % 3), 0f, null, Paint(Grass * (0.85f + 0.06f * (i % 3))));
                    }
                    break;
            }
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        /// <summary>Устье — конец горловины у груди, система мешка.</summary>
        private static Vector3 Mouth => OnSack(SackNeckOverShoulder[^1]) + MouthTurn * new Vector3(0f, 0.004f, 0f);

        /// <summary>Поворот от оси мешка (+Y) к направлению, куда смотрит устье.</summary>
        private static Quaternion MouthTurn =>
            Quaternion.FromToRotation(Vector3.up, OnSack(SackNeckOverShoulder[^1]) - OnSack(SackNeckOverShoulder[^2]));

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

        private static void Sack(FacetedMesh mesh, Color cloth)
        {
            // Тело мешка — шире к дну; сверху горловина, перехваченная завязкой, через плечо вниз по груди, и раскрытый край.
            mesh.AddBlob(Vector3.zero, SackBody, 1, 0.08f, new System.Random(7), Paint(cloth));
            mesh.AddBlob(new Vector3(0f, -0.05f, 0f), new Vector3(SackBody.x * 1.08f, SackBody.y * 0.68f, SackBody.z * 1.08f), 1, 0.06f,
                new System.Random(9), Paint(cloth * 0.95f));
            mesh.AddLoft(new[] { new LoftRing(0.11f, 0.075f, 0.06f), new LoftRing(0.19f, 0.03f, 0.026f), new LoftRing(0.25f, 0.022f, 0.02f) },
                8, Paint(cloth * 0.93f), capStart: false, capEnd: false);
            var neck = new Vector3[SackNeckOverShoulder.Length + 1];
            var radii = new Vector2[neck.Length];
            neck[0] = new Vector3(0f, 0.25f, 0f);
            radii[0] = new Vector2(0.022f, 0.02f);
            for (var i = 0; i < SackNeckOverShoulder.Length; i++)
            {
                neck[i + 1] = OnSack(SackNeckOverShoulder[i]);
                radii[i + 1] = new Vector2(0.021f, 0.02f);
            }
            mesh.AddTube(neck, radii, 8, Paint(cloth * 0.93f));
            var mouth = OnSack(SackNeckOverShoulder[^1]);
            mesh.AddTube(new[] { mouth - MouthTurn * new Vector3(0f, 0.012f, 0f), mouth }, new[] { new Vector2(0.034f, 0.03f), new Vector2(0.03f, 0.027f) },
                8, Paint(cloth * 0.93f));
            mesh.AddLoft(new[] { new LoftRing(0.235f, 0.026f, 0.024f), new LoftRing(0.255f, 0.026f, 0.024f) }, 8, Paint(Twine));
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);

        /// <summary>Перегиб мешка на плече: у горловины, доли роста по оси мешка.</summary>
        private const float SackHinge = 0.05f;

        /// <summary>Насколько тело мешка может уйти к спине (градусы): дальше спина не пускает.</summary>
        private const float SackBackLimit = -52f;

        /// <summary>
        /// Тело мешка висит под действием тяжести: ниже перегиба оно поворачивается так, чтобы смотреть вниз,
        /// с пружиной — при шаге, повороте и рывке качается и успокаивается. На спине (flatten = 1) мешок
        /// сплющивается и не уходит внутрь спины. Бревно не гнётся. Вершины меняются в сетке ноши.
        /// </summary>
        public sealed class Shape
        {
            private const float Stiffness = 70f;
            private const float Damping = 9f;
            private const float InertiaScale = 0.08f;

            private readonly Mesh mesh;
            private readonly Vector3[] restVertices;
            private readonly Vector3[] restNormals;
            private readonly Vector3[] vertices;
            private readonly Vector3[] normals;
            private readonly float height;
            private readonly bool bends;

            /// <summary>Насколько тело мешка на спине отодвинуто назад (система мешка, блоки): за снаряжением роли.</summary>
            private readonly Vector3 clearance;

            /// <summary>Где вершина горловины, когда горловина распрямлена; у тела мешка — там же, где в сетке.</summary>
            private readonly Vector3[] straight;
            private Vector3 hang = Vector3.down;
            private Vector3 hangVelocity;
            private Vector3 lastPosition;
            private Vector3 lastVelocity;
            private bool hasLast;

            /// <summary>role — чья ноша: мешок на спине ложится поверх того, что у роли уже висит на спине.</summary>
            public Shape(Mesh mesh, WorkerCarry carry, float height, WorkerRole role = WorkerRole.Free)
            {
                this.mesh = mesh;
                this.height = height;
                var shift = WorkerGearBuilder.BackLoadShift(role);
                clearance = Quaternion.Inverse(SackRotation) * new Vector3(0f, shift.y, -shift.x) * height;
                bends = carry is WorkerCarry.StoneSack or WorkerCarry.GrassSack;
                if (!bends)
                    return;
                mesh.MarkDynamic();
                restVertices = mesh.vertices;
                restNormals = mesh.normals;
                vertices = new Vector3[restVertices.Length];
                normals = new Vector3[restNormals.Length];
                straight = new Vector3[restVertices.Length];
                for (var v = 0; v < restVertices.Length; v++)
                    straight[v] = Straighten(restVertices[v] / height) * height;
            }

            /// <summary>
            /// Точка горловины (доли роста) там, где она окажется, если горловину распрямить вдоль оси мешка: ближайший отрезок
            /// оси (последний — с продолжением, там устье с камнями или травой) поворачивается вдоль +Y вместе с точкой.
            /// </summary>
            private static Vector3 Straighten(Vector3 p)
            {
                if (p.y <= NeckLine[0].y)
                    return p;
                var best = float.MaxValue;
                var result = p;
                for (var k = 1; k < NeckLine.Length; k++)
                {
                    var a = NeckLine[k - 1];
                    var ab = NeckLine[k] - a;
                    var t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
                    t = k == NeckLine.Length - 1 ? Mathf.Max(0f, t) : Mathf.Clamp01(t);
                    var on = a + ab * t;
                    var distance = Vector3.Distance(p, on);
                    if (distance >= best)
                        continue;
                    best = distance;
                    var from = NeckStraight(k - 1);
                    result = from + Vector3.up * (ab.magnitude * t) + Quaternion.FromToRotation(ab, Vector3.up) * (p - on);
                }
                return result;
            }

            public Mesh Mesh => mesh;

            /// <summary>
            /// Обновить форму. loadToWorld — где ноша в мире; flatten — насколько мешок прижат к спине (0…1);
            /// deltaTime — шаг для качания, 0 — сразу повиснуть отвесно (снимок, перемотка).
            /// </summary>
            public void Apply(Matrix4x4 loadToWorld, float flatten, float deltaTime)
            {
                if (!bends)
                    return;
                Swing(loadToWorld.GetColumn(3), deltaTime);
                var down = Quaternion.Inverse(loadToWorld.rotation) * hang;
                // Повороты, при которых ось мешка (−Y) смотрит вдоль down: к +Z — вокруг X, вбок — вокруг Z.
                var aroundX = -Mathf.Atan2(down.z, -down.y) * Mathf.Rad2Deg;
                var aroundZ = Mathf.Atan2(down.x, -down.y) * Mathf.Rad2Deg;
                flatten = Mathf.Clamp01(flatten);
                aroundX = Mathf.Max(aroundX, Mathf.Lerp(-180f, SackBackLimit, flatten));
                aroundZ = Mathf.Clamp(aroundZ, Mathf.Lerp(-180f, -30f, flatten), Mathf.Lerp(180f, 30f, flatten));

                var hinge = new Vector3(0f, SackHinge * height, 0f);
                for (var v = 0; v < restVertices.Length; v++)
                {
                    // Горловина перекинута через плечо, пока мешок на спине, и распрямляется, когда его снимают.
                    var p = Vector3.Lerp(straight[v], restVertices[v], flatten);
                    // На спине тело мешка лежит поверх снаряжения роли.
                    p += clearance * (flatten * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NeckLine[1].y, NeckLine[0].y - 0.08f, p.y / height)));
                    // Ниже перегиба тело мешка плавно поворачивается вниз; на спине — сплющено.
                    var w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SackHinge + 0.02f, SackHinge - 0.12f, p.y / height));
                    var turn = Quaternion.Euler(aroundX * w, 0f, aroundZ * w);
                    var flat = new Vector3(1f, 1f, 1f - 0.3f * w * flatten);
                    vertices[v] = hinge + turn * Vector3.Scale(p - hinge, flat);
                    normals[v] = (turn * Vector3.Scale(restNormals[v], new Vector3(flat.y * flat.z, flat.x * flat.z, flat.x * flat.y))).normalized;
                }
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.RecalculateBounds();
            }

            /// <summary>Направление «вниз» для тела мешка: тяжесть минус ускорение ноши, с пружиной и затуханием.</summary>
            private void Swing(Vector3 position, float deltaTime)
            {
                if (deltaTime <= 0f || !hasLast)
                {
                    hang = Vector3.down;
                    hangVelocity = Vector3.zero;
                    lastVelocity = Vector3.zero;
                    lastPosition = position;
                    hasLast = deltaTime > 0f;
                    return;
                }
                var velocity = (position - lastPosition) / deltaTime;
                var acceleration = (velocity - lastVelocity) / deltaTime;
                lastPosition = position;
                lastVelocity = velocity;
                var target = (Vector3.down * 9.8f - Vector3.ClampMagnitude(acceleration, 40f) * InertiaScale).normalized;
                hangVelocity += ((target - hang) * Stiffness - hangVelocity * Damping) * deltaTime;
                hang = (hang + hangVelocity * deltaTime).normalized;
            }
        }
    }
}
