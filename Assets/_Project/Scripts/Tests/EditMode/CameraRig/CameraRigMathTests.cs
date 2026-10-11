using Emberhold.CameraRig;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.CameraRig
{
    public sealed class CameraRigMathTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void CameraPosition_LooksAtFocusFromDistance()
        {
            var state = new CameraRigState(new Vector3(5f, 2f, -3f), 30f, 50f, 20f);

            var toFocus = state.Focus - state.CameraPosition;

            Assert.That(toFocus.magnitude, Is.EqualTo(20f).Within(Eps));
            Assert.That(Vector3.Angle(toFocus, state.Rotation * Vector3.forward), Is.LessThan(0.01f));
            Assert.That(state.CameraPosition.y, Is.GreaterThan(state.Focus.y));
        }

        [Test]
        public void Rotating_KeepsFocusAndDistance()
        {
            var a = new CameraRigState(new Vector3(1f, 0f, 1f), 0f, 45f, 10f);
            var b = a;
            b.Yaw = 120f;

            Assert.That(Vector3.Distance(b.CameraPosition, b.Focus), Is.EqualTo(10f).Within(Eps));
            Assert.That(Vector3.Distance(a.CameraPosition, b.CameraPosition), Is.GreaterThan(1f));
        }

        [Test]
        public void PanOffset_ForwardFollowsYaw_AndStaysOnGround()
        {
            var offset = CameraRigMath.PanOffset(new Vector2(0f, 1f), 90f, 2f);

            Assert.That(offset.x, Is.EqualTo(2f).Within(Eps));
            Assert.That(offset.y, Is.EqualTo(0f).Within(Eps));
            Assert.That(offset.z, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void WorldUnitsPerPixel_MatchesVisibleHeight()
        {
            // При FOV 90° видимая высота на дистанции 10 равна 20.
            Assert.That(CameraRigMath.WorldUnitsPerPixel(10f, 90f, 1000f), Is.EqualTo(0.02f).Within(Eps));
            Assert.That(CameraRigMath.WorldUnitsPerPixel(10f, 60f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void DragPanOffset_StretchesVerticalByPitch()
        {
            var sideways = CameraRigMath.DragPanOffset(new Vector2(10f, 0f), 0f, 30f, 1f);
            var forward = CameraRigMath.DragPanOffset(new Vector2(0f, 10f), 0f, 30f, 1f);

            Assert.That(sideways.x, Is.EqualTo(10f).Within(Eps));
            Assert.That(forward.z, Is.EqualTo(20f).Within(Eps));
        }

        [Test]
        public void ScaledPanSpeed_GrowsWithDistance()
        {
            Assert.That(CameraRigMath.ScaledPanSpeed(10f, 60f, 30f), Is.EqualTo(20f).Within(Eps));
        }

        [Test]
        public void Zoom_ScrollUpMovesCloser()
        {
            Assert.That(CameraRigMath.Zoom(30f, 1f, 0.1f, 5f, 80f), Is.EqualTo(27f).Within(Eps));
            Assert.That(CameraRigMath.Zoom(30f, -1f, 0.1f, 5f, 80f), Is.GreaterThan(30f));
        }

        [Test]
        public void Zoom_IsClampedToMinAndMax()
        {
            Assert.That(CameraRigMath.Zoom(6f, 10f, 0.2f, 5f, 80f), Is.EqualTo(5f));
            Assert.That(CameraRigMath.Zoom(70f, -10f, 0.2f, 5f, 80f), Is.EqualTo(80f));
        }

        [Test]
        public void NormalizeScroll_LimitsToOneStepPerFrame()
        {
            Assert.That(CameraRigMath.NormalizeScroll(120f), Is.EqualTo(1f));
            Assert.That(CameraRigMath.NormalizeScroll(-120f), Is.EqualTo(-1f));
            Assert.That(CameraRigMath.NormalizeScroll(0.3f), Is.EqualTo(0.3f).Within(Eps));
        }

        [Test]
        public void ClampToBounds_KeepsHeight()
        {
            var clamped = CameraRigMath.ClampToBounds(new Vector3(500f, 7f, -500f), new Vector2(-10f, -10f), new Vector2(10f, 10f));

            Assert.That(clamped, Is.EqualTo(new Vector3(10f, 7f, -10f)));
        }

        [Test]
        public void Damp_IsFrameRateIndependent()
        {
            var from = new CameraRigState(Vector3.zero, 0f, 30f, 10f);
            var to = new CameraRigState(new Vector3(10f, 0f, 0f), 90f, 60f, 40f);

            var oneFrame = CameraRigMath.Damp(from, to, 12f, 1f / 30f);

            var twoFrames = from;
            for (var i = 0; i < 2; i++)
                twoFrames = CameraRigMath.Damp(twoFrames, to, 12f, 1f / 60f);

            Assert.That(Vector3.Distance(oneFrame.Focus, twoFrames.Focus), Is.LessThan(Eps));
            Assert.That(oneFrame.Yaw, Is.EqualTo(twoFrames.Yaw).Within(Eps));
            Assert.That(oneFrame.Pitch, Is.EqualTo(twoFrames.Pitch).Within(Eps));
            Assert.That(oneFrame.Distance, Is.EqualTo(twoFrames.Distance).Within(Eps));
        }

        [Test]
        public void Damp_WithoutSharpnessSnapsToTarget()
        {
            var to = new CameraRigState(Vector3.one, 10f, 40f, 25f);

            var result = CameraRigMath.Damp(default, to, 0f, 0.016f);

            Assert.That(result.Focus, Is.EqualTo(to.Focus));
            Assert.That(result.Distance, Is.EqualTo(to.Distance));
        }
    }
}
