#if UNITY_EDITOR
using System.Collections;
using Emberhold.Core;
using Emberhold.World;
using Emberhold.World.Sandbox;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.World
{
    public sealed class SlopeSandboxSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox/SlopeSandbox.unity";

        [UnityTest]
        public IEnumerator SandboxScene_BuildsPadsOnGroundLayer()
        {
            // Песочницы нет в Build Settings, поэтому грузим по пути через редактор.
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(Object.FindAnyObjectByType<SlopeSandbox>(), Is.Not.Null);
            Assert.That(view.World, Is.Not.Null, "Песочница не построила мир");
            Assert.That(view.World.SizeInChunks, Is.EqualTo(SlopeShowcase.SizeInChunks));
            Assert.That(view.World.DirtyCount, Is.Zero);

            // Луч в центр холма попадает в землю выше ровной части площадок.
            var hill = SlopeShowcase.PadCenter(4);
            var top = view.transform.TransformPoint(new Vector3(hill.x, 40f, hill.y));
            Assert.That(Physics.Raycast(top, Vector3.down, out var hit, 60f, Layers.GroundMask), Is.True);
            Assert.That(hit.point.y, Is.GreaterThan(6f));
        }
    }
}
#endif
