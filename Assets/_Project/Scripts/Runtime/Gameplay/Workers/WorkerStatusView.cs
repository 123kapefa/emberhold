using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Заглушка вида статуса рабочих, пока у них нет своего облика: подпись над головой, что рабочий делает
    /// (<see cref="WorkerStatus"/>), и тонкая линия по оставшемуся пути до цели. Цвет подписи и линии — по виду работы.
    /// Только читает рабочих из <see cref="WorkerCrew"/>, в логику не лезет: облик можно заменить целиком, не трогая механику.
    /// Ставит и связывает его сам WorkerCrew при старте.
    /// </summary>
    public sealed class WorkerStatusView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Насколько линия пути поднята над землёй, в блоках, чтобы не тонуть в ней.</summary>
        private const float RouteLift = 0.12f;

        /// <summary>На сколько блоков подпись выше макушки.</summary>
        private const float LabelLift = 0.35f;

        [SerializeField] private Color idleColor = new(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color buildColor = new(0.35f, 0.75f, 1f);
        [SerializeField] private Color digColor = new(1f, 0.45f, 0.15f);
        [SerializeField] private Color chopColor = new(0.45f, 0.9f, 0.35f);
        [SerializeField] private Color fuelColor = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color campfireColor = new(1f, 0.6f, 0.55f);
        [SerializeField] private Color haulColor = new(0.95f, 0.8f, 0.45f);
        [SerializeField] private Color fleeColor = new(1f, 0.2f, 0.2f);

        private readonly List<GameObject> routes = new();
        private readonly List<Vector3> points = new();
        private readonly List<int> indices = new();
        private WorkerCrew crew;
        private Transform space;
        private Material lineMaterial;
        private float height;
        private GUIStyle labelStyle;

        /// <summary>Связать с отрядом: space — мир блоков, material — Unlit-материал линий (цвет через _BaseColor), height — рост рабочего.</summary>
        public void Bind(WorkerCrew crew, Transform space, Material material, float height)
        {
            this.crew = crew;
            this.space = space;
            lineMaterial = material;
            this.height = height;
        }

        public Color ColorOf(WorkerActivity activity) => activity switch
        {
            WorkerActivity.Build => buildColor,
            WorkerActivity.Dig => digColor,
            WorkerActivity.Chop => chopColor,
            WorkerActivity.Fuel => fuelColor,
            WorkerActivity.Campfire => campfireColor,
            WorkerActivity.Haul => haulColor,
            WorkerActivity.Flee => fleeColor,
            _ => idleColor,
        };

        private void LateUpdate()
        {
            if (crew == null || space == null)
                return;
            var workers = crew.Workers;
            while (routes.Count < workers.Count)
                routes.Add(CreateRoute(routes.Count));
            for (var i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                if (i >= workers.Count)
                {
                    route.SetActive(false);
                    continue;
                }
                route.transform.SetPositionAndRotation(space.position, space.rotation);
                route.transform.localScale = space.lossyScale;
                FillRoute(workers[i], route);
            }
        }

        private void FillRoute(Worker worker, GameObject route)
        {
            points.Clear();
            worker.RemainingRoute(points);
            if (points.Count == 0)
            {
                route.SetActive(false);
                return;
            }
            // Последний отрезок — к самой цели: к блоку задачи или к поддону.
            if (worker.State == WorkerState.ToSite)
            {
                if (worker.HaulTarget != null)
                    points.Add(worker.HaulTarget.Center);
                else if (worker.Order != null)
                    points.Add(worker.Order.Cell + new Vector3(0.5f, 0.5f, 0.5f));
            }
            indices.Clear();
            for (var k = 0; k < points.Count; k++)
            {
                points[k] += Vector3.up * RouteLift;
                indices.Add(k);
            }
            var renderer = route.GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, ColorOf(WorkerStatus.Activity(worker)));
            renderer.SetPropertyBlock(properties);
            var mesh = route.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            mesh.SetVertices(points);
            mesh.SetIndices(indices, MeshTopology.LineStrip, 0);
            mesh.RecalculateBounds();
            route.SetActive(true);
        }

        private GameObject CreateRoute(int index)
        {
            var go = new GameObject($"Worker {index + 1} Route");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = lineMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>Подписи над рабочими: что делает, цветом вида работы.</summary>
        private void OnGUI()
        {
            if (crew == null || space == null || crew.Workers.Count == 0)
                return;
            var camera = UnityEngine.Camera.main;
            if (camera == null)
                return;
            labelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
            foreach (var worker in crew.Workers)
            {
                var top = space.TransformPoint(worker.Position + Vector3.up * (height + LabelLift));
                var screen = camera.WorldToScreenPoint(top);
                if (screen.z <= 0f)
                    continue;
                var text = WorkerStatus.Text(worker);
                labelStyle.normal.textColor = ColorOf(WorkerStatus.Activity(worker));
                var size = labelStyle.CalcSize(new GUIContent(text));
                // GUI считает y сверху, экран — снизу.
                GUI.Label(new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y), text, labelStyle);
            }
        }

        private void OnDestroy()
        {
            foreach (var route in routes)
            {
                if (route == null)
                    continue;
                Destroy(route.GetComponent<MeshFilter>().sharedMesh);
                Destroy(route);
            }
            routes.Clear();
        }
    }
}
