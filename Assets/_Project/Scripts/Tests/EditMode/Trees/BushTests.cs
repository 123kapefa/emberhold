using System.Linq;
using Emberhold.World.Bushes;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Trees
{
    /// <summary>Кусты (#88): срез даёт траву, голый куст отрастает, мешает ставить блок, пропадает без земли, разброс по профилям.</summary>
    public sealed class BushTests
    {
        private const int Ground = 4;

        private VoxelWorld world;
        private DroppedItems items;
        private Forest forest;
        private BushSpecies bush;
        private TreeSpecies birch;

        [SetUp]
        public void SetUp()
        {
            // Ровная земля 64×64 под травой: блоки y = 0..3, кусты встают в y = 4.
            world = new VoxelWorld(new Vector3Int(4, 1, 4));
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(y == Ground - 1 ? BlockMaterials.Grass : BlockMaterials.Dirt));
            items = new DroppedItems(new NoDrops());
            forest = new Forest(items);
            bush = BushSpecies.Create("Куст", 50f, 3, 2f);
            birch = TreeSpecies.Create("Берёза", 75f, 3);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(bush);
            Object.DestroyImmediate(birch);
        }

        [Test]
        public void Hit_TwoBlowsCutGrass_BushStaysBare()
        {
            var b = forest.Thicket.Plant(Cell(10, 10), bush);
            Assert.That(forest.Thicket.Plant(Cell(10, 10), bush), Is.Null, "Один куст на столбец");
            Bush harvested = null;
            forest.Thicket.Harvested += x => harvested = x;

            Assert.That(forest.Thicket.Hit(world, b, 25f), Is.False, "Первый удар — ещё стоит");
            Assert.That(forest.Thicket.Hit(world, b, 25f), Is.True, "Срезается за пару ударов");

            Assert.That(harvested, Is.SameAs(b));
            Assert.That(b.IsPresent, Is.True, "Куст не исчезает");
            Assert.That(b.IsGrown, Is.False, "Голый");
            Assert.That(b.Height, Is.LessThan(bush.Height), "Голый ниже зелёного");
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("grass", 3)));
            Assert.That(items.Items[0].Cell, Is.EqualTo(b.Cell));
            Assert.That(forest.Thicket.Hit(world, b, 1000f), Is.False, "С голого куста срезать нечего");
            Assert.That(items.Items[0].Drop.Count, Is.EqualTo(3));
        }

        [Test]
        public void Grow_BareBushRegrowsAfterRegrowDays()
        {
            var b = forest.Thicket.Plant(Cell(10, 10), bush);
            forest.Thicket.Hit(world, b, 1000f);
            var version = forest.Thicket.Version;

            Assert.That(forest.Thicket.Grow(1.5f), Is.EqualTo(0));
            Assert.That(b.IsGrown, Is.False, "Полтора дня — ещё голый");
            Assert.That(b.RegrowLeft, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(forest.Thicket.Grow(0.6f), Is.EqualTo(1));
            Assert.That(b.IsGrown, Is.True, "Через двое суток снова зелёный");
            Assert.That(forest.Thicket.Version, Is.GreaterThan(version), "Вид узнаёт, что куст отрос");
            Assert.That(forest.Thicket.Hit(world, b, 1000f), Is.True, "Отросший снова даёт траву");
        }

        [Test]
        public void Bush_BlocksPlacingInItsCell_ButNotDiggingBelow()
        {
            var b = forest.Thicket.Plant(Cell(10, 10), bush);
            Assert.That(forest.BlocksEdit(b.Cell), Is.True, "В клетку куста блок не поставить");
            Assert.That(forest.BlocksEdit(b.Cell + Vector3Int.down), Is.False, "Землю под кустом копать можно");
            Assert.That(forest.Thicket.BlocksPlace(b.Cell + Vector3Int.up), Is.False);
        }

        [Test]
        public void Settle_DiggingGroundRemovesBush()
        {
            var b = forest.Thicket.Plant(Cell(10, 10), bush);
            var other = forest.Thicket.Plant(Cell(12, 10), bush);
            world.SetBlock(b.Cell + Vector3Int.down, Block.Air);

            forest.Settle(world);

            Assert.That(b.IsPresent, Is.False, "Под кустом выкопали землю — куст пропал");
            Assert.That(forest.Thicket.At(b.Column), Is.Null);
            Assert.That(forest.Thicket.Bushes, Is.EquivalentTo(new[] { other }));
            Assert.That(items.Items, Is.Empty, "Пропавший куст травы не даёт");
        }

        [Test]
        public void Raycast_HitsNearestBush()
        {
            var near = forest.Thicket.Plant(Cell(10, 10), bush);
            forest.Thicket.Plant(Cell(14, 10), bush);
            var origin = new Vector3(5f, Ground + 0.3f, 10.5f);

            Assert.That(forest.Thicket.Raycast(world, origin, Vector3.right, 50f, out var hit, out var distance), Is.True);
            Assert.That(hit, Is.SameAs(near));
            Assert.That(distance, Is.InRange(4f, 5.5f));
            Assert.That(forest.Thicket.Raycast(world, origin + Vector3.up * 3f, Vector3.right, 50f, out _, out _), Is.False, "Над кустом мимо");
        }

        [Test]
        public void Scatter_DenseProfileGrowsMoreBushes_NotOnTrees()
        {
            var dense = new ForestMix(0f).WithBushes(new BushMix(0.08f, Entry(bush)));
            var sparse = new ForestMix(0f).WithBushes(new BushMix(0.008f, Entry(bush)));
            var tree = forest.Plant(Cell(20, 20), birch);
            // Левая половина карты — пойма (густо), правая — холмы (редко).
            var planted = BushScatter.Scatter(world, 5, column => column.x < 32 ? dense : sparse, forest);

            var left = forest.Thicket.Bushes.Count(b => b.Cell.x < 32);
            var right = forest.Thicket.Bushes.Count(b => b.Cell.x >= 32);
            Assert.That(planted, Is.EqualTo(forest.Thicket.Bushes.Count));
            Assert.That(left, Is.InRange(80, 250), "Густо: около 0,08 от 2048 столбцов");
            Assert.That(right, Is.LessThan(left / 4), "Редко");
            Assert.That(forest.Thicket.At(tree.Column), Is.Null, "В столбце дерева куст не растёт");
            Assert.That(forest.Thicket.Bushes.All(b => b.Cell.y == Ground), Is.True, "На земле");
            Assert.That(forest.Thicket.Bushes.All(b => Mathf.Abs(b.Offset.x) <= ForestScatter.MaxOffset && Mathf.Abs(b.Offset.y) <= ForestScatter.MaxOffset),
                Is.True);

            var again = new Forest(items);
            again.Plant(Cell(20, 20), birch);
            BushScatter.Scatter(world, 5, column => column.x < 32 ? dense : sparse, again);
            Assert.That(again.Thicket.Bushes.Select(b => b.Cell), Is.EqualTo(forest.Thicket.Bushes.Select(b => b.Cell)), "Тот же seed — те же кусты");
        }

        [Test]
        public void Scatter_FewBushesGrowInLightOfEachStart()
        {
            var mix = new ForestMix(0f).WithBushes(new BushMix(0.001f, Entry(bush)));
            var fire = new Vector2(32.5f, 32.5f);
            const float light = 8f;
            // У самого огня сами по себе кусты не растут, а у старта — свои, в свету.
            BushScatter.Scatter(world, 11, _ => mix, forest, column => (new Vector2(column.x + 0.5f, column.y + 0.5f) - fire).magnitude < 3f,
                new[] { new ForestStart(fire, light) });

            var near = Mathf.Max(BushScatter.StartMinDistance, light * BushScatter.StartNear) - 1f;
            var far = light * BushScatter.StartFar + 1f;
            var inLight = forest.Thicket.Bushes.Count(b => (b.Position - fire).magnitude >= near && (b.Position - fire).magnitude <= far);
            Assert.That(inLight, Is.GreaterThanOrEqualTo(3), "Несколько кустов в свете старта, не у самого огня");
            Assert.That(forest.Thicket.Bushes.Any(b => (b.Position - fire).magnitude < 2f), Is.False, "У самого огня кустов нет");
        }

        [Test]
        public void MeshBuilder_BareBushLooksDifferent()
        {
            var green = BushMeshBuilder.Build(bush, 0, false);
            var bare = BushMeshBuilder.Build(bush, 0, true);
            try
            {
                Assert.That(green.bounds.min.y, Is.GreaterThanOrEqualTo(-1e-3f), "Стоит на земле");
                Assert.That(bare.bounds.max.y, Is.LessThan(green.bounds.max.y), "Голый ниже");
                Assert.That(green.colors.Any(c => c.a > 0.5f), Is.True, "У зелёного есть листва (красится оттенком)");
                Assert.That(bare.colors.All(c => c.a < 0.5f), Is.True, "У голого — только ветки");
                Assert.That(green.bounds.size.x, Is.LessThan(bush.Width * 1.3f), "Не шире вида");
                var same = BushMeshBuilder.Build(bush, 0, false);
                Assert.That(same.vertices, Is.EqualTo(green.vertices), "Та же форма — та же сетка");
                Object.DestroyImmediate(same);
            }
            finally
            {
                Object.DestroyImmediate(green);
                Object.DestroyImmediate(bare);
            }
        }

        private static Vector3Int Cell(int x, int z) => new(x, Ground, z);

        private static BushMix.Entry Entry(BushSpecies species) => new() { species = species, weight = 1f };

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
