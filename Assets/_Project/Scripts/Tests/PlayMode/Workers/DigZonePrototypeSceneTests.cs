using System;
using System.Collections;
using System.Linq;
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
    /// <summary>Зона копки в сцене Prototype (#136): инструмент 9 ставит прямоугольник с уровнем, выбирает и снимает зону.</summary>
    public sealed class DigZonePrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_DigZoneToolPlacesSelectsAndOrdersTopLayer()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();

            // Протяжка у костра: от блока (2.5, 2.5) до (4.5, 3.5) — прямоугольник 3×2, уровень на блок ниже начала.
            editor.Tool = BlockTool.DigZone;
            var from = SurfaceHit(view, 2.5f, 2.5f).Block;
            var to = SurfaceHit(view, 4.5f, 3.5f).Block;
            var zone = editor.ApplyDigZone(from, to);
            Assert.That(zone, Is.Not.Null);
            Assert.That(crew.SelectedZone, Is.SameAs(zone));
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(Mathf.Abs(to.x - from.x) + 1, Mathf.Abs(to.z - from.z) + 1)));
            Assert.That(zone.Level, Is.EqualTo(from.y - 1));

            // Клик без протяжки внутри зоны выбирает её, а не ставит вторую.
            crew.SelectedZone = null;
            Assert.That(editor.ApplyDigZone(to, to), Is.SameAs(zone));
            Assert.That(crew.Zones.All.OfType<LevelZone>().Count(), Is.EqualTo(1));

            // Новая зона — черновик: видна, но приказов нет, рабочие не идут.
            Assert.That(zone.IsDraft, Is.True);
            yield return null;
            Assert.That(zone.Assignment.Orders, Is.Empty, "Черновик приказов не ставит");
            Assert.That(WorkZoneView.Label(zone), Does.StartWith("черновик"));
            // Черновик, где копать нечего, сам не уходит.
            var level = zone.Level;
            crew.Zones.SetLevel(zone, from.y + 5, crew.Orders);
            yield return null;
            Assert.That(crew.Zones.Contains(zone), Is.True, "Черновик не уходит, даже если копать нечего");
            crew.Zones.SetLevel(zone, level, crew.Orders);

            // «Начать»: через кадр на верхних блоках прямоугольника выше уровня — приказы копать этой зоны.
            Assert.That(crew.StartZone(zone), Is.True);
            Assert.That(crew.StartZone(zone), Is.False, "Начать можно один раз");
            yield return null;
            Assert.That(zone.IsDraft, Is.False);
            Assert.That(zone.Assignment.Orders, Is.Not.Empty);
            foreach (var order in zone.Assignment.Orders)
            {
                Assert.That(order.Kind, Is.EqualTo(WorkKind.Dig));
                Assert.That(order.Cell.y, Is.GreaterThan(zone.Level));
                Assert.That(zone.Covers(order.Cell), Is.True);
            }

            // Вид зоны: жёлтый объём «выкопать» — одним мешем на зону, без рамок на каждом блоке.
            var volume = crew.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name.StartsWith("Dig Zone Volume"));
            Assert.That(volume, Is.Not.Null, "У зоны копки есть объём");
            Assert.That(volume.sharedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(volume.sharedMesh.GetTriangles(0), Is.Not.Empty, "Жёлтая часть: есть что выкопать");
            Assert.That(volume.GetComponent<MeshRenderer>().sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000), "Объём прозрачный");
            Assert.That(WorkZoneView.Label(zone), Does.Not.Contain("обычный"), "Приоритет на карте не показывается");

            // Уровень вверх до начального блока: копать нечего, приказы уходят.
            crew.Zones.SetLevel(zone, from.y + 5, crew.Orders);
            Assert.That(zone.Assignment.Orders, Is.Empty);
            yield return null;
            Assert.That(crew.Zones.All, Has.No.Member(zone), "Копать нечего — зона ушла");
            Assert.That(crew.SelectedZone, Is.Null);
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
