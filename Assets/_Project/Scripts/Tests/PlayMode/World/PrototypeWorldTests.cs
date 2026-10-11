using System.Collections;
using Emberhold.CameraRig;
using Emberhold.Core;
using Emberhold.World;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.World
{
    public sealed class PrototypeWorldTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_GeneratesValleyOnGroundLayer()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(view, Is.Not.Null, "В сцене Prototype нет VoxelWorldView");
            Assert.That(view.World, Is.Not.Null, "Мир не сгенерировался");
            Assert.That(view.World.DirtyCount, Is.Zero);
            Assert.That(view.transform.childCount, Is.GreaterThan(0));
            foreach (Transform chunk in view.transform)
                Assert.That(chunk.gameObject.layer, Is.EqualTo(Layers.Ground), chunk.name);

            // Луч сверху в центр долины попадает в землю.
            Assert.That(Physics.Raycast(new Vector3(0.5f, 200f, 0.5f), Vector3.down, out var hit, 400f, Layers.GroundMask), Is.True);
            Assert.That(hit.point.y, Is.GreaterThan(1f));

            // Фокус камеры встал на землю.
            var rig = Object.FindAnyObjectByType<CameraRigController>();
            Assert.That(rig.Target.Focus.y, Is.EqualTo(hit.point.y).Within(1.5f));
        }

        [UnityTest]
        public IEnumerator DiggingBlock_RebuildsChunkMesh()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var world = view.World;
            var center = new Vector3Int(world.SizeInBlocks.x / 2, 0, world.SizeInBlocks.z / 2);
            var chunk = view.transform.Find($"Chunk {center.x / ChunkData.Size},0,{center.z / ChunkData.Size}");
            Assert.That(chunk, Is.Not.Null);
            var mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
            var verticesBefore = mesh.vertexCount;

            // Вырыть пустоту внутри земли: вокруг неё появляются новые грани.
            world.SetBlock(center + Vector3Int.up * 2, Block.Air);
            yield return null;

            Assert.That(world.DirtyCount, Is.Zero);
            Assert.That(mesh.vertexCount, Is.Not.EqualTo(verticesBefore));
        }
    }
}
