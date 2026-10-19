using Emberhold.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    public sealed class StoneHeapTests
    {
        private static void Simulate(StoneHeap heap, float seconds)
        {
            for (var t = 0f; t < seconds; t += 1f / 60f)
                heap.Step(1f / 60f);
        }

        [Test]
        public void Stones_FlyOutFallAndComeToRestInsideTheCell()
        {
            var heap = new StoneHeap(5);
            heap.Burst(new Vector3(0f, 0.5f, 0f), StoneHeap.PerUnit * 4);
            heap.Step(0.1f);
            var flying = 0;
            foreach (var stone in heap.Stones)
            {
                if (stone.Position.y > 0.5f)
                    flying++;
            }
            Assert.That(flying, Is.GreaterThan(0), "Камни сначала летят вверх");

            Simulate(heap, 5f);

            Assert.That(heap.Settled, Is.True, "Все камни легли");
            foreach (var stone in heap.Stones)
            {
                Assert.That(Mathf.Abs(stone.Position.x) + stone.Radius, Is.LessThanOrEqualTo(0.5001f));
                Assert.That(Mathf.Abs(stone.Position.z) + stone.Radius, Is.LessThanOrEqualTo(0.5001f));
                Assert.That(stone.Position.y, Is.GreaterThanOrEqualTo(stone.Radius - 1e-4f), "Не уходит в землю");
                Assert.That(stone.Position.y, Is.LessThan(0.6f), "Не висит в воздухе");
            }
        }

        [Test]
        public void Stones_DoNotSinkIntoEachOther()
        {
            var heap = new StoneHeap(9);
            heap.Burst(new Vector3(0f, 0.5f, 0f), StoneHeap.PerUnit * 4);
            Simulate(heap, 5f);
            var stones = heap.Stones;
            for (var i = 0; i < stones.Count; i++)
            for (var j = i + 1; j < stones.Count; j++)
            {
                var apart = Vector3.Distance(stones[i].Position, stones[j].Position);
                Assert.That(apart, Is.GreaterThan((stones[i].Radius + stones[j].Radius) * 0.8f), $"{i} и {j}");
            }
        }

        [Test]
        public void RemoveLast_TakesStonesAwayAndTheRestSettleAgain()
        {
            var heap = new StoneHeap(3);
            heap.Burst(new Vector3(0f, 0.5f, 0f), 6);
            Simulate(heap, 5f);
            heap.RemoveLast(StoneHeap.PerUnit);

            Assert.That(heap.Stones, Has.Count.EqualTo(3));
            Simulate(heap, 3f);
            Assert.That(heap.Settled, Is.True);
        }
    }
}
