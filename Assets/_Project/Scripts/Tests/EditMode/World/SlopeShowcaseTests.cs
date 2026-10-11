using Emberhold.World.Sandbox;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class SlopeShowcaseTests
    {
        private const float BaseHeight = 4f;

        private static VoxelWorld Build(int step = 4, bool snap = true, float steepness = 1f)
        {
            var world = new VoxelWorld(SlopeShowcase.SizeInChunks);
            SlopeShowcase.Fill(world, step, snap, steepness, BaseHeight, dirtDepth: 3, seed: 1);
            return world;
        }

        /// <summary>Абсолютная высота угла (0 — C00 … 3 — C11) столбца: сумма по слоям.</summary>
        private static int Corner(VoxelWorld world, int x, int z, int corner)
        {
            var total = 0;
            for (var y = 0; y < world.SizeInBlocks.y; y++)
            {
                var s = world.GetBlock(new Vector3Int(x, y, z)).Shape;
                total += corner switch { 0 => s.C00, 1 => s.C10, 2 => s.C01, _ => s.C11 };
            }
            return total;
        }

        [Test]
        public void Pads_FillTheGrid()
        {
            // Девять маленьких площадок и большая «Подъём» за ними.
            Assert.That(SlopeShowcase.Pads, Has.Length.EqualTo(SlopeShowcase.PadsPerRow * SlopeShowcase.PadsPerRow + 1));
            var size = Build().SizeInBlocks;
            foreach (var pad in SlopeShowcase.Pads)
            {
                Assert.That(pad.Area.xMax, Is.LessThanOrEqualTo(size.x), pad.Name);
                Assert.That(pad.Area.yMax, Is.LessThanOrEqualTo(size.z), pad.Name);
            }
        }

        [Test]
        public void PadBorders_AreFlatGroundAtBaseHeight()
        {
            // Без выравнивания по слоям: оно может сдвинуть угол границы на соседний слой.
            var world = Build(snap: false);
            var baseSixteenths = (int)(BaseHeight * BlockShape.MaxHeight);

            // Только сетка маленьких площадок: у большой края не ровные (там обрывы).
            var gridSize = SlopeShowcase.PadsPerRow * SlopeShowcase.PadSize;
            for (var i = 0; i < gridSize; i++)
            for (var pad = 0; pad < SlopeShowcase.PadsPerRow; pad++)
            {
                var edge = pad * SlopeShowcase.PadSize;
                Assert.That(Corner(world, edge, i, 0), Is.EqualTo(baseSixteenths), $"Граница по X: {edge},{i}");
                Assert.That(Corner(world, i, edge, 0), Is.EqualTo(baseSixteenths), $"Граница по Z: {i},{edge}");
            }
        }

        [Test]
        public void EveryPad_HasRelief()
        {
            var world = Build();
            var baseSixteenths = (int)(BaseHeight * BlockShape.MaxHeight);

            for (var pad = 0; pad < SlopeShowcase.Pads.Length; pad++)
            {
                var c = SlopeShowcase.PadCenter(pad);
                var differs = false;
                for (var dz = -4; dz <= 4 && !differs; dz++)
                for (var dx = -4; dx <= 4 && !differs; dx++)
                    differs = Corner(world, (int)c.x + dx, (int)c.y + dz, 0) != baseSixteenths;
                Assert.That(differs, Is.True, SlopeShowcase.Pads[pad].Name);
            }
        }

        [Test]
        public void Rise_ClimbsTwelveBlocksTurning90Degrees()
        {
            var world = Build(snap: false);
            var area = SlopeShowcase.Pads[^1].Area;
            var baseSixteenths = (int)(BaseHeight * BlockShape.MaxHeight);
            var high = baseSixteenths + 12 * BlockShape.MaxHeight;
            int At(Vector2 local) => Corner(world, area.x + Mathf.RoundToInt(local.x), area.y + Mathf.RoundToInt(local.y), 0);

            // Начало подъёма — у нижнего уровня, выход — у верхнего, и он под 90° к началу.
            Assert.That(At(SlopeShowcase.RiseCentre(0f)), Is.LessThan(baseSixteenths + 2 * BlockShape.MaxHeight), "Начало");
            Assert.That(At(SlopeShowcase.RiseCentre(1f)), Is.GreaterThan(high - 2 * BlockShape.MaxHeight), "Выход");
            var start = SlopeShowcase.RiseCentre(0.02f) - SlopeShowcase.RiseCentre(0f);
            var end = SlopeShowcase.RiseCentre(1f) - SlopeShowcase.RiseCentre(0.98f);
            Assert.That(Vector2.Angle(start, end), Is.EqualTo(90f).Within(3f));

            // Высота растёт вдоль поворота.
            Assert.That(At(SlopeShowcase.RiseCentre(0.25f)), Is.LessThan(At(SlopeShowcase.RiseCentre(0.5f))));
            Assert.That(At(SlopeShowcase.RiseCentre(0.5f)), Is.LessThan(At(SlopeShowcase.RiseCentre(0.75f))));

            // Поперёк прохода профиль вогнутый: у бортов выше, чем посередине.
            var middle = SlopeShowcase.RiseCentre(0.5f);
            var outward = (middle - SlopeShowcase.RisePivot).normalized;
            Assert.That(At(middle), Is.LessThan(At(middle + outward * 7f)));
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(16)]
        public void HeightStep_QuantizesCorners(int step)
        {
            var world = Build(step);

            for (var z = 0; z < world.SizeInBlocks.z; z++)
            for (var x = 0; x < world.SizeInBlocks.x; x++)
                Assert.That(Corner(world, x, z, 0) % step, Is.Zero, $"{x},{z}");
        }

        [Test]
        public void SnapToLayers_CanBeTurnedOff()
        {
            var snapped = Build(snap: true);
            var raw = Build(snap: false);

            var differ = 0;
            for (var z = 0; z < snapped.SizeInBlocks.z; z++)
            for (var x = 0; x < snapped.SizeInBlocks.x; x++)
                if (Corner(snapped, x, z, 0) != Corner(raw, x, z, 0))
                    differ++;

            Assert.That(differ, Is.GreaterThan(0));
        }

        [Test]
        public void Fill_AgainOverSameWorld_MarksChangedChunksOnly()
        {
            var world = Build();
            world.TakeDirtyChunks(new System.Collections.Generic.List<Vector3Int>());

            SlopeShowcase.Fill(world, 4, true, 1f, BaseHeight, 3, 1);
            Assert.That(world.DirtyCount, Is.Zero, "Те же настройки — ничего не меняется");

            SlopeShowcase.Fill(world, 4, true, 2f, BaseHeight, 3, 1);
            Assert.That(world.DirtyCount, Is.GreaterThan(0));
        }
    }
}
