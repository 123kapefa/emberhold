using System.Collections.Generic;
using System.Linq;
using Emberhold.World.Meshing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockGroupMesherTests
    {
        private static readonly Block Stone = Block.Full(BlockMaterials.Stone);

        [Test]
        public void GrassTint_ColoursVerticesByTheirPlaceInTheWorld()
        {
            // Падающая трава сохраняет оттенок своего места: цвет вершин — оттенок мира в их точке.
            var corners = new Color[65, 65];
            for (var z = 0; z <= 64; z++)
            for (var x = 0; x <= 64; x++)
                corners[x, z] = new Color(x / 64f, 0.5f, z / 64f, 1f);
            var tint = new GroundTint(corners);
            var data = new ChunkMeshData();
            var cell = new Vector3Int(40, 3, 7);

            var origin = BlockGroupMesher.Build(new List<Vector3Int> { cell }, new List<Block> { Block.Full(BlockMaterials.Grass) },
                data, null, tint);

            Assert.That(data.Colors, Has.Count.EqualTo(data.Vertices.Count));
            for (var i = 0; i < data.Vertices.Count; i++)
            {
                var p = data.Vertices[i] + origin;
                Assert.That(data.Colors[i], Is.EqualTo(tint.Sample(p.x, p.z)));
            }
            Assert.That(data.Colors, Has.None.EqualTo(Color.white));
        }

        [Test]
        public void SingleBlock_HasAllSixFacesIncludingBottom()
        {
            var data = new ChunkMeshData();
            var cell = new Vector3Int(40, 0, 7);

            var origin = BlockGroupMesher.Build(new List<Vector3Int> { cell }, new List<Block> { Stone }, data);

            Assert.That(data.TriangleCount, Is.EqualTo(12), "Шесть граней по два треугольника, низ тоже");
            // Вершины + начало координат = настоящие углы блока.
            var min = data.Vertices.Aggregate(Vector3.Min) + origin;
            var max = data.Vertices.Aggregate(Vector3.Max) + origin;
            Assert.That(min, Is.EqualTo((Vector3)cell));
            Assert.That(max, Is.EqualTo(cell + Vector3.one));
        }

        [Test]
        public void StackedBlocks_ShareNoInnerFaces()
        {
            var data = new ChunkMeshData();
            var cells = new List<Vector3Int> { new(3, 10, 3), new(3, 11, 3) };

            BlockGroupMesher.Build(cells, new List<Block> { Stone, Stone }, data);

            Assert.That(data.TriangleCount, Is.EqualTo(20), "У столбика из двух блоков десять граней");
        }

        [Test]
        public void GroupWiderThanChunk_IsMeshedWhole()
        {
            var data = new ChunkMeshData();
            var cells = new List<Vector3Int>();
            var blocks = new List<Block>();
            for (var x = 0; x < ChunkData.Size + 4; x++)
            {
                cells.Add(new Vector3Int(100 + x, 5, 0));
                blocks.Add(Stone);
            }

            var origin = BlockGroupMesher.Build(cells, blocks, data);

            // Брус 20×1×1: верх, низ, перед, зад по 20 граней и два торца.
            Assert.That(data.TriangleCount, Is.EqualTo((4 * cells.Count + 2) * 2));
            Assert.That(data.Vertices.Aggregate(Vector3.Max).x + origin.x, Is.EqualTo(100f + cells.Count));
        }

        [Test]
        public void MaterialsGoToTheirOwnLists()
        {
            var data = new ChunkMeshData();
            var cells = new List<Vector3Int> { new(0, 1, 0), new(5, 1, 0) };

            BlockGroupMesher.Build(cells, new List<Block> { Stone, Block.Full(BlockMaterials.Wood) }, data);

            Assert.That(data.Triangles[BlockMaterials.Stone].Count / 3, Is.EqualTo(12));
            Assert.That(data.Triangles[BlockMaterials.Wood].Count / 3, Is.EqualTo(12));
        }
    }
}
