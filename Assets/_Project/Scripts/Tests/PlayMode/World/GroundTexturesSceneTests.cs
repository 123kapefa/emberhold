#if UNITY_EDITOR
using System.Collections;
using Emberhold.CameraRig;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Sandbox;
using Emberhold.World.Voxels;
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

        [UnityTest]
        public IEnumerator Hover_FloorOfOuterCornerSlope_PicksSlope()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            // На траве полный блок земли, над ним внешний угол ската: половина его верха — на полу ячейки.
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var below = new Vector3Int(2, GroundTexturesShowcase.BaseHeight, 2);
            var slope = below + Vector3Int.up;
            view.World.SetBlock(below, Block.Full(BlockMaterials.Dirt));
            view.World.SetBlock(slope, new Block(BlockMaterials.Dirt, new BlockShape(16, 0, 0, 0)));
            yield return null;
            Physics.SyncTransforms();

            Object.FindAnyObjectByType<CameraRigController>().enabled = false;
            var camera = UnityEngine.Camera.main;
            camera.transform.SetPositionAndRotation(view.transform.TransformPoint(new Vector3(2.5f, 20f, 2.5f)),
                Quaternion.Euler(90f, 0f, 0f));

            // Пол ската (дальний от высокого угла треугольник) и сам скат — оба выбирают скат.
            foreach (var point in new[] { new Vector3(2.8f, slope.y, 2.8f), new Vector3(2.2f, slope.y + 0.5f, 2.2f) })
            {
                var screen = camera.WorldToScreenPoint(view.transform.TransformPoint(point));
                Assert.That(editor.Pick(screen), Is.EqualTo((BlockHit?)new BlockHit(slope, Vector3Int.up)), point.ToString());
            }
        }
    }
}
#endif
