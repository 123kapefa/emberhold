using System.Collections.Generic;
using System.Linq;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>Объёмы зоны копки для её вида (#136): жёлтый — выкопать, голубой — нарастить; соседние столбцы сливаются.</summary>
    public sealed class LevelZoneShapeTests
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> dig = new();
        private readonly List<int> fill = new();

        [Test]
        public void FlatGroundAboveLevel_OneSlabWithOuterWallsOnly()
        {
            // Земля на высоте 4, уровень 2 (верх уровня — 3): выкопать слой 3..4 в прямоугольнике 3×2.
            LevelZoneShape.Build(new Vector2Int(5, 5), new Vector2Int(3, 2), 2, 99, -99, _ => 4f, _ => true, vertices, dig, fill);

            Assert.That(fill, Is.Empty, "Ниже уровня ничего нет");
            // Верх: 6 столбцов; стенки только по краю: периметр 3+2+3+2 = 10 граней. По 6 индексов на грань.
            Assert.That(dig.Count / 6, Is.EqualTo(6 + 10), "Между столбцами граней нет");
            var used = dig.Select(i => vertices[i]).ToList();
            Assert.That(used.Min(v => v.y), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(used.Max(v => v.y), Is.EqualTo(4f + LevelZoneShape.Inflate).Within(1e-4f));
            Assert.That(used.Min(v => v.x), Is.EqualTo(5f - LevelZoneShape.Inflate).Within(1e-4f));
            Assert.That(used.Max(v => v.z), Is.EqualTo(7f + LevelZoneShape.Inflate).Within(1e-4f));
        }

        [Test]
        public void GroundBelowLevel_GoesToFillVolumeUpToLevel()
        {
            // Столбец x = 5 ниже уровня (2 < 3), столбец x = 6 выше (5 > 3).
            LevelZoneShape.Build(new Vector2Int(5, 5), new Vector2Int(2, 1), 2, 99, -99, c => c.x == 5 ? 2f : 5f, _ => true, vertices, dig, fill);

            Assert.That(fill, Is.Not.Empty);
            Assert.That(dig, Is.Not.Empty);
            var low = fill.Select(i => vertices[i]).ToList();
            Assert.That(low.Max(v => v.y), Is.EqualTo(3f + LevelZoneShape.Inflate).Within(1e-4f), "Голубой — до плоскости уровня");
            Assert.That(low.Min(v => v.y), Is.EqualTo(2f).Within(1e-4f), "От земли столбца");
            Assert.That(low.All(v => v.x <= 6f + LevelZoneShape.Inflate), Is.True);
            var high = dig.Select(i => vertices[i]).ToList();
            Assert.That(high.All(v => v.x >= 6f - LevelZoneShape.Inflate), Is.True, "Жёлтый — только над столбцом выше уровня");
        }

        [Test]
        public void GroundAtLevel_OrUndiggable_HasNoVolume()
        {
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 2), 2, 99, -99, _ => 3f, _ => true, vertices, dig, fill);
            Assert.That(dig, Is.Empty);
            Assert.That(fill, Is.Empty, "Ровно на уровне — ничего не делать");

            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 2), 2, 99, -99, _ => 6f, _ => false, vertices, dig, fill);
            Assert.That(dig, Is.Empty, "Скалу не копают — жёлтого нет");
        }

        [Test]
        public void TopClipsDig_BottomClipsFill()
        {
            // Столбец 0 — земля на 8 (выше верха 5), столбец 1 — на 1.5 (неполный блок на высоте низа 1). Уровень 2.
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 1), 2, 5, 1, c => c.x == 0 ? 8f : 1.5f, _ => true, vertices, dig, fill);

            Assert.That(dig.Select(i => vertices[i].y).Max(), Is.EqualTo(6f + LevelZoneShape.Inflate).Within(1e-4f), "Жёлтое — не выше верха");
            Assert.That(fill.Select(i => vertices[i].y).Min(), Is.EqualTo(2f).Within(1e-4f), "Голубое — не ниже низа");
            Assert.That(fill.Select(i => vertices[i].y).Max(), Is.EqualTo(3f + LevelZoneShape.Inflate).Within(1e-4f));
        }

        [Test]
        public void GroundBelowBottom_HasNoFill()
        {
            // Земля столбца (верхний блок на высоте 0) ниже низа 1: нижней клетке голубого (2) не на что лечь — его не наращивают.
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(1, 1), 4, 9, 1, _ => 1f, _ => true, vertices, dig, fill);
            Assert.That(fill, Is.Empty);

            // Земля на низу (верхний блок на высоте 1): голубое от земли до уровня.
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(1, 1), 4, 9, 1, _ => 2f, _ => true, vertices, dig, fill);
            Assert.That(fill.Select(i => vertices[i].y).Min(), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Mode_HidesVolumeItDoesNotWork()
        {
            // Столбец 0 выше уровня, столбец 1 ниже: в режиме «выкопать» нет голубого, в «нарастить» — жёлтого (#169).
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 1), 2, 9, 0, c => c.x == 0 ? 6f : 2f, _ => true, vertices, dig, fill,
                showDig: true, showFill: false);
            Assert.That(dig, Is.Not.Empty);
            Assert.That(fill, Is.Empty);

            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 1), 2, 9, 0, c => c.x == 0 ? 6f : 2f, _ => true, vertices, dig, fill,
                showDig: false, showFill: true);
            Assert.That(dig, Is.Empty);
            Assert.That(fill, Is.Not.Empty);
        }

        [Test]
        public void StepBetweenColumns_WallOnlyForTheTallerPart()
        {
            // Два столбца выше уровня: 5 и 4. Между ними видна только стенка высокого от 4 до 5.
            LevelZoneShape.Build(new Vector2Int(0, 0), new Vector2Int(2, 1), 2, 99, -99, c => c.x == 0 ? 5f : 4f, _ => true, vertices, dig, fill);

            // Верх 2 + внешние стенки: столбец 0 — 3 стороны, столбец 1 — 3 стороны + стенка высокого над низким = 9.
            Assert.That(dig.Count / 6, Is.EqualTo(2 + 6 + 1));
        }
    }
}
