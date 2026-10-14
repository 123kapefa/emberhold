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
    /// Бревно лежит на левом плече вдоль хода, рука обхватывает его сбоку сверху. Мешок висит на спине, его короткая
    /// горловина идёт через левое плечо, рука держит её на плече. Единицы — блоки при росте height.
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

        /// <summary>Длина мешка от середины тела до конца горловины, доли роста.</summary>
        private const float SackLength = 0.31f;

        /// <summary>Середина мешка на спине, в системе рабочего (доли роста).</summary>
        private static readonly Vector3 SackOnBack = new(-0.08f, 0.6f, -0.21f);

        /// <summary>Конец горловины на вершине левого плеча — за него держит рука; система рабочего.</summary>
        private static readonly Vector3 SackGripOnShoulder = new(-0.125f, 0.81f, -0.02f);

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
            // Мешок висит на спине, горловина — через плечо к руке на плече.
            var up = (SackGripOnShoulder - SackOnBack).normalized;
            var rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up), up);
            return Matrix4x4.TRS((SackOnBack - Vector3.up * Waist) * height, rotation, Vector3.one);
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
                var along = Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f));
                var level = Quaternion.LookRotation(new Vector3(0.05f, 0f, 1f));
                return new[]
                {
                    At(new Vector3(-0.17f, 0.8f, 0.22f), along),
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

        /// <summary>За что держит левая рука, в системе ноши.</summary>
        public static Vector3 Grip(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            // Сбоку снаружи и чуть сверху, ближе к переднему концу.
            ? new Vector3(-LogRadius - 0.022f, 0.022f, 0.1f) * height
            // Конец горловины.
            : new Vector3(0f, SackLength - 0.02f, 0f) * height;

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
                        mesh.AddBlob(Mouth + new Vector3(Mathf.Cos(a) * 0.014f, -0.004f + 0.006f * (i % 2), Mathf.Sin(a) * 0.012f),
                            Vector3.one * (0.024f - 0.002f * i), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.85f + 0.05f * i)));
                    }
                    break;
                case WorkerCarry.GrassSack:
                    Sack(mesh, Burlap * 0.9f);
                    // Из горловины торчат пучки травы.
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i * 1.05f;
                        mesh.Transform = scale * Matrix4x4.TRS(Mouth + new Vector3(Mathf.Cos(a) * 0.012f, -0.01f, Mathf.Sin(a) * 0.01f),
                            Quaternion.Euler(Mathf.Sin(a) * 25f, 0f, Mathf.Cos(a) * 25f), new Vector3(1f, 1f, 0.35f));
                        mesh.AddCone(4, Vector3.zero, 0.013f, 0.08f + 0.012f * (i % 3), 0f, null, Paint(Grass * (0.85f + 0.06f * (i % 3))));
                    }
                    break;
            }
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static Vector3 Mouth => new(0f, SackLength + 0.004f, 0f);

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
            // Тело мешка — шире к дну; сверху короткая прямая горловина, перехваченная завязкой, и раскрытый край.
            mesh.AddBlob(Vector3.zero, SackBody, 1, 0.08f, new System.Random(7), Paint(cloth));
            mesh.AddBlob(new Vector3(0f, -0.05f, 0f), new Vector3(SackBody.x * 1.08f, SackBody.y * 0.68f, SackBody.z * 1.08f), 1, 0.06f,
                new System.Random(9), Paint(cloth * 0.95f));
            mesh.AddLoft(new[]
            {
                new LoftRing(0.11f, 0.075f, 0.06f), new LoftRing(0.19f, 0.03f, 0.026f), new LoftRing(0.25f, 0.022f, 0.02f),
                new LoftRing(SackLength - 0.012f, 0.034f, 0.03f), new LoftRing(SackLength, 0.03f, 0.027f),
            }, 8, Paint(cloth * 0.93f), capStart: false, capEnd: false);
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
            private Vector3 hang = Vector3.down;
            private Vector3 hangVelocity;
            private Vector3 lastPosition;
            private Vector3 lastVelocity;
            private bool hasLast;

            public Shape(Mesh mesh, WorkerCarry carry, float height)
            {
                this.mesh = mesh;
                this.height = height;
                bends = carry is WorkerCarry.StoneSack or WorkerCarry.GrassSack;
                if (!bends)
                    return;
                mesh.MarkDynamic();
                restVertices = mesh.vertices;
                restNormals = mesh.normals;
                vertices = new Vector3[restVertices.Length];
                normals = new Vector3[restNormals.Length];
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
                    var p = restVertices[v];
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
