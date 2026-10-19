using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Меш гор за краем мира (#102): кольцо вокруг мира из рядов-рамок на растущем расстоянии от края.
    /// Первый ряд лежит точно на углах края мира, каждый блок, поэтому горы примыкают к земле без щели;
    /// дальше ряды реже. Смежные ряды сшиваются треугольниками по положению вдоль рамки.
    /// Рисуется подмешем травы, как гладкая земля: тот же шейдер смешивает траву, камень, скалы и снег.
    /// Координаты — в блоках сетки мира, (0, 0) — угол мира.
    /// </summary>
    public static class WorldSkirtMesh
    {
        /// <summary>Ширина кольца гор, в блоках от края мира.</summary>
        public const float Width = 224f;

        // Вид травы далёких гор: горная трава и скалы, ущелья нейтральные (как у шейдера земли 1.5).
        private static readonly Vector4 MountainKinds = new(0f, 0f, 1f, 1.5f);

        /// <summary>Расстояния рядов от края и шаг вершин вдоль каждого ряда.</summary>
        public static IEnumerable<(float Distance, float Step)> Rows()
        {
            // У края шаг как у сетки мира (блок), дальше, где горы уже в дымке, реже.
            for (var d = 0f; d <= 16f; d += 1f)
                yield return (d, 1f);
            for (var d = 18f; d <= 48f; d += 2f)
                yield return (d, 2f);
            for (var d = 52f; d <= 128f; d += 4f)
                yield return (d, 4f);
            for (var d = 136f; d <= Width; d += 8f)
                yield return (d, 8f);
        }

        /// <summary>Строит кольцо в data (очищается): треугольники — в подмеше травы.</summary>
        public static void Build(WorldSkirtShape shape, TerrainSurface surface, GroundTint tint, ChunkMeshData data)
        {
            data.Clear();
            List<(int Index, float T)> inner = null;
            foreach (var (distance, step) in Rows())
            {
                var row = AddRow(shape, surface, tint, data, distance, step);
                if (inner != null)
                    Stitch(data, inner, row);
                inner = row;
            }
        }

        /// <summary>
        /// Ряд-рамка на расстоянии distance от края, против часовой стрелки от угла (0, 0), если смотреть сверху.
        /// T — положение вдоль рамки: номер стороны плюс доля стороны; последняя вершина повторяет первую с T = 4.
        /// </summary>
        private static List<(int Index, float T)> AddRow(WorldSkirtShape shape, TerrainSurface surface, GroundTint tint,
            ChunkMeshData data, float distance, float step)
        {
            var row = new List<(int Index, float T)>();
            float min = -distance, maxX = shape.SizeX + distance, maxZ = shape.SizeZ + distance;
            for (var side = 0; side < 4; side++)
            {
                var length = side % 2 == 0 ? maxX - min : maxZ - min;
                var count = Mathf.Max(1, Mathf.CeilToInt(length / step));
                for (var i = 0; i < count; i++)
                {
                    var f = (float)i / count;
                    var along = min + f * length;
                    var (x, z) = side switch
                    {
                        0 => (along, min),
                        1 => (maxX, along),
                        2 => (maxX + min - along, maxZ),
                        _ => (min, maxZ + min - along),
                    };
                    row.Add((AddVertex(shape, surface, tint, data, x, z, distance), side + f));
                }
            }
            row.Add((row[0].Index, 4f));
            return row;
        }

        private static int AddVertex(WorldSkirtShape shape, TerrainSurface surface, GroundTint tint, ChunkMeshData data,
            float x, float z, float distance)
        {
            float height, rock;
            Vector3 normal;
            if (distance <= 0f)
            {
                // Край мира: высота, нормаль и камень точно как у угла земли.
                int cx = Mathf.RoundToInt(x), cz = Mathf.RoundToInt(z);
                height = surface.Corner(cx, cz) / (float)BlockShape.MaxHeight;
                normal = surface.Normal(cx, cz);
                rock = surface.Rock(cx, cz);
            }
            else
            {
                height = shape.Height(x, z);
                shape.Normal(x, z, out var nx, out var ny, out var nz);
                normal = new Vector3(nx, ny, nz);
                int cx = Mathf.Clamp(Mathf.RoundToInt(x), 0, shape.SizeX), cz = Mathf.Clamp(Mathf.RoundToInt(z), 0, shape.SizeZ);
                // Крутое и высокое — камень, как на хребте мира.
                var mountainRock = Mathf.Max(1f - Mathf.InverseLerp(0.55f, 0.8f, ny), Mathf.InverseLerp(30f, 45f, height));
                rock = Mathf.Lerp(surface.Rock(cx, cz), mountainRock, shape.Spread(distance));
            }

            var index = data.Vertices.Count;
            data.Vertices.Add(new Vector3(x, height, z));
            data.Normals.Add(normal);
            data.Uvs.Add(new Vector2(x, z));
            data.Blends.Add(new Vector4(rock, ChunkMeshData.GrassVertex, 0f, 0f));
            // Оттенок и вид травы за краем — как у ближайшей точки края, дальше вид сменяется горным.
            data.Colors.Add(tint != null ? tint.Sample(x, z) : Color.white);
            var kinds = tint != null ? (Vector4)tint.SampleKinds(x, z) : Vector4.zero;
            data.Kinds.Add(Vector4.Lerp(kinds, MountainKinds, shape.Spread(distance)));
            return index;
        }

        /// <summary>Сшивает два соседних ряда в ленту: всегда шагаем по тому ряду, чья следующая вершина ближе вдоль рамки.</summary>
        private static void Stitch(ChunkMeshData data, List<(int Index, float T)> inner, List<(int Index, float T)> outer)
        {
            var tris = data.Triangles[BlockMaterials.Grass];
            var blocks = data.TriangleBlocks[BlockMaterials.Grass];
            int i = 0, j = 0;
            while (i < inner.Count - 1 || j < outer.Count - 1)
            {
                var advanceInner = j >= outer.Count - 1
                                   || (i < inner.Count - 1 && inner[i + 1].T <= outer[j + 1].T);
                // По часовой стрелке, если смотреть сверху: лицевая сторона смотрит вверх.
                tris.Add(inner[i].Index);
                if (advanceInner)
                {
                    tris.Add(inner[i + 1].Index);
                    tris.Add(outer[j].Index);
                    i++;
                }
                else
                {
                    tris.Add(outer[j + 1].Index);
                    tris.Add(outer[j].Index);
                    j++;
                }
                blocks.Add(-1);
            }
        }
    }
}
