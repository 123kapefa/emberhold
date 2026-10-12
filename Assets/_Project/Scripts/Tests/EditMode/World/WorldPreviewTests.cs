using Emberhold.World;
using Emberhold.World.Generation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Tests.EditMode.World
{
    /// <summary>Предпросмотр рельефа вне Play: кнопка инспектора мира зовёт BuildPreview и ClearPreview.</summary>
    public sealed class WorldPreviewTests
    {
        private GameObject worldObject;
        private VoxelWorldView view;
        private TerrainSettings terrain;
        private TerrainProfile flatProfile;
        private GameObject zoneObject;

        [SetUp]
        public void SetUp()
        {
            terrain = ScriptableObject.CreateInstance<TerrainSettings>();
            var settings = new SerializedObject(terrain);
            settings.FindProperty("sizeInChunks").vector3IntValue = new Vector3Int(2, 2, 2);
            settings.ApplyModifiedPropertiesWithoutUndo();

            worldObject = new GameObject("World");
            view = worldObject.AddComponent<VoxelWorldView>();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("terrain").objectReferenceValue = terrain;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in new Object[] { worldObject, zoneObject, terrain, flatProfile })
                if (obj != null)
                    Object.DestroyImmediate(obj);
        }

        private Transform PreviewRoot() => worldObject.transform.Find(VoxelWorldView.PreviewName);

        [Test]
        public void BuildPreview_ShowsChunksThatAreNotSaved()
        {
            view.BuildPreview();

            var root = PreviewRoot();
            Assert.That(root, Is.Not.Null);
            Assert.That(view.HasPreview, Is.True);
            Assert.That(root.childCount, Is.GreaterThan(0));
            Assert.That(root.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
            foreach (Transform chunk in root)
            {
                Assert.That(chunk.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
                Assert.That(chunk.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
            }
        }

        [Test]
        public void BuildPreview_CentersWorldWithoutMovingTheView()
        {
            view.BuildPreview();

            Assert.That(worldObject.transform.position, Is.EqualTo(Vector3.zero));
            // Мир 32×32 блока по X и Z, центр в начале координат.
            Assert.That(PreviewRoot().position, Is.EqualTo(new Vector3(-16f, 0f, -16f)));
        }

        [Test]
        public void BuildPreview_Twice_KeepsOnePreview()
        {
            view.BuildPreview();
            var chunks = PreviewRoot().childCount;
            view.BuildPreview();

            var roots = 0;
            foreach (Transform child in worldObject.transform)
                roots++;
            Assert.That(roots, Is.EqualTo(1));
            Assert.That(PreviewRoot().childCount, Is.EqualTo(chunks));
        }

        [Test]
        public void ClearPreview_RemovesChunksAndMeshes()
        {
            view.BuildPreview();
            var mesh = PreviewRoot().GetChild(0).GetComponent<MeshFilter>().sharedMesh;

            view.ClearPreview();

            Assert.That(worldObject.transform.childCount, Is.Zero);
            Assert.That(view.HasPreview, Is.False);
            Assert.That(view.World, Is.Null);
            Assert.That(mesh == null, Is.True, "меш чанка не удалён");
        }

        [Test]
        public void BuildPreview_UsesTerrainZonesOfTheScene()
        {
            view.BuildPreview();
            var before = TopHeight();

            // Зона на всю карту с ровным полом высотой 2 блока: рельеф становится ниже.
            flatProfile = ScriptableObject.CreateInstance<TerrainProfile>();
            var profile = new SerializedObject(flatProfile);
            profile.FindProperty("parameters.Shape").intValue = (int)TerrainShape.Flat;
            profile.FindProperty("parameters.BaseHeight").floatValue = 2f;
            profile.FindProperty("parameters.NoiseAmplitude").floatValue = 0f;
            profile.FindProperty("parameters.TerraceHeight").floatValue = 0f;
            profile.FindProperty("parameters.CliffAmount").floatValue = 0f;
            profile.ApplyModifiedPropertiesWithoutUndo();
            zoneObject = new GameObject("Zone");
            var zone = zoneObject.AddComponent<TerrainZone>();
            zone.Shape = TerrainZoneShape.Rectangle;
            zone.Size = new Vector2(200f, 200f);
            zone.BlendWidth = 0f;
            zone.Profile = flatProfile;

            view.BuildPreview();

            Assert.That(TopHeight(), Is.LessThan(before));
        }

        /// <summary>Верх рельефа предпросмотра по границам мешей чанков.</summary>
        private float TopHeight()
        {
            var top = float.MinValue;
            foreach (Transform chunk in PreviewRoot())
                top = Mathf.Max(top, chunk.GetComponent<MeshRenderer>().bounds.max.y);
            return top;
        }
    }
}
