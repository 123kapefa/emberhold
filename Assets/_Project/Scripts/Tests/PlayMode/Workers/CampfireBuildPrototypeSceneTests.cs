using System;
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    public sealed class CampfireBuildPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_WorkersBuildNewCampfireThatLightsAfterFuel()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var zone = Object.FindAnyObjectByType<BuildZone>();
            var dropped = Object.FindAnyObjectByType<DroppedItemsView>();
            Assert.That(crew.BuildsCampfires, Is.True, "У рабочих в сцене назначен префаб костра");
            var main = zone.Sources[0];
            // Главный костёр полон: поленья пойдут только на новый.
            main.AddFuel(main.Fuel.Capacity);

            // Вне света чертёж костра не ставится.
            editor.Tool = BlockTool.Campfire;
            Assert.That(editor.Apply(SurfaceHit(view, main.transform.position.x + main.FullRadius + 4.5f, main.transform.position.z)), Is.False, "Вне света костёр не поставить");

            // Чертёж ближе к краю света, на первом подходящем месте (рельеф сцены неровный); повторный клик его снимает.
            var hit = default(BlockHit);
            var found = false;
            for (var r = 8.5f; r >= 3.5f && !found; r -= 1f)
            for (var a = 0; a < 8 && !found; a++)
            {
                var angle = a * Mathf.PI / 4f;
                var probe = SurfaceHit(view, main.transform.position.x + Mathf.Cos(angle) * r, main.transform.position.z + Mathf.Sin(angle) * r);
                var above = probe.Block + Vector3Int.up;
                if (!editor.CanEdit(above) || !WorkOrders.CanHoldCampfire(view.World, above) || crew.Orders.At(above) != null)
                    continue;
                hit = probe;
                found = true;
            }
            Assert.That(found, Is.True, "Не нашлось места под костёр в свету");
            var cell = hit.Block + Vector3Int.up;
            Assert.That(editor.Apply(hit), Is.True, $"Чертёж костра не поставился в {cell}");
            Assert.That(crew.Orders.At(cell)?.Kind, Is.EqualTo(WorkKind.Campfire));
            Assert.That(crew.Orders.At(cell).Needed, Is.EqualTo(6), "Костёр стоит 6 поленьев");
            Assert.That(crew.CampfireCost, Is.EqualTo(new ItemDrop("wood", 6)), "Цену в подсказке берём из той же таблицы");
            Assert.That(CostLabel.Amount(crew.CampfireCost.Value), Is.EqualTo("6 брёвен"));
            Assert.That(editor.Apply(hit), Is.True, "Повторный клик");
            Assert.That(crew.Orders.At(cell), Is.Null, "Повторный клик снимает чертёж");
            Assert.That(editor.Apply(hit), Is.True, "Чертёж снова");

            // Поддон и костёр не ставятся друг на друга.
            for (var dz = -1; dz <= 0; dz++)
            for (var dx = -1; dx <= 0; dx++)
                Assert.That(crew.TogglePallet(cell + new Vector3Int(dx, 0, dz)), Is.False, "Поддон на чертеже костра");
            var palletPlaced = false;
            for (var x = -8.5f; x <= -3.5f && !palletPlaced; x += 1f)
            {
                var origin = SurfaceHit(view, x, -4.5f).Block + Vector3Int.up;
                if (!crew.TogglePallet(origin))
                    continue;
                palletPlaced = true;
                Assert.That(crew.ToggleCampfire(origin), Is.False, "Костёр на поддоне");
                Assert.That(crew.ToggleCampfire(origin + new Vector3Int(1, 0, 1)), Is.False, "Костёр на поддоне");
                Assert.That(crew.TogglePallet(origin), Is.True, "Поддон убран");
            }
            Assert.That(palletPlaced, Is.True, "Не нашлось места под поддон для проверки");

            var pile = (main.transform.position + view.transform.TransformPoint(cell + new Vector3(0.5f, 0f, 0.5f))) * 0.5f;
            dropped.Items.Drop(view.World, SurfaceHit(view, pile.x, pile.z).Block + Vector3Int.up, new ItemDrop("wood", 7));

            var timeScale = Time.timeScale;
            Time.timeScale = 4f;
            LightSource fire;
            try
            {
                var start = Time.realtimeSinceStartup;
                while (crew.BuiltFires.Count == 0 && Time.realtimeSinceStartup - start < 60f)
                    yield return null;
                Assert.That(crew.BuiltFires, Has.Count.EqualTo(1), "Рабочие не построили костёр");
                fire = crew.BuiltFires[0];

                // Построенный костёр пустой и не горит, но уже в списке огней — рабочие понесут в него топливо.
                Assert.That(fire.Fuel, Is.Not.Null);
                Assert.That(fire.Fuel.Amount, Is.Zero, "Топливо вносится отдельно от постройки");
                Assert.That(fire.IsLit, Is.False);
                Assert.That(zone.Sources, Does.Contain(fire));
                Assert.That(fire.FullRadius, Is.EqualTo(main.FullRadius), "Настройки как у стартового");
                Assert.That(fire.GetComponent<CampfireFuelControls>(), Is.Null, "Подпись топлива и L — только у главного");
                Assert.That(main.GetComponent<CampfireFuelControls>(), Is.Not.Null);
                var local = view.transform.InverseTransformPoint(fire.transform.position);
                Assert.That(new Vector2(local.x - (cell.x + 0.5f), local.z - (cell.z + 0.5f)).magnitude, Is.LessThan(0.01f), "Костёр стоит в клетке чертежа");

                start = Time.realtimeSinceStartup;
                while (!fire.IsLit && Time.realtimeSinceStartup - start < 30f)
                    yield return null;
            }
            finally
            {
                Time.timeScale = timeScale;
            }

            Assert.That(fire.IsLit, Is.True, "Рабочие принесли полено — костёр загорелся");
            Assert.That(fire.Fuel.Amount, Is.GreaterThan(0f));
            Assert.That(main.IsLit, Is.True);
            Assert.That(zone.Contains(fire.transform.position + Vector3.right * 2f), Is.True);
        }

        /// <summary>Верхний блок под точкой сцены (x, z) и попадание по его верхней грани.</summary>
        private static BlockHit SurfaceHit(VoxelWorldView view, float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var raycast, 400f, Layers.GroundMask)
                || !view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                throw new InvalidOperationException($"Нет земли под ({x}, {z})");
            return new BlockHit(block, Vector3Int.up);
        }
    }
}
