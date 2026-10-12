using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Результат меширования одного чанка: общие вершины и отдельный список треугольников на каждый материал.
    /// Для каждого треугольника запоминается блок, которому он принадлежит, — так луч мыши находит блок
    /// точно, даже на склоне. Переиспользуется между перестройками, чтобы не плодить мусор.
    /// </summary>
    public sealed class ChunkMeshData
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        /// <summary>
        /// Вес смешивания цвета у каждой вершины (в UV3.x меша): 0 — цвет материала, 1 — цвет камня.
        /// Не ноль только у гладкой земли (<see cref="TerrainSurface"/>).
        /// </summary>
        public readonly List<Vector2> Blends = new();
        /// <summary>Индекс — номер материала блока; для воздуха список всегда пуст.</summary>
        public readonly List<int>[] Triangles;
        /// <summary>
        /// Параллельно <see cref="Triangles"/>: на каждый треугольник (три индекса) — индекс блока в чанке
        /// (<see cref="ChunkData.Index"/>).
        /// </summary>
        public readonly List<int>[] TriangleBlocks;

        public ChunkMeshData(int materialCount = BlockMaterials.Max + 1)
        {
            Triangles = new List<int>[materialCount];
            TriangleBlocks = new List<int>[materialCount];
            for (var i = 0; i < materialCount; i++)
            {
                Triangles[i] = new List<int>();
                TriangleBlocks[i] = new List<int>();
            }
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
            Blends.Clear();
            foreach (var list in Triangles)
                list.Clear();
            foreach (var list in TriangleBlocks)
                list.Clear();
        }

        /// <summary>Четырёхугольник; вершины по часовой стрелке, если смотреть снаружи (лицевая сторона в Unity).</summary>
        public void AddQuad(byte material, int block, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            var start = Vertices.Count;
            AddVertex(a, normal, uvA);
            AddVertex(b, normal, uvB);
            AddVertex(c, normal, uvC);
            AddVertex(d, normal, uvD);
            AddIndices(material, block, start, start + 1, start + 2);
            AddIndices(material, block, start, start + 2, start + 3);
        }

        /// <summary>Треугольник; вершины по часовой стрелке, если смотреть снаружи.</summary>
        public void AddTriangle(byte material, int block, Vector3 a, Vector3 b, Vector3 c, Vector3 normal,
            Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            var start = Vertices.Count;
            AddVertex(a, normal, uvA);
            AddVertex(b, normal, uvB);
            AddVertex(c, normal, uvC);
            AddIndices(material, block, start, start + 1, start + 2);
        }

        /// <summary>Треугольник гладкой земли: свои нормали и вес смешивания травы с камнем в каждой вершине.</summary>
        public void AddTriangle(byte material, int block, Vector3 a, Vector3 b, Vector3 c,
            Vector3 normalA, Vector3 normalB, Vector3 normalC, Vector2 uvA, Vector2 uvB, Vector2 uvC,
            float blendA, float blendB, float blendC)
        {
            var start = Vertices.Count;
            AddVertex(a, normalA, uvA, blendA);
            AddVertex(b, normalB, uvB, blendB);
            AddVertex(c, normalC, uvC, blendC);
            AddIndices(material, block, start, start + 1, start + 2);
        }

        /// <summary>Треугольник со своей нормалью в каждой вершине (сглаженное освещение).</summary>
        public void AddTriangle(byte material, int block, Vector3 a, Vector3 b, Vector3 c,
            Vector3 normalA, Vector3 normalB, Vector3 normalC, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            var start = Vertices.Count;
            AddVertex(a, normalA, uvA);
            AddVertex(b, normalB, uvB);
            AddVertex(c, normalC, uvC);
            AddIndices(material, block, start, start + 1, start + 2);
        }

        private void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, float blend = 0f)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            Blends.Add(new Vector2(blend, 0f));
        }

        private void AddIndices(byte material, int block, int a, int b, int c)
        {
            var tris = Triangles[material];
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
            TriangleBlocks[material].Add(block);
        }
    }
}
