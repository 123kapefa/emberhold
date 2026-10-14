using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Плавная деформация мягкой части (подол, пелерина): каждая вершина — смесь положений по нескольким костям
    /// (линейное смешивание, как у скелетной анимации). Части не режутся на жёсткие куски, поэтому на сгибах
    /// ткань не рвётся и не выпирает. Сетка части — в системе её точки вращения; после <see cref="Apply"/> вершины
    /// в системе рабочего (ноги в нуле), рисовать её надо единичной матрицей рабочего.
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

        /// <summary>mesh — сетка части part из <see cref="WorkerMeshBuilder.BuildParts"/>; её вершины будут меняться.</summary>
        public WorkerSkin(Mesh mesh, WorkerPart part, float height)
        {
            this.mesh = mesh;
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
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        /// <summary>Кости и веса вершины p (доли роста, система рабочего в покое).</summary>
        private void Weights(WorkerPart part, Vector3 p, int at)
        {
            var torso = (int)WorkerPart.Torso;
            if (part == WorkerPart.Cape)
            {
                // Пелерина над плечом идёт за плечом, к середине — за корпусом.
                var arm = 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.07f, 0.17f, Mathf.Abs(p.x)));
                Set(at, torso, 1f - arm, p.x < 0f ? (int)WorkerPart.UpperArmLeft : (int)WorkerPart.UpperArmRight, arm, torso, 0f);
                return;
            }
            // Подол: спереди книзу идёт за бёдрами (за каждым — своя сторона), сзади и у пояса — за корпусом.
            // Спереди подол идёт за бёдрами от самого пояса: при наклоне корпус уводит подол назад, а бёдра — вперёд.
            var low = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.535f, 0.42f, p.y));
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
