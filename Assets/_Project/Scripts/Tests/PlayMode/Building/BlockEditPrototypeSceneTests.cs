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
        public IEnumerator PrototypeScene_DigLowersSurfaceBlockAndCollider()
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
            var before = view.World.GetBlock(hit.Block).Shape.Top;

            Assert.That(editor.Apply(hit), Is.True, "Копать");
            yield return null;
            Assert.That(view.World.GetBlock(hit.Block).Shape.Top, Is.LessThan(before));
            Assert.That(view.World.DirtyCount, Is.Zero, "Меш перестроен в том же кадре");

            // Луч теперь попадает ниже: коллайдер чанка обновился.
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var after, 400f, Layers.GroundMask), Is.True);
            Assert.That(after.point.y, Is.LessThan(raycast.point.y));
        }
    }
}
