using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Трещины на повреждённом блоке: на каждой грани, кроме нижней, столько ломаных трещин, каков этап
    /// разрушения. Каждый отрезок — узкая полоска из двух треугольников, лицом наружу: линии в один пиксель
    /// издалека не видно. Рисунок зависит только от seed, поэтому
    /// трещины прошлого этапа остаются на месте, а новые добавляются к ним.
    /// </summary>
    public static class BlockCrackMesh
    {
        /// <summary>Больше этапов не рисуем: трещин на грани становится слишком много.</summary>
        public const int MaxStages = 8;

        /// <summary>Ширина трещины в долях блока.</summary>
        public const float Width = 0.035f;

        private const int SegmentsPerCrack = 3;
        private static readonly Vector3Int[] Faces =
        {
            Vector3Int.up, Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back,
        };

        /// <param name="origin">Угол ячейки блока в координатах меша.</param>
        /// <param name="height">Высота блока в долях (0..1].</param>
        /// <param name="offset">На сколько трещины вынесены наружу от граней, чтобы не мерцать.</param>
        public static void Append(Vector3 origin, float height, int stage, int seed, float offset,
            List<Vector3> vertices, List<int> indices) =>
            Append(origin, Vector3.zero, new Vector3(1f, height, 1f), stage, seed, offset, vertices, indices);

        /// <summary>Трещины на гранях коробки от boxMin до boxMax внутри ячейки (по размеру формы блока).</summary>
        public static void Append(Vector3 origin, Vector3 boxMin, Vector3 boxMax, int stage, int seed, float offset,
            List<Vector3> vertices, List<int> indices)
        {
            if (stage <= 0 || boxMax.x <= boxMin.x || boxMax.y <= boxMin.y || boxMax.z <= boxMin.z)
                return;
            stage = Mathf.Min(stage, MaxStages);

            var random = new System.Random(seed);
            foreach (var face in Faces)
            {
                // Случайные числа тянем для всех трещин всех этапов, чтобы рисунок не зависел от этапа.
                for (var crack = 0; crack < MaxStages; crack++)
                {
                    var start = new Vector2(Range(random, 0.2f, 0.8f), Range(random, 0.2f, 0.8f));
                    var angle = Range(random, 0f, Mathf.PI * 2f);
                    var bends = new float[SegmentsPerCrack];
                    var lengths = new float[SegmentsPerCrack];
                    for (var i = 0; i < SegmentsPerCrack; i++)
                    {
                        bends[i] = Range(random, -0.7f, 0.7f);
                        lengths[i] = Range(random, 0.15f, 0.25f);
                    }
                    if (crack >= stage)
                        continue;

                    var point = start;
                    for (var i = 0; i < SegmentsPerCrack; i++)
                    {
                        angle += bends[i];
                        var next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * lengths[i];
                        next = new Vector2(Mathf.Clamp01(next.x), Mathf.Clamp01(next.y));
                        AddStrip(face, point, next, origin, boxMin, boxMax, offset, vertices, indices);
                        point = next;
                    }
                }
            }
        }

        private static void AddStrip(Vector3Int face, Vector2 a, Vector2 b, Vector3 origin, Vector3 min, Vector3 max,
            float offset, List<Vector3> vertices, List<int> indices)
        {
            var along = b - a;
            if (along.sqrMagnitude < 1e-8f)
                return;
            var side = new Vector2(-along.y, along.x).normalized * (Width * 0.5f);

            var first = vertices.Count;
            vertices.Add(origin + OnFace(face, Clamp(a - side), min, max, offset));
            vertices.Add(origin + OnFace(face, Clamp(a + side), min, max, offset));
            vertices.Add(origin + OnFace(face, Clamp(b + side), min, max, offset));
            vertices.Add(origin + OnFace(face, Clamp(b - side), min, max, offset));

            // Обход выбираем так, чтобы полоска смотрела наружу грани (обратные грани не рисуются).
            var normal = Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]);
            var outward = Vector3.Dot(normal, face) > 0f;
            if (outward)
            {
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            }
            else
            {
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 1);
                indices.Add(first); indices.Add(first + 3); indices.Add(first + 2);
            }
        }

        private static Vector2 Clamp(Vector2 uv) => new(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

        /// <summary>Точка грани коробки по координатам (u, v) в 0..1 вдоль двух других осей грани.</summary>
        private static Vector3 OnFace(Vector3Int face, Vector2 uv, Vector3 min, Vector3 max, float offset)
        {
            var x = Mathf.Lerp(min.x, max.x, uv.x);
            var z = Mathf.Lerp(min.z, max.z, uv.y);
            if (face == Vector3Int.up)
                return new Vector3(x, max.y + offset, z);
            var y = Mathf.Lerp(min.y, max.y, uv.y);
            var along = Mathf.Lerp(min.z, max.z, uv.x);
            if (face == Vector3Int.right)
                return new Vector3(max.x + offset, y, along);
            if (face == Vector3Int.left)
                return new Vector3(min.x - offset, y, along);
            if (face == Vector3Int.forward)
                return new Vector3(x, y, max.z + offset);
            return new Vector3(x, y, min.z - offset);
        }

        private static float Range(System.Random random, float min, float max) =>
            min + (float)random.NextDouble() * (max - min);
    }
}
