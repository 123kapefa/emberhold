using System;
using System.Collections;
using Emberhold.CameraRig;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Trees;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Trees;
using Emberhold.World.Vegetation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Trees
{
    public sealed class ForestPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_FartherZonesGrowBiggerTrees()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var forest = Object.FindAnyObjectByType<ForestView>().Forest;
            var campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(forest, Is.Not.Null);
            Assert.That(forest.Trees.Count, Is.GreaterThan(50), "Лес посажен");

            // Каждое дерево — гранёная модель вида на шейдере деревьев.
            yield return null;
            var renderers = Object.FindAnyObjectByType<ForestView>().GetComponentsInChildren<MeshRenderer>();
            Assert.That(renderers.Length, Is.EqualTo(forest.Trees.Count), "У каждого дерева своя модель");
            Assert.That(renderers[0].sharedMaterial.shader.name, Is.EqualTo("Emberhold/World/Tree"));
            Assert.That(renderers[0].GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));

            var fire = view.transform.InverseTransformPoint(campfire.transform.position);
            var nearYield = 0;
            var farYield = 0;
            foreach (var tree in forest.Trees)
            {
                var distance = new Vector2(tree.Cell.x + 0.5f - fire.x, tree.Cell.z + 0.5f - fire.z).magnitude;
                Assert.That(distance, Is.GreaterThan(5f), "У костра место свободно");
                if (distance < 20f)
                    nearYield = Mathf.Max(nearYield, tree.Species.WoodYield);
                else if (distance > 40f)
                    farYield = Mathf.Max(farYield, tree.Species.WoodYield);
            }
            Assert.That(nearYield, Is.GreaterThan(0), "У костра есть что рубить");
            var lit = 0;
            foreach (var tree in forest.Trees)
            {
                if (Vector2.Distance(tree.Position, new Vector2(fire.x, fire.z)) <= campfire.FullRadius)
                    lit++;
            }
            Assert.That(lit, Is.GreaterThanOrEqualTo(3), "Край стартовой рощи в круге света костра");
            Assert.That(farYield, Is.GreaterThan(nearYield), "Дальше растут деревья крупнее");
        }

        [UnityTest]
        public IEnumerator PrototypeScene_GroundIsCoveredAroundCamera()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var view = Object.FindAnyObjectByType<GroundCoverView>();
            Assert.That(view, Is.Not.Null, "В сцене есть мелочь на земле");
            var cover = view.Cover;
            Assert.That(cover, Is.Not.Null);
            Assert.That(cover.Count, Is.GreaterThan(1000), "Мелочь посажена ковром");
            var forest = Object.FindAnyObjectByType<ForestView>().Forest;
            foreach (var patch in cover.Patches)
                Assert.That(forest.At(new Vector2Int(patch.Cell.x, patch.Cell.z)), Is.Null, "Не в столбце дерева");
            Assert.That(view.ShownRegions, Is.GreaterThan(0), "У камеры участки нарисованы");
        }

        [UnityTest]
        public IEnumerator PrototypeScene_GroundCoverHidesWhenCameraIsFar()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var view = Object.FindAnyObjectByType<GroundCoverView>();
            Assert.That(view.ThinningLevel, Is.EqualTo(GroundCoverThinning.Steps), "У земли видна вся мелочь");
            Assert.That(view.ShownRegions, Is.GreaterThan(0));

            // Камеру ведём руками: риг иначе вернёт её на место.
            foreach (var rig in Object.FindObjectsByType<CameraRigController>())
                rig.enabled = false;
            var camera = Camera.main.transform;
            var start = camera.position;
            camera.position = start + Vector3.up * 400f;
            yield return null;
            yield return null;
            Assert.That(view.ThinningLevel, Is.EqualTo(0), "Издалека мелочь скрыта");
            Assert.That(view.ShownRegions, Is.EqualTo(0), "И её участки убраны");

            camera.position = start;
            for (var i = 0; i < 60 && view.ShownRegions == 0; i++)
                yield return null;
            Assert.That(view.ThinningLevel, Is.EqualTo(GroundCoverThinning.Steps), "Вернулись — мелочь снова вся");
            Assert.That(view.ShownRegions, Is.GreaterThan(0), "И участки снова нарисованы");
        }

        [UnityTest]
        public IEnumerator PrototypeScene_WorkersChopTreeAndFeedCampfire()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var forestView = Object.FindAnyObjectByType<ForestView>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var dropped = Object.FindAnyObjectByType<DroppedItemsView>();
            var campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            var forest = forestView.Forest;

            // Своя берёза в четырёх блоках от костра: лес вокруг зависит от seed.
            var ground = SurfaceBlock(view, campfire.transform.position + new Vector3(4f, 0f, 0.5f));
            var cell = ground + Vector3Int.up;
            var column = new Vector2Int(cell.x, cell.z);
            if (forest.At(column) is { } old)
                forest.Fell(view.World, old);
            while (dropped.Items.Items.Count > 0)
                dropped.Items.Remove(dropped.Items.Items[0]);
            var birch = TreeSpecies.Create("Берёза", 75f, 3);
            try
            {
                var tree = forest.Plant(cell, birch);
                Assert.That(tree, Is.Not.Null);

                var start = campfire.Fuel.Amount;
                Assert.That(start, Is.LessThan(campfire.Fuel.Capacity - 1f));
                Assert.That(HasFuelOrder(crew), Is.True, "Костёр ждёт поленья");

                editor.Tool = BlockTool.DigOrder;
                Assert.That(editor.ApplyToTree(tree), Is.True);
                Assert.That(crew.Orders.At(cell)?.Kind, Is.EqualTo(WorkKind.Chop));
                Assert.That(editor.ApplyToTree(tree), Is.True);
                Assert.That(crew.Orders.At(cell), Is.Null, "Повторный клик снимает приказ");
                Assert.That(editor.ApplyToTree(tree), Is.True);

                var timeScale = Time.timeScale;
                Time.timeScale = 4f;
                try
                {
                    var began = Time.realtimeSinceStartup;
                    while ((tree.IsStanding || campfire.Fuel.Amount < start + 3f - 0.01f) && Time.realtimeSinceStartup - began < 40f)
                        yield return null;
                }
                finally
                {
                    Time.timeScale = timeScale;
                }

                Assert.That(tree.IsStanding, Is.False, "Рабочие срубили берёзу");
                Assert.That(campfire.Fuel.Amount, Is.EqualTo(start + 3f).Within(0.01f), "Все три полена в костре");
                Assert.That(dropped.Items.Items, Is.Empty);
            }
            finally
            {
                Object.Destroy(birch);
            }
        }

        [UnityTest]
        public IEnumerator PrototypeScene_CampfireBurnsFasterEveryNight()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            var campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            Assert.That(campfire.Night, Is.EqualTo(1));
            var first = campfire.BurnPerSecond;
            Assert.That(first, Is.GreaterThan(0f));

            // Четыре фазы — следующие сутки, вторая ночь.
            for (var i = 0; i < 4; i++)
                cycle.SkipToNextPhase();
            Assert.That(campfire.Night, Is.EqualTo(2));
            Assert.That(campfire.BurnPerSecond, Is.EqualTo(first * 1.25f).Within(1e-5f), "Вторая ночь на четверть дороже");
        }

        [UnityTest]
        public IEnumerator PrototypeScene_FuelOrderGoesAwayWithFire()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            Assert.That(HasFuelOrder(crew), Is.True, "Костёр ждёт поленья");
            var order = FuelOrder(crew);
            Assert.That(order.Cell.y, Is.GreaterThan(campfire.transform.position.y - 0.5f), "Задача в пустой клетке над землёй");

            campfire.enabled = false;
            yield return null;
            Assert.That(HasFuelOrder(crew), Is.False, "У выключенного огня задачи топлива нет");

            campfire.enabled = true;
            yield return null;
            Assert.That(HasFuelOrder(crew), Is.True, "Огонь вернулся — задача снова есть");
        }

        private static WorkOrder FuelOrder(WorkerCrew crew)
        {
            foreach (var order in crew.Orders.Orders)
            {
                if (order.Kind == WorkKind.Fuel)
                    return order;
            }
            return null;
        }

        private static bool HasFuelOrder(WorkerCrew crew)
        {
            foreach (var order in crew.Orders.Orders)
            {
                if (order.Kind == WorkKind.Fuel)
                    return true;
            }
            return false;
        }

        /// <summary>Верхний блок под точкой сцены.</summary>
        private static Vector3Int SurfaceBlock(VoxelWorldView view, Vector3 point)
        {
            if (!Physics.Raycast(new Vector3(point.x, 200f, point.z), Vector3.down, out var raycast, 400f, Layers.GroundMask)
                || !view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                throw new InvalidOperationException($"Нет земли под {point}");
            return block;
        }
    }
}
