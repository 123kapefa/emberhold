using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Заглушка вида зон рубки (#167), пока у них нет своего облика: круг каждой зоны по земле (цвет — по уровню),
    /// подпись над центром, круг будущей зоны под курсором при инструменте 8 и панель выбранной зоны внизу экрана:
    /// радиус, уровень и число людей кнопками «−» / «+», кнопка «Снять зону». Логику зон меняет только через
    /// <see cref="WorkerCrew"/> и <see cref="ChopZones"/>. Ставит и связывает его WorkerCrew при старте.
    /// </summary>
    public sealed class ChopZoneView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const int Segments = 64;
        private const float Lift = 0.08f;
        private const float PanelWidth = 340f;
        private const float PanelHeight = 178f;
        private const float PanelBottom = 16f;

        [SerializeField] private Color normalColor = new(0.45f, 0.9f, 0.35f);
        [SerializeField] private Color highColor = new(1f, 0.8f, 0.2f);
        [SerializeField] private Color urgentColor = new(1f, 0.25f, 0.2f);
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color previewColor = new(0.45f, 0.9f, 0.35f, 0.5f);

        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();
        private readonly GameObject[] lines = new GameObject[5];
        private WorkerCrew crew;
        private BlockEditController editor;
        private Transform space;
        private Material material;
        private GUIStyle labelStyle;

        /// <summary>Связать с отрядом и редактором: space — мир блоков, material — Unlit-материал линий (цвет через _BaseColor).</summary>
        public void Bind(WorkerCrew crew, BlockEditController editor, Transform space, Material material)
        {
            this.crew = crew;
            this.editor = editor;
            this.space = space;
            this.material = material;
        }

        /// <summary>Подпись над зоной: «рубка · высокий · люди 1/2».</summary>
        public static string Label(ChopZone zone) =>
            $"рубка · {PriorityLabel.Name(zone.Priority)} · люди {zone.Assignment.Workers.Count}/{zone.Assignment.MaxWorkers}";

        /// <summary>Указатель (экранные координаты Input System, y снизу) над панелью зоны.</summary>
        public bool IsOverPanel(Vector2 pointer) => PanelShown && PanelRect().Contains(new Vector2(pointer.x, Screen.height - pointer.y));

        private bool PanelShown => crew != null && editor != null && editor.Tool == BlockTool.ChopZone && crew.SelectedZone != null;

        private static Rect PanelRect() =>
            new((Screen.width - PanelWidth) * 0.5f, Screen.height - PanelHeight - PanelBottom, PanelWidth, PanelHeight);

        private void LateUpdate()
        {
            if (crew == null || space == null)
                return;
            // Зону могли снять, пока она была выбрана.
            if (crew.SelectedZone != null && !Contains(crew.Zones.All, crew.SelectedZone))
                crew.SelectedZone = null;
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] ??= CreateLines(i);
                lines[i].transform.SetPositionAndRotation(space.position, space.rotation);
                lines[i].transform.localScale = space.lossyScale;
            }
            foreach (var priority in new[] { OrderPriority.Normal, OrderPriority.High, OrderPriority.Urgent })
            {
                Begin();
                foreach (var zone in crew.Zones.All)
                {
                    if (zone.Priority == priority)
                        AppendCircle(zone.Cell, zone.Radius);
                }
                End(lines[(int)priority]);
            }
            Begin();
            if (PanelShown)
                AppendCircle(crew.SelectedZone.Cell, crew.SelectedZone.Radius - 0.15f);
            End(lines[3]);
            Begin();
            if (editor != null && editor.Tool == BlockTool.ChopZone && editor.Hovered is { } hit)
            {
                var cell = hit.Block + Vector3Int.up;
                if (crew.Zones.At(cell) == null)
                    AppendCircle(cell, crew.NewZoneRadius);
            }
            End(lines[4]);
        }

        private void OnDestroy()
        {
            foreach (var go in lines)
            {
                if (go == null)
                    continue;
                Destroy(go.GetComponent<MeshFilter>().sharedMesh);
                Destroy(go);
            }
        }

        private void OnGUI()
        {
            if (crew == null || space == null)
                return;
            var camera = UnityEngine.Camera.main;
            labelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
            if (camera != null)
            {
                foreach (var zone in crew.Zones.All)
                {
                    var point = new Vector3(zone.Cell.x + 0.5f, zone.Cell.y + 1.6f, zone.Cell.z + 0.5f);
                    var screen = camera.WorldToScreenPoint(space.TransformPoint(point));
                    if (screen.z <= 0f)
                        continue;
                    var text = Label(zone);
                    var size = labelStyle.CalcSize(new GUIContent(text));
                    GUI.Label(new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y), text, labelStyle);
                }
            }
            if (PanelShown)
                DrawPanel(crew.SelectedZone);
        }

        private void DrawPanel(ChopZone zone)
        {
            GUILayout.BeginArea(PanelRect(), GUI.skin.box);
            GUILayout.Label("Зона рубки: деревья и пни в круге");
            var radius = Stepper("Радиус", zone.Radius.ToString(), zone.Radius > ChopZones.MinRadius, zone.Radius < ChopZones.MaxRadius);
            if (radius != 0)
                crew.Zones.SetRadius(zone, zone.Radius + radius, crew.Orders);
            var level = Stepper("Уровень", PriorityLabel.Name(zone.Priority), zone.Priority > OrderPriority.Normal,
                zone.Priority < OrderPriority.Urgent);
            if (level != 0)
                crew.Zones.SetPriority(zone, zone.Priority + level);
            var max = zone.Assignment.MaxWorkers;
            var people = Stepper("Людей", $"{max} (сейчас {zone.Assignment.Workers.Count})", max > 1, max < crew.Workers.Count);
            if (people != 0)
                crew.Zones.SetMaxWorkers(zone, max + people, crew.Workers.Count);
            GUILayout.Label($"Приказов в зоне: {zone.Assignment.Orders.Count}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Снять зону"))
                crew.RemoveZone(zone);
            if (GUILayout.Button("Закрыть"))
                crew.SelectedZone = null;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>Строка «название [−] значение [+]»; возвращает −1, 0 или +1 — какую кнопку нажали.</summary>
        private static int Stepper(string title, string value, bool canLower, bool canRaise)
        {
            var step = 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(70f));
            GUI.enabled = canLower;
            if (GUILayout.Button("−", GUILayout.Width(32f)))
                step = -1;
            GUI.enabled = true;
            GUILayout.Label(value, GUILayout.Width(150f));
            GUI.enabled = canRaise;
            if (GUILayout.Button("+", GUILayout.Width(32f)))
                step = 1;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            return step;
        }

        private static bool Contains(IReadOnlyList<ChopZone> zones, ChopZone zone)
        {
            foreach (var z in zones)
            {
                if (z == zone)
                    return true;
            }
            return false;
        }

        private void Begin()
        {
            vertices.Clear();
            indices.Clear();
        }

        /// <summary>Круг по земле вокруг середины клетки center, в координатах мира блоков.</summary>
        private void AppendCircle(Vector3Int center, float radius)
        {
            var first = vertices.Count;
            for (var i = 0; i < Segments; i++)
            {
                var angle = i * Mathf.PI * 2f / Segments;
                var x = center.x + 0.5f + Mathf.Cos(angle) * radius;
                var z = center.z + 0.5f + Mathf.Sin(angle) * radius;
                var y = crew.GroundHeight(new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) + Lift;
                vertices.Add(new Vector3(x, y, z));
                indices.Add(first + i);
                indices.Add(first + (i + 1) % Segments);
            }
        }

        private void End(GameObject target)
        {
            var mesh = target.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            target.SetActive(indices.Count > 0);
        }

        private GameObject CreateLines(int index)
        {
            var color = index switch
            {
                0 => normalColor,
                1 => highColor,
                2 => urgentColor,
                3 => selectedColor,
                _ => previewColor,
            };
            var go = new GameObject($"Chop Zones {index}");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(properties);
            go.SetActive(false);
            return go;
        }
    }
}
