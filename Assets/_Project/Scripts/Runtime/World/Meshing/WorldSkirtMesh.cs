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
            List<(Point P, float T)> inner = null;
            foreach (var (distance, step) in Rows())
            {
                var row = Row(shape, surface, tint, distance, step);
                if (inner != null)
                    Stitch(data, inner, row);
                inner = row;
            }
        }

        /// <summary>Точка рельефа: место, мягкая нормаль, каменность, оттенок и вид травы.</summary>
        private readonly struct Point
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly float Rock;
            public readonly Color Tint;
            public readonly Vector4 Kinds;

            public Point(Vector3 position, Vector3 normal, float rock, Color tint, Vector4 kinds)
            {
                Position = position;
                Normal = normal;
                Rock = rock;
                Tint = tint;
                Kinds = kinds;
            }
        }

        /// <summary>
        /// Ряд-рамка на расстоянии distance от края, против часовой стрелки от угла (0, 0), если смотреть сверху.
        /// T — положение вдоль рамки: номер стороны плюс доля стороны; последняя точка повторяет первую с T = 4.
        /// </summary>
        private static List<(Point P, float T)> Row(WorldSkirtShape shape, TerrainSurface surface, GroundTint tint,
            float distance, float step)
        {
            var row = new List<(Point P, float T)>();
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
                    row.Add((PointAt(shape, surface, tint, x, z, distance), side + f));
                }
            }
            row.Add((row[0].P, 4f));
            return row;
        }

        private static Point PointAt(WorldSkirtShape shape, TerrainSurface surface, GroundTint tint,
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

            // Оттенок и вид травы за краем — как у ближайшей точки края, дальше вид сменяется горным.
            var kinds = tint != null ? (Vector4)tint.SampleKinds(x, z) : Vector4.zero;
            return new Point(new Vector3(x, height, z), normal, rock, tint != null ? tint.Sample(x, z) : Color.white,
                Vector4.Lerp(kinds, MountainKinds, shape.Spread(distance)));
        }

        /// <summary>Сшивает два соседних ряда в ленту: всегда шагаем по тому ряду, чья следующая точка ближе вдоль рамки.</summary>
        private static void Stitch(ChunkMeshData data, List<(Point P, float T)> inner, List<(Point P, float T)> outer)
        {
            int i = 0, j = 0;
            while (i < inner.Count - 1 || j < outer.Count - 1)
            {
                var advanceInner = j >= outer.Count - 1
                                   || (i < inner.Count - 1 && inner[i + 1].T <= outer[j + 1].T);
                // По часовой стрелке, если смотреть сверху: лицевая сторона смотрит вверх.
                if (advanceInner)
                {
                    AddTriangle(data, inner[i].P, inner[i + 1].P, outer[j].P);
                    i++;
                }
                else
                {
                    AddTriangle(data, inner[i].P, outer[j + 1].P, outer[j].P);
                    j++;
                }
            }
        }

        /// <summary>
        /// Треугольник со своими вершинами, как у гладкой земли мира (ChunkMesher): трава освещается мягко,
        /// камень — гранями: чем каменнее вершина, тем ближе её нормаль к нормали треугольника.
        /// </summary>
        private static void AddTriangle(ChunkMeshData data, in Point a, in Point b, in Point c)
        {
            var face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position).normalized;
            var start = data.Vertices.Count;
            data.AddTriangle(BlockMaterials.Grass, -1, a.Position, b.Position, c.Position,
                Vector3.Lerp(a.Normal, face, a.Rock).normalized,
                Vector3.Lerp(b.Normal, face, b.Rock).normalized,
                Vector3.Lerp(c.Normal, face, c.Rock).normalized,
                new Vector2(a.Position.x, a.Position.z), new Vector2(b.Position.x, b.Position.z),
                new Vector2(c.Position.x, c.Position.z),
                a.Rock, b.Rock, c.Rock);
            SetGround(data, start, a);
            SetGround(data, start + 1, b);
            SetGround(data, start + 2, c);
        }

        private static void SetGround(ChunkMeshData data, int index, in Point point)
        {
            data.Blends[index] = new Vector4(point.Rock, ChunkMeshData.GrassVertex, 0f, 0f);
            data.Colors[index] = point.Tint;
            data.Kinds[index] = point.Kinds;
        }
    }
}
