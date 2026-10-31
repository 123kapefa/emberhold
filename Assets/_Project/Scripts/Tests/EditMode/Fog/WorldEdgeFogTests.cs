using Emberhold.CameraRig;
using Emberhold.Gameplay.Fog;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Fog
{
    /// <summary>Море тумана за краем мира и камера кампании, которая не уезжает к краю (#102).</summary>
    public sealed class WorldEdgeFogTests
    {
        private const string EdgeFogPath = "Assets/_Project/Data/Fog/WorldEdgeFog.asset";
        private const string CampaignCameraPath = "Assets/_Project/Data/CameraRig/CampaignCameraRigSettings.asset";

        [Test]
        public void DistanceOutside_IsNegativeInside_ZeroOnTheEdge_DistanceOutside()
        {
            var rect = new Rect(-256f, -256f, 512f, 512f);
            Assert.That(WorldEdgeFog.DistanceOutside(rect, Vector3.zero), Is.EqualTo(-256f));
            Assert.That(WorldEdgeFog.DistanceOutside(rect, new Vector3(-250f, 40f, 0f)), Is.EqualTo(-6f), "внутри у края");
            Assert.That(WorldEdgeFog.DistanceOutside(rect, new Vector3(256f, 0f, 10f)), Is.EqualTo(0f), "на краю");
            Assert.That(WorldEdgeFog.DistanceOutside(rect, new Vector3(0f, 0f, 266f)), Is.EqualTo(10f), "за краем");
            Assert.That(WorldEdgeFog.DistanceOutside(rect, new Vector3(259f, 0f, 260f)), Is.EqualTo(5f).Within(1e-4f), "за углом");
        }

        [Test]
        public void EdgeFog_CoversTheFootOfTheOuterSlope_NotTheMountainsOrTheMap()
        {
            var settings = AssetDatabase.LoadAssetAtPath<FogSettings>(EdgeFogPath);
            Assert.That(settings, Is.Not.Null);
            var look = settings.LookAt(0f);
            const float inset = 12f;
            var rect = new Rect(-256f, -256f, 512f, 512f);
            float Density(Vector3 p) => look.DensityAt(WorldEdgeFog.DistanceOutside(rect, p) + inset, p.y);

            Assert.That(Density(new Vector3(-256f, 4f, 0f)), Is.GreaterThan(0.6f), "подножие у самого края в пелене");
            Assert.That(Density(new Vector3(-300f, 10f, 0f)), Is.GreaterThan(0.99f), "за краем сплошная пелена");
            Assert.That(Density(new Vector3(-240f, 60f, 0f)), Is.Zero, "вершины над пеленой");
            Assert.That(Density(new Vector3(-200f, 10f, 0f)), Is.Zero, "внутри карты тумана нет");
        }

        [Test]
        public void CampaignCamera_FocusStaysAwayFromTheEdge_AndDoesNotZoomOutTooFar()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CameraRigSettings>(CampaignCameraPath);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.FocusBoundsMin, Is.EqualTo(new Vector2(-200f, -200f)));
            Assert.That(settings.FocusBoundsMax, Is.EqualTo(new Vector2(200f, 200f)));
            Assert.That(settings.MaxDistance, Is.EqualTo(140f));
        }
    }
}
