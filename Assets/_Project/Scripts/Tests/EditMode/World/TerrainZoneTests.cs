using System.Collections.Generic;
using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class TerrainZoneTests
    {
        private const int Seed = 7;
        /// <summary>Мир 64×32×64 блока, центр карты в (32, 32).</summary>
        private static readonly Vector3Int Size = new(4, 2, 4);
        private static readonly Vector2 MapCenter = new(32f, 32f);

        /// <summary>Ровная местность на заданной высоте: без шума, террас и скал, чтобы видеть только смешивание.</summary>
        private static TerrainProfileParameters Flat(float height)
        {
            var p = TerrainProfileParameters.Valley;
            p.Shape = TerrainShape.Flat;
            p.BaseHeight = height;
            p.NoiseAmplitude = 0f;
            p.TerraceHeight = 0f;
            p.CliffAmount = 0f;
            p.CapDepth = 0f;
            return p;
        }

        private static TerrainZoneArea Circle(Vector2 center, float diameter, float blendWidth, TerrainProfileParameters profile) =>
            new()
            {
                Shape = TerrainZoneShape.Circle,
                Center = center,
                Size = new Vector2(diameter, diameter),
                BlendWidth = blendWidth,
                Profile = profile,
            };

        private static TerrainZoneArea Rectangle(Vector2 center, Vector2 size, float blendWidth, TerrainProfileParameters profile,
            float yaw = 0f) =>
            new()
            {
                Shape = TerrainZoneShape.Rectangle,
                Center = center,
                Size = size,
                Yaw = yaw,
                BlendWidth = blendWidth,
                Profile = profile,
            };

        private static VoxelWorld Generate(TerrainProfileParameters background, params TerrainZoneArea[] zones) =>
            TerrainGenerator.Generate(background, Seed, Size, zones);

        /// <summary>Высота поверхности столбца в шестнадцатых.</summary>
        private static int ColumnHeight(VoxelWorld world, int x, int z)
        {
            for (var y = world.SizeInBlocks.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (!block.IsAir)
                    return y * BlockShape.MaxHeight + block.Shape.Top;
            }
            return 0;
        }

        private static Block SurfaceBlock(VoxelWorld world, int x, int z)
        {
            for (var y = world.SizeInBlocks.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (!block.IsAir)
                    return block;
            }
            return Block.Air;
        }

        private static float[] Weights(Vector2 point, params TerrainZoneArea[] zones)
        {
            var weights = new float[zones.Length + 1];
            TerrainGenerator.ProfileWeights(zones, point, weights);
            return weights;
        }

        [Test]
        public void CircleWeight_IsOneDeepInside_ZeroOnBorderAndOutside_SmoothBetween()
        {
            var zone = Circle(MapCenter, 20f, 4f, Flat(10f));

            Assert.That(zone.Weight(MapCenter), Is.EqualTo(1f));
            Assert.That(zone.Weight(MapCenter + new Vector2(6f, 0f)), Is.EqualTo(1f), "Глубже ширины перехода");
            Assert.That(zone.Weight(MapCenter + new Vector2(10f, 0f)), Is.EqualTo(0f), "На границе");
            Assert.That(zone.Weight(MapCenter + new Vector2(0f, -15f)), Is.EqualTo(0f), "Снаружи");
            Assert.That(zone.Weight(MapCenter + new Vector2(8f, 0f)), Is.EqualTo(0.5f).Within(1e-4f), "Середина перехода");

            var previous = 1f;
            for (var r = 6f; r <= 10f; r += 0.25f)
            {
                var w = zone.Weight(MapCenter + new Vector2(0f, r));
                Assert.That(w, Is.LessThanOrEqualTo(previous), $"Радиус {r}");
                previous = w;
            }
        }

        [Test]
        public void RectangleWeight_ZeroBlendWidth_IsSharpEdge()
        {
            var zone = Rectangle(MapCenter, new Vector2(20f, 10f), 0f, Flat(10f));

            Assert.That(zone.Weight(MapCenter + new Vector2(9.9f, 4.9f)), Is.EqualTo(1f));
            Assert.That(zone.Weight(MapCenter + new Vector2(10.1f, 0f)), Is.EqualTo(0f));
            Assert.That(zone.Weight(MapCenter + new Vector2(0f, 5.1f)), Is.EqualTo(0f));
        }

        [Test]
        public void RotatedRectangle_TurnsWithYaw()
        {
            // Длинная сторона по локальному X; после поворота на 90° она лежит вдоль Z карты.
            var zone = Rectangle(MapCenter, new Vector2(40f, 10f), 0f, Flat(10f), 90f);

            Assert.That(zone.Weight(MapCenter + new Vector2(0f, 15f)), Is.EqualTo(1f));
            Assert.That(zone.Weight(MapCenter + new Vector2(15f, 0f)), Is.EqualTo(0f));
        }

        [Test]
        public void ProfileWeights_SumToOne_BackgroundGetsTheRest()
        {
            var a = Rectangle(MapCenter, new Vector2(20f, 20f), 4f, Flat(10f));
            var b = Rectangle(MapCenter + new Vector2(4f, 0f), new Vector2(20f, 20f), 4f, Flat(20f));

            Assert.That(Weights(MapCenter), Is.EqualTo(new[] { 1f }), "Без зон — только фон");
            Assert.That(Weights(Vector2.zero, a), Is.EqualTo(new[] { 1f, 0f }), "Вне зоны — фон");
            Assert.That(Weights(MapCenter, a), Is.EqualTo(new[] { 0f, 1f }), "В глубине зоны фона нет");

            var band = Weights(MapCenter + new Vector2(-8f, 0f), a);
            Assert.That(band[1], Is.EqualTo(0.5f).Within(1e-4f), "Середина перехода");
            Assert.That(band[0], Is.EqualTo(0.5f).Within(1e-4f));

            var both = Weights(MapCenter + new Vector2(2f, 0f), a, b);
            Assert.That(both, Is.EqualTo(new[] { 0f, 0.5f, 0.5f }).Within(1e-4f), "Пересечение — поровну");

            for (var x = 10f; x <= 50f; x += 0.5f)
            {
                var w = Weights(new Vector2(x, 33f), a, b);
                Assert.That(w[0] + w[1] + w[2], Is.EqualTo(1f).Within(1e-4f), $"x = {x}");
            }
        }

        [Test]
        public void FlatZone_RaisesGroundInside_KeepsBackgroundOutside()
        {
            var world = Generate(Flat(6f), Circle(MapCenter, 30f, 8f, Flat(14f)));

            Assert.That(ColumnHeight(world, 32, 32), Is.EqualTo(14 * BlockShape.MaxHeight), "Внутри зоны");
            Assert.That(ColumnHeight(world, 2, 2), Is.EqualTo(6 * BlockShape.MaxHeight), "Вне зоны");
            Assert.That(ColumnHeight(world, 61, 32), Is.EqualTo(6 * BlockShape.MaxHeight), "Вне зоны");
        }

        [Test]
        public void ZoneBorder_ChangesHeightSmoothly_WithoutWalls()
        {
            var world = Generate(Flat(6f), Circle(MapCenter, 40f, 12f, Flat(14f)));
            var size = world.SizeInBlocks;

            // От центра к краю по радиусу высота только убывает и проходит промежуточные значения.
            var heights = new HashSet<int>();
            var previous = int.MaxValue;
            for (var x = 32; x < size.x; x++)
            {
                var h = ColumnHeight(world, x, 32);
                Assert.That(h, Is.LessThanOrEqualTo(previous), $"Столбец {x}");
                previous = h;
                heights.Add(h);
            }
            Assert.That(heights.Count, Is.GreaterThanOrEqualTo(5), "Переход ступенями, а не одной стенкой");

            // Соседние столбцы отличаются не больше чем на блок — стен нет.
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x - 1; x++)
            {
                Assert.That(Mathf.Abs(ColumnHeight(world, x, z) - ColumnHeight(world, x + 1, z)),
                    Is.LessThanOrEqualTo(BlockShape.MaxHeight), $"{x},{z} по X");
                Assert.That(Mathf.Abs(ColumnHeight(world, z, x) - ColumnHeight(world, z, x + 1)),
                    Is.LessThanOrEqualTo(BlockShape.MaxHeight), $"{z},{x} по Z");
            }
        }

        [Test]
        public void OverlappingZones_BlendByWeight()
        {
            // Обе зоны накрывают всю карту без перехода: везде поровну, высота посередине.
            var whole = new Vector2(200f, 200f);
            var world = Generate(Flat(4f), Rectangle(MapCenter, whole, 0f, Flat(10f)), Rectangle(MapCenter, whole, 0f, Flat(20f)));

            for (var z = 0; z < world.SizeInBlocks.z; z += 7)
            for (var x = 0; x < world.SizeInBlocks.x; x += 7)
                Assert.That(ColumnHeight(world, x, z), Is.EqualTo(15 * BlockShape.MaxHeight), $"{x},{z}");
        }

        [Test]
        public void ZoneProfile_BuildsItsShapeInsideTheZone()
        {
            // Гора в зоне в углу карты: вершина в центре зоны, а не в центре карты.
            var mountain = Flat(4f);
            mountain.Shape = TerrainShape.Mountain;
            // Невысокая: склоны вне скал не круче 45°, и подножие высокой горы растеклось бы до центра карты.
            mountain.ShapeHeight = 12f;
            mountain.ShapeWidth = 0.5f;
            var zoneCenter = new Vector2(16f, 48f);
            var world = Generate(Flat(4f), Circle(zoneCenter, 28f, 6f, mountain));

            Assert.That(ColumnHeight(world, 16, 48), Is.GreaterThanOrEqualTo(15 * BlockShape.MaxHeight), "Вершина");
            Assert.That(ColumnHeight(world, 32, 32), Is.EqualTo(4 * BlockShape.MaxHeight), "Центр карты вне зоны");
        }

        [Test]
        public void ZoneMaterials_ApplyInsideTheZone()
        {
            var rocky = Flat(10f);
            rocky.Materials.Surface = BlockMaterials.Stone;
            var world = Generate(Flat(10f), Circle(MapCenter, 20f, 4f, rocky));

            Assert.That(SurfaceBlock(world, 32, 32).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(SurfaceBlock(world, 2, 2).Material, Is.EqualTo(BlockMaterials.Grass));
        }

        [Test]
        public void EmptyOrOutsideZones_DoNotChangeTheWorld()
        {
            var background = TerrainProfileParameters.Valley;
            var plain = TerrainGenerator.Generate(background, Seed, Size);
            var empty = TerrainGenerator.Generate(background, Seed, Size, new List<TerrainZoneArea>());
            var outside = Generate(background, Circle(new Vector2(-100f, -100f), 30f, 8f, Flat(20f)));

            for (var z = 0; z < plain.SizeInBlocks.z; z++)
            for (var x = 0; x < plain.SizeInBlocks.x; x++)
            for (var y = 0; y < plain.SizeInBlocks.y; y++)
            {
                var p = new Vector3Int(x, y, z);
                Assert.That(empty.GetBlock(p), Is.EqualTo(plain.GetBlock(p)), $"{p} без зон");
                Assert.That(outside.GetBlock(p), Is.EqualTo(plain.GetBlock(p)), $"{p} зона вне карты");
            }
        }

        [Test]
        public void SceneZone_ConvertsToWorldGridCoordinates()
        {
            var go = new GameObject("Zone");
            var profile = ScriptableObject.CreateInstance<TerrainProfile>();
            try
            {
                var zone = go.AddComponent<TerrainZone>();
                go.transform.SetPositionAndRotation(new Vector3(5f, 3f, -3f), Quaternion.Euler(0f, 30f, 0f));
                zone.Shape = TerrainZoneShape.Rectangle;
                zone.Size = new Vector2(12f, 6f);
                zone.BlendWidth = 2f;

                Assert.That(zone.TryGetArea(new Vector3(-64f, 0f, -64f), out _), Is.False, "Без профиля зона не действует");

                zone.Profile = profile;
                Assert.That(zone.TryGetArea(new Vector3(-64f, 0f, -64f), out var area), Is.True);
                Assert.That(area.Center, Is.EqualTo(new Vector2(69f, 61f)));
                Assert.That(area.Yaw, Is.EqualTo(30f).Within(1e-3f));
                Assert.That(area.Size, Is.EqualTo(new Vector2(12f, 6f)));
                Assert.That(area.BlendWidth, Is.EqualTo(2f));
                Assert.That(area.Shape, Is.EqualTo(TerrainZoneShape.Rectangle));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
