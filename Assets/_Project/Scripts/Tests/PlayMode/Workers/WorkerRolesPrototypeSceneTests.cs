using System.Collections;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Workers
{
    /// <summary>Роль рабочего меняется во время игры, и облик сразу пересобирается под неё (#161).</summary>
    public sealed class WorkerRolesPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_RoleChangeRebuildsLookAtOnce()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            Assert.That(crew, Is.Not.Null, "В сцене Prototype нет WorkerCrew");
            var index = -1;
            for (var i = 0; i < crew.Workers.Count && index < 0; i++)
            {
                if (crew.Workers[i].Role == WorkerRole.Free)
                    index = i;
            }
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "В стартовом составе есть свободный");
            var worker = crew.Workers[index];
            var name = $"Worker {index + 1}";
            var before = crew.transform.Find(name);
            Assert.That(before, Is.Not.Null);
            var meshesBefore = Meshes(before);
            var place = before.position;

            worker.Role = WorkerRole.Digger;
            yield return null;
            yield return null;

            Assert.That(worker.Role, Is.EqualTo(WorkerRole.Digger));
            Assert.That(before == null, Is.True, "Старый облик убран");
            var after = crew.transform.Find(name);
            Assert.That(after, Is.Not.Null, "Облик пересобран под тем же именем");
            Assert.That(Vector3.Distance(after.position, place), Is.LessThan(0.5f), "На том же месте");
            Assert.That(Meshes(after), Is.Not.EqualTo(meshesBefore), "У копателя другой облик (инструмент, капюшон)");
            Assert.That(crew.transform.Find($"Worker {crew.Workers.Count + 1}"), Is.Null, "Лишних рабочих не появилось");
        }

        /// <summary>Сколько вершин во всех частях облика: у разных ролей разное снаряжение.</summary>
        private static int Meshes(Transform body)
        {
            var count = 0;
            foreach (var filter in body.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh != null && filter.gameObject.name != "Carried Item")
                    count += filter.sharedMesh.vertexCount;
            }
            return count;
        }
    }
}
