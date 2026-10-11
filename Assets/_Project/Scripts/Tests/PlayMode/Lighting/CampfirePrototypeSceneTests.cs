using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Editing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Lighting
{
    public sealed class CampfirePrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_CampfireLimitsDiggingToItsLight()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var zone = Object.FindAnyObjectByType<BuildZone>();
            Assert.That(zone, Is.Not.Null, "В сцене Prototype нет BuildZone");
            Assert.That(zone.Sources.Count, Is.EqualTo(1), "Ровно один костёр");
            var campfire = zone.Sources[0];
            Assert.That(campfire.Radius, Is.GreaterThan(3f));

            // Костёр стоит на земле в центре долины.
            var center = campfire.transform.position;
            Assert.That(Physics.Raycast(new Vector3(center.x, 200f, center.z), Vector3.down, out var ground, 400f, Layers.GroundMask), Is.True);
            Assert.That(center.y, Is.EqualTo(ground.point.y).Within(0.01f), "Костёр стоит на земле");

            // Свет и граница радиуса.
            var light = campfire.GetComponentInChildren<Light>();
            Assert.That(light, Is.Not.Null);
            Assert.That(light.range, Is.EqualTo(campfire.Radius).Within(0.001f));
            var ring = campfire.GetComponentInChildren<LineRenderer>();
            Assert.That(ring, Is.Not.Null);
            Assert.That(ring.positionCount, Is.GreaterThan(16));
            Assert.That(Vector3.Distance(Flat(ring.GetPosition(0)), Flat(center)), Is.EqualTo(campfire.Radius).Within(0.01f));

            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();

            // Внутри света — можно, снаружи — нельзя, и удар снаружи ничего не делает.
            var inside = SurfaceBlock(view, center + new Vector3(campfire.Radius - 2f, 0f, 0f));
            Assert.That(editor.CanEdit(inside.Block), Is.True, "Блок внутри света");

            var outside = SurfaceBlock(view, center + new Vector3(campfire.Radius + 3f, 0f, 0f));
            Assert.That(editor.CanEdit(outside.Block), Is.False, "Блок вне света");
            var before = view.World.GetBlock(outside.Block);
            for (var i = 0; i < 10; i++)
                Assert.That(editor.Apply(outside), Is.False);
            Assert.That(view.World.GetBlock(outside.Block), Is.EqualTo(before), "Блок вне света не тронут");
            Assert.That(editor.Damage.Taken(outside.Block), Is.Zero, "Урона нет");

            // Погасший костёр (выключенный источник) больше не даёт строить.
            campfire.enabled = false;
            Assert.That(editor.CanEdit(inside.Block), Is.False);
            campfire.enabled = true;
            Assert.That(editor.CanEdit(inside.Block), Is.True);
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static BlockHit SurfaceBlock(VoxelWorldView view, Vector3 worldPoint)
        {
            Assert.That(Physics.Raycast(new Vector3(worldPoint.x, 200f, worldPoint.z), Vector3.down, out var raycast, 400f, Layers.GroundMask), Is.True);
            var normal = view.transform.InverseTransformDirection(raycast.normal);
            Assert.That(view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block), Is.True);
            return new BlockHit(block, BlockPicking.DominantAxis(normal));
        }
    }
}
