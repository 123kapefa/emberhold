using Emberhold.Gameplay.Workers;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Editor.Workers
{
    /// <summary>
    /// Окно Emberhold → Worker Viewer: рабочий на нейтральном фоне без сцены и Play. Левая кнопка мыши — вращение
    /// вокруг, колесо — приближение, средняя или правая — сдвиг. Роль — из списка или стрелками ◀ ▶ (и клавишами ← →),
    /// «Все роли» ставит их в ряд. Третья строка — анимация: выбор, ▶/❚❚ (пробел), скорость, ноша, ползунок времени.
    /// Облик — по номеру рабочего из WorkerSettings; после перекомпиляции кода и правки настроек модель перестраивается сама.
    /// </summary>
    public sealed class WorkerViewerWindow : EditorWindow
    {
        private const float AutoTurnDegreesPerSecond = 30f;

        [SerializeField] private WorkerSettings settings;
        [SerializeField] private int lookIndex;
        [SerializeField] private WorkerRole role;
        [SerializeField] private bool lineup;
        [SerializeField] private WorkerPreview.BeardMode beard;
        [SerializeField] private bool wireframe;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool autoTurn;
        [SerializeField] private WorkerClip clip = WorkerClip.Idle;
        [SerializeField] private WorkerCarry carry = WorkerCarry.Log;
        [SerializeField] private bool playing = true;
        [SerializeField] private float playbackSpeed = 1f;

        private WorkerPreview preview;
        private int settingsDirtyCount = -1;
        private (WorkerSettings, int, WorkerRole, bool, WorkerPreview.BeardMode, int) builtKey;
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
            if (playing)
            {
                preview.Advance(dt * playbackSpeed);
                Repaint();
            }
        }

        private void OnGUI()
        {
            HandleKeys();
            EditorGUI.BeginChangeCheck();
            var wasLineup = lineup;
            // Первая строка — что смотреть: роль или все роли, ракурсы.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Роль", GUILayout.Width(32f));
                using (new EditorGUI.DisabledScope(lineup))
                {
                    if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                        role = Step(role, -1);
                    role = (WorkerRole)EditorGUILayout.EnumPopup(role, EditorStyles.toolbarPopup, GUILayout.Width(95f));
                    if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                        role = Step(role, 1);
                }
                lineup = GUILayout.Toggle(lineup, "Все роли", EditorStyles.toolbarButton);
                GUILayout.Space(12f);
                if (GUILayout.Button("Спереди", EditorStyles.toolbarButton))
                    preview.ResetView(preview.Height);
                if (GUILayout.Button("Сбоку", EditorStyles.toolbarButton))
                    preview.Yaw = 90f;
                if (GUILayout.Button("Сзади", EditorStyles.toolbarButton))
                    preview.Yaw = 0f;
                using (new EditorGUI.DisabledScope(lineup))
                {
                    if (GUILayout.Button("Голова", EditorStyles.toolbarButton))
                    {
                        preview.Pivot = new Vector3(0f, preview.Height * 0.88f, 0f);
                        preview.Distance = preview.Height * 0.55f;
                    }
                }
                autoTurn = GUILayout.Toggle(autoTurn, "Поворот", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                if (!lineup)
                    GUILayout.Label($"{preview.TriangleCount} треуг.", EditorStyles.miniLabel);
            }
            // Вторая строка — чей облик: настройки, номер рабочего, борода, вспомогательное.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                settings = (WorkerSettings)EditorGUILayout.ObjectField(settings, typeof(WorkerSettings), false, GUILayout.Width(150f));
                GUILayout.Label("Облик", GUILayout.Width(40f));
                lookIndex = EditorGUILayout.IntSlider(lookIndex, 0, 15, GUILayout.Width(150f));
                GUILayout.Label("Борода", GUILayout.Width(46f));
                beard = (WorkerPreview.BeardMode)EditorGUILayout.EnumPopup(beard, EditorStyles.toolbarPopup, GUILayout.Width(70f));
                wireframe = GUILayout.Toggle(wireframe, "Рёбра", EditorStyles.toolbarButton);
                showGrid = GUILayout.Toggle(showGrid, "Пол", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
            }
            // Третья строка — анимация.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Анимация", GUILayout.Width(62f));
                var newClip = (WorkerClip)EditorGUILayout.EnumPopup(clip, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                if (newClip != clip)
                {
                    clip = newClip;
                    preview.Time = 0f;
                    // Рабочая анимация роли — сразу у этой роли: инструмент в руке только у неё.
                    if (WorkerAnimation.RoleOf(clip) is { } owner)
                        role = owner;
                }
                if (GUILayout.Button(playing ? "❚❚" : "▶", EditorStyles.toolbarButton, GUILayout.Width(26f)))
                    playing = !playing;
                GUILayout.Label("×", GUILayout.Width(12f));
                playbackSpeed = EditorGUILayout.Slider(playbackSpeed, 0.1f, 2f, GUILayout.Width(130f));
                GUILayout.Label("Ноша", GUILayout.Width(36f));
                carry = (WorkerCarry)EditorGUILayout.EnumPopup(carry, EditorStyles.toolbarPopup, GUILayout.Width(80f));
                var length = preview.OneShotLength > 0f ? preview.OneShotLength : 4f;
                var scrub = EditorGUILayout.Slider(preview.Time % length, 0f, length, GUILayout.MinWidth(120f));
                if (!Mathf.Approximately(scrub, preview.Time % length))
                {
                    preview.Time = scrub;
                    playing = false;
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                Apply();
                if (wasLineup != lineup)
                    preview.ResetView(preview.Height);
            }

            var rect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f);
            HandleMouse(rect);
            if (Event.current.type == EventType.Repaint)
                preview.Draw(rect);
            GUI.Label(new Rect(rect.x + 6f, rect.yMax - 20f, rect.width, 18f),
                "ЛКМ — вращать, колесо — ближе/дальше, СКМ/ПКМ — сдвиг, ← → — роль, пробел — пауза", EditorStyles.whiteMiniLabel);
            if (!lineup)
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width, 18f), RoleName(role), EditorStyles.whiteLargeLabel);
        }

        private void Apply()
        {
            preview.Settings = settings;
            preview.LookIndex = lookIndex;
            preview.Role = role;
            preview.Lineup = lineup;
            preview.Beard = beard;
            preview.Wireframe = wireframe;
            preview.ShowGrid = showGrid;
            preview.Clip = clip;
            preview.Carry = carry;
            // Сетки перестраиваются, только если поменялось то, из чего они строятся; анимация и ноша — без перестройки.
            var dirty = settings != null ? EditorUtility.GetDirtyCount(settings) : -1;
            var key = (settings, lookIndex, role, lineup, beard, dirty);
            if (!key.Equals(builtKey) || preview.FigureCount == 0)
            {
                preview.Rebuild();
                builtKey = key;
            }
            settingsDirtyCount = dirty;
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

        /// <summary>← и → листают роли, когда окно в фокусе.</summary>
        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
            {
                playing = !playing;
                e.Use();
                return;
            }
            if (lineup || e.type != EventType.KeyDown || (e.keyCode != KeyCode.LeftArrow && e.keyCode != KeyCode.RightArrow))
                return;
            role = Step(role, e.keyCode == KeyCode.LeftArrow ? -1 : 1);
            Apply();
            e.Use();
            Repaint();
        }

        private static WorkerRole Step(WorkerRole current, int delta)
        {
            var count = System.Enum.GetValues(typeof(WorkerRole)).Length;
            return (WorkerRole)(((int)current + delta + count) % count);
        }

        private static string RoleName(WorkerRole role) => role switch
        {
            WorkerRole.Free => "Свободный",
            WorkerRole.Woodcutter => "Лесоруб",
            WorkerRole.Digger => "Копатель",
            WorkerRole.Builder => "Строитель",
            WorkerRole.Stoker => "Истопник",
            WorkerRole.Swordsman => "Мечник",
            WorkerRole.Archer => "Лучник",
            WorkerRole.Spearman => "Копейщик",
            _ => role.ToString(),
        };

        private static WorkerSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(WorkerSettings));
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<WorkerSettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }
    }
}
