using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Trees
{
    public sealed class ForestTests
    {
        private const int Ground = 4;

        private VoxelWorld world;
        private DroppedItems items;
        private Forest forest;
        private TreeSpecies birch;
        private TreeSpecies pine;

        [SetUp]
        public void SetUp()
        {
            // Ровная земля 64×64 под травой: блоки y = 0..3, деревья встают в y = 4.
            world = new VoxelWorld(new Vector3Int(4, 1, 4));
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(y == Ground - 1 ? BlockMaterials.Grass : BlockMaterials.Dirt));
            items = new DroppedItems(new NoDrops());
            forest = new Forest(items);
            birch = TreeSpecies.Create("Берёза", 75f, 3);
            pine = TreeSpecies.Create("Сосна", 150f, 6, 5f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(birch);
            Object.DestroyImmediate(pine);
        }

        [Test]
        public void Mix_PicksSpeciesByWeight()
        {
            var mix = new ForestMix(0.1f, Entry(birch, 3f), Entry(pine, 1f));
            Assert.That(mix.Pick(0f), Is.SameAs(birch));
            Assert.That(mix.Pick(0.74f), Is.SameAs(birch));
            Assert.That(mix.Pick(0.76f), Is.SameAs(pine));
            Assert.That(mix.Pick(1f), Is.SameAs(pine));
            Assert.That(new ForestMix(0.1f).IsEmpty, Is.True, "Без видов сажать нечего");
            Assert.That(new ForestMix(0f, Entry(birch, 1f)).IsEmpty, Is.True, "Без густоты тоже");
        }

        [Test]
        public void Scatter_FarZoneGrowsItsOwnBiggerTrees()
        {
            var background = new ForestMix(0.2f, Entry(birch, 1f));
            // Зона на дальней половине карты (z от 32) со своим лесом из сосен.
            var zones = new List<TerrainZoneArea>
            {
                new()
                {
                    Shape = TerrainZoneShape.Rectangle, Center = new Vector2(32f, 52f), Size = new Vector2(80f, 40f),
                    BlendWidth = 2f, Forest = new ForestMix(0.2f, Entry(pine, 1f)),
                },
            };

            var planted = ForestScatter.Scatter(world, 7, background, zones, forest, column => column.x < 4 && column.y < 4);

            Assert.That(planted, Is.GreaterThan(50));
            Assert.That(forest.Trees, Has.Count.EqualTo(planted));
            foreach (var tree in forest.Trees)
            {
                Assert.That(tree.Cell.y, Is.EqualTo(Ground), "Дерево стоит на траве");
                if (tree.Cell.z < 28)
                    Assert.That(tree.Species, Is.SameAs(birch), $"Ближе к старту берёзы: {tree.Cell}");
                if (tree.Cell.z > 36)
                    Assert.That(tree.Species, Is.SameAs(pine), $"В дальней зоне сосны: {tree.Cell}");
                Assert.That(tree.Cell.x < 4 && tree.Cell.z < 4, Is.False, "У костра место свободно");
                for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dz != 0)
                        Assert.That(forest.At(tree.Column + new Vector2Int(dx, dz)), Is.Null, "Соседние столбцы не заняты");
                }
            }

            // Тот же seed — тот же лес.
            var again = new Forest(items);
            ForestScatter.Scatter(world, 7, background, zones, again, column => column.x < 4 && column.y < 4);
            Assert.That(again.Trees, Has.Count.EqualTo(planted));
            for (var i = 0; i < planted; i++)
                Assert.That(again.Trees[i].Cell, Is.EqualTo(forest.Trees[i].Cell));
        }

        [Test]
        public void Scatter_GrowsOnGrassAndOnStoneOnlyInMountainForest()
        {
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 32; x++)
                world.SetBlock(new Vector3Int(x, Ground - 1, z), Block.Full(BlockMaterials.Stone));

            ForestScatter.Scatter(world, 3, new ForestMix(0.2f, Entry(birch, 1f)), null, forest);

            Assert.That(forest.Trees, Is.Not.Empty);
            foreach (var tree in forest.Trees)
                Assert.That(tree.Cell.x, Is.GreaterThanOrEqualTo(32), "На камне деревья не растут");

            // Горный лес растёт и на камне.
            var mountain = new Forest(items);
            ForestScatter.Scatter(world, 3, new ForestMix(0.2f, true, Entry(pine, 1f)), null, mountain);
            Assert.That(mountain.Trees, Has.Some.Matches<TreeStand>(tree => tree.Cell.x < 32));
        }

        [Test]
        public void Scatter_OnSmoothGround_RockyPlacesCountAsStone()
        {
            // Гладкая земля: на каменной половине (x < 32) верх — кусок склона из травы над камнем,
            // но место каменное, и обычный лес там не растёт.
            const int h = BlockShape.MaxHeight;
            var smooth = new VoxelWorld(new Vector3Int(4, 1, 4));
            var corners = new int[65, 65];
            var rockWeights = new byte[65, 65];
            for (var z = 0; z <= 64; z++)
            for (var x = 0; x <= 64; x++)
            {
                corners[x, z] = Ground * h + 4;
                rockWeights[x, z] = x <= 32 ? (byte)255 : (byte)0;
            }
            var rock = new TerrainMaterials
            {
                Surface = BlockMaterials.Stone, Subsurface = BlockMaterials.Stone, SubsurfaceDepth = 1,
                Deep = BlockMaterials.Stone, Bed = BlockMaterials.Stone,
            };
            var materials = new TerrainMaterials[64, 64];
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
                materials[x, z] = x < 32 ? rock : TerrainMaterials.Default;
            CornerTerrain.FillSmooth(smooth, corners, materials, new int[64, 64]).SetRock(rockWeights);
            Assert.That(smooth.GetBlock(new Vector3Int(10, Ground, 10)).Material, Is.EqualTo(BlockMaterials.Grass));

            ForestScatter.Scatter(smooth, 3, new ForestMix(0.2f, Entry(birch, 1f)), null, forest);

            Assert.That(forest.Trees, Is.Not.Empty);
            foreach (var tree in forest.Trees)
                Assert.That(tree.Cell.x, Is.GreaterThanOrEqualTo(32), "На каменном месте деревья не растут");
        }

        [Test]
        public void Hit_FellsTreeWhenDurabilityIsGoneAndDropsWoodAtBase()
        {
            var cell = new Vector3Int(10, Ground, 10);
            var tree = forest.Plant(cell, birch);
            var felled = new List<TreeStand>();
            forest.Felled += felled.Add;

            Assert.That(forest.Hit(world, tree, 25f), Is.False);
            Assert.That(forest.Hit(world, tree, 25f), Is.False);
            Assert.That(tree.Health, Is.EqualTo(25f));
            Assert.That(items.Items, Is.Empty);
            Assert.That(forest.Hit(world, tree, 25f), Is.True, "Берёза падает с третьего удара");

            Assert.That(tree.IsStanding, Is.False);
            Assert.That(forest.Trees, Is.Empty);
            Assert.That(felled, Is.EqualTo(new[] { tree }));
            Assert.That(items.Items, Has.Count.EqualTo(1));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 3)));
            Assert.That(items.Items[0].Cell, Is.EqualTo(cell));
            Assert.That(forest.Hit(world, tree, 25f), Is.False, "Срубленное дерево больше не бьётся");
        }

        [Test]
        public void BiggerSpeciesNeedsMoreHitsAndGivesMoreWood()
        {
            var tree = forest.Plant(new Vector3Int(20, Ground, 20), pine);
            var hits = 0;
            while (!forest.Hit(world, tree, 25f))
                hits++;
            Assert.That(hits + 1, Is.EqualTo(6));
            Assert.That(items.Items[0].Drop, Is.EqualTo(new ItemDrop("wood", 6)));
        }

        [Test]
        public void Settle_FellsTreeWhenGroundIsDugOrCellIsBuilt()
        {
            var dug = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            var built = forest.Plant(new Vector3Int(20, Ground, 20), birch);
            var untouched = forest.Plant(new Vector3Int(30, Ground, 30), birch);
            Assert.That(forest.Plant(new Vector3Int(30, Ground, 30), pine), Is.Null, "Одно дерево на столбец");

            world.SetBlock(dug.Cell + Vector3Int.down, Block.Air);
            world.SetBlock(built.Cell, Block.Full(BlockMaterials.Stone));

            Assert.That(forest.Settle(world), Is.EqualTo(2));
            Assert.That(forest.Trees, Is.EqualTo(new[] { untouched }));
            Assert.That(items.Items, Has.Count.EqualTo(2), "Упавшие деревья тоже дают поленья");
            Assert.That(forest.Settle(world), Is.Zero);
        }

        [Test]
        public void Raycast_FindsNearestTreeAlongRay()
        {
            var near = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            forest.Plant(new Vector3Int(20, Ground, 10), birch);

            var origin = new Vector3(0f, Ground + 1f, 10.5f);
            Assert.That(forest.Raycast(world, origin, Vector3.right, 100f, out var hit, out var distance), Is.True);
            Assert.That(hit, Is.SameAs(near));
            Assert.That(distance, Is.EqualTo(10.5f - birch.CrownWidth * 0.5f).Within(1e-4f));

            Assert.That(forest.Raycast(world, origin + Vector3.up * 10f, Vector3.right, 100f, out _, out _), Is.False, "Над деревьями мимо");
            Assert.That(forest.Raycast(world, origin, Vector3.right, 5f, out _, out _), Is.False, "Дальше луча не ищем");
        }

        private static ForestMix.Entry Entry(TreeSpecies species, float weight) => new() { species = species, weight = weight };

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
