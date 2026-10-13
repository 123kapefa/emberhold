#if UNITY_EDITOR
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Trees;
using Emberhold.World;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.World
{
    /// <summary>Сцена карты кампании (#60): мир 512×512 по карте, лес по локациям.</summary>
    public sealed class CampaignMapSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox/CampaignMap.unity";

        [UnityTest]
        public IEnumerator CampaignScene_BuildsWholeMapWithForestInLocations()
        {
            // Сцены нет в Build Settings, поэтому грузим по пути через редактор.
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(view.World, Is.Not.Null);
            Assert.That(view.World.SizeInBlocks, Is.EqualTo(new Vector3Int(512, 48, 512)));
            Assert.That(view.World.DirtyCount, Is.Zero);

            // Берёзовая долина (центр 112, 102) — дно на 14 блоках, хребет рядом выше 30.
            Assert.That(GroundHeight(view, 112, 102), Is.InRange(11f, 17f));
            Assert.That(GroundHeight(view, 20, 250), Is.GreaterThan(30f));

            var forest = Object.FindAnyObjectByType<ForestView>().Forest;
            Assert.That(forest, Is.Not.Null);
            var birches = 0;
            foreach (var tree in forest.Trees)
            {
                if (Vector2.Distance(new Vector2(tree.Cell.x, tree.Cell.z), new Vector2(112f, 102f)) < 30f)
                    birches++;
            }
            Assert.That(birches, Is.GreaterThan(10), "в Берёзовой долине растут деревья");
        }

        private static float GroundHeight(VoxelWorldView view, int x, int z)
        {
            var top = view.transform.TransformPoint(new Vector3(x + 0.5f, 60f, z + 0.5f));
            Assert.That(Physics.Raycast(top, Vector3.down, out var hit, 80f, Layers.GroundMask), Is.True, $"земля в {x},{z}");
            return hit.point.y - view.transform.position.y;
        }
    }
}
#endif
