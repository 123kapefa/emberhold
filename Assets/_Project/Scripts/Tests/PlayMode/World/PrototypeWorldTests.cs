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

        [UnityTest]
        public IEnumerator RayOnSlope_FindsSlopeBlockByTriangle()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var world = view.World;
            var size = world.SizeInBlocks;
            var checkedSlopes = 0;
            for (var z = 0; z < size.z && checkedSlopes < 20; z += 3)
            for (var x = 0; x < size.x && checkedSlopes < 20; x += 3)
            {
                // Поверхность столбца — верхний непустой блок; берём только склоны.
                var y = size.y - 1;
                while (y >= 0 && world.GetBlock(new Vector3Int(x, y, z)).IsAir)
                    y--;
                var block = new Vector3Int(x, y, z);
                // Только склоны, закрывающие весь пол: у внешнего угла половина ячейки пустая,
                // и луч в её середину честно попадает в блок ниже.
                if (y < 0 || world.GetBlock(block).Shape.IsFlat || !world.GetBlock(block).Shape.CoversFloor)
                    continue;

                var top = view.transform.TransformPoint(new Vector3(x + 0.5f, size.y + 1, z + 0.5f));
                Assert.That(Physics.Raycast(top, Vector3.down, out var hit, size.y + 2, Layers.GroundMask), Is.True);
                Assert.That(view.TryGetBlock(hit.collider, hit.triangleIndex, out var picked), Is.True);
                Assert.That(picked, Is.EqualTo(block), $"Склон {block} {world.GetBlock(block)}");
                checkedSlopes++;
            }

            Assert.That(checkedSlopes, Is.GreaterThan(5), "В долине должны быть склоны");
        }
    }
}
