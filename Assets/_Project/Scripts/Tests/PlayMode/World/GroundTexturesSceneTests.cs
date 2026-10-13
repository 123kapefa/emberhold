#if UNITY_EDITOR
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Sandbox;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.World
{
    public sealed class GroundTexturesSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox/GroundTextures.unity";

        [UnityTest]
        public IEnumerator Scene_BuildsShowcase_WithCampfireOnGround()
        {
            // Песочницы нет в Build Settings, поэтому грузим по пути через редактор.
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(Object.FindAnyObjectByType<GroundTexturesSandbox>(), Is.Not.Null);
            Assert.That(view.World, Is.Not.Null, "Макет не построил мир");
            Assert.That(view.World.SizeInChunks, Is.EqualTo(GroundTexturesShowcase.SizeInChunks));
            Assert.That(view.World.Surface, Is.Not.Null, "Нет гладкой поверхности");
            Assert.That(Object.FindAnyObjectByType<DayNightCycle>(), Is.Not.Null, "Нет смены дня и ночи");

            // Костёр стоит на земле в своей клетке.
            var fire = Object.FindAnyObjectByType<LightSource>().transform.position;
            var cell = GroundTexturesShowcase.Campfire;
            var expected = view.transform.TransformPoint(new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f));
            Assert.That(new Vector2(fire.x, fire.z), Is.EqualTo(new Vector2(expected.x, expected.z)));
            Assert.That(fire.y, Is.EqualTo(GroundTexturesShowcase.BaseHeight).Within(0.5f));

            // Луч на верх крутого склона попадает в землю на плато.
            var steep = GroundTexturesShowcase.SteepSlope.Rect;
            var top = view.transform.TransformPoint(new Vector3(steep.xMax + 4f, 45f, steep.center.y));
            Assert.That(Physics.Raycast(top, Vector3.down, out var hit, 60f, Layers.GroundMask), Is.True);
            Assert.That(hit.point.y, Is.GreaterThan(GroundTexturesShowcase.BaseHeight + 20f));
        }
    }
}
#endif
