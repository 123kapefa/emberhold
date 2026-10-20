using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Горы за краем мира (#102): примыкают к краю без щели, уходят в туман, кольцо без дыр.</summary>
    public sealed class WorldSkirtTests
    {
        private const int Size = 32;
        private const int H = BlockShape.MaxHeight;

        /// <summary>Поверхность Size×Size: хребет у края высотой около 50 блоков, неровный по краю.</summary>
        private static TerrainSurface Ridge()
        {
            var corners = new int[Size + 1, Size + 1];
            var topY = new int[Size, Size];
            for (var x = 0; x <= Size; x++)
            for (var z = 0; z <= Size; z++)
                corners[x, z] = (50 + (x * 7 + z * 3) % 9) * H + (x + z) % H;
            for (var x = 0; x < Size; x++)
            for (var z = 0; z < Size; z++)
                topY[x, z] = 60;
            return new TerrainSurface(corners, topY);
        }

        private static WorldSkirtShape ShapeOf(TerrainSurface surface) =>
            new((x, z) => surface.Corner(x, z) / (float)H, surface.SizeX, surface.SizeZ, 12345);

        [Test]
        public void Shape_AtTheEdge_IsTheEdgeHeight_AndSinksFarAway()
        {
            var surface = Ridge();
            var shape = ShapeOf(surface);
            for (var t = 0; t <= Size; t++)
            {
                Assert.That(shape.Height(t, 0f), Is.EqualTo(surface.Corner(t, 0) / (float)H).Within(1e-4f));
                Assert.That(shape.Height(Size, t), Is.EqualTo(surface.Corner(Size, t) / (float)H).Within(1e-4f));
                // Шаг наружу почти не меняет высоту: у края нет ступеньки.
                Assert.That(shape.Height(t, -0.5f), Is.EqualTo(shape.Height(t, 0f)).Within(0.5f));
            }

            float near = 0f, far = 0f;
            for (var t = 0; t < Size; t++)
            {
                near += shape.Height(t, -4f);
                far += shape.Height(t, -WorldSkirtMesh.Width);
            }
            Assert.That(far / Size, Is.LessThan(near / Size - 20f), "вдали горы опускаются");
            Assert.That(far / Size, Is.LessThan(26f), "вдали горы в тумане");
        }

        [Test]
        public void Shape_AcrossTheLinesFromWorldCorners_HasNoStep()
        {
            // Южный край низкий (20), западный высокий (60): напротив угла они сходятся без ступеньки.
            var shape = new WorldSkirtShape((x, z) => x == 0 && z > 0 ? 60f : 20f, Size, Size, 12345);
            foreach (var d in new[] { 4f, 10f, 30f })
            {
                Assert.That(shape.Height(-d, -0.01f), Is.EqualTo(shape.Height(-d, 0.01f)).Within(0.5f), $"вдоль запада, {d}");
                Assert.That(shape.Height(-0.01f, -d), Is.EqualTo(shape.Height(0.01f, -d)).Within(0.5f), $"вдоль юга, {d}");
            }
        }

        [Test]
        public void Mesh_StartsOnTheEdgeCorners_FacesUp_AndCoversTheRingWithoutHoles()
        {
            var surface = Ridge();
            var data = new ChunkMeshData();
            WorldSkirtMesh.Build(ShapeOf(surface), surface, null, data);

            var tris = data.Triangles[BlockMaterials.Grass];
            Assert.That(tris.Count, Is.GreaterThan(0));
            Assert.That(data.TriangleBlocks[BlockMaterials.Grass].Count, Is.EqualTo(tris.Count / 3));
            Assert.That(data.Vertices.Count, Is.EqualTo(tris.Count), "у каждого треугольника свои вершины: камень гранями, как в мире");

            var area = 0.0;
            for (var t = 0; t < tris.Count; t += 3)
            {
                var a = data.Vertices[tris[t]];
                var b = data.Vertices[tris[t + 1]];
                var c = data.Vertices[tris[t + 2]];
                var up = Vector3.Cross(b - a, c - a).y;
                Assert.That(up, Is.GreaterThan(0f), "лицевая сторона смотрит вверх");
                area += up * 0.5;
            }
            var outer = Size + 2.0 * WorldSkirtMesh.Width;
            Assert.That(area, Is.EqualTo(outer * outer - Size * Size).Within(1.0), "кольцо целиком, без дыр и наложений");

            // Каждый угол края мира — вершина гор на высоте земли.
            // Вершины у треугольников свои (грани камня), поэтому углы считаем без повторов.
            var onEdge = new System.Collections.Generic.HashSet<Vector2Int>();
            foreach (var v in data.Vertices)
            {
                int x = Mathf.RoundToInt(v.x), z = Mathf.RoundToInt(v.z);
                if (Mathf.Abs(v.x - x) > 1e-4f || Mathf.Abs(v.z - z) > 1e-4f || x < 0 || z < 0 || x > Size || z > Size)
                    continue;
                Assert.That(x == 0 || z == 0 || x == Size || z == Size, Is.True, "внутрь мира горы не заходят");
                Assert.That(v.y, Is.EqualTo(surface.Corner(x, z) / (float)H).Within(1e-4f));
                onEdge.Add(new Vector2Int(x, z));
            }
            Assert.That(onEdge.Count, Is.EqualTo(4 * Size));

            foreach (var blend in data.Blends)
                Assert.That(blend.y, Is.EqualTo(ChunkMeshData.GrassVertex), "рисуется как трава гладкой земли");
        }
    }
}
