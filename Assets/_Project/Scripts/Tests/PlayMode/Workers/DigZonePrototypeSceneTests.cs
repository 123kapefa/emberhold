using System;
using System.Collections;
using System.Linq;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    /// <summary>
    /// Зона копки в сцене Prototype (#136, #169): инструмент 9 ставит прямоугольник с уровнем, выбирает зону; она копает,
    /// наращивает и уходит, когда в её режиме делать нечего.
    /// </summary>
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

            // Ручка стороны больших x: навести, зажать и протянуть на два блока — зона шире на два столбца.
            var handles = new System.Collections.Generic.List<ZoneHandle>();
            ZoneHandleLayout.Build(zone, crew.GroundHeight, handles);
            var east = handles.Single(h => h.Kind == ZoneHandleKind.Edge && h.Dx == 1 && h.Dz == 0);
            var camera = Camera.main;
            Vector2 ToScreen(Vector3 local) => camera.WorldToScreenPoint(view.transform.TransformPoint(local));
            var width = zone.Size.x;
            Assert.That(crew.UpdateZoneHandles(camera, ToScreen(east.Position), false, false), Is.True, "Курсор над ручкой — клик её");
            Assert.That(crew.UpdateZoneHandles(camera, ToScreen(east.Position), true, true), Is.True);
            crew.UpdateZoneHandles(camera, ToScreen(east.Position + new Vector3(2f, 0f, 0f)), false, true);
            crew.UpdateZoneHandles(camera, ToScreen(east.Position + new Vector3(2f, 0f, 0f)), false, false);
            Assert.That(zone.Size.x, Is.EqualTo(width + 2), "Сторона протянута на два блока");
            Assert.That(crew.UpdateZoneHandles(camera, new Vector2(-100f, -100f), false, false), Is.False, "Мимо ручек — клик миру");
            crew.Zones.SetSize(zone, new Vector2Int(width, zone.Size.y), crew.Orders);

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

            // Выравнивание (#169), режим по умолчанию: уровень на блок выше земли, верх коробки — на уровне. Копать нечего
            // (бугры выше верха не трогают), и зона наращивает землёй; пока было бы что копать, наращивать бы не начали.
            Assert.That(zone.Mode, Is.EqualTo(LevelZoneMode.Level));
            crew.Zones.SetLevel(zone, from.y + 1, crew.Orders);
            crew.Zones.SetTop(zone, from.y + 1, crew.Orders);
            yield return null;
            Assert.That(zone.IsFilling, Is.True);
            Assert.That(zone.Assignment.Orders, Is.Not.Empty, "Чертежи земли до уровня");
            foreach (var order in zone.Assignment.Orders)
            {
                Assert.That(order.Kind, Is.EqualTo(WorkKind.Build));
                // Пустую клетку засыпают землёй, неполный блок дополняют до полного его же материалом (трава — травой).
                if (order.FillsExisting)
                    Assert.That(order.Block, Is.EqualTo(Block.Full(crew.World.GetBlock(order.Cell).Material)));
                else
                    Assert.That(order.Block, Is.EqualTo(LevelZone.FillBlock));
                Assert.That(order.Cell.y, Is.GreaterThan(zone.Bottom).And.LessThanOrEqualTo(zone.Level));
            }
            Assert.That(volume.sharedMesh.GetTriangles(1), Is.Not.Empty, "Голубая часть: есть что нарастить");
            Assert.That(WorkZoneView.Label(zone), Does.StartWith("выровнять до"));

            // Режим «выкопать»: чертежи сняты, голубое не рисуется; копать нечего — зона ушла.
            crew.Zones.SetMode(zone, LevelZoneMode.Dig, crew.Orders);
            Assert.That(zone.Assignment.Orders, Is.Empty);
            yield return null;
            Assert.That(crew.Zones.All, Has.No.Member(zone), "В режиме зоны делать нечего — зона ушла");
            Assert.That(crew.SelectedZone, Is.Null);
        }

        [UnityTest]
        public IEnumerator PrototypeScene_FillZoneLevelsDipOnSmoothGround()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var world = view.World;

            // Ямка на гладкой земле у костра (#78): в одном столбце верхний блок — половинка, в соседнем верхнего блока нет.
            var half = SurfaceHit(view, 2.5f, 2.5f).Block;
            var hole = SurfaceHit(view, 3.5f, 2.5f).Block;
            var level = Mathf.Max(half.y, hole.y);
            var material = world.GetBlock(half).Material;
            world.SetBlock(half, Block.Flat(material, BlockShape.MaxHeight / 2));
            editor.OnWorldChanged(half);
            world.SetBlock(hole, Block.Air);
            editor.OnWorldChanged(hole);

            var zone = crew.Zones.AddLevel(new Vector3Int(half.x, level + 1, half.z), new Vector3Int(hole.x, level + 1, hole.z),
                world: world, mode: LevelZoneMode.Fill);
            Assert.That(zone.Level, Is.EqualTo(level));
            yield return null;

            // Голубое — ровно то, что засыплют: половинку дополняют её же материалом, в пустую клетку кладут землю.
            var topUp = crew.Orders.At(half);
            Assert.That(topUp?.Assignment, Is.SameAs(zone.Assignment), "Неполный блок в ямке — тоже работа зоны");
            Assert.That(topUp.FillsExisting, Is.True);
            Assert.That(topUp.Block, Is.EqualTo(Block.Full(material)));
            Assert.That(crew.Orders.At(hole)?.Assignment, Is.SameAs(zone.Assignment));
            var volume = crew.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name.StartsWith("Dig Zone Volume"));
            Assert.That(volume, Is.Not.Null);
            Assert.That(volume.sharedMesh.GetTriangles(1), Is.Not.Empty, "Голубое над ямкой");

            var timeScale = Time.timeScale;
            Time.timeScale = 4f;
            try
            {
                var start = Time.realtimeSinceStartup;
                while (crew.Zones.Contains(zone) && Time.realtimeSinceStartup - start < 60f)
                    yield return null;
            }
            finally
            {
                Time.timeScale = timeScale;
            }

            Assert.That(crew.Zones.Contains(zone), Is.False, "Ямку засыпали — зона ушла");
            Assert.That(world.GetBlock(half), Is.EqualTo(Block.Full(material)), "Половинку дополнили до полного");
            for (var y = hole.y; y <= level; y++)
                Assert.That(world.GetBlock(new Vector3Int(hole.x, y, hole.z)).IsAir, Is.False, $"Клетка {y} в ямке засыпана");
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
