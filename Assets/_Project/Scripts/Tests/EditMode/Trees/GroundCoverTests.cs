using System.Collections.Generic;
using System.Linq;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Vegetation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Trees
{
    public sealed class GroundCoverTests
    {
        private const int Ground = 4;

        private readonly List<Object> created = new();
        private VoxelWorld world;
        private Forest forest;
        private GroundCoverKind grass;
        private GroundCoverKind fern;

        [SetUp]
        public void SetUp()
        {
            // Ровная трава 128×128: блоки y = 0..3, мелочь встаёт в y = 4.
            world = World(8, BlockMaterials.Grass);
            forest = new Forest(new DroppedItems(new NoDrops()));
            grass = Kind("Трава", GroundCoverShape.Grass);
            fern = Kind("Папоротник", GroundCoverShape.Fern, 0.35f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void Mix_PicksKindsByWeightAndFallsBackToOpen()
        {
            var mix = new GroundCoverMix(1f, false, new[] { Entry(grass, 3f), Entry(fern, 1f) });
            Assert.That(mix.PickOpen(0.7f), Is.SameAs(grass));
            Assert.That(mix.PickOpen(0.8f), Is.SameAs(fern));
            Assert.That(mix.HasUnderTrees, Is.False);
            Assert.That(mix.PickUnderTrees(0.1f), Is.SameAs(grass), "Без своих видов под деревьями — как на открытом месте");
            Assert.That(new GroundCoverMix(0f, false, new[] { Entry(grass, 1f) }).IsEmpty, Is.True, "Без густоты пусто");
            Assert.That(new GroundCoverMix(1f, false, new GroundCoverMix.Entry[0]).IsEmpty, Is.True, "Без видов пусто");
        }

        [Test]
        public void Scatter_CarpetCoversAboutEveryOtherColumnAndDensityThinsIt()
        {
            var carpet = Scatter(1f, 7);
            var patches = Scatter(0.3f, 7);
            var none = Scatter(0f, 7);

            var columns = world.SizeInBlocks.x * world.SizeInBlocks.z;
            Assert.That(carpet.Count, Is.InRange(columns * 0.45f, columns * 0.6f), "Ковёр: пучок примерно в каждом втором столбце");
            Assert.That(patches.Count, Is.InRange(carpet.Count * 0.15f, carpet.Count * 0.5f), "0,3 — островки на трети площади");
            Assert.That(none.Count, Is.Zero);
        }

        [Test]
        public void Scatter_SameSeedSameCover()
        {
            var first = Scatter(0.6f, 11).Patches.OrderBy(p => p.Cell.x * 1000 + p.Cell.z).ToList();
            var again = Scatter(0.6f, 11).Patches.OrderBy(p => p.Cell.x * 1000 + p.Cell.z).ToList();
            Assert.That(again, Has.Count.EqualTo(first.Count));
            for (var i = 0; i < first.Count; i++)
            {
                Assert.That(again[i].Cell, Is.EqualTo(first[i].Cell));
                Assert.That(again[i].Kind, Is.SameAs(first[i].Kind));
                Assert.That(again[i].Offset, Is.EqualTo(first[i].Offset));
            }
        }

        [Test]
        public void Scatter_UnderTreesKindsGrowOnlyNearTreesAndNotInTreeColumns()
        {
            var birch = TreeSpecies.Create("Берёза", 75f, 3);
            created.Add(birch);
            for (var i = 0; i < 6; i++)
                forest.Plant(new Vector3Int(20 + i * 15, Ground, 30 + i * 12), birch);
            var mix = new ForestMix(0f).WithGroundCover(new GroundCoverMix(0.5f, false, new[] { Entry(grass, 1f) }, new[] { Entry(fern, 1f) }));
            var cover = new GroundCover();
            GroundCoverScatter.Scatter(world, 5, _ => mix, forest, cover);

            var ferns = 0;
            foreach (var patch in cover.Patches)
            {
                var column = new Vector2Int(patch.Cell.x, patch.Cell.z);
                Assert.That(forest.At(column), Is.Null, "В столбце дерева мелочи нет");
                Assert.That(patch.Cell.y, Is.EqualTo(Ground));
                Assert.That(Mathf.Abs(patch.Offset.x), Is.LessThanOrEqualTo(GroundCoverScatter.MaxOffset));
                if (patch.Kind != fern)
                    continue;
                ferns++;
                var near = forest.Trees.Any(t => Mathf.Abs(t.Cell.x - column.x) <= GroundCoverScatter.TreeReach
                                                 && Mathf.Abs(t.Cell.z - column.y) <= GroundCoverScatter.TreeReach);
                Assert.That(near, Is.True, $"Папоротник в {column} далеко от деревьев");
            }
            Assert.That(ferns, Is.GreaterThan(10), "Под деревьями растут свои виды");
        }

        [Test]
        public void Scatter_StoneOnlyForMixThatGrowsOnStone()
        {
            world = World(4, BlockMaterials.Stone);
            Assert.That(Scatter(1f, 3, onStone: false).Count, Is.Zero, "Трава на камне не растёт");
            Assert.That(Scatter(1f, 3, onStone: true).Count, Is.GreaterThan(0), "Лишайник растёт и на камне");
        }

        [Test]
        public void Scatter_KeepsFireCellsClear()
        {
            var cover = Scatter(1f, 9, keepClear: c => c.x < 20);
            Assert.That(cover.Patches.Any(p => p.Cell.x < 20), Is.False);
        }

        [Test]
        public void Watch_PatchVanishesWhenBuiltOverOrDugUnder()
        {
            var cover = Scatter(1f, 13);
            cover.Watch(world);
            var removed = new List<GroundCoverPatch>();
            cover.Removed += removed.Add;
            var patches = cover.Patches.Take(3).ToList();

            world.SetBlock(patches[0].Cell, Block.Full(BlockMaterials.Stone));
            world.SetBlock(patches[1].Cell + Vector3Int.down, Block.Air);
            // Блок рядом, а не в клетке: пучок остаётся.
            world.SetBlock(patches[2].Cell + Vector3Int.up * 2, Block.Full(BlockMaterials.Stone));

            Assert.That(removed, Is.EquivalentTo(new[] { patches[0], patches[1] }));
            Assert.That(cover.At(new Vector2Int(patches[0].Cell.x, patches[0].Cell.z)), Is.Null);
            Assert.That(cover.At(new Vector2Int(patches[2].Cell.x, patches[2].Cell.z)), Is.SameAs(patches[2]));

            cover.Watch(null);
            world.SetBlock(patches[2].Cell, Block.Full(BlockMaterials.Stone));
            Assert.That(removed, Has.Count.EqualTo(2), "Без слежки мир больше не трогает мелочь");
        }

        [Test]
        public void BaseHeight_StandsOnBlockTop()
        {
            var patch = Scatter(1f, 2).Patches.First();
            Assert.That(GroundCover.BaseHeight(world, patch), Is.EqualTo(Ground).Within(1e-4f));
        }

        [Test]
        public void MeshBuilder_EveryShapeBuildsFacetedTintableMesh()
        {
            foreach (GroundCoverShape shape in System.Enum.GetValues(typeof(GroundCoverShape)))
            {
                var kind = Kind(shape.ToString(), shape, shape == GroundCoverShape.Reeds ? 1.1f : 0.3f, Color.red);
                var mesh = GroundCoverMeshBuilder.Build(kind, 1);
                Assert.That(mesh.Vertices, Is.Not.Empty, $"{shape}: пустая сетка");
                Assert.That(mesh.Vertices.Count % 3, Is.Zero, $"{shape}: у каждого треугольника свои вершины");
                Assert.That(mesh.Normals, Has.Count.EqualTo(mesh.Vertices.Count));
                Assert.That(mesh.Colors, Has.Count.EqualTo(mesh.Vertices.Count));
                var top = mesh.Vertices.Max(v => v.y);
                Assert.That(top, Is.GreaterThan(0f).And.LessThanOrEqualTo(kind.Height * 1.6f + 0.1f), $"{shape}: выходит за высоту вида");
                Assert.That(mesh.Vertices.Min(v => v.y), Is.GreaterThan(-0.5f), $"{shape}: уходит под землю");

                var again = GroundCoverMeshBuilder.Build(kind, 1);
                Assert.That(again.Vertices, Is.EqualTo(mesh.Vertices), $"{shape}: та же форма — та же сетка");
            }
        }

        [Test]
        public void MeshBuilder_DryGrassIgnoresGrassTint()
        {
            var tinted = GroundCoverMeshBuilder.Build(grass, 0);
            Assert.That(tinted.Colors.Any(c => c.a > 0.5f), Is.True, "Зелёная трава подстраивается под траву локации");

            var dry = Kind("Сухая трава", GroundCoverShape.Grass);
            var serialized = new UnityEditor.SerializedObject(dry);
            serialized.FindProperty("tintedByGrass").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(GroundCoverMeshBuilder.Build(dry, 0).Colors.All(c => c.a < 0.5f), Is.True);

            var color = new Color(0.2f, 0.5f, 0.1f, 1f);
            Assert.That(GroundCoverMeshBuilder.Tint(color, Color.white), Is.EqualTo(color), "Белый оттенок — как есть");
        }

        private GroundCover Scatter(float density, int seed, bool onStone = false, System.Func<Vector2Int, bool> keepClear = null)
        {
            var mix = new ForestMix(0f).WithGroundCover(new GroundCoverMix(density, onStone, new[] { Entry(grass, 1f) }));
            var cover = new GroundCover();
            GroundCoverScatter.Scatter(world, seed, _ => mix, forest, cover, keepClear);
            return cover;
        }

        private GroundCoverKind Kind(string name, GroundCoverShape shape, float height = 0.45f, params Color[] accents)
        {
            var kind = GroundCoverKind.Create(name, shape, height, accents);
            created.Add(kind);
            return kind;
        }

        private static VoxelWorld World(int chunks, byte top)
        {
            var w = new VoxelWorld(new Vector3Int(chunks, 1, chunks));
            var size = w.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            for (var y = 0; y < Ground; y++)
                w.SetBlock(new Vector3Int(x, y, z), Block.Full(y == Ground - 1 ? top : BlockMaterials.Dirt));
            return w;
        }

        private static GroundCoverMix.Entry Entry(GroundCoverKind kind, float weight) => new() { kind = kind, weight = weight };

        private sealed class NoDrops : IBlockDrops
        {
            public bool TryGetDrop(byte material, out ItemDrop drop)
            {
                drop = default;
                return false;
            }
        }
    }
}
