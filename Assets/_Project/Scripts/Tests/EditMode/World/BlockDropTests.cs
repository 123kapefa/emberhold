using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockDropTests
    {
        private const string DropsAssetPath = "Assets/_Project/Data/Blocks/BlockDrops.asset";
        private static readonly Vector3Int Column = new(3, 0, 2);

        private BlockDropTable table;

        [SetUp]
        public void SetUp() => table = AssetDatabase.LoadAssetAtPath<BlockDropTable>(DropsAssetPath);

        [Test]
        public void DropsAsset_StoneDropsOneStone()
        {
            Assert.That(table, Is.Not.Null, $"Нет таблицы выпадения {DropsAssetPath}");
            Assert.That(table.TryGetDrop(BlockMaterials.Stone, out var drop), Is.True);
            Assert.That(drop, Is.EqualTo(new ItemDrop("stone", 1)));
        }

        [Test]
        public void DropsAsset_GrassAndDirtDropNothing()
        {
            Assert.That(table.TryGetDrop(BlockMaterials.Grass, out _), Is.False);
            Assert.That(table.TryGetDrop(BlockMaterials.Dirt, out _), Is.False);
            Assert.That(table.TryGetDrop(BlockMaterials.Air, out _), Is.False);
        }

        [Test]
        public void DigStone_DropsStoneOnItsPlace()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);
            var top = Column + new Vector3Int(0, 3, 0);

            Assert.That(damage.Hit(world, top, 100f), Is.True);

            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("stone", 1)));
            // Лежит в центре клетки разрушенного блока, на верхе блока под ним.
            Assert.That(items.Items[0].Position, Is.EqualTo(new Vector3(3.5f, 3f, 2.5f)));
        }

        [Test]
        public void DigGrassOrDirt_DropsNothing()
        {
            var world = ColumnWorld(BlockMaterials.Dirt);
            world.SetBlock(Column + new Vector3Int(0, 3, 0), Block.Full(BlockMaterials.Grass));
            var (damage, items) = Digging(world);

            Assert.That(damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f), Is.True);
            Assert.That(damage.Hit(world, Column + new Vector3Int(0, 2, 0), 100f), Is.True);

            Assert.That(items.Items, Is.Empty);
        }

        [Test]
        public void UnfinishedHit_DropsNothing()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);

            Assert.That(damage.Hit(world, Column + new Vector3Int(0, 3, 0), 50f), Is.False);

            Assert.That(items.Items, Is.Empty);
        }

        [Test]
        public void DigBlockUnderItem_ItemSettlesAndStacksWithNextDrop()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);
            damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f);

            damage.Hit(world, Column + new Vector3Int(0, 2, 0), 100f);
            Assert.That(items.Settle(world), Is.True);

            // Первый камень опустился на блок y = 1, не провалился и сложился в одну кучку со вторым.
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Position, Is.EqualTo(new Vector3(3.5f, 2f, 2.5f)));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("stone", 2)));
            Assert.That(items.Settle(world), Is.False, "Лежащие предметы больше не двигаются");
        }

        [Test]
        public void TwoDropsInSameCell_StackIntoOnePile()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);
            var top = Column + new Vector3Int(0, 3, 0);
            damage.Hit(world, top, 100f);

            // Блок снова поставили и снова разбили: второй камень падает в ту же клетку.
            world.SetBlock(top, Block.Full(BlockMaterials.Stone));
            items.Settle(world);
            damage.Hit(world, top, 100f);
            items.Settle(world);

            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop.Count, Is.EqualTo(2));
            Assert.That(items.Items[0].Position.y, Is.EqualTo(3f));
        }

        [Test]
        public void DropsInNeighbourCells_StaySeparate()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var neighbour = Column + Vector3Int.right;
            for (var y = 0; y < 4; y++)
                world.SetBlock(neighbour + new Vector3Int(0, y, 0), Block.Full(BlockMaterials.Stone));
            var (damage, items) = Digging(world);

            damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f);
            damage.Hit(world, neighbour + new Vector3Int(0, 3, 0), 100f);

            Assert.That(items.Items, Has.Count.EqualTo(2));
        }

        [Test]
        public void ItemOnPartialBlock_LiesOnItsSurface()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            world.SetBlock(Column + new Vector3Int(0, 2, 0), Block.Flat(BlockMaterials.Dirt, BlockShape.MaxHeight / 2));
            var (damage, items) = Digging(world);

            damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f);

            Assert.That(items.Items[0].Position.y, Is.EqualTo(2.5f));
        }

        [Test]
        public void DigWholeColumn_ItemRestsOnWorldBottom()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);

            for (var y = 3; y >= 0; y--)
                damage.Hit(world, Column + new Vector3Int(0, y, 0), 100f);
            items.Settle(world);

            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop.Count, Is.EqualTo(4));
            Assert.That(items.Items[0].Position.y, Is.EqualTo(0f));
        }

        [Test]
        public void BlockPlacedOnItem_LiftsItemOnTop()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);
            damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f);

            world.SetBlock(Column + new Vector3Int(0, 3, 0), Block.Full(BlockMaterials.Stone));
            items.Settle(world);

            Assert.That(items.Items[0].Position.y, Is.EqualTo(4f));
        }

        [Test]
        public void RemoveItem_ForgetsIt()
        {
            var world = ColumnWorld(BlockMaterials.Stone);
            var (damage, items) = Digging(world);
            damage.Hit(world, Column + new Vector3Int(0, 3, 0), 100f);
            var version = items.Version;

            Assert.That(items.Remove(items.Items[0]), Is.True);

            Assert.That(items.Items, Is.Empty);
            Assert.That(items.Version, Is.GreaterThan(version));
        }

        /// <summary>Столбик из 4 полных блоков y = 0..3 в мире из одного чанка.</summary>
        private static VoxelWorld ColumnWorld(byte material)
        {
            var world = new VoxelWorld(Vector3Int.one);
            for (var y = 0; y < 4; y++)
                world.SetBlock(Column + new Vector3Int(0, y, 0), Block.Full(material));
            return world;
        }

        private (BlockDamage, DroppedItems) Digging(VoxelWorld world)
        {
            var damage = new BlockDamage(new UniformBlockDurability(100f));
            var items = new DroppedItems(table);
            damage.Destroyed += (block, destroyed) => items.OnBlockDestroyed(world, block, destroyed);
            return (damage, items);
        }
    }
}
