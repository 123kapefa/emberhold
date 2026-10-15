using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Плавная деформация мягкой части (подол, пелерина): каждая вершина — смесь положений по нескольким костям
    /// (линейное смешивание, как у скелетной анимации). Части не режутся на жёсткие куски, поэтому на сгибах
    /// ткань не рвётся и не выпирает. Смешивание само по себе проседает внутрь тела на сгибах (поднятая рука тянет
    /// пелерину, а плечо рубахи остаётся на месте), поэтому после него ткань выталкивается из простых форм тела под ней:
    /// сечений корпуса и капсул плеч (пелерина), капсул бёдер (подол). Насколько точка ткани была снаружи формы в покое —
    /// настолько и держится (но не дальше самой формы), так что в покое ткань не сдвигается.
    /// Сетка части — в системе её точки вращения; после <see cref="Apply"/> вершины в системе рабочего (ноги в нуле),
    /// рисовать её надо единичной матрицей рабочего.
    /// </summary>
    public sealed class WorkerSkin
    {
        private const int Bones = 3;

        private readonly Mesh mesh;
        private readonly Vector3[] restVertices;
        private readonly Vector3[] restNormals;
        private readonly Vector3[] vertices;
        private readonly Vector3[] normals;
        private readonly int[] boneIndices;
        private readonly float[] boneWeights;
        private readonly Vector3[] bonePivots = new Vector3[WorkerPose.PartCount];
        private readonly Matrix4x4[] boneMatrices = new Matrix4x4[WorkerPose.PartCount];
        private readonly Matrix4x4[] boneInverses = new Matrix4x4[WorkerPose.PartCount];
        private readonly Body[] bodies;
        private readonly float height;

        /// <summary>Насколько снаружи формы тела точка ткани должна быть (доля от формы, не больше 1 — на её поверхности).</summary>
        private readonly float[] keep;

        /// <summary>
        /// Форма тела под тканью, в долях роста в системе рабочего в покое: либо сечения корпуса (y, полуширина, полуглубина,
        /// сдвиг вперёд) с запасом на броню, либо цепочка капсул по оси руки или бедра с радиусами с запасом на рукава брони.
        /// </summary>
        private sealed class Body
        {
            public int Bone;
            public Vector4[] Rings;
            public Vector3[] Points;
            public float[] Radii;
        }

        /// <summary>Запас поверх тела, доли роста: ткань не ложится вплотную.</summary>
        private const float Margin = 0.004f;

        private static Body[] BodiesUnder(WorkerPart part, WorkerRole role)
        {
            if (part == WorkerPart.Cape)
            {
                // Корпус — по тому, что надето под пелериной (рубаха, кольчуга, стёганка), плечи — по рукавам.
                var rings = WorkerGearBuilder.TorsoUnderCape(role);
                var torso = new Body { Bone = (int)WorkerPart.Torso, Rings = new Vector4[rings.Length] };
                var extra = Margin + WorkerGearBuilder.UnderCapeExtra(role);
                for (var i = 0; i < rings.Length; i++)
                    torso.Rings[i] = rings[i] + new Vector4(0f, extra, extra, 0f);
                var sleeve = WorkerGearBuilder.SleeveExtra(role) + Margin;
                return new[] { torso, Arm(-1f, sleeve), Arm(1f, sleeve) };
            }
            return new[] { Thigh(-1f), Thigh(1f) };
        }

        /// <summary>Плечо до локтя — как в <see cref="WorkerMeshBuilder"/>, радиусы толще на extra.</summary>
        private static Body Arm(float side, float extra) => new()
        {
            Bone = (int)(side < 0f ? WorkerPart.UpperArmLeft : WorkerPart.UpperArmRight),
            Points = new[]
            {
                new Vector3(side * 0.11f, 0.695f, 0f), new Vector3(side * 0.152f, 0.672f, -0.004f), new Vector3(side * 0.17f, 0.62f, -0.01f),
                new Vector3(side * 0.175f, 0.55f, -0.008f),
            },
            Radii = new[] { 0.034f + extra, 0.043f + extra, 0.036f + extra, 0.032f + extra },
        };

        private static Body Thigh(float side) => new()
        {
            Bone = (int)(side < 0f ? WorkerPart.ThighLeft : WorkerPart.ThighRight),
            Points = new[] { new Vector3(side * 0.047f, 0.44f, 0f), new Vector3(side * 0.052f, 0.36f, 0.004f), new Vector3(side * 0.055f, 0.265f, 0.002f) },
            Radii = new[] { 0.059f, 0.054f, 0.037f },
        };

        /// <summary>
        /// mesh — сетка части part из <see cref="WorkerMeshBuilder.BuildParts"/>; её вершины будут меняться.
        /// role — чья сетка: от неё зависит, что надето под тканью.
        /// </summary>
        public WorkerSkin(Mesh mesh, WorkerPart part, float height, WorkerRole role = WorkerRole.Free)
        {
            this.mesh = mesh;
            this.height = height;
            mesh.MarkDynamic();
            restVertices = mesh.vertices;
            restNormals = mesh.normals;
            vertices = new Vector3[restVertices.Length];
            normals = new Vector3[restNormals.Length];
            boneIndices = new int[restVertices.Length * Bones];
            boneWeights = new float[restVertices.Length * Bones];
            var pivot = WorkerSkeleton.Pivot(part, height);
            for (var i = 0; i < WorkerPose.PartCount; i++)
                bonePivots[i] = WorkerSkeleton.Pivot((WorkerPart)i, height);
            for (var v = 0; v < restVertices.Length; v++)
            {
                restVertices[v] += pivot;
                Weights(part, restVertices[v] / height, v * Bones);
            }
            bodies = BodiesUnder(part, role);
            keep = new float[restVertices.Length * bodies.Length];
            for (var b = 0; b < bodies.Length; b++)
            {
                for (var v = 0; v < restVertices.Length; v++)
                    keep[v * bodies.Length + b] = Mathf.Min(1f, Outside(bodies[b], restVertices[v] / height, out _));
            }
        }

        public Mesh Mesh => mesh;

        /// <summary>Мягкая ли часть: её рисует не матрица части, а <see cref="WorkerSkin"/>.</summary>
        public static bool IsSoft(WorkerPart part) => part is WorkerPart.Hips or WorkerPart.Cape;

        /// <summary>Поставить вершины по матрицам частей (из <see cref="WorkerSkeleton.Evaluate"/>).</summary>
        public void Apply(Matrix4x4[] parts)
        {
            for (var b = 0; b < WorkerPose.PartCount; b++)
                boneMatrices[b] = parts[b] * Matrix4x4.Translate(-bonePivots[b]);
            for (var v = 0; v < restVertices.Length; v++)
            {
                var position = Vector3.zero;
                var normal = Vector3.zero;
                for (var k = 0; k < Bones; k++)
                {
                    var weight = boneWeights[v * Bones + k];
                    if (weight <= 0f)
                        continue;
                    var matrix = boneMatrices[boneIndices[v * Bones + k]];
                    position += matrix.MultiplyPoint3x4(restVertices[v]) * weight;
                    normal += matrix.MultiplyVector(restNormals[v]) * weight;
                }
                vertices[v] = position;
                normals[v] = normal.normalized;
            }
            PushOut();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        /// <summary>Вытолкнуть ткань из форм тела (два прохода: на стыке корпуса и плеча формы толкают в разные стороны).</summary>
        private void PushOut()
        {
            // Формы заданы в покое: точку ткани переводим в покой кости (матрица кости в позе, обратно — матрица покоя).
            foreach (var body in bodies)
                boneInverses[body.Bone] = boneMatrices[body.Bone].inverse;
            for (var pass = 0; pass < 2; pass++)
            for (var v = 0; v < vertices.Length; v++)
            for (var b = 0; b < bodies.Length; b++)
            {
                var body = bodies[b];
                var rest = boneInverses[body.Bone].MultiplyPoint3x4(vertices[v]) / height;
                var need = keep[v * bodies.Length + b];
                var outside = Outside(body, rest, out var origin);
                if (outside >= need)
                    continue;
                vertices[v] = boneMatrices[body.Bone].MultiplyPoint3x4((origin + (rest - origin) * (need / outside)) * height);
            }
        }

        /// <summary>
        /// Насколько точка rest (доли роста, система рабочего в покое) снаружи формы: 1 — на поверхности, больше — снаружи;
        /// float.MaxValue — форма здесь не действует. Отодвинуть до доли s: origin + (rest − origin) · s / outside.
        /// </summary>
        private static float Outside(Body body, Vector3 rest, out Vector3 origin)
        {
            origin = Vector3.zero;
            if (body.Rings != null)
            {
                var rings = body.Rings;
                if (rest.y < rings[0].x || rest.y > rings[^1].x)
                    return float.MaxValue;
                var i = 1;
                while (rest.y > rings[i].x)
                    i++;
                var ring = Vector4.Lerp(rings[i - 1], rings[i], Mathf.InverseLerp(rings[i - 1].x, rings[i].x, rest.y));
                var dx = rest.x / ring.y;
                var dz = (rest.z - ring.w) / ring.z;
                var s = Mathf.Sqrt(dx * dx + dz * dz);
                if (s < 1e-5f)
                    return float.MaxValue;
                origin = new Vector3(0f, rest.y, ring.w);
                return s;
            }
            // Ближайшая точка на цепочке капсул и радиус в ней.
            var best = float.MaxValue;
            var axis = Vector3.zero;
            for (var k = 1; k < body.Points.Length; k++)
            {
                var a = body.Points[k - 1];
                var ab = body.Points[k] - a;
                var t = Mathf.Clamp01(Vector3.Dot(rest - a, ab) / ab.sqrMagnitude);
                var q = a + ab * t;
                var r = Mathf.Lerp(body.Radii[k - 1], body.Radii[k], t);
                var d = Vector3.Distance(rest, q) / r;
                if (d < best)
                {
                    best = d;
                    axis = q;
                }
            }
            if ((rest - axis).sqrMagnitude < 1e-10f)
                return float.MaxValue;
            origin = axis;
            return best;
        }

        /// <summary>Кости и веса вершины p (доли роста, система рабочего в покое).</summary>
        private void Weights(WorkerPart part, Vector3 p, int at)
        {
            var torso = (int)WorkerPart.Torso;
            if (part == WorkerPart.Cape)
            {
                // Пелерина над плечом во многом идёт за рукой, к шее плавно переходит к корпусу. Целиком за рукой край
                // не идёт: поднятая рука утянула бы его и открыла плечо; рукав под пелериной держит выталкивание (PushOut).
                var arm = 0.7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.19f, Mathf.Abs(p.x)));
                Set(at, torso, 1f - arm, p.x < 0f ? (int)WorkerPart.UpperArmLeft : (int)WorkerPart.UpperArmRight, arm, torso, 0f);
                return;
            }
            // Подол: спереди книзу идёт за бёдрами (за каждым — своя сторона), сзади и у пояса — за корпусом.
            // Спереди подол идёт за бёдрами от самого пояса: при наклоне корпус уводит подол назад, а бёдра — вперёд.
            var low = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.49f, 0.42f, p.y));
            var front = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.06f, p.z));
            var legs = 0.9f * low * front;
            var right = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.05f, p.x));
            Set(at, torso, 1f - legs, (int)WorkerPart.ThighLeft, legs * (1f - right), (int)WorkerPart.ThighRight, legs * right);
        }

        private void Set(int at, int a, float wa, int b, float wb, int c, float wc)
        {
            boneIndices[at] = a;
            boneWeights[at] = wa;
            boneIndices[at + 1] = b;
            boneWeights[at + 1] = wb;
            boneIndices[at + 2] = c;
            boneWeights[at + 2] = wc;
        }
    }
}
