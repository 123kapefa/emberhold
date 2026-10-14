using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Editor.Workers
{
    /// <summary>
    /// Отрисовка рабочего вне сцены (PreviewRenderUtility): части из WorkerMeshBuilder.BuildParts по облику из
    /// WorkerSettings и роли, поза — из анимации Clip, ноша на плече — Carry. Орбитальная камера вокруг точки Pivot,
    /// по желанию — рёбра граней и пол-сетка в блоках. В режиме витрины рядом стоят все роли одного и того же человека.
    /// Окно WorkerViewerWindow показывает её и крутит мышью, тест рисует в текстуру.
    /// </summary>
    public sealed class WorkerPreview : IDisposable
    {
        public enum BeardMode { ByLook, On, Off }

        private const string WorkerMaterialPath = "Assets/_Project/Art/Materials/Workers/Worker.mat";
        private const float LineupSpacing = 0.9f;
        /// <summary>Пауза между повторами разовой анимации, секунды.</summary>
        private const float OneShotPause = 0.5f;
        private static readonly Color Background = new(0.32f, 0.34f, 0.36f);

        private readonly PreviewRenderUtility preview = new();
        private readonly Material workerMaterial;
        private readonly Material wireMaterial;
        private readonly Material gridMaterial;
        private readonly List<Figure> figures = new();
        private readonly Matrix4x4[] matrices = new Matrix4x4[WorkerPose.PartCount];
        private Mesh grid;
        private WorkerCarryBuilder.Shape carryShape;
        private WorkerCarry builtCarry = (WorkerCarry)(-1);
        private float time;

        /// <summary>Шаг времени с прошлой отрисовки — для качания мешка; 0 после перемотки и в снимках.</summary>
        private float stepSinceDraw;

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
        public WorkerRole Role { get; set; }

        /// <summary>Витрина: все роли в ряд вместо одной.</summary>
        public bool Lineup { get; set; }

        /// <summary>Сколько фигур в кадре.</summary>
        public int FigureCount => figures.Count;
        public BeardMode Beard { get; set; }
        public bool Wireframe { get; set; }
        public bool ShowGrid { get; set; } = true;

        /// <summary>Какая анимация играет.</summary>
        public WorkerClip Clip { get; set; } = WorkerClip.Idle;

        /// <summary>Что несёт в анимациях с ношей (нести, поднять, положить).</summary>
        public WorkerCarry Carry { get; set; } = WorkerCarry.Log;

        /// <summary>Скорость ходьбы и бега, блоков в секунду; 0 — по умолчанию у анимации.</summary>
        public float MoveSpeed { get; set; }

        /// <summary>Время текущей анимации, секунды.</summary>
        public float Time
        {
            get => time;
            set
            {
                time = Mathf.Max(0f, value);
                stepSinceDraw = 0f;
            }
        }

        /// <summary>Длина разовой анимации с паузой; у цикла — 0.</summary>
        public float OneShotLength => WorkerAnimation.IsOneShot(Clip) ? WorkerAnimation.DefaultDuration(Clip) + OneShotPause : 0f;

        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Distance { get; set; }
        public Vector3 Pivot { get; set; }

        /// <summary>Рост, по которому построена текущая сетка.</summary>
        public float Height { get; private set; } = 1.9f;

        public int VertexCount => figures.Count > 0 ? Sum(figures[0].Parts, m => m.vertexCount) : 0;
        public int TriangleCount => figures.Count > 0 ? Sum(figures[0].Parts, m => m.triangles.Length / 3) : 0;

        /// <summary>Середина ряда витрины по X, в блоках; для одной фигуры — 0.</summary>
        // Ряд слева направо в порядке ролей, если смотреть спереди (камера смотрит в −Z, поэтому +X — слева).
        public static float LineupX(int index, int count) => ((count - 1) * 0.5f - index) * LineupSpacing;

        /// <summary>Вид спереди на всего рабочего или на весь ряд витрины.</summary>
        public void ResetView(float height)
        {
            Yaw = 180f;
            Pitch = 8f;
            var count = Lineup ? Enum.GetValues(typeof(WorkerRole)).Length : 1;
            Distance = Mathf.Max(height * 2.6f, count * LineupSpacing * 1.45f);
            Pivot = new Vector3(0f, height * 0.55f, 0f);
        }

        /// <summary>Продвинуть анимацию; разовая повторяется по кругу с короткой паузой.</summary>
        public void Advance(float deltaTime)
        {
            time += deltaTime;
            stepSinceDraw += deltaTime;
            if (OneShotLength > 0f && time > OneShotLength)
                time %= OneShotLength;
        }

        /// <summary>Перестроить сетки по настройкам, облику, роли и бороде.</summary>
        public void Rebuild()
        {
            DestroyMeshes();
            Height = Settings != null ? Settings.Height : 1.9f;
            var roles = Lineup ? (WorkerRole[])Enum.GetValues(typeof(WorkerRole)) : new[] { Role };
            for (var i = 0; i < roles.Length; i++)
            {
                var look = LookFor(roles[i]);
                var parts = WorkerMeshBuilder.BuildParts(look, Height, "Worker Preview " + roles[i]);
                var edges = new Mesh[parts.Length];
                var skins = new WorkerSkin[parts.Length];
                for (var k = 0; k < parts.Length; k++)
                {
                    Hidden(parts[k]);
                    edges[k] = Hidden(Edges(parts[k]));
                    if (WorkerSkin.IsSoft((WorkerPart)k))
                        skins[k] = new WorkerSkin(parts[k], (WorkerPart)k, Height, look.Role);
                }
                var x = Lineup ? LineupX(i, roles.Length) : 0f;
                figures.Add(new Figure(look, parts, edges, skins, new Vector3(x, 0f, 0f), Lineup ? i * 0.137f % 1f : 0f));
            }
            grid = Hidden(Grid(Lineup ? Mathf.CeilToInt(roles.Length * LineupSpacing * 0.5f) + 1 : 2));
        }

        private WorkerLook LookFor(WorkerRole role)
        {
            var look = Settings != null
                ? Settings.LookFor(LookIndex, role)
                : new WorkerLook(new Color(0.91f, 0.75f, 0.6f), new Color(0.23f, 0.17f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                    new Color(0.26f, 0.36f, 0.3f), false, role);
            return Beard == BeardMode.ByLook ? look : look.WithBeard(Beard == BeardMode.On);
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
            if (figures.Count == 0)
                Rebuild();
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            preview.camera.transform.SetPositionAndRotation(Pivot - rotation * Vector3.forward * Distance, rotation);
            var oneShot = WorkerAnimation.IsOneShot(Clip);
            var duration = WorkerAnimation.DefaultDuration(Clip);
            var sampleTime = oneShot ? Mathf.Clamp01(time / duration) : time;
            var carry = CarryShown();
            if (carry != builtCarry)
            {
                if (carryShape != null)
                    UnityEngine.Object.DestroyImmediate(carryShape.Mesh);
                carryShape = carry == WorkerCarry.None
                    ? null
                    : new WorkerCarryBuilder.Shape(Hidden(WorkerCarryBuilder.Build(carry, Height, "Carry Preview")), carry, Height);
                builtCarry = carry;
            }
            foreach (var figure in figures)
            {
                var motion = WorkerMotion.For(figure.Look, Height, MoveSpeed, figure.Seed, carry);
                var pose = WorkerAnimation.Sample(Clip, oneShot ? sampleTime : time, motion);
                WorkerSkeleton.Evaluate(pose, Height, matrices);
                var place = Matrix4x4.Translate(figure.Offset);
                for (var k = 0; k < figure.Parts.Length; k++)
                {
                    // Мягкие части (подол, пелерина) гнутся по костям, их вершины уже в системе рабочего.
                    var matrix = matrices[k];
                    if (figure.Skins[k] != null)
                    {
                        figure.Skins[k].Apply(matrices);
                        matrix = Matrix4x4.identity;
                    }
                    preview.DrawMesh(figure.Parts[k], place * matrix, workerMaterial, 0);
                    if (Wireframe && figure.Skins[k] == null)
                        preview.DrawMesh(figure.Edges[k], place * matrix * Matrix4x4.Scale(Vector3.one * 1.004f), wireMaterial, 0);
                }
                if (carryShape != null)
                {
                    // Сетка мешка одна на витрину: качание считаем по первой фигуре, остальные рисуют ту же форму.
                    if (figure == figures[0])
                        carryShape.Apply(place * Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one), pose.LoadBend, stepSinceDraw);
                    preview.DrawMesh(carryShape.Mesh, place * Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one), workerMaterial, 0);
                }
            }
            stepSinceDraw = 0f;
            if (ShowGrid)
                preview.DrawMesh(grid, Matrix4x4.identity, gridMaterial, 0);
            preview.Render(true);
        }

        private WorkerCarry CarryShown() => Clip is WorkerClip.Carry or WorkerClip.PickUp or WorkerClip.Put ? Carry : WorkerCarry.None;

        private void DestroyMeshes()
        {
            foreach (var figure in figures)
            {
                foreach (var mesh in figure.Parts)
                    UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var mesh in figure.Edges)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
            figures.Clear();
            if (grid != null)
                UnityEngine.Object.DestroyImmediate(grid);
            if (carryShape != null)
                UnityEngine.Object.DestroyImmediate(carryShape.Mesh);
            grid = null;
            carryShape = null;
            builtCarry = (WorkerCarry)(-1);
        }

        private static int Sum(Mesh[] meshes, Func<Mesh, int> value)
        {
            var total = 0;
            foreach (var mesh in meshes)
                total += value(mesh);
            return total;
        }

        private sealed class Figure
        {
            public Figure(WorkerLook look, Mesh[] parts, Mesh[] edges, WorkerSkin[] skins, Vector3 offset, float seed)
            {
                Look = look;
                Parts = parts;
                Edges = edges;
                Skins = skins;
                Offset = offset;
                Seed = seed;
            }

            public WorkerLook Look { get; }
            public Mesh[] Parts { get; }
            public Mesh[] Edges { get; }
            public WorkerSkin[] Skins { get; }
            public Vector3 Offset { get; }
            public float Seed { get; }
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
