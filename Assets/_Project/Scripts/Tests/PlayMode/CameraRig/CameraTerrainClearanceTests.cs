using System.Collections;
using System.Collections.Generic;
using Emberhold.CameraRig;
using Emberhold.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.CameraRig
{
    /// <summary>Камера не уходит под рельеф (#145): блоки-заглушки на слое Ground вместо мира.</summary>
    public sealed class CameraTerrainClearanceTests
    {
        private const float Radius = 0.5f;
        private const float Clearance = 1f;
        private const float Probe = 200f;
        // Сцена от прошлых тестов может остаться загруженной, поэтому строим свои блоки далеко от её мира.
        private static readonly Vector3 Far = new(5000f, 0f, 5000f);

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                Object.Destroy(go);
            spawned.Clear();
        }

        private void Box(Vector3 min, Vector3 max)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.layer = Layers.Ground;
            go.transform.position = Far + (min + max) * 0.5f;
            go.transform.localScale = max - min;
            spawned.Add(go);
        }

        private static Vector3 Desired(Vector3 focus, float pitch, float distance) =>
            new CameraRigState(focus, 0f, pitch, distance).CameraPosition;

        private static Vector3 Resolve(Vector3 focus, Vector3 desired) =>
            CameraTerrainClearance.Resolve(Far + focus, Far + desired, Layers.GroundMask, Radius, Clearance, Probe) - Far;

        private static void AssertNotInGround(Vector3 local)
        {
            var position = Far + local;
            Assert.That(Physics.CheckSphere(position, Radius * 0.9f, Layers.GroundMask, QueryTriggerInteraction.Ignore),
                Is.False, $"Камера {position} внутри земли");
            var top = new Vector3(position.x, position.y + Probe, position.z);
            if (Physics.Raycast(top, Vector3.down, out var hit, Probe * 2f, Layers.GroundMask))
                Assert.That(position.y, Is.GreaterThanOrEqualTo(hit.point.y + Clearance - 1e-3f),
                    $"Камера {position} ниже земли под ней ({hit.point.y})");
        }

        [UnityTest]
        public IEnumerator FlatGround_CameraStaysWhereRigPutsIt()
        {
            Box(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f));
            yield return new WaitForFixedUpdate();

            var focus = Vector3.zero;
            var desired = Desired(focus, 50f, 30f);
            Assert.That(Vector3.Distance(Resolve(focus, desired), desired), Is.LessThan(1e-3f));
        }

        [UnityTest]
        public IEnumerator HillBehindFocus_CameraMovesInFrontOfIt()
        {
            Box(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f));
            Box(new Vector3(-20f, 0f, -14f), new Vector3(20f, 10f, -8f));
            yield return new WaitForFixedUpdate();

            var focus = Vector3.zero;
            var position = Resolve(focus, Desired(focus, 25f, 30f));
            Assert.That(position.z, Is.GreaterThan(-8f), "Камера не подъехала к фокусу перед холмом");
            AssertNotInGround(position);
        }

        [UnityTest]
        public IEnumerator LowCameraNearGround_IsLiftedAboveIt()
        {
            Box(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f));
            yield return new WaitForFixedUpdate();

            var focus = Vector3.zero;
            var position = Resolve(focus, new Vector3(0f, 0.2f, -20f));
            AssertNotInGround(position);
        }

        [UnityTest]
        public IEnumerator FocusInPit_CameraDoesNotGoIntoWall()
        {
            // Яма 6×6 глубиной 5: дно на y=-5, вокруг земля до y=0.
            Box(new Vector3(-50f, -6f, -50f), new Vector3(50f, -5f, 50f));
            Box(new Vector3(-50f, -5f, -50f), new Vector3(50f, 0f, -3f));
            Box(new Vector3(-50f, -5f, 3f), new Vector3(50f, 0f, 50f));
            Box(new Vector3(-50f, -5f, -3f), new Vector3(-3f, 0f, 3f));
            Box(new Vector3(3f, -5f, -3f), new Vector3(50f, 0f, 3f));
            yield return new WaitForFixedUpdate();

            var focus = new Vector3(0f, -5f, 0f);
            foreach (var pitch in new[] { 25f, 50f, 80f })
                AssertNotInGround(Resolve(focus, Desired(focus, pitch, 30f)));
        }

        [Test]
        public void LiftAbove_KeepsClearanceOnlyWhenTooLow()
        {
            Assert.That(CameraTerrainClearance.LiftAbove(0.2f, 0f, 1f), Is.EqualTo(1f));
            Assert.That(CameraTerrainClearance.LiftAbove(5f, 0f, 1f), Is.EqualTo(5f));
        }
    }
}
