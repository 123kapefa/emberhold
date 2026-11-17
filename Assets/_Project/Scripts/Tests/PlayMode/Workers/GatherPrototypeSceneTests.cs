using System.Collections;
using System.Linq;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Trees;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Bushes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    /// <summary>
    /// Кусты и сбор травы в сцене Prototype (#88): у костра в свету растут кусты, инструмент 5 по кусту ставит приказ собрать,
    /// зона сбора (H) берёт только кусты, рабочий срезает траву, куст остаётся голым.
    /// </summary>
    public sealed class GatherPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_BushesGrowInLight_ToolAndZoneGatherGrass()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var forest = Object.FindAnyObjectByType<ForestView>().Forest;
            var thicket = forest.Thicket;

            Assert.That(thicket.Bushes, Is.Not.Empty, "На карте растут кусты");
            var dropped = Object.FindAnyObjectByType<DroppedItemsView>();
            Assert.That(dropped.ShapeFor("grass"), Is.EqualTo(DroppedItemsView.ItemShape.Sheaf), "Трава на земле — снопы, не кубики");
            Assert.That(dropped.MaterialFor("grass"), Is.Not.SameAs(dropped.MaterialFor("stone")), "У травы свой материал, не камня");
            var lit = thicket.Bushes.Where(b => editor.CanEdit(b.Cell)).ToList();
            Assert.That(lit, Has.Count.GreaterThanOrEqualTo(3), "Несколько кустов у костра, в свету");
            Assert.That(lit.All(b => b.IsGrown), Is.True, "Сначала все зелёные");
            Assert.That(thicket.Bushes.All(b => forest.At(b.Column) == null), Is.True, "Куст не растёт в столбце дерева");

            // Клавиша H — инструмент «Зона сбора».
            var action = InputSystem.actions.FindAction("Building/ToolGatherZone");
            Assert.That(action, Is.Not.Null);
            Assert.That(action.bindings.Any(b => b.path == "<Keyboard>/h"), Is.True);

            // Инструмент 5 по кусту: приказ собрать, повторный клик снимает.
            var bush = lit[0];
            editor.Tool = BlockTool.DigOrder;
            Assert.That(editor.ApplyToBush(bush), Is.True);
            Assert.That(crew.Orders.At(bush.Cell)?.Kind, Is.EqualTo(WorkKind.Gather));
            Assert.That(crew.Orders.At(bush.Cell).Bush, Is.SameAs(bush));
            Assert.That(editor.ApplyToBush(bush), Is.True);
            Assert.That(crew.Orders.At(bush.Cell), Is.Null, "Повторный клик снимает приказ");

            // Зона сбора по клику на куст: черновик, после «Начать» — приказы только на кусты в круге.
            editor.Tool = BlockTool.GatherZone;
            Assert.That(editor.UseOnBush(bush), Is.True);
            var zone = crew.SelectedZone as GatherZone;
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.IsDraft, Is.True);
            Assert.That(zone.Covers(bush.Cell), Is.True);
            yield return null;
            Assert.That(zone.Assignment.Orders, Is.Empty, "Черновик приказов не ставит");
            Assert.That(crew.StartZone(zone), Is.True);
            yield return null;
            var covered = thicket.Bushes.Count(b => b.IsGrown && zone.Covers(b.Cell));
            Assert.That(zone.Assignment.Orders, Has.Count.EqualTo(covered));
            Assert.That(zone.Assignment.Orders.All(o => o.Kind == WorkKind.Gather), Is.True, "Деревья зона сбора не трогает");
            Assert.That(WorkZoneView.Label(zone), Does.StartWith("сбор травы"));

            // Рабочие срезают траву: куст голый, на земле или в руках трава, рабочий был сборщиком.
            var timeScale = Time.timeScale;
            Time.timeScale = 4f;
            var gatherer = false;
            try
            {
                var start = Time.realtimeSinceStartup;
                while (bush.IsGrown && Time.realtimeSinceStartup - start < 40f)
                {
                    gatherer |= crew.Workers.Any(w => w.Role == WorkerRole.Gatherer);
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = timeScale;
            }

            Assert.That(bush.IsGrown, Is.False, "Рабочий срезал траву");
            yield return null;
            if (dropped.Items.Items.Any(i => i.Drop.ItemId == "grass"))
            {
                var meshes = dropped.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).Where(m => m != null).ToList();
                Assert.That(meshes.Any(m => m.name == "Dropped Sheaf"), Is.True, "Трава на земле лежит снопом");
            }
            Assert.That(bush.IsPresent, Is.True, "Куст остался голым");
            Assert.That(gatherer, Is.True, "Срезает сборщик");
            Assert.That(crew.Zones.Contains(zone), Is.True, "Зона ждёт, пока куст отрастёт");
            yield return null;
            Assert.That(crew.Orders.At(bush.Cell), Is.Null, "На голый куст приказа нет");
        }
    }
}
