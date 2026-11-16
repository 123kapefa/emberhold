using System;
using System.Collections;
using System.Linq;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Trees;
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
    /// <summary>Зона рубки в сцене Prototype (#167): инструмент 8 ставит и выбирает зону, зона ставит приказы на деревья.</summary>
    public sealed class ChopZonePrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_ChopZoneToolPlacesSelectsAndOrdersTrees()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var forest = Object.FindAnyObjectByType<ForestView>().Forest;
            Assert.That(crew.HasForest, Is.True);

            // Зона у костра: ставится по клику инструментом 8 и сразу выбрана.
            editor.Tool = BlockTool.ChopZone;
            crew.NewZoneRadius = ChopZone.MaxRadius;
            var hit = SurfaceHit(view, 3.5f, 0.5f);
            var zone = editor.ApplyChopZone(hit);
            Assert.That(zone, Is.Not.Null);
            Assert.That(crew.SelectedZone, Is.SameAs(zone));
            Assert.That(zone.Radius, Is.EqualTo(ChopZone.MaxRadius));
            Assert.That(zone.Assignment.MaxWorkers, Is.EqualTo(Mathf.Min(WorkZones.DefaultMaxWorkers, crew.Workers.Count)));

            // Повторный клик в зоне её выбирает, а не ставит вторую.
            crew.SelectedZone = null;
            Assert.That(editor.ApplyChopZone(SurfaceHit(view, 4.5f, 1.5f)), Is.SameAs(zone));
            Assert.That(crew.Zones.All, Has.Count.EqualTo(1));
            Assert.That(crew.SelectedZone, Is.SameAs(zone));

            // Черновик приказов не ставит; после «Начать» через кадр на всех деревьях и пнях в круге — приказы зоны.
            Assert.That(zone.IsDraft, Is.True);
            yield return null;
            Assert.That(zone.Assignment.Orders, Is.Empty, "Черновик приказов не ставит");
            Assert.That(crew.StartZone(zone), Is.True);
            yield return null;
            var covered = forest.Trees.Count(t => t.IsStanding && zone.Covers(t.Cell))
                          + forest.Stumps.Count(s => s.IsPresent && zone.Covers(s.Cell));
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(covered));
            Assert.That(zone.Assignment.Orders.All(o => o.Kind == WorkKind.Chop), Is.True);

            // Снять зону: её приказы уходят.
            Assert.That(crew.RemoveZone(zone), Is.True);
            Assert.That(crew.SelectedZone, Is.Null);
            Assert.That(crew.Orders.Orders.Any(o => o.Assignment == zone.Assignment), Is.False);

            // Клик инструментом 8 прямо по дереву в свету тоже ставит зону с центром в его клетке, а не уходит в рубку одного дерева.
            var tree = forest.Trees.FirstOrDefault(t => t.IsStanding && editor.CanEdit(t.Cell));
            Assume.That(tree, Is.Not.Null, "В свету костра нет ни одного дерева");
            Assert.That(editor.UseOnTree(tree), Is.True, "Зона по клику на дерево");
            Assert.That(crew.SelectedZone, Is.Not.Null);
            Assert.That(crew.SelectedZone.Covers(tree.Cell), Is.True);
            Assert.That(crew.Orders.At(tree.Cell), Is.Null, "Клик не поставил приказ рубить одно дерево");
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
