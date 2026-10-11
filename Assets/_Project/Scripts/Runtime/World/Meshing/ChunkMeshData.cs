using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Результат меширования одного чанка: общие вершины и отдельный список треугольников на каждый материал.
    /// Переиспользуется между перестройками, чтобы не плодить мусор.
    /// </summary>
    public sealed class ChunkMeshData
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        /// <summary>Индекс — номер материала блока; для воздуха список всегда пуст.</summary>
        public readonly List<int>[] Triangles;

        public ChunkMeshData(int materialCount = BlockMaterials.Max + 1)
        {
            Triangles = new List<int>[materialCount];
            for (var i = 0; i < materialCount; i++)
                Triangles[i] = new List<int>();
        }

        public bool IsEmpty => Vertices.Count == 0;

        public int TriangleCount
        {
            get
            {
                var count = 0;
                foreach (var list in Triangles)
                    count += list.Count / 3;
                return count;
            }
        }

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            foreach (var list in Triangles)
                list.Clear();
        }

        /// <summary>Четырёхугольник; вершины по часовой стрелке, если смотреть снаружи (лицевая сторона в Unity).</summary>
        public void AddQuad(byte material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            var start = Vertices.Count;
            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);
            Vertices.Add(d);
            for (var i = 0; i < 4; i++)
                Normals.Add(normal);
            Uvs.Add(uvA);
            Uvs.Add(uvB);
            Uvs.Add(uvC);
            Uvs.Add(uvD);

            var tris = Triangles[material];
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }
    }
}
