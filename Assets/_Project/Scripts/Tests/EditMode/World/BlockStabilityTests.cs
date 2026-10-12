using System.Collections.Generic;
using System.Diagnostics;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class BlockStabilityTests
    {
        private static readonly Block Stone = Block.Full(BlockMaterials.Stone);

        [Test]
        public void PillarOnGround_IsSupported()
        {
            var world = Empty();
            Pillar(world, 3, 2, 4);
            var stability = new BlockStability();

            Assert.That(stability.IsSupported(world, new Vector3Int(3, 3, 2)), Is.True);
            Assert.That(stability.Settle(world, new Vector3Int(3, 3, 2)), Is.Zero);
        }

        [Test]
        public void RemoveMiddleOfPillar_TopFallsOntoBase()
        {
            var world = Empty();
            Pillar(world, 3, 2, 4);
            var falls = new List<BlockFall>();

            world.SetBlock(new Vector3Int(3, 1, 2), Block.Air);
            var count = new BlockStability().Settle(world, new Vector3Int(3, 1, 2), falls);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(world.GetBlock(new Vector3Int(3, 1, 2)), Is.EqualTo(Stone));
            Assert.That(world.GetBlock(new Vector3Int(3, 2, 2)), Is.EqualTo(Stone));
            Assert.That(world.GetBlock(new Vector3Int(3, 3, 2)).IsAir, Is.True);
            Assert.That(falls, Is.EquivalentTo(new[]
            {
                new BlockFall(new Vector3Int(3, 2, 2), new Vector3Int(3, 1, 2), Stone),
                new BlockFall(new Vector3Int(3, 3, 2), new Vector3Int(3, 2, 2), Stone),
            }));
        }

        [Test]
        public void RemovePillarUnderOverhang_ArmFallsColumnByColumn()
        {
            var world = Empty();
            Pillar(world, 3, 2, 3);
            world.SetBlock(new Vector3Int(4, 2, 2), Stone);
            world.SetBlock(new Vector3Int(5, 2, 2), Stone);

            world.SetBlock(new Vector3Int(3, 1, 2), Block.Air);
            var count = new BlockStability().Settle(world, new Vector3Int(3, 1, 2));

            Assert.That(count, Is.EqualTo(3));
            Assert.That(world.GetBlock(new Vector3Int(3, 1, 2)), Is.EqualTo(Stone), "Верх столба лёг на основание");
            Assert.That(world.GetBlock(new Vector3Int(4, 0, 2)), Is.EqualTo(Stone), "Вынос упал на дно");
            Assert.That(world.GetBlock(new Vector3Int(5, 0, 2)), Is.EqualTo(Stone));
            Assert.That(world.GetBlock(new Vector3Int(4, 2, 2)).IsAir, Is.True);
        }

        [Test]
        public void BridgeBetweenPillars_StaysWhenOnePillarRemoved()
        {
            var world = Empty();
            Pillar(world, 2, 2, 3);
            Pillar(world, 6, 2, 3);
            for (var x = 3; x <= 5; x++)
                world.SetBlock(new Vector3Int(x, 2, 2), Stone);

            for (var y = 0; y < 2; y++)
                world.SetBlock(new Vector3Int(2, y, 2), Block.Air);
            var count = new BlockStability().Settle(world, new Vector3Int(2, 1, 2));

            Assert.That(count, Is.Zero, "Мост держится за второй столб");
            Assert.That(world.GetBlock(new Vector3Int(2, 2, 2)), Is.EqualTo(Stone));
        }

        [Test]
        public void SlabSlopeAndStairs_HoldEachOtherAndKeepShapeWhenFalling()
        {
            var world = Empty();
            var slab = Block.Flat(BlockMaterials.Stone, 8);
            var slope = new Block(BlockMaterials.Dirt, new BlockShape(16, 16, 0, 0));
            var stairs = new Block(BlockMaterials.Wood, BlockShape.Catalog(BlockForm.Stairs, 3));
            world.SetBlock(new Vector3Int(3, 0, 2), Stone);
            world.SetBlock(new Vector3Int(3, 1, 2), slab);
            world.SetBlock(new Vector3Int(3, 2, 2), slope);
            world.SetBlock(new Vector3Int(4, 2, 2), stairs);
            var stability = new BlockStability();

            Assert.That(stability.IsSupported(world, new Vector3Int(4, 2, 2)), Is.True, "Лестница держится за скат, скат — за полублок");

            world.SetBlock(new Vector3Int(3, 1, 2), Block.Air);
            stability.Settle(world, new Vector3Int(3, 1, 2));

            Assert.That(world.GetBlock(new Vector3Int(3, 1, 2)), Is.EqualTo(slope));
            Assert.That(world.GetBlock(new Vector3Int(4, 0, 2)), Is.EqualTo(stairs), "Форма и поворот сохраняются");
        }

        [Test]
        public void PlacedBlockOnFloatingBlock_BothFall()
        {
            var world = Empty();
            world.SetBlock(new Vector3Int(3, 5, 2), Stone);
            world.SetBlock(new Vector3Int(3, 6, 2), Stone);

            var count = new BlockStability().Settle(world, new Vector3Int(3, 6, 2));

            Assert.That(count, Is.EqualTo(2));
            Assert.That(world.GetBlock(new Vector3Int(3, 0, 2)), Is.EqualTo(Stone));
            Assert.That(world.GetBlock(new Vector3Int(3, 1, 2)), Is.EqualTo(Stone));
        }

        [Test]
        public void Terrain_DigTunnelUnderLedge_LedgeStays()
        {
            var world = Ground(4);
            // Сквозной проход под верхним слоем: потолок держится за края по бокам.
            for (var x = 4; x <= 8; x++)
                world.SetBlock(new Vector3Int(x, 2, 5), Block.Air);
            var stability = new BlockStability();

            Assert.That(stability.Settle(world, new Vector3Int(6, 2, 5)), Is.Zero);
            Assert.That(world.GetBlock(new Vector3Int(6, 3, 5)), Is.EqualTo(Stone));
        }

        [Test]
        public void Terrain_CutOffPiece_Falls()
        {
            var world = Ground(4);
            // Верхний слой 3×3 отрезан со всех сторон и снизу.
            for (var x = 3; x <= 7; x++)
            for (var z = 3; z <= 7; z++)
            {
                var edge = x == 3 || x == 7 || z == 3 || z == 7;
                if (edge)
                    world.SetBlock(new Vector3Int(x, 3, z), Block.Air);
                world.SetBlock(new Vector3Int(x, 2, z), Block.Air);
            }

            var count = new BlockStability().Settle(world, new Vector3Int(5, 2, 5));

            Assert.That(count, Is.EqualTo(9));
            Assert.That(world.GetBlock(new Vector3Int(5, 2, 5)), Is.EqualTo(Stone));
            Assert.That(world.GetBlock(new Vector3Int(5, 3, 5)).IsAir, Is.True);
        }

        [Test]
        public void DroppedItems_FollowFallingBlock()
        {
            var world = Empty();
            Pillar(world, 3, 2, 3);
            var items = new DroppedItems(new StoneDrops());
            // Камень лежит на верхе столба.
            items.OnBlockDestroyed(world, new Vector3Int(3, 3, 2), Stone);
            Assume.That(items.Items[0].Position.y, Is.EqualTo(3f));

            world.SetBlock(new Vector3Int(3, 1, 2), Block.Air);
            new BlockStability().Settle(world, new Vector3Int(3, 1, 2));
            items.Settle(world);

            Assert.That(items.Items[0].Position.y, Is.EqualTo(2f), "Предмет опустился вместе с блоком");
        }

        [Test]
        public void DroppedItems_LiftedByBlockLandingInTheirCell()
        {
            var world = Empty();
            world.SetBlock(new Vector3Int(3, 0, 2), Stone);
            var items = new DroppedItems(new StoneDrops());
            items.OnBlockDestroyed(world, new Vector3Int(3, 1, 2), Stone);
            world.SetBlock(new Vector3Int(3, 5, 2), Stone);

            new BlockStability().Settle(world, new Vector3Int(3, 5, 2));
            items.Settle(world);

            Assert.That(world.GetBlock(new Vector3Int(3, 1, 2)), Is.EqualTo(Stone));
            Assert.That(items.Items[0].Position.y, Is.EqualTo(2f), "Предмет теперь лежит на упавшем блоке");
        }

        [Test]
        public void Damage_MovesWithFallingBlock()
        {
            var world = Empty();
            Pillar(world, 3, 2, 4);
            var damage = new BlockDamage(new FixedDurability(100f));
            damage.Hit(world, new Vector3Int(3, 3, 2), 30f);
            damage.Hit(world, new Vector3Int(3, 2, 2), 10f);
            var falls = new List<BlockFall>();

            Assert.That(damage.Hit(world, new Vector3Int(3, 1, 2), 100f), Is.True);
            new BlockStability().Settle(world, new Vector3Int(3, 1, 2), falls);
            damage.Move(falls);

            Assert.That(damage.Taken(new Vector3Int(3, 1, 2)), Is.EqualTo(10f));
            Assert.That(damage.Taken(new Vector3Int(3, 2, 2)), Is.EqualTo(30f));
            Assert.That(damage.Taken(new Vector3Int(3, 3, 2)), Is.Zero);
        }

        [Test]
        public void GroupLargerThanSearchLimit_CountsAsSupported()
        {
            var world = Empty();
            for (var x = 0; x < 10; x++)
                world.SetBlock(new Vector3Int(x, 5, 2), Stone);

            Assert.That(new BlockStability(searchLimit: 4).Settle(world, new Vector3Int(0, 5, 2)), Is.Zero);
            Assert.That(new BlockStability().Settle(world, new Vector3Int(0, 5, 2)), Is.EqualTo(10));
        }

        [Test]
        public void Terrain_ManyDigs_AreFast()
        {
            // Мир размером с прототип: 64 × 32 × 64, земля толщиной 20 блоков.
            var world = new VoxelWorld(new Vector3Int(4, 2, 4));
            for (var y = 0; y < 20; y++)
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
            var stability = new BlockStability();
            var random = new System.Random(10);

            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 500; i++)
            {
                var cell = new Vector3Int(random.Next(64), 19 - random.Next(10), random.Next(64));
                world.SetBlock(cell, Block.Air);
                stability.Settle(world, cell);
            }
            timer.Stop();

            // В среднем на правку уходит доля миллисекунды; порог с большим запасом на медленные машины.
            Assert.That(timer.ElapsedMilliseconds, Is.LessThan(1000), $"500 правок заняли {timer.ElapsedMilliseconds} мс");
        }

        private static VoxelWorld Empty() => new(Vector3Int.one);

        /// <summary>Сплошная земля толщиной height блоков на весь чанк.</summary>
        private static VoxelWorld Ground(int height)
        {
            var world = Empty();
            for (var y = 0; y < height; y++)
            for (var z = 0; z < ChunkData.Size; z++)
            for (var x = 0; x < ChunkData.Size; x++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
            return world;
        }

        private static void Pillar(VoxelWorld world, int x, int z, int height)
        {
            for (var y = 0; y < height; y++)
                world.SetBlock(new Vector3Int(x, y, z), Stone);
        }

        private sealed class StoneDrops : IBlockDrops
        {
            public bool TryGetDrop(byte material, out ItemDrop drop)
            {
                drop = new ItemDrop("stone", 1);
                return material == BlockMaterials.Stone;
            }
        }

        private sealed class FixedDurability : IBlockDurability
        {
            private readonly float hitPoints;

            public FixedDurability(float hitPoints) => this.hitPoints = hitPoints;

            public float HitPoints(byte material) => hitPoints;
        }
    }
}
