using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Заглушка вида зон работ, пока у них нет своего облика. Зона рубки (#167) — круг по земле, зона копки (#136) —
    /// рамка прямоугольника по земле и сетка плоскости целевого уровня; цвет рамки — по приоритету. Над зоной подпись,
    /// под курсором при инструменте 8 или 9 — будущая зона (у копки — вся протяжка ЛКМ), внизу экрана — панель выбранной
    /// зоны: размеры, уровень, приоритет и число людей кнопками «−» / «+», кнопка «Снять зону». Логику зон меняет только
    /// через <see cref="WorkerCrew"/> и <see cref="WorkZones"/>. Ставит и связывает его WorkerCrew при старте.
    /// </summary>
    public sealed class WorkZoneView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const int Segments = 64;
        private const float Lift = 0.08f;
        private const float PanelWidth = 360f;
        private const float PanelBottom = 16f;

        // Линии: 0–2 — рамки по приоритету, 3 — выбранная зона, 4 — будущая зона, 5 — плоскости уровня.
        private const int SelectedLines = 3;
        private const int PreviewLines = 4;
        private const int LevelLines = 5;

        [SerializeField] private Color normalColor = new(0.45f, 0.9f, 0.35f);
        [SerializeField] private Color highColor = new(1f, 0.8f, 0.2f);
        [SerializeField] private Color urgentColor = new(1f, 0.25f, 0.2f);
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color previewColor = new(0.45f, 0.9f, 0.35f, 0.5f);
        [SerializeField] private Color levelColor = new(0.35f, 0.85f, 1f, 0.6f);

        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();
        private readonly GameObject[] lines = new GameObject[6];
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

        /// <summary>Подпись над зоной: «рубка · высокий · люди 1/2», «копка до 5 · обычный · люди 0/2».</summary>
        public static string Label(WorkZone zone)
        {
            var what = zone is LevelZone level ? $"копка до {level.Level}" : "рубка";
            return $"{what} · {PriorityLabel.Name(zone.Priority)} · люди {zone.Assignment.Workers.Count}/{zone.Assignment.MaxWorkers}";
        }

        /// <summary>Указатель (экранные координаты Input System, y снизу) над панелью зоны.</summary>
        public bool IsOverPanel(Vector2 pointer) => PanelShown && PanelRect().Contains(new Vector2(pointer.x, Screen.height - pointer.y));

        private bool ZoneTool => editor != null && editor.Tool is BlockTool.ChopZone or BlockTool.DigZone;

        private bool PanelShown => crew != null && ZoneTool && crew.SelectedZone != null;

        private Rect PanelRect()
        {
            var height = crew != null && crew.SelectedZone is LevelZone ? 232f : 178f;
            return new Rect((Screen.width - PanelWidth) * 0.5f, Screen.height - height - PanelBottom, PanelWidth, height);
        }

        private void LateUpdate()
        {
            if (crew == null || space == null)
                return;
            // Зону могли снять (или она сама ушла), пока была выбрана.
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
                        AppendOutline(zone, 0f);
                }
                End(lines[(int)priority]);
            }
            Begin();
            foreach (var zone in crew.Zones.All)
            {
                if (zone is LevelZone level)
                    AppendLevelPlane(level.Min, level.Size, level.Level);
            }
            End(lines[LevelLines]);
            Begin();
            if (PanelShown)
                AppendOutline(crew.SelectedZone, 0.15f);
            End(lines[SelectedLines]);
            Begin();
            AppendPreview();
            End(lines[PreviewLines]);
        }

        /// <summary>Будущая зона под курсором: круг рубки или прямоугольник копки от начала протяжки до курсора с уровнем.</summary>
        private void AppendPreview()
        {
            if (editor == null || editor.Hovered is not { } hit)
                return;
            if (editor.Tool == BlockTool.ChopZone)
            {
                var cell = hit.Block + Vector3Int.up;
                if (crew.Zones.At(cell, WorkKind.Chop) == null)
                    AppendCircle(cell, crew.NewZoneRadius);
                return;
            }
            if (editor.Tool != BlockTool.DigZone)
                return;
            var from = editor.DigZoneDragFrom ?? hit.Block;
            if (editor.DigZoneDragFrom == null && crew.Zones.At(from, WorkKind.Dig) != null)
                return;
            var min = new Vector2Int(Mathf.Min(from.x, hit.Block.x), Mathf.Min(from.z, hit.Block.z));
            var size = new Vector2Int(Mathf.Abs(hit.Block.x - from.x) + 1, Mathf.Abs(hit.Block.z - from.z) + 1);
            AppendRect(min, size, 0f);
            AppendLevelPlane(min, size, from.y - 1);
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
                    var center = zone.Center;
                    var column = new Vector2Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y));
                    var point = new Vector3(center.x, crew.GroundHeight(column) + 1.6f, center.y);
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

        private void DrawPanel(WorkZone zone)
        {
            GUILayout.BeginArea(PanelRect(), GUI.skin.box);
            if (zone is ChopZone chop)
            {
                GUILayout.Label("Зона рубки: деревья и пни в круге");
                var radius = Stepper("Радиус", chop.Radius.ToString(), chop.Radius > ChopZone.MinRadius, chop.Radius < ChopZone.MaxRadius);
                if (radius != 0)
                    crew.Zones.SetRadius(chop, chop.Radius + radius, crew.Orders);
            }
            else if (zone is LevelZone level)
            {
                GUILayout.Label("Зона копки: всё выше уровня выкопать");
                var length = Stepper("Длина", level.Size.x.ToString(), level.Size.x > LevelZone.MinSize, level.Size.x < LevelZone.MaxSize);
                if (length != 0)
                    crew.Zones.SetSize(level, level.Size + new Vector2Int(length, 0), crew.Orders);
                var width = Stepper("Ширина", level.Size.y.ToString(), level.Size.y > LevelZone.MinSize, level.Size.y < LevelZone.MaxSize);
                if (width != 0)
                    crew.Zones.SetSize(level, level.Size + new Vector2Int(0, width), crew.Orders);
                var height = Stepper("Уровень", $"высота {level.Level}", level.Level > 0, true);
                if (height != 0)
                    crew.Zones.SetLevel(level, level.Level + height, crew.Orders);
            }
            var priority = Stepper("Приоритет", PriorityLabel.Name(zone.Priority), zone.Priority > OrderPriority.Normal,
                zone.Priority < OrderPriority.Urgent);
            if (priority != 0)
                crew.Zones.SetPriority(zone, zone.Priority + priority);
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
            GUILayout.Label(title, GUILayout.Width(80f));
            GUI.enabled = canLower;
            if (GUILayout.Button("−", GUILayout.Width(32f)))
                step = -1;
            GUI.enabled = true;
            GUILayout.Label(value, GUILayout.Width(160f));
            GUI.enabled = canRaise;
            if (GUILayout.Button("+", GUILayout.Width(32f)))
                step = 1;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            return step;
        }

        private static bool Contains(IReadOnlyList<WorkZone> zones, WorkZone zone)
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

        /// <summary>Граница зоны по земле; inset — насколько отступить внутрь (рамка выбранной зоны).</summary>
        private void AppendOutline(WorkZone zone, float inset)
        {
            if (zone is ChopZone chop)
                AppendCircle(chop.Cell, chop.Radius - inset);
            else if (zone is LevelZone level)
                AppendRect(level.Min, level.Size, inset);
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
                vertices.Add(new Vector3(x, Ground(x, z), z));
                indices.Add(first + i);
                indices.Add(first + (i + 1) % Segments);
            }
        }

        /// <summary>Рамка прямоугольника столбцов по земле, по полблока, чтобы следовать рельефу.</summary>
        private void AppendRect(Vector2Int min, Vector2Int size, float inset)
        {
            var x0 = min.x + inset;
            var z0 = min.y + inset;
            var x1 = min.x + size.x - inset;
            var z1 = min.y + size.y - inset;
            var corners = new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
            var first = vertices.Count;
            for (var side = 0; side < 4; side++)
            {
                var a = corners[side];
                var b = corners[(side + 1) % 4];
                var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                for (var i = 0; i < steps; i++)
                {
                    var p = Vector2.Lerp(a, b, (float)i / steps);
                    // Высоту берём у столбца внутри прямоугольника, а не у соседа за краем.
                    var inside = new Vector2(Mathf.Clamp(p.x, x0 + 0.01f, x1 - 0.01f), Mathf.Clamp(p.y, z0 + 0.01f, z1 - 0.01f));
                    vertices.Add(new Vector3(p.x, Ground(inside.x, inside.y), p.y));
                }
            }
            var count = vertices.Count - first;
            for (var i = 0; i < count; i++)
            {
                indices.Add(first + i);
                indices.Add(first + (i + 1) % count);
            }
        }

        /// <summary>Плоскость уровня: сетка по блокам на высоте верха блока level.</summary>
        private void AppendLevelPlane(Vector2Int min, Vector2Int size, int level)
        {
            var y = level + 1f + 0.02f;
            for (var x = 0; x <= size.x; x++)
                AppendSegment(new Vector3(min.x + x, y, min.y), new Vector3(min.x + x, y, min.y + size.y));
            for (var z = 0; z <= size.y; z++)
                AppendSegment(new Vector3(min.x, y, min.y + z), new Vector3(min.x + size.x, y, min.y + z));
        }

        private void AppendSegment(Vector3 a, Vector3 b)
        {
            indices.Add(vertices.Count);
            vertices.Add(a);
            indices.Add(vertices.Count);
            vertices.Add(b);
        }

        private float Ground(float x, float z) => crew.GroundHeight(new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) + Lift;

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
                SelectedLines => selectedColor,
                PreviewLines => previewColor,
                _ => levelColor,
            };
            var go = new GameObject($"Work Zones {index}");
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
