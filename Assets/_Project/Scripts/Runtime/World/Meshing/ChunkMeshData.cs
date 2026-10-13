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
        /// Смешивание у каждой вершины (UV3 меша). x — вес камня поверх травы, не ноль только у гладкой земли
        /// (<see cref="TerrainSurface"/>). y — маска соседей-травы (<see cref="GrassEdge"/>) у верха блоков земли
        /// и камня рядом с травой или <see cref="GrassVertex"/> у вершин травы; zw — положение вершины в ячейке (0..1).
        /// </summary>
        public readonly List<Vector4> Blends = new();
        /// <summary>
        /// Цвет вершин: оттенок травы своего места (<see cref="GroundTint"/>), rgb — множитель, a — насыщенность.
        /// Его берут трава (гладкая земля и блоки травы) и бахрома травы на соседних блоках.
        /// </summary>
        public readonly List<Color> Colors = new();
        /// <summary>
        /// Смесь видов травы своего места (UV4, <see cref="GroundTint.SampleKinds"/>): x — топь, y — мёртвый город,
        /// z — горы, остаток — луговая. Её берут трава и бахрома травы, как и оттенок.
        /// </summary>
        public readonly List<Vector4> Kinds = new();

        /// <summary>Значение UV3.y у вершин подмеша травы: шейдер красит их оттенком травы.</summary>
        public const float GrassVertex = -1f;
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
            Colors.Clear();
            Kinds.Clear();
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

        /// <summary>
        /// Маска соседей-травы (UV3.y) и положение в ячейке (UV3.zw) вершинам, добавленным начиная с start, —
        /// верху блока в ячейке p.
        /// </summary>
        public void SetGrass(int start, Vector3 p, int mask)
        {
            for (var i = start; i < Vertices.Count; i++)
            {
                var u = Mathf.Clamp01(Vertices[i].x - p.x);
                var v = Mathf.Clamp01(Vertices[i].z - p.z);
                Blends[i] = new Vector4(Blends[i].x, mask, u, v);
            }
        }

        /// <summary>
        /// Оттенок и вид травы вершинам по их месту: tint в точке (origin + вершина) по X и Z; вершины подмеша травы
        /// помечаются <see cref="GrassVertex"/>. Без оттенка цвет остаётся белым, трава — луговой.
        /// </summary>
        public void ApplyGrassTint(GroundTint tint, Vector3 origin)
        {
            if (tint != null)
            {
                for (var i = 0; i < Vertices.Count; i++)
                {
                    float x = origin.x + Vertices[i].x, z = origin.z + Vertices[i].z;
                    Colors[i] = tint.Sample(x, z);
                    Kinds[i] = tint.SampleKinds(x, z);
                }
            }
            foreach (var index in Triangles[BlockMaterials.Grass])
            {
                var b = Blends[index];
                Blends[index] = new Vector4(b.x, GrassVertex, b.z, b.w);
            }
        }

        private void AddVertex(Vector3 position, Vector3 normal, Vector2 uv, float blend = 0f)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            Blends.Add(new Vector4(blend, 0f, 0f, 0f));
            Colors.Add(Color.white);
            Kinds.Add(Vector4.zero);
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
