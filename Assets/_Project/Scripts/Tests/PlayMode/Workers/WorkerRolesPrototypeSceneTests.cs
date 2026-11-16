using System;
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    /// <summary>Класс даёт задание, и облик сразу пересобирается под него; задание сняли — снова свободный (#161).</summary>
    public sealed class WorkerRolesPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_TaskGivesClassAndLookFollows()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(crew, Is.Not.Null, "В сцене Prototype нет WorkerCrew");
            var looks = new Transform[crew.Workers.Count];
            for (var i = 0; i < looks.Length; i++)
            {
                Assert.That(crew.Workers[i].Role, Is.EqualTo(WorkerRole.Free), "На старте все свободные");
                looks[i] = crew.transform.Find($"Worker {i + 1}");
                Assert.That(looks[i], Is.Not.Null);
            }

            // Приказ копать блок земли рядом с костром.
            var dig = crew.Orders.AddDig(SurfaceBlock(view, 3.5f, -0.5f));
            Assert.That(dig, Is.Not.Null);

            var index = -1;
            var start = Time.realtimeSinceStartup;
            while (index < 0 && Time.realtimeSinceStartup - start < 10f)
            {
                yield return null;
                for (var i = 0; i < crew.Workers.Count; i++)
                {
                    if (crew.Workers[i].Order == dig)
                        index = i;
                }
            }
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Приказ копать никто не взял");
            var worker = crew.Workers[index];
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Digger), "Взял копку — копатель");
            yield return null;
            yield return null;

            var name = $"Worker {index + 1}";
            Assert.That(looks[index] == null, Is.True, "Облик свободного убран");
            var digger = crew.transform.Find(name);
            Assert.That(digger, Is.Not.Null, "Облик копателя под тем же именем");
            Assert.That(Vertices(digger), Is.Not.EqualTo(Vertices(crew.transform.Find(OtherFree(crew, index)))),
                "У копателя другое снаряжение, чем у свободного");

            // Игрок снял приказ: снова свободный, облик тоже.
            crew.Orders.Cancel(dig);
            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Free));
            yield return null;
            yield return null;
            Assert.That(digger == null, Is.True, "Облик копателя убран");
            Assert.That(crew.transform.Find(name), Is.Not.Null);
            Assert.That(crew.transform.Find($"Worker {crew.Workers.Count + 1}"), Is.Null, "Лишних рабочих не появилось");
        }

        private static string OtherFree(WorkerCrew crew, int except)
        {
            for (var i = 0; i < crew.Workers.Count; i++)
            {
                if (i != except && crew.Workers[i].Role == WorkerRole.Free)
                    return $"Worker {i + 1}";
            }
            throw new InvalidOperationException("Нет второго свободного");
        }

        /// <summary>Сколько вершин во всех частях облика без ноши: у разных классов разное снаряжение.</summary>
        private static int Vertices(Transform body)
        {
            var count = 0;
            foreach (var filter in body.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh != null && filter.gameObject.name != "Carried Item")
                    count += filter.sharedMesh.vertexCount;
            }
            return count;
        }

        /// <summary>Верхний блок под точкой сцены (x, z).</summary>
        private static Vector3Int SurfaceBlock(VoxelWorldView view, float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var raycast, 400f, Layers.GroundMask)
                || !view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                throw new InvalidOperationException($"Нет земли под ({x}, {z})");
            return block;
        }
    }
}
