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
        /// <summary>Мешок с травой и прочим сыпучим, перекинутый через плечо на спину.</summary>
        GrassSack,
    }

    /// <summary>
    /// Сетка ноши и где она бывает: на плече или спине (<see cref="ShoulderAnchor"/>, в системе корпуса), путь оттуда
    /// на землю (<see cref="PathDown"/>, в системе рабочего), за что её держит левая рука (<see cref="Grip"/>, в системе ноши).
    /// Бревно лежит на левом плече вдоль хода, рука обхватывает его сбоку сверху. Мешок — широкий, с плоским дном
    /// и короткой собранной горловиной на завязке — перекинут через левое плечо и висит на спине, рука держит собранный
    /// верх у плеча спереди. Корзина висит на спине на двух лямках через плечи, руки свободны; её снимают и надевают
    /// без рук — присев, опускают за спиной на землю. Носят только свободные рабочие (без роли). Единицы — блоки при росте height.
    /// </summary>
    public static class WorkerCarryBuilder
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.7f, 0.54f, 0.34f);
        private static readonly Color Burlap = new(0.6f, 0.5f, 0.34f);
        private static readonly Color Wicker = new(0.62f, 0.47f, 0.27f);
        private static readonly Color Stone = new(0.52f, 0.52f, 0.5f);
        private static readonly Color Grass = new(0.42f, 0.56f, 0.24f);
        private static readonly Color Twine = new(0.4f, 0.3f, 0.18f);
        private static readonly Color Strap = new(0.3f, 0.21f, 0.14f);

        private const float LogRadius = 0.05f;
        private const float LogLength = 0.46f;

        /// <summary>Пояс — точка вращения корпуса, доли роста: от неё считаются места ноши на плече.</summary>
        private const float Waist = 0.515f;

        // ── Мешок ────────────────────────────────────────────────────────────────────────

        /// <summary>Половина высоты тела мешка (от середины до дна), доли роста. Начало координат мешка — середина тела, верх — +Y.</summary>
        private const float SackHalfHeight = 0.15f;

        /// <summary>Середина мешка на спине, в системе рабочего (доли роста).</summary>
        private static readonly Vector3 SackOnBack = new(-0.09f, 0.6f, -0.235f);

        /// <summary>Завязка мешка — за плечом, у спины: по оси от середины к ней стоит мешок; система рабочего.</summary>
        private static readonly Vector3 SackTie = new(-0.11f, 0.765f, -0.15f);

        /// <summary>
        /// Собранный верх мешка за завязкой, система рабочего: через вершину плеча поверх пелерины вперёд к руке
        /// (за <see cref="SackGripIndex"/> держит рука), конец — «хохолок» ткани чуть выходит из кулака.
        /// </summary>
        private static readonly Vector3[] SackTopOverShoulder =
        {
            new(-0.12f, 0.805f, -0.07f), new(-0.12f, 0.812f, 0f), new(-0.12f, 0.785f, 0.09f), new(-0.12f, 0.772f, 0.125f),
        };

        private const int SackGripIndex = 2;

        /// <summary>Поворот мешка на спине в системе рабочего: верх (+Y) — к завязке.</summary>
        private static Quaternion SackRotation
        {
            get
            {
                var up = (SackTie - SackOnBack).normalized;
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, up), up);
            }
        }

        /// <summary>Точка рабочего (мешок на спине) в системе мешка, доли роста.</summary>
        private static Vector3 OnSack(Vector3 worker) => Quaternion.Inverse(SackRotation) * (worker - SackOnBack);

        /// <summary>Где на оси мешка завязка: выше неё — собранный верх, доли роста.</summary>
        private static float TieY => Vector3.Distance(SackTie, SackOnBack);

        // ── Корзина ──────────────────────────────────────────────────────────────────────

        /// <summary>Половина высоты корзины (от середины до дна), доли роста.</summary>
        private const float BasketHalfHeight = 0.12f;

        /// <summary>Середина корзины на спине и её наклон (верх чуть назад), система рабочего.</summary>
        private static readonly Vector3 BasketOnBack = new(0f, 0.56f, -0.245f);

        private static readonly Quaternion BasketTilt = Quaternion.Euler(-6f, 0f, 0f);

        /// <summary>
        /// Лямка корзины на спине (правая — зеркально по X), система рабочего: от края корзины вверх по спине поверх
        /// пелерины, через плечо у шеи и вниз по груди.
        /// </summary>
        private static readonly Vector3[] BasketStrap =
        {
            new(-0.08f, 0.675f, -0.16f), new(-0.1f, 0.72f, -0.124f), new(-0.1f, 0.758f, -0.084f), new(-0.1f, 0.779f, -0.02f),
            new(-0.1f, 0.768f, 0.042f), new(-0.1f, 0.738f, 0.083f), new(-0.1f, 0.702f, 0.108f),
        };

        /// <summary>Точка рабочего (корзина на спине) в системе корзины, доли роста.</summary>
        private static Vector3 OnBasket(Vector3 worker) => Quaternion.Inverse(BasketTilt) * (worker - BasketOnBack);

        /// <summary>
        /// Ноша по ресурсу: поленья — бревно, камень — корзина, остальное — мешок. Носят только свободные рабочие:
        /// у рабочего с ролью ноши не бывает.
        /// </summary>
        public static WorkerCarry CarryFor(string itemId, WorkerRole role) => itemId switch
        {
            null or "" => WorkerCarry.None,
            _ when role != WorkerRole.Free => WorkerCarry.None,
            "wood" => WorkerCarry.Log,
            "stone" => WorkerCarry.StoneBasket,
            _ => WorkerCarry.GrassSack,
        };

        /// <summary>Держит ли рука ношу, пока её несут: бревно и мешок — да, корзина висит на лямках сама.</summary>
        public static bool HeldWhileCarried(WorkerCarry carry) => carry is WorkerCarry.Log or WorkerCarry.GrassSack;

        /// <summary>Ноша на плече или спине: матрица в системе корпуса (начало — его точка вращения).</summary>
        public static Matrix4x4 ShoulderAnchor(WorkerCarry carry, float height) => carry switch
        {
            // Бревно на вершине левого плеча, передний конец чуть выше.
            WorkerCarry.Log => Matrix4x4.TRS(new Vector3(-0.138f, 0.284f, 0.02f) * height, Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f)),
                Vector3.one),
            WorkerCarry.StoneBasket => Matrix4x4.TRS((BasketOnBack - Vector3.up * Waist) * height, BasketTilt, Vector3.one),
            // Мешок висит на спине, собранный верх — через плечо к руке.
            _ => Matrix4x4.TRS((SackOnBack - Vector3.up * Waist) * height, SackRotation, Vector3.one),
        };

        /// <summary>
        /// Путь ноши с плеча или спины на землю (без начальной точки): система рабочего, ноги в нуле. Путь обходит тело:
        /// бревно сначала уходит вперёд и наружу с плеча, потом опускается вдоль левого бока; мешок снимается со спины
        /// влево, потом вперёд и вниз. Последняя точка — на земле (у корзины — только она: где именно за спиной она встанет,
        /// решает присед). Поднять — тот же путь обратно.
        /// </summary>
        public static Matrix4x4[] PathDown(WorkerCarry carry, float height)
        {
            Matrix4x4 At(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position * height, rotation, Vector3.one);
            var upright = Quaternion.identity;
            switch (carry)
            {
                case WorkerCarry.Log:
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
                case WorkerCarry.StoneBasket:
                    // Корзину снимают без рук (WorkerAnimation): присев, оставляют стоять за спиной. Здесь — только как она стоит.
                    return new[] { At(new Vector3(0f, BasketHalfHeight + 0.004f, -0.3f), upright) };
                default:
                    return new[]
                    {
                        At(new Vector3(-0.32f, 0.5f, -0.16f), Quaternion.Euler(20f, 0f, 10f)),
                        At(new Vector3(-0.3f, 0.42f, 0.06f), upright),
                        At(new Vector3(-0.24f, 0.3f, 0.22f), upright),
                        At(new Vector3(-0.22f, SackHalfHeight + 0.004f, 0.25f), upright),
                    };
            }
        }

        /// <summary>Где ноша, положенная на землю перед рабочим: система рабочего, ноги в нуле.</summary>
        public static Matrix4x4 Ground(WorkerCarry carry, float height) => PathDown(carry, height)[^1];

        /// <summary>
        /// За что держит левая рука, в системе ноши. bend — изгиб мешка (<see cref="WorkerPose.LoadBend"/>): на плече
        /// собранный верх перекинут и рука держит его у плеча, снятый мешок висит на распрямлённом верху.
        /// </summary>
        public static Vector3 Grip(WorkerCarry carry, float height, float bend = 1f) => carry switch
        {
            // Сбоку снаружи и чуть сверху, ближе к переднему концу.
            WorkerCarry.Log => new Vector3(-LogRadius - 0.022f, 0.022f, 0.1f) * height,
            // Корзину рука не держит (её снимают и надевают без рук); точка — край сверху.
            WorkerCarry.StoneBasket => new Vector3(0f, 0.14f, 0.1f) * height,
            _ => Vector3.Lerp(NeckStraight(NeckGripIndex), NeckLine[NeckGripIndex], Mathf.Clamp01(bend)) * height,
        };

        /// <summary>
        /// Ось верха мешка в системе мешка (доли роста), когда мешок на плече: от сборки над телом к завязке, через плечо
        /// к руке и «хохолку».
        /// </summary>
        private static readonly Vector3[] NeckLine = BuildNeckLine();

        private const int NeckGripIndex = SackGripIndex + 2;

        private static Vector3[] BuildNeckLine()
        {
            var line = new Vector3[SackTopOverShoulder.Length + 2];
            line[0] = new Vector3(0f, TieY - 0.05f, 0f);
            line[1] = new Vector3(0f, TieY, 0f);
            for (var i = 0; i < SackTopOverShoulder.Length; i++)
                line[i + 2] = OnSack(SackTopOverShoulder[i]);
            return line;
        }

        /// <summary>Во сколько раз короче собранный верх снятого с плеча мешка: он сминается в короткий хохолок над завязкой.</summary>
        private const float TopGathered = 0.3f;

        /// <summary>Длина отрезка k оси верха, когда мешок снят с плеча: за завязкой верх смят.</summary>
        private static float GatheredLength(int k) => Vector3.Distance(NeckLine[k - 1], NeckLine[k]) * (k >= 2 ? TopGathered : 1f);

        /// <summary>Точка i оси верха мешка, когда он снят с плеча: стоит над завязкой вдоль оси мешка, смятый.</summary>
        private static Vector3 NeckStraight(int i)
        {
            var length = NeckLine[0].y;
            for (var k = 1; k <= i; k++)
                length += GatheredLength(k);
            return new Vector3(0f, length, 0f);
        }

        /// <summary>Лямки корзины, снятой со спины, опадают к краю: во сколько раз короче.</summary>
        private const float StrapsDropped = 0.15f;

        /// <summary>На сколько градусов лямка, соскальзывая с плеча, поворачивается назад вокруг места, где пришита.</summary>
        private const float StrapsSlide = 150f;

        /// <summary>
        /// Куда отводить локоть, когда рука держит ношу: в системе рабочего, относительно точки хвата. Ношу на плече рука
        /// придерживает локтем вниз; висящую под рукой (снятый мешок, корзина за край) — локтем вверх и наружу, чтобы
        /// предплечье не уходило в ношу. bend — <see cref="WorkerPose.LoadBend"/>.
        /// </summary>
        public static Vector3 ElbowHint(WorkerCarry carry, float height, float bend = 1f)
        {
            var onShoulder = new Vector3(-0.25f, -0.35f, -0.05f);
            var hanging = new Vector3(-0.3f, 0.15f, -0.05f);
            var held = carry == WorkerCarry.Log ? 1f : carry == WorkerCarry.StoneBasket ? 0f : Mathf.Clamp01(bend);
            return Vector3.Lerp(hanging, onShoulder, held) * height;
        }

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
                    break;
                case WorkerCarry.GrassSack:
                    Sack(mesh, scale);
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

        private static void Sack(FacetedMesh mesh, Matrix4x4 scale)
        {
            // Тело — широкое, с плоским дном и уголками внизу, кверху сужается и собирается в складки под завязкой.
            var cloth = Paint(Burlap * 0.9f);
            var tie = TieY;
            var bottom = -SackHalfHeight;
            mesh.AddLoft(new[]
            {
                new LoftRing(bottom, 0.1f, 0.055f), new LoftRing(bottom + 0.012f, 0.128f, 0.072f), new LoftRing(bottom + 0.06f, 0.136f, 0.082f),
                new LoftRing(0f, 0.13f, 0.082f), new LoftRing(0.06f, 0.112f, 0.074f), new LoftRing(tie - 0.07f, 0.075f, 0.055f),
                new LoftRing(tie - 0.03f, 0.036f, 0.03f), new LoftRing(tie - 0.005f, 0.024f, 0.022f),
            }, 10, cloth, capStart: true, capEnd: false);
            // Уголки дна торчат в стороны.
            foreach (var side in new[] { -1f, 1f })
                mesh.AddBlob(new Vector3(side * 0.128f, bottom + 0.012f, 0f), new Vector3(0.028f, 0.016f, 0.03f), 0, 0f, null, cloth);
            // Складки сборки над телом.
            for (var i = 0; i < 5; i++)
            {
                var a = i * 1.257f;
                mesh.AddBlob(new Vector3(Mathf.Cos(a) * 0.04f, tie - 0.045f, Mathf.Sin(a) * 0.03f), new Vector3(0.016f, 0.03f, 0.016f), 0, 0f, null,
                    Paint(Burlap * (0.84f + 0.04f * (i % 2))));
            }
            // Собранный верх: от завязки через плечо к руке и «хохолок» ткани за кулаком.
            var top = new Vector3[NeckLine.Length - 1];
            var radii = new Vector2[top.Length];
            for (var i = 0; i < top.Length; i++)
            {
                top[i] = NeckLine[i + 1];
                radii[i] = new Vector2(0.022f, 0.02f);
            }
            mesh.AddTube(top, radii, 8, Paint(Burlap * 0.86f));
            var end = NeckLine[^1];
            var along = (NeckLine[^1] - NeckLine[^2]).normalized;
            mesh.AddTube(new[] { end - along * 0.01f, end + along * 0.025f }, new[] { new Vector2(0.024f, 0.022f), new Vector2(0.04f, 0.034f) }, 8,
                Paint(Burlap * 0.92f));
            var turn = Quaternion.FromToRotation(Vector3.up, along);
            for (var i = 0; i < 4; i++)
            {
                var a = i * 1.571f + 0.4f;
                mesh.AddBlob(end + turn * new Vector3(Mathf.Cos(a) * 0.028f, 0.03f, Mathf.Sin(a) * 0.024f), new Vector3(0.016f, 0.014f, 0.016f), 0, 0f, null,
                    Paint(Burlap * (0.86f + 0.05f * (i % 2))));
            }
            // Пара пучков травы из-под «хохолка».
            for (var i = 0; i < 3; i++)
            {
                var a = i * 2.09f;
                mesh.Transform = scale * Matrix4x4.TRS(end + turn * new Vector3(Mathf.Cos(a) * 0.01f, 0.02f, Mathf.Sin(a) * 0.01f),
                    turn * Quaternion.Euler(Mathf.Sin(a) * 25f, 0f, Mathf.Cos(a) * 25f), new Vector3(1f, 1f, 0.35f));
                mesh.AddCone(4, Vector3.zero, 0.011f, 0.05f + 0.01f * i, 0f, null, Paint(Grass * (0.85f + 0.06f * i)));
            }
            mesh.Transform = scale;
            // Завязка.
            mesh.AddLoft(new[] { new LoftRing(tie - 0.008f, 0.03f, 0.027f), new LoftRing(tie + 0.008f, 0.03f, 0.027f) }, 8, Paint(Twine));
        }

        private static void Basket(FacetedMesh mesh)
        {
            // Плетёная корзина шире кверху, снаружи полосы плетения, сверху толстый край и горка камней.
            var bottom = -BasketHalfHeight;
            mesh.AddLoft(new[]
            {
                new LoftRing(bottom, 0.084f, 0.064f), new LoftRing(bottom + 0.02f, 0.09f, 0.069f), new LoftRing(0f, 0.1f, 0.077f),
                new LoftRing(0.09f, 0.112f, 0.086f), new LoftRing(0.12f, 0.116f, 0.089f),
            }, 12, Paint(Wicker), capStart: true, capEnd: false, twoSided: true, innerColor: Paint(Wicker * 0.55f));
            foreach (var (y, r) in new[] { (-0.085f, 0.093f), (-0.04f, 0.097f), (0.005f, 0.101f), (0.05f, 0.106f) })
                mesh.AddLoft(new[] { new LoftRing(y, r + 0.004f, r * 0.77f + 0.004f), new LoftRing(y + 0.012f, r + 0.004f, r * 0.77f + 0.004f) }, 12,
                    Paint(Wicker * 0.78f), capStart: false, capEnd: false);
            mesh.AddLoft(new[] { new LoftRing(0.112f, 0.122f, 0.095f), new LoftRing(0.132f, 0.122f, 0.095f) }, 12, Paint(Wicker * 0.85f));
            // Камни горкой из корзины.
            var stones = new[]
            {
                new Vector3(-0.05f, 0.125f, 0.03f), new Vector3(0.05f, 0.127f, 0.025f), new Vector3(0f, 0.128f, -0.035f),
                new Vector3(-0.045f, 0.13f, -0.03f), new Vector3(0.045f, 0.13f, -0.03f), new Vector3(0.005f, 0.155f, 0.005f),
            };
            for (var i = 0; i < stones.Length; i++)
                mesh.AddBlob(stones[i], Vector3.one * (0.04f - 0.003f * (i % 3)), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.82f + 0.05f * (i % 4))));
            // Две лямки через плечи (правая — зеркально).
            foreach (var side in new[] { -1f, 1f })
            {
                var path = new Vector3[BasketStrap.Length];
                var radii = new Vector2[path.Length];
                for (var i = 0; i < path.Length; i++)
                {
                    path[i] = OnBasket(Vector3.Scale(BasketStrap[i], new Vector3(-side, 1f, 1f)));
                    radii[i] = new Vector2(0.012f, 0.005f);
                }
                mesh.AddTube(path, radii, 6, Paint(Strap));
            }
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);

        /// <summary>Перегиб мешка: под сборкой, доли роста по оси мешка.</summary>
        private static float SackHinge => TieY - 0.06f;

        /// <summary>Насколько тело мешка может уйти к спине (градусы): дальше спина не пускает.</summary>
        private const float SackBackLimit = -30f;

        /// <summary>
        /// Тело мешка висит под действием тяжести: ниже перегиба оно поворачивается так, чтобы смотреть вниз,
        /// с пружиной — при шаге, повороте и рывке качается и успокаивается. На спине (flatten = 1) мешок
        /// сплющивается и не уходит внутрь спины. Бревно и корзина не гнутся. Вершины меняются в сетке ноши.
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

            /// <summary>
            /// Где вершина, когда ноша снята с плеча или спины: верх мешка смят над завязкой, лямки корзины опали к краю;
            /// у тела — там же, где в сетке.
            /// </summary>
            private readonly Vector3[] straight;

            /// <summary>Тело мешка висит и качается; корзина не гнётся, у неё только опадают лямки.</summary>
            private readonly bool hangs;

            /// <summary>У вершины лямки корзины — где лямка пришита (блоки, система корзины); у остальных — NaN.</summary>
            private readonly Vector3[] strapAttach;
            private Vector3 hang = Vector3.down;
            private Vector3 hangVelocity;
            private Vector3 lastPosition;
            private Vector3 lastVelocity;
            private bool hasLast;

            public Shape(Mesh mesh, WorkerCarry carry, float height)
            {
                this.mesh = mesh;
                this.height = height;
                bends = carry is WorkerCarry.GrassSack or WorkerCarry.StoneBasket;
                hangs = carry == WorkerCarry.GrassSack;
                if (!bends)
                    return;
                mesh.MarkDynamic();
                restVertices = mesh.vertices;
                restNormals = mesh.normals;
                vertices = new Vector3[restVertices.Length];
                normals = new Vector3[restNormals.Length];
                straight = new Vector3[restVertices.Length];
                if (hangs)
                {
                    for (var v = 0; v < restVertices.Length; v++)
                        straight[v] = Straighten(restVertices[v] / height) * height;
                    return;
                }
                strapAttach = new Vector3[restVertices.Length];
                for (var v = 0; v < restVertices.Length; v++)
                    strapAttach[v] = StrapAttach(restVertices[v] / height) * height;
            }

            /// <summary>Если точка (доли роста, система корзины) — на лямке, то где эта лямка пришита к краю; иначе NaN.</summary>
            private static Vector3 StrapAttach(Vector3 p)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var mirror = new Vector3(-side, 1f, 1f);
                    for (var k = 1; k < BasketStrap.Length; k++)
                    {
                        var a = OnBasket(Vector3.Scale(BasketStrap[k - 1], mirror));
                        var ab = OnBasket(Vector3.Scale(BasketStrap[k], mirror)) - a;
                        var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                        if (Vector3.Distance(p, a + ab * t) < 0.02f)
                            return OnBasket(Vector3.Scale(BasketStrap[0], mirror));
                    }
                }
                return new Vector3(float.NaN, float.NaN, float.NaN);
            }

            /// <summary>
            /// Лямки корзины: на спине (flatten = 1) — через плечи; пока корзину снимают, соскальзывают назад — поворачиваются
            /// вокруг места, где пришиты, вверх и за спину, и укорачиваются до петель у края.
            /// </summary>
            private void ApplyStraps(float flatten)
            {
                var off = 1f - Mathf.Clamp01(flatten);
                var turn = Quaternion.Euler(-StrapsSlide * off, 0f, 0f);
                var shrink = Mathf.Lerp(1f, StrapsDropped, off);
                for (var v = 0; v < restVertices.Length; v++)
                {
                    var attach = strapAttach[v];
                    if (float.IsNaN(attach.x))
                    {
                        vertices[v] = restVertices[v];
                        normals[v] = restNormals[v];
                        continue;
                    }
                    vertices[v] = attach + turn * (restVertices[v] - attach) * shrink;
                    normals[v] = turn * restNormals[v];
                }
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.RecalculateBounds();
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
                    result = from + Vector3.up * (GatheredLength(k) * t) + Quaternion.FromToRotation(ab, Vector3.up) * (p - on);
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
                if (!hangs)
                {
                    ApplyStraps(flatten);
                    return;
                }
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
                    // Собранный верх перекинут через плечо, пока мешок на спине, и сминается, когда его снимают;
                    // лямки корзины на спине идут через плечи, снятой — опадают.
                    var p = Vector3.Lerp(straight[v], restVertices[v], flatten);
                    // Ниже перегиба тело мешка плавно поворачивается вниз; на спине — чуть сплющено. Корзина не гнётся.
                    var w = hangs ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SackHinge + 0.02f, SackHinge - 0.12f, p.y / height)) : 0f;
                    var turn = Quaternion.Euler(aroundX * w, 0f, aroundZ * w);
                    var flat = new Vector3(1f, 1f, 1f - 0.15f * w * flatten);
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
