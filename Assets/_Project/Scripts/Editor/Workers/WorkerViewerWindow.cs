using Emberhold.Gameplay.Workers;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Editor.Workers
{
    /// <summary>
    /// Окно Emberhold → Worker Viewer: один рабочий на нейтральном фоне без сцены и Play. Левая кнопка мыши — вращение
    /// вокруг, колесо — приближение, средняя или правая — сдвиг. Облик — по номеру рабочего из WorkerSettings; после
    /// перекомпиляции кода и правки настроек модель перестраивается сама.
    /// </summary>
    public sealed class WorkerViewerWindow : EditorWindow
    {
        private const float AutoTurnDegreesPerSecond = 30f;

        [SerializeField] private WorkerSettings settings;
        [SerializeField] private int lookIndex;
        [SerializeField] private WorkerPreview.BeardMode beard;
        [SerializeField] private bool wireframe;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool autoTurn;

        private WorkerPreview preview;
        private int settingsDirtyCount = -1;
        private double lastTime;

        [MenuItem("Emberhold/Worker Viewer")]
        public static void Open() => GetWindow<WorkerViewerWindow>("Worker Viewer");

        private void OnEnable()
        {
            // После перекомпиляции окно пересоздаёт превью — сетка строится заново по новому коду.
            if (settings == null)
                settings = FindSettings();
            preview = new WorkerPreview();
            Apply();
            preview.ResetView(preview.Height);
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            preview?.Dispose();
            preview = null;
        }

        private void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)(now - lastTime);
            lastTime = now;
            if (settings != null && EditorUtility.GetDirtyCount(settings) != settingsDirtyCount)
            {
                Apply();
                Repaint();
            }
            if (autoTurn)
            {
                preview.Yaw += AutoTurnDegreesPerSecond * dt;
                Repaint();
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                settings = (WorkerSettings)EditorGUILayout.ObjectField(settings, typeof(WorkerSettings), false, GUILayout.Width(160f));
                GUILayout.Label("Облик", GUILayout.Width(40f));
                lookIndex = EditorGUILayout.IntSlider(lookIndex, 0, 15, GUILayout.Width(150f));
                beard = (WorkerPreview.BeardMode)EditorGUILayout.EnumPopup(beard, EditorStyles.toolbarPopup, GUILayout.Width(70f));
                wireframe = GUILayout.Toggle(wireframe, "Рёбра", EditorStyles.toolbarButton);
                showGrid = GUILayout.Toggle(showGrid, "Пол", EditorStyles.toolbarButton);
                if (EditorGUI.EndChangeCheck())
                    Apply();
                autoTurn = GUILayout.Toggle(autoTurn, "Поворот", EditorStyles.toolbarButton);
                if (GUILayout.Button("Спереди", EditorStyles.toolbarButton))
                    preview.ResetView(preview.Height);
                if (GUILayout.Button("Сбоку", EditorStyles.toolbarButton))
                    preview.Yaw = 90f;
                if (GUILayout.Button("Сзади", EditorStyles.toolbarButton))
                    preview.Yaw = 0f;
                if (GUILayout.Button("Голова", EditorStyles.toolbarButton))
                {
                    preview.Pivot = new Vector3(0f, preview.Height * 0.88f, 0f);
                    preview.Distance = preview.Height * 0.55f;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{preview.TriangleCount} треуг.", EditorStyles.miniLabel);
            }

            var rect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f);
            HandleMouse(rect);
            if (Event.current.type == EventType.Repaint)
                preview.Draw(rect);
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 20f, rect.width, 18f),
                "ЛКМ — вращать, колесо — ближе/дальше, СКМ/ПКМ — сдвиг", EditorStyles.whiteMiniLabel);
        }

        private void Apply()
        {
            preview.Settings = settings;
            preview.LookIndex = lookIndex;
            preview.Beard = beard;
            preview.Wireframe = wireframe;
            preview.ShowGrid = showGrid;
            preview.Rebuild();
            settingsDirtyCount = settings != null ? EditorUtility.GetDirtyCount(settings) : -1;
        }

        private void HandleMouse(Rect rect)
        {
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    if (e.button == 0 && !e.alt)
                    {
                        preview.Yaw += e.delta.x * 0.5f;
                        preview.Pitch = Mathf.Clamp(preview.Pitch + e.delta.y * 0.5f, -85f, 85f);
                    }
                    else
                    {
                        // Сдвиг в плоскости экрана, пропорционально расстоянию.
                        var rotation = Quaternion.Euler(preview.Pitch, preview.Yaw, 0f);
                        var scale = preview.Distance * 0.0015f;
                        preview.Pivot += rotation * new Vector3(-e.delta.x * scale, e.delta.y * scale, 0f);
                    }
                    e.Use();
                    Repaint();
                    break;
                case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                    preview.Distance = Mathf.Clamp(preview.Distance * (1f + e.delta.y * 0.04f), 0.15f, 20f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        private static WorkerSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(WorkerSettings));
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<WorkerSettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
