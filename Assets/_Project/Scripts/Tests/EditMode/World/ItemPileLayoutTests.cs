using Emberhold.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class ItemPileLayoutTests
    {
        [TestCase(1, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(6, 3)]
        [TestCase(12, 5)]
        public void LogBase_IsTheSmallestRowThatHoldsThePile(int count, int expected)
        {
            Assert.That(ItemPileLayout.LogBase(count), Is.EqualTo(expected));
        }

        [TestCase("wood")]
        [TestCase("stone")]
        public void Slots_StayInsideTheirCellAndAboveGround(string itemId)
        {
            for (var count = 1; count <= 12; count++)
            for (var i = 0; i < count; i++)
            {
                var slot = ItemPileLayout.Slot(itemId, i, count);
                var reach = ItemPileLayout.IsLog(itemId)
                    ? Mathf.Max(Mathf.Abs(slot.Position.x) + ItemPileLayout.LogRadius, Mathf.Abs(slot.Position.z) + ItemPileLayout.LogLength * 0.5f)
                    : Mathf.Max(Mathf.Abs(slot.Position.x), Mathf.Abs(slot.Position.z)) + ItemPileLayout.StoneRadius * 1.3f * slot.Scale;
                Assert.That(reach, Is.LessThan(0.5f), $"{itemId} {i} из {count}");
                Assert.That(slot.Position.y, Is.GreaterThan(0f), $"{itemId} {i} из {count}");
            }
        }

        [Test]
        public void Logs_DoNotOverlap()
        {
            for (var count = 2; count <= 12; count++)
            for (var a = 0; a < count; a++)
            for (var b = a + 1; b < count; b++)
            {
                var p = ItemPileLayout.Slot("wood", a, count).Position;
                var q = ItemPileLayout.Slot("wood", b, count).Position;
                var apart = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(q.x, q.y));
                Assert.That(apart, Is.GreaterThanOrEqualTo(ItemPileLayout.LogRadius * 1.99f), $"{a} и {b} из {count}");
            }
        }

        [Test]
        public void Logs_StackBottomRowFirst()
        {
            // 6 поленьев: 3 внизу, 2 на них, 1 сверху.
            var heights = new float[6];
            for (var i = 0; i < 6; i++)
                heights[i] = ItemPileLayout.Slot("wood", i, 6).Position.y;
            Assert.That(heights[0], Is.EqualTo(heights[2]).Within(1e-5f));
            Assert.That(heights[3], Is.GreaterThan(heights[0]));
            Assert.That(heights[5], Is.GreaterThan(heights[3]));
        }
    }
}
