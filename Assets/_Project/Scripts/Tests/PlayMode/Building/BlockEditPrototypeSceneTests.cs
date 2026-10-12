using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Building
{
    public sealed class BlockEditPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_DigBreaksSurfaceBlockAndUpdatesCollider()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            Assert.That(editor, Is.Not.Null, "В сцене Prototype нет BlockEditController");
            Assert.That(editor.enabled, Is.True);
            var view = Object.FindAnyObjectByType<VoxelWorldView>();

            // Блок на поверхности в центре долины — тот, в который попадает луч сверху.
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var raycast, 400f, Layers.GroundMask), Is.True);
            var local = view.transform.InverseTransformPoint(raycast.point);
            var normal = view.transform.InverseTransformDirection(raycast.normal);
            Assert.That(BlockPicking.TryPick(view.World, local, normal, out var hit), Is.True);
            // Блок по треугольнику меша совпадает с блоком по точке (на ровном месте оба способа точны).
            Assert.That(view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var byTriangle), Is.True);
            Assert.That(byTriangle, Is.EqualTo(hit.Block));
            var before = view.World.GetBlock(hit.Block);

            // Копание: блок держится до последнего удара и исчезает целиком, меш перестроен в том же кадре.
            Assert.That(editor.Apply(hit), Is.False, "Первый удар");
            Assert.That(view.World.GetBlock(hit.Block), Is.EqualTo(before));
            Assert.That(editor.Damage.Taken(hit.Block), Is.GreaterThan(0f));
            var hits = 1;
            while (!editor.Apply(hit))
            {
                hits++;
                Assert.That(hits, Is.LessThan(20), "Блок не разрушается");
            }
            yield return null;
            Assert.That(view.World.GetBlock(hit.Block).IsAir, Is.True);
            Assert.That(view.World.DirtyCount, Is.Zero, "Меш перестроен в том же кадре");

            // Луч теперь попадает ниже: коллайдер чанка обновился.
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var after, 400f, Layers.GroundMask), Is.True);
            Assert.That(after.point.y, Is.LessThan(raycast.point.y));

            // У каждого чанка постоянный набор подмешей и материалов, сколько бы раз его ни перестраивали.
            foreach (Transform chunk in view.transform)
            {
                var subMeshes = chunk.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                Assert.That(chunk.GetComponent<MeshRenderer>().sharedMaterials.Length, Is.EqualTo(subMeshes), chunk.name);
                Assert.That(subMeshes, Is.EqualTo(Emberhold.World.Voxels.BlockMaterials.Count - 1), chunk.name);
            }
        }

        [UnityTest]
        public IEnumerator PrototypeScene_BuildPlacesStairsOnSurface()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var raycast, 400f, Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var surface), Is.True);
            // Ставим на верх поверхности: ячейка над ней должна быть свободна.
            var hit = new BlockHit(surface, Vector3Int.up);
            var above = surface + Vector3Int.up;
            Assume.That(view.World.GetBlock(above).IsAir, Is.True);

            editor.Tool = BlockTool.Build;
            while (editor.Selection.Form != BuildForm.Stairs)
                editor.Selection.NextForm();
            editor.Selection.Rotate();

            // Верх неполной травы досыпается, только если материал тот же; наш — не трава, значит, встанет сверху.
            Assert.That(editor.Apply(hit), Is.True);
            yield return null;
            Assert.That(view.World.GetBlock(above).Shape, Is.EqualTo(BlockShape.Catalog(BlockForm.Stairs, 1)));
            Assert.That(view.World.DirtyCount, Is.Zero, "Меш перестроен в том же кадре");

            // Луч сверху теперь попадает в лестницу.
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var onStairs, 400f, Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(onStairs.collider, onStairs.triangleIndex, out var picked), Is.True);
            Assert.That(picked, Is.EqualTo(above));
        }

        [UnityTest]
        public IEnumerator PrototypeScene_BlocksWithoutSupportFall()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var raycast, 400f, Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var surface), Is.True);
            var world = view.World;
            var bottom = surface + Vector3Int.up;
            var middle = bottom + Vector3Int.up;
            var top = middle + Vector3Int.up;
            var arm = top + Vector3Int.right;
            Assume.That(world.GetBlock(bottom).IsAir && world.GetBlock(middle).IsAir && world.GetBlock(top).IsAir && world.GetBlock(arm).IsAir, Is.True);

            // Столб из трёх полных блоков и вынос вбок от его верха.
            editor.Tool = BlockTool.Build;
            Assert.That(editor.Apply(new BlockHit(surface, Vector3Int.up)), Is.True);
            Assert.That(editor.Apply(new BlockHit(bottom, Vector3Int.up)), Is.True);
            Assert.That(editor.Apply(new BlockHit(middle, Vector3Int.up)), Is.True);
            Assert.That(editor.Apply(new BlockHit(top, Vector3Int.right)), Is.True);
            Assert.That(editor.LastFalls, Is.Empty, "Вынос держится за столб");
            var placed = world.GetBlock(arm);
            Assert.That(placed.IsAir, Is.False);

            // Убрали середину: верх и вынос потеряли опору и упали.
            editor.Tool = BlockTool.Remove;
            Assert.That(editor.Apply(new BlockHit(middle, Vector3Int.right)), Is.True);
            yield return null;
            Assert.That(editor.LastFalls, Is.Not.Empty);
            Assert.That(world.GetBlock(top).IsAir, Is.True);
            Assert.That(world.GetBlock(arm).IsAir, Is.True);
            Assert.That(world.GetBlock(middle).IsAir, Is.False, "Верх столба лёг на основание");
            Assert.That(view.World.DirtyCount, Is.Zero, "Меш перестроен в том же кадре");
        }

        [UnityTest]
        public IEnumerator PrototypeScene_FallingBlockFliesDownWhileWorldIsAlreadyUpdated()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var fallView = Object.FindAnyObjectByType<BlockFallView>();
            Assert.That(fallView, Is.Not.Null, "В сцене Prototype нет BlockFallView");
            Assert.That(fallView.enabled, Is.True);
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var raycast, 400f, Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var surface), Is.True);
            var world = view.World;
            var bottom = surface + Vector3Int.up;
            var middle = bottom + Vector3Int.up;
            var top = middle + Vector3Int.up;
            Assume.That(world.GetBlock(bottom).IsAir && world.GetBlock(middle).IsAir && world.GetBlock(top).IsAir, Is.True);

            // Столб из трёх блоков; убираем середину — верх падает на основание.
            editor.Tool = BlockTool.Build;
            Assert.That(editor.Apply(new BlockHit(surface, Vector3Int.up)), Is.True);
            Assert.That(editor.Apply(new BlockHit(bottom, Vector3Int.up)), Is.True);
            Assert.That(editor.Apply(new BlockHit(middle, Vector3Int.up)), Is.True);
            editor.Tool = BlockTool.Remove;
            Assert.That(editor.Apply(new BlockHit(middle, Vector3Int.right)), Is.True);
            Assert.That(editor.LastFalls, Has.Count.EqualTo(1));
            yield return null;

            // Данные уже новые, а картинка — ещё в полёте: чанк прячет блок в новой клетке, коллайдер его видит.
            Assert.That(world.GetBlock(middle).IsAir, Is.False);
            Assert.That(world.GetBlock(top).IsAir, Is.True);
            Assert.That(fallView.FlightCount, Is.EqualTo(1));
            Assert.That(view.IsHidden(middle), Is.True);
            var column = view.transform.TransformPoint(new Vector3(surface.x + 0.5f, 200f, surface.z + 0.5f));
            Assert.That(Physics.Raycast(column, Vector3.down, out var onFallen, 400f, Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(onFallen.collider, onFallen.triangleIndex, out var picked), Is.True);
            Assert.That(picked, Is.EqualTo(middle), "Луч мыши уже попадает в блок на новом месте");

            // После падения и подскока блок снова рисует чанк.
            var wait = fallView.Duration(1) + 0.2f;
            var until = Time.time + wait;
            while (Time.time < until)
                yield return null;
            Assert.That(fallView.FlightCount, Is.Zero);
            Assert.That(view.IsHidden(middle), Is.False);
            Assert.That(view.World.DirtyCount, Is.Zero, "Меш перестроен в том же кадре");
        }
    }
}
