using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Editor.Workers
{
    /// <summary>
    /// Отрисовка одного рабочего вне сцены (PreviewRenderUtility): сетка из WorkerMeshBuilder по облику из WorkerSettings,
    /// орбитальная камера вокруг точки Pivot, по желанию — рёбра граней и пол-сетка в блоках. Окно WorkerViewerWindow
    /// показывает её и крутит мышью, тест рисует в текстуру.
    /// </summary>
    public sealed class WorkerPreview : IDisposable
    {
        public enum BeardMode { ByLook, On, Off }

        private const string WorkerMaterialPath = "Assets/_Project/Art/Materials/Workers/Worker.mat";
        private static readonly Color Background = new(0.32f, 0.34f, 0.36f);

        private readonly PreviewRenderUtility preview = new();
        private readonly Material workerMaterial;
        private readonly Material wireMaterial;
        private readonly Material gridMaterial;
        private Mesh body;
        private Mesh wire;
        private Mesh grid;

        public WorkerPreview()
        {
            var shared = AssetDatabase.LoadAssetAtPath<Material>(WorkerMaterialPath);
            workerMaterial = shared != null ? shared : Hidden(new Material(Shader.Find("Emberhold/World/Tree")));
            wireMaterial = Unlit(new Color(0.05f, 0.05f, 0.05f));
            gridMaterial = Unlit(new Color(0.48f, 0.5f, 0.52f));

            var camera = preview.camera;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            preview.lights[1].intensity = 0.5f;
            preview.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);
            ResetView(1.9f);
        }

        public WorkerSettings Settings { get; set; }
        public int LookIndex { get; set; }
        public BeardMode Beard { get; set; }
        public bool Wireframe { get; set; }
        public bool ShowGrid { get; set; } = true;

        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Distance { get; set; }
        public Vector3 Pivot { get; set; }

        /// <summary>Рост, по которому построена текущая сетка.</summary>
        public float Height { get; private set; } = 1.9f;

        public int VertexCount => body != null ? body.vertexCount : 0;
        public int TriangleCount => body != null ? body.triangles.Length / 3 : 0;

        /// <summary>Вид спереди на всего рабочего.</summary>
        public void ResetView(float height)
        {
            Yaw = 180f;
            Pitch = 8f;
            Distance = height * 2.6f;
            Pivot = new Vector3(0f, height * 0.55f, 0f);
        }

        /// <summary>Перестроить сетку по настройкам, облику и бороде.</summary>
        public void Rebuild()
        {
            DestroyMeshes();
            Height = Settings != null ? Settings.Height : 1.9f;
            var look = Settings != null
                ? Settings.LookFor(LookIndex)
                : new WorkerLook(new Color(0.91f, 0.75f, 0.6f), new Color(0.23f, 0.17f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                    new Color(0.26f, 0.36f, 0.3f), false);
            if (Beard != BeardMode.ByLook)
                look = new WorkerLook(look.Skin, look.Hair, look.Tunic, look.Hood, Beard == BeardMode.On);
            body = Hidden(WorkerMeshBuilder.Build(look, Height, "Worker Preview"));
            wire = Hidden(Edges(body));
            grid = Hidden(Grid(2));
        }

        /// <summary>Нарисовать в прямоугольник окна (вызывать в OnGUI при Repaint).</summary>
        public void Draw(Rect rect)
        {
            preview.BeginPreview(rect, GUIStyle.none);
            RenderScene();
            preview.EndAndDrawPreview(rect);
        }

        /// <summary>Нарисовать в текстуру размером width × height.</summary>
        public Texture2D RenderToTexture(int width, int height)
        {
            preview.BeginStaticPreview(new Rect(0f, 0f, width, height));
            RenderScene();
            return preview.EndStaticPreview();
        }

        public void Dispose()
        {
            DestroyMeshes();
            preview.Cleanup();
            UnityEngine.Object.DestroyImmediate(wireMaterial);
            UnityEngine.Object.DestroyImmediate(gridMaterial);
            if (workerMaterial.hideFlags == HideFlags.HideAndDontSave)
                UnityEngine.Object.DestroyImmediate(workerMaterial);
        }

        private void RenderScene()
        {
            if (body == null)
                Rebuild();
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            preview.camera.transform.SetPositionAndRotation(Pivot - rotation * Vector3.forward * Distance, rotation);
            preview.DrawMesh(body, Matrix4x4.identity, workerMaterial, 0);
            if (Wireframe)
            {
                // Чуть больше тела, чтобы рёбра не тонули в гранях.
                var center = body.bounds.center;
                preview.DrawMesh(wire, Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * 1.003f) * Matrix4x4.Translate(-center), wireMaterial, 0);
            }
            if (ShowGrid)
                preview.DrawMesh(grid, Matrix4x4.identity, gridMaterial, 0);
            preview.Render(true);
        }

        private void DestroyMeshes()
        {
            foreach (var mesh in new[] { body, wire, grid })
            {
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
            body = wire = grid = null;
        }

        /// <summary>Рёбра треугольников сетки линиями, каждое ребро один раз.</summary>
        private static Mesh Edges(Mesh source)
        {
            var vertices = source.vertices;
            var triangles = source.triangles;
            var keys = new HashSet<(Vector3, Vector3)>();
            var points = new List<Vector3>();
            for (var i = 0; i < triangles.Length; i += 3)
            for (var k = 0; k < 3; k++)
            {
                var a = vertices[triangles[i + k]];
                var b = vertices[triangles[i + (k + 1) % 3]];
                var key = Compare(a, b) < 0 ? (a, b) : (b, a);
                if (!keys.Add(key))
                    continue;
                points.Add(a);
                points.Add(b);
            }
            return Lines("Worker Edges", points);
        }

        private static int Compare(Vector3 a, Vector3 b) =>
            a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);

        /// <summary>Пол-сетка в блоках вокруг ног: от −half до half по X и Z.</summary>
        private static Mesh Grid(int half)
        {
            var points = new List<Vector3>();
            for (var i = -half; i <= half; i++)
            {
                points.Add(new Vector3(i, 0f, -half));
                points.Add(new Vector3(i, 0f, half));
                points.Add(new Vector3(-half, 0f, i));
                points.Add(new Vector3(half, 0f, i));
            }
            return Lines("Block Grid", points);
        }

        private static Mesh Lines(string name, List<Vector3> points)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(points);
            var indices = new int[points.Count];
            for (var i = 0; i < indices.Length; i++)
                indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }

        private static Material Unlit(Color color)
        {
            var material = Hidden(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            material.SetColor("_BaseColor", color);
            return material;
        }

        private static T Hidden<T>(T o) where T : UnityEngine.Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            return o;
        }
    }
}
