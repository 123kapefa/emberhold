using System;
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    /// <summary>Уровни приказов в сцене Prototype (#163): Shift+клик поднимает уровень, срочный приказ сразу берут.</summary>
    public sealed class OrderPriorityPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_RaisePriorityCyclesOwnOrderAndUrgentIsTaken()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(crew, Is.Not.Null);
            Assert.That(editor, Is.Not.Null);

            // Приказ копать в трёх блоках от костра ставится обычным.
            var hit = SurfaceHit(view, 3.5f, 0.5f);
            editor.Tool = BlockTool.DigOrder;
            Assert.That(editor.Apply(hit), Is.True);
            var order = crew.Orders.At(hit.Block);
            Assert.That(order, Is.Not.Null);
            Assert.That(order.Priority, Is.EqualTo(OrderPriority.Normal));

            // Чужой инструмент уровень не меняет; пустая клетка — тоже.
            editor.Tool = BlockTool.Blueprint;
            Assert.That(editor.RaisePriority(hit.Block), Is.False);
            editor.Tool = BlockTool.DigOrder;
            Assert.That(editor.RaisePriority(hit.Block + Vector3Int.up * 3), Is.False);

            // По кругу: обычный → высокий → срочный → обычный → высокий → срочный.
            Assert.That(editor.RaisePriority(hit.Block), Is.True);
            Assert.That(order.Priority, Is.EqualTo(OrderPriority.High));
            Assert.That(editor.RaisePriority(hit.Block), Is.True);
            Assert.That(order.Priority, Is.EqualTo(OrderPriority.Urgent));
            Assert.That(editor.RaisePriority(hit.Block), Is.True);
            Assert.That(order.Priority, Is.EqualTo(OrderPriority.Normal));
            editor.RaisePriority(hit.Block);
            editor.RaisePriority(hit.Block);
            Assert.That(order.Priority, Is.EqualTo(OrderPriority.Urgent));
            Assert.That(crew.Orders.Contains(order), Is.True, "Смена уровня не снимает приказ");

            // Срочный приказ кто-то из рабочих берёт сразу, чем бы он ни был занят.
            var start = Time.realtimeSinceStartup;
            while (order.Worker == null && crew.Orders.Contains(order) && Time.realtimeSinceStartup - start < 3f)
                yield return null;
            Assert.That(order.Worker != null || !crew.Orders.Contains(order), Is.True, "Срочный приказ никто не взял");
        }

        private static BlockHit SurfaceHit(VoxelWorldView view, float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var raycast, 400f, Layers.GroundMask)
                || !view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                throw new InvalidOperationException($"Нет земли под ({x}, {z})");
            return new BlockHit(block, Vector3Int.up);
        }
    }
}
