using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    /// <summary>
    /// Рамка подсветки блока для меша из линий (MeshTopology.Lines): 12 рёбер коробки формы
    /// и квадрат поменьше на грани под курсором. Координаты — от угла ячейки блока, в единицах блока.
    /// </summary>
    public static class BlockOutlineMesh
    {
        /// <summary>Доля грани, которую занимает квадрат-метка.</summary>
        public const float FaceMarkScale = 0.6f;

        /// <summary>Рамка на всю ячейку до высоты height.</summary>
        public static void Build(float height, Vector3Int face, float padding, List<Vector3> vertices, List<int> indices) =>
            Build(Vector3.zero, new Vector3(1f, height, 1f), face, padding, vertices, indices);

        /// <summary>Рамка вокруг коробки от boxMin до boxMax внутри ячейки (столб уже ячейки, балка лежит поверху).</summary>
        public static void Build(Vector3 boxMin, Vector3 boxMax, Vector3Int face, float padding,
            List<Vector3> vertices, List<int> indices)
        {
            vertices.Clear();
            indices.Clear();

            var min = boxMin - Vector3.one * padding;
            var max = boxMax + Vector3.one * padding;
            for (var i = 0; i < 8; i++)
                vertices.Add(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            // Рёбра соединяют углы, отличающиеся одним битом.
            for (var i = 0; i < 8; i++)
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                {
                    indices.Add(i);
                    indices.Add(i | bit);
                }
            }

            AddFaceMark(min, max, face, vertices, indices);
        }

        private static void AddFaceMark(Vector3 min, Vector3 max, Vector3Int face, List<Vector3> vertices, List<int> indices)
        {
            if (face == Vector3Int.zero)
                return;

            var center = (min + max) * 0.5f;
            var half = (max - min) * 0.5f;
            // Центр грани — на плоскости грани, оси квадрата — две другие оси.
            var axis = face.x != 0 ? 0 : face.y != 0 ? 1 : 2;
            center[axis] = face[axis] > 0 ? max[axis] : min[axis];
            var u = Vector3.zero;
            var v = Vector3.zero;
            u[(axis + 1) % 3] = half[(axis + 1) % 3] * FaceMarkScale;
            v[(axis + 2) % 3] = half[(axis + 2) % 3] * FaceMarkScale;

            var start = vertices.Count;
            vertices.Add(center - u - v);
            vertices.Add(center + u - v);
            vertices.Add(center + u + v);
            vertices.Add(center - u + v);
            for (var i = 0; i < 4; i++)
            {
                indices.Add(start + i);
                indices.Add(start + (i + 1) % 4);
            }
        }
    }
}
