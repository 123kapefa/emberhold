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

            Assert.That(forest.Groves, Is.Empty, "Лес без рощ — ровный, как раньше");

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
            // Гладкая земля: столбцы из травы, но углы на половине x < 32 каменные (как на краю скалы).
            // Место каменное по поверхности, и обычный лес там не растёт.
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
            var materials = new TerrainMaterials[64, 64];
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
                materials[x, z] = TerrainMaterials.Default;
            CornerTerrain.FillSmooth(smooth, corners, materials, new int[64, 64]).SetRock(rockWeights);
            Assert.That(smooth.GetBlock(new Vector3Int(10, Ground, 10)).Material, Is.EqualTo(BlockMaterials.Grass));

            ForestScatter.Scatter(smooth, 3, new ForestMix(0.2f, Entry(birch, 1f)), null, forest);

            Assert.That(forest.Trees, Is.Not.Empty);
            foreach (var tree in forest.Trees)
                Assert.That(tree.Cell.x, Is.GreaterThanOrEqualTo(32), "На каменном месте деревья не растут");
        }

        [Test]
        public void BaseHeight_OnSmoothSlope_IsGroundAtTrunkNotAtCellCenter()
        {
            // Гладкий склон вдоль X: восьмая блока на столбец (мир высотой 16, склон до 12). Ствол, сдвинутый от центра клетки, стоит на земле в своей точке.
            const int h = BlockShape.MaxHeight;
            var slope = new VoxelWorld(new Vector3Int(4, 1, 4));
            var corners = new int[65, 65];
            for (var z = 0; z <= 64; z++)
            for (var x = 0; x <= 64; x++)
                corners[x, z] = Ground * h + x * 2;
            var materials = new TerrainMaterials[64, 64];
            for (var z = 0; z < 64; z++)
            for (var x = 0; x < 64; x++)
                materials[x, z] = TerrainMaterials.Default;
            var surface = CornerTerrain.FillSmooth(slope, corners, materials, new int[64, 64]);
            var cell = new Vector3Int(10, surface.TopY(10, 10) + 1, 10);

            Assert.That(Forest.GroundHeight(slope, cell, new Vector2(10.8f, 10.5f)), Is.EqualTo(Ground + 10.8f / 8f).Within(1e-4f));
            Assert.That(Forest.GroundHeight(slope, cell, new Vector2(10.2f, 10.5f)), Is.EqualTo(Ground + 10.2f / 8f).Within(1e-4f));

            ForestScatter.Scatter(slope, 3, new ForestMix(0.3f, Entry(birch, 1f)), null, forest);
            var shifted = 0;
            foreach (var tree in forest.Trees)
            {
                Assert.That(Forest.BaseHeight(slope, tree), Is.EqualTo(Ground + tree.Position.x / 8f).Within(1e-4f), "Основание в точке ствола");
                if (Mathf.Abs(tree.Offset.x) > 0.1f)
                    shifted++;
            }
            Assert.That(shifted, Is.GreaterThan(0), "Есть стволы, сдвинутые вдоль склона");
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
        public void Hit_ChoppedTreeLeavesAStump()
        {
            var cell = new Vector3Int(10, Ground, 10);
            var tree = forest.Plant(cell, birch);
            while (!forest.Hit(world, tree, 25f)) { }

            Assert.That(tree.Chopped, Is.True);
            Assert.That(forest.Stumps, Has.Count.EqualTo(1));
            Assert.That(forest.Stumps[0].Cell, Is.EqualTo(cell));
            Assert.That(forest.Stumps[0].Species, Is.SameAs(birch));
            Assert.That(forest.StumpAt(cell), Is.SameAs(forest.Stumps[0]));
        }

        [Test]
        public void Stump_GoesAwayWhenItsCellIsBuiltOrGroundIsDug()
        {
            var built = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            var dug = forest.Plant(new Vector3Int(20, Ground, 20), birch);
            while (!forest.Hit(world, built, 25f)) { }
            while (!forest.Hit(world, dug, 25f)) { }
            Assert.That(forest.Stumps, Has.Count.EqualTo(2));

            world.SetBlock(built.Cell, Block.Full(BlockMaterials.Stone));
            world.SetBlock(dug.Cell + Vector3Int.down, Block.Air);
            var version = forest.Version;
            forest.Settle(world);

            Assert.That(forest.Stumps, Is.Empty);
            Assert.That(forest.Version, Is.GreaterThan(version));
        }

        [Test]
        public void Stump_BlocksBuildingInItsCellAndDiggingUnderIt()
        {
            var tree = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            Assert.That(forest.BlocksEdit(tree.Cell), Is.False, "Стоящее дерево не мешает");
            while (!forest.Hit(world, tree, 25f)) { }

            Assert.That(forest.BlocksEdit(tree.Cell), Is.True, "В клетку пня блок не поставить");
            Assert.That(forest.BlocksEdit(tree.Cell + Vector3Int.down), Is.True, "Блок под пнём не выкопать");
            Assert.That(forest.BlocksEdit(tree.Cell + Vector3Int.right), Is.False);
            Assert.That(forest.BlocksEdit(tree.Cell + Vector3Int.up), Is.False);
        }

        [Test]
        public void HitStump_UprootsItAfterSomeHitsAndDropsOneLog()
        {
            var tree = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            while (!forest.Hit(world, tree, 25f)) { }
            var stump = forest.Stumps[0];
            var logs = items.Items[0].Drop.Count;

            var hits = 1;
            while (!forest.HitStump(world, stump, 25f))
                hits++;

            Assert.That(hits, Is.GreaterThan(1), "Пень корчуют не с одного удара");
            Assert.That(stump.IsPresent, Is.False);
            Assert.That(forest.Stumps, Is.Empty);
            Assert.That(forest.BlocksEdit(tree.Cell), Is.False);
            Assert.That(items.Items, Has.Count.EqualTo(1), "Полено ложится в ту же кучку");
            Assert.That(items.Items[0].Drop.Count, Is.EqualTo(logs + 1));
            Assert.That(forest.HitStump(world, stump, 25f), Is.False, "Выкорчеванный пень больше не бьётся");
        }

        [Test]
        public void TreeThatLostItsGround_LeavesNoStump()
        {
            var tree = forest.Plant(new Vector3Int(10, Ground, 10), birch);
            world.SetBlock(tree.Cell + Vector3Int.down, Block.Air);

            Assert.That(forest.Settle(world), Is.EqualTo(1));
            Assert.That(tree.Chopped, Is.False);
            Assert.That(forest.Stumps, Is.Empty);
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

        [Test]
        public void Scatter_WithGroves_PlantsDenseGrovesAndSparseSinglesBetween()
        {
            var big = GrassWorld(8);
            var mix = new ForestMix(0.04f, Entry(birch, 1f), Entry(pine, 1f)).WithGroves(0.75f, 6f);

            var planted = ForestScatter.Scatter(big, 11, mix, null, forest);

            Assert.That(forest.Groves, Has.Count.GreaterThanOrEqualTo(3), "Рощи посажены");
            var inGroves = 0;
            foreach (var grove in forest.Groves)
            {
                Assert.That(grove.Trees, Has.Count.GreaterThanOrEqualTo(ForestScatter.MinGroveTrees));
                foreach (var tree in grove.Trees)
                {
                    Assert.That(tree.Grove, Is.SameAs(grove));
                    Assert.That(grove.Contains(tree.Column), Is.True, "Дерево рощи стоит в её области");
                }
                inGroves += grove.Trees.Count;
            }
            var singles = planted - inGroves;
            Assert.That(singles, Is.GreaterThan(0), "Между рощами растут одиночные деревья");
            Assert.That(inGroves, Is.GreaterThan(singles), "Большая часть деревьев — в рощах");

            var groveArea = 0;
            var size = big.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                if (forest.GroveAt(new Vector2Int(x, z)) != null)
                    groveArea++;
            }
            var insideDensity = (float)inGroves / groveArea;
            var outsideDensity = (float)singles / (size.x * size.z - groveArea);
            Assert.That(insideDensity, Is.GreaterThan(outsideDensity * 5f), "В роще гуще, чем на лугу");
            var expected = mix.Density * size.x * size.z;
            Assert.That(planted, Is.InRange(expected * 0.5f, expected * 1.5f), "Деревьев примерно столько же, сколько без рощ");

            var shifted = 0;
            foreach (var tree in forest.Trees)
            {
                Assert.That(Mathf.Abs(tree.Offset.x), Is.LessThanOrEqualTo(ForestScatter.MaxOffset));
                Assert.That(Mathf.Abs(tree.Offset.y), Is.LessThanOrEqualTo(ForestScatter.MaxOffset));
                if (tree.Offset.sqrMagnitude > 0.01f)
                    shifted++;
            }
            Assert.That(shifted, Is.GreaterThan(planted / 2), "Стволы сдвинуты в клетке: роща не стоит рядами");

            // Тот же seed — те же рощи.
            var again = new Forest(items);
            ForestScatter.Scatter(big, 11, mix, null, again);
            Assert.That(again.Groves, Has.Count.EqualTo(forest.Groves.Count));
            for (var i = 0; i < forest.Groves.Count; i++)
                Assert.That(again.Groves[i].Center, Is.EqualTo(forest.Groves[i].Center));
        }

        [Test]
        public void Scatter_StartGroveEdgeReachesIntoFireLight()
        {
            var fire = new Vector2(64f, 64f);
            const float light = 12f;
            var mix = new ForestMix(0.04f, Entry(birch, 1f)).WithGroves(0.75f, 6f);

            ForestScatter.Scatter(GrassWorld(8), 3, mix, null, forest,
                column => (new Vector2(column.x + 0.5f, column.y + 0.5f) - fire).sqrMagnitude <= 9f,
                new[] { new ForestStart(fire, light) });

            var start = forest.Groves[0];
            Assert.That(Vector2.Distance(start.Center, fire), Is.EqualTo(light).Within(1e-3f), "Центр рощи на краю круга света");
            var lit = 0;
            foreach (var tree in start.Trees)
            {
                var distance = Vector2.Distance(tree.Position, fire);
                Assert.That(distance, Is.GreaterThan(3f), "У самого костра место свободно");
                if (distance <= light)
                    lit++;
            }
            Assert.That(lit, Is.GreaterThanOrEqualTo(4), "Край рощи в круге света: первые дрова рядом");
        }

        [Test]
        public void Scatter_GroveGrowsItsMainSpecies()
        {
            var mix = new ForestMix(0.04f, Entry(birch, 1f), Entry(pine, 1f)).WithGroves(1f, 6f, 0.4f, 1f);

            ForestScatter.Scatter(GrassWorld(8), 5, mix, null, forest);

            Assert.That(forest.Groves, Is.Not.Empty);
            foreach (var grove in forest.Groves)
            {
                Assert.That(grove.MainSpecies, Is.Not.Null);
                foreach (var tree in grove.Trees)
                    Assert.That(tree.Species, Is.SameAs(grove.MainSpecies), "Берёзовая роща — из берёз, бор — из сосен");
            }
        }

        [Test]
        public void Fell_TakesTreeOutOfItsGrove_ClearedGroveStaysInForest()
        {
            var big = GrassWorld(8);
            ForestScatter.Scatter(big, 11, new ForestMix(0.04f, Entry(birch, 1f)).WithGroves(0.75f, 6f), null, forest);
            var grove = forest.Groves[0];
            var count = grove.Trees.Count;
            var first = grove.Trees[0];

            Assert.That(forest.GroveAt(first.Column), Is.SameAs(grove));
            forest.Fell(big, first);
            Assert.That(grove.Trees, Has.Count.EqualTo(count - 1));
            Assert.That(grove.Trees, Has.No.Member(first));

            while (grove.Trees.Count > 0)
                forest.Fell(big, grove.Trees[0]);
            Assert.That(grove.IsCleared, Is.True);
            Assert.That(forest.Groves, Has.Member(grove), "Вырубленная роща остаётся местом на карте");
        }

        // Ровная трава chunks × chunks чанков, как в SetUp.
        private static VoxelWorld GrassWorld(int chunks)
        {
            var world = new VoxelWorld(new Vector3Int(chunks, 1, chunks));
            var size = world.SizeInBlocks;
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            for (var y = 0; y < Ground; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Full(y == Ground - 1 ? BlockMaterials.Grass : BlockMaterials.Dirt));
            return world;
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
