using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.World;
using Emberhold.World.Editing;
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
    }
}
