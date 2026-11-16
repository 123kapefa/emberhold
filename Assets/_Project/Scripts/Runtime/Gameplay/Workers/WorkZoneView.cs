using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Заглушка вида зон работ, пока у них нет своего облика. Зона рубки (#167) — круг по земле, зона копки (#136) —
    /// коробка от низа до верха (какие высоты захвачены) и полупрозрачные объёмы. Черновик (ещё не начатая зона) — пунктирный бледный
    /// край и бледные объёмы тех же цветов, в подписи «черновик», в панели кнопка «Начать». Объёмы: жёлтый — что выкопать (выше уровня), голубой — что
    /// нарастить до уровня (покажет и выравнивание #169). Объёмы — один меш на зону, пересобираются, только когда зона или
    /// мир поменялись. Приоритет на карте не показывается (он виден в панели). Над зоной подпись,
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

        // Линии: 0 — края работающих зон, 1 — выбранная зона, 2 — будущая зона, 3 — края черновиков (пунктир).
        private const int ZoneLines = 0;
        private const int SelectedLines = 1;
        private const int PreviewLines = 2;
        private const int DraftLines = 3;
        private const int HandleLines = 4;
        private const int HoverHandleLines = 5;

        /// <summary>Ближе скольких пикселей экрана к ручке курсор её берёт.</summary>
        private const float HandlePickPixels = 16f;

        /// <summary>Полуразмер квадратика ручки, в блоках.</summary>
        private const float HandleSize = 0.22f;

        /// <summary>Во сколько раз бледнее объёмы черновика.</summary>
        private const float DraftAlpha = 0.45f;

        [SerializeField] private Color zoneColor = new(0.9f, 0.9f, 0.85f);
        [SerializeField] private Color selectedColor = Color.white;
        [SerializeField] private Color previewColor = new(0.9f, 0.9f, 0.85f, 0.5f);
        [SerializeField] private Color draftColor = new(0.75f, 0.75f, 0.7f, 0.6f);
        [SerializeField] private Color handleColor = Color.white;
        [SerializeField] private Color hoverHandleColor = new(1f, 0.55f, 0.1f);
        [SerializeField] private Color digColor = new(1f, 0.85f, 0.15f, 0.35f);
        [SerializeField] private Color fillColor = new(0.3f, 0.8f, 1f, 0.35f);

        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();
        private readonly List<Vector3> volumeVertices = new();
        private readonly List<int> digTriangles = new();
        private readonly List<int> fillTriangles = new();
        private readonly GameObject[] lines = new GameObject[6];
        private readonly Dictionary<LevelZone, Volume> volumes = new();
        private readonly List<LevelZone> goneVolumes = new();
        private WorkerCrew crew;
        private BlockEditController editor;
        private Transform space;
        private Material material;
        private Material volumeMaterial;
        private bool ownsVolumeMaterial;
        private Volume preview;
        private GUIStyle labelStyle;
        private readonly List<ZoneHandle> handles = new();
        // Ручка под курсором и та, которую тянут (номер в раскладке выбранной зоны); −1 — нет.
        private int hoveredHandle = -1;
        private int draggedHandle = -1;
        private Plane dragPlane;
        private Vector2 grabOffset;
        private float grabHeight;
        private int grabLevel;
        // Растёт, когда в мире поставили или убрали блок: объёмы зон копки пора пересобрать.
        private int worldStamp;

        /// <summary>
        /// Связать с отрядом и редактором: space — мир блоков, material — Unlit-материал линий (цвет через _BaseColor),
        /// volumeMaterial — прозрачный Unlit-материал объёмов (null — сделать его из material).
        /// </summary>
        public void Bind(WorkerCrew crew, BlockEditController editor, Transform space, Material material, Material volumeMaterial = null)
        {
            this.crew = crew;
            this.editor = editor;
            this.space = space;
            this.material = material;
            this.volumeMaterial = volumeMaterial;
            if (editor != null)
            {
                editor.BlockChanged += OnBlockChanged;
                editor.BlocksFell += OnBlocksFell;
            }
        }

        /// <summary>
        /// Подпись над зоной: «рубка · люди 1/2», «выровнять до 5 · люди 0/2» (или «выкопать до», «нарастить до» — по режиму),
        /// у черновика — «черновик · выровнять до 5 · люди 0/2». Приоритет — только в панели.
        /// </summary>
        public static string Label(WorkZone zone)
        {
            var what = zone is LevelZone level ? $"{ModeVerb(level.Mode)} до {level.Level}" : "рубка";
            var draft = zone.IsDraft ? "черновик · " : "";
            return $"{draft}{what} · люди {zone.Assignment.Workers.Count}/{zone.Assignment.MaxWorkers}";
        }

        /// <summary>Режим зоны с уровнем глаголом: «выкопать», «нарастить», «выровнять».</summary>
        public static string ModeVerb(LevelZoneMode mode) => mode switch
        {
            LevelZoneMode.Dig => "выкопать",
            LevelZoneMode.Fill => "нарастить",
            _ => "выровнять",
        };

        private static string ModeHint(LevelZoneMode mode) => mode switch
        {
            LevelZoneMode.Dig => "Выкопать: всё выше уровня (жёлтое)",
            LevelZoneMode.Fill => "Нарастить: землёй до уровня (голубое)",
            _ => "Выровнять: сначала выкопать жёлтое, потом нарастить голубое",
        };

        private static readonly LevelZoneMode[] Modes = { LevelZoneMode.Dig, LevelZoneMode.Fill, LevelZoneMode.Level };

        private void OnBlockChanged(Vector3Int _) => worldStamp++;

        private void OnBlocksFell(IReadOnlyList<Emberhold.World.Editing.BlockFall> _) => worldStamp++;

        /// <summary>Указатель (экранные координаты Input System, y снизу) над панелью зоны.</summary>
        public bool IsOverPanel(Vector2 pointer) => PanelShown && PanelRect().Contains(new Vector2(pointer.x, Screen.height - pointer.y));

        private bool ZoneTool => editor != null && editor.Tool is BlockTool.ChopZone or BlockTool.DigZone;

        private bool PanelShown => crew != null && ZoneTool && crew.SelectedZone != null;

        private Rect PanelRect()
        {
            var height = crew != null && crew.SelectedZone is LevelZone ? 330f : 178f;
            if (crew != null && crew.SelectedZone is { IsDraft: true })
                height += 46f;
            return new Rect((Screen.width - PanelWidth) * 0.5f, Screen.height - height - PanelBottom, PanelWidth, height);
        }

        private void LateUpdate()
        {
            if (crew == null || space == null)
                return;
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] ??= CreateLines(i);
                lines[i].transform.SetPositionAndRotation(space.position, space.rotation);
                lines[i].transform.localScale = space.lossyScale;
            }
            foreach (var (index, drafts) in new[] { (ZoneLines, false), (DraftLines, true) })
            {
                Begin();
                foreach (var zone in crew.Zones.All)
                {
                    if (zone.IsDraft == drafts)
                        AppendOutline(zone, 0f, drafts);
                }
                End(lines[index]);
            }
            UpdateVolumes();
            Begin();
            if (PanelShown)
                AppendOutline(crew.SelectedZone, 0.15f);
            End(lines[SelectedLines]);
            Begin();
            AppendPreview();
            End(lines[PreviewLines]);
            DrawHandles();
        }

        /// <summary>
        /// Ручки выбранной зоны (#136): навести и потянуть ЛКМ. Зовёт редактор раз в кадр раньше своих кликов;
        /// true — курсор над ручкой или ручку тянут: клик и протяжка достаются ручке, а не миру. Изменения сразу идут в зону
        /// (<see cref="WorkZones"/>): у работающей пересчитываются приказы, как от кнопок панели.
        /// </summary>
        public bool UpdateHandles(UnityEngine.Camera camera, Vector2 pointer, bool pressed, bool held)
        {
            if (!PanelShown || camera == null)
            {
                hoveredHandle = draggedHandle = -1;
                return false;
            }
            var zone = crew.SelectedZone;
            ZoneHandleLayout.Build(zone, crew.GroundHeight, handles);
            var worldRay = camera.ScreenPointToRay(pointer);
            var ray = new Ray(space.InverseTransformPoint(worldRay.origin), space.InverseTransformVector(worldRay.direction));
            if (draggedHandle >= 0)
            {
                if (!held || draggedHandle >= handles.Count)
                {
                    draggedHandle = -1;
                    return true;
                }
                Drag(zone, handles[draggedHandle], ray);
                return true;
            }
            hoveredHandle = NearestHandle(camera, pointer);
            if (hoveredHandle < 0)
                return false;
            if (pressed)
                StartDrag(zone, handles[hoveredHandle], ray, camera);
            return true;
        }

        private int NearestHandle(UnityEngine.Camera camera, Vector2 pointer)
        {
            var best = -1;
            var bestDistance = HandlePickPixels;
            for (var i = 0; i < handles.Count; i++)
            {
                var screen = camera.WorldToScreenPoint(space.TransformPoint(handles[i].Position));
                if (screen.z <= 0f)
                    continue;
                var distance = Vector2.Distance(new Vector2(screen.x, screen.y), pointer);
                if (distance >= bestDistance)
                    continue;
                best = i;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// Взять ручку: стороны, перенос и радиус тянутся по горизонтальной плоскости на высоте ручки, уровень — по
        /// вертикальной плоскости через стрелку, повёрнутой к камере. Так шаг не зависит от поворота камеры.
        /// </summary>
        private void StartDrag(WorkZone zone, ZoneHandle handle, Ray ray, UnityEngine.Camera camera)
        {
            if (handle.Kind is ZoneHandleKind.Level or ZoneHandleKind.Top or ZoneHandleKind.Bottom)
            {
                var facing = space.InverseTransformVector(camera.transform.forward);
                facing.y = 0f;
                if (facing.sqrMagnitude < 1e-6f)
                    facing = Vector3.forward;
                dragPlane = new Plane(facing.normalized, handle.Position);
            }
            else
                dragPlane = new Plane(Vector3.up, handle.Position);
            if (!dragPlane.Raycast(ray, out var enter))
                return;
            var hit = ray.GetPoint(enter);
            grabHeight = hit.y;
            grabLevel = zone is LevelZone level
                ? handle.Kind switch
                {
                    ZoneHandleKind.Top => level.Top,
                    ZoneHandleKind.Bottom => level.Bottom,
                    _ => level.Level,
                }
                : 0;
            grabOffset = zone switch
            {
                LevelZone levelZone => new Vector2(hit.x - levelZone.Min.x, hit.z - levelZone.Min.y),
                ChopZone chop => new Vector2(hit.x - chop.Cell.x, hit.z - chop.Cell.z),
                _ => Vector2.zero,
            };
            draggedHandle = hoveredHandle;
        }

        private void Drag(WorkZone zone, ZoneHandle handle, Ray ray)
        {
            if (!dragPlane.Raycast(ray, out var enter))
                return;
            var hit = ray.GetPoint(enter);
            var orders = crew.Orders;
            switch (handle.Kind)
            {
                case ZoneHandleKind.Edge when zone is LevelZone level:
                    // Сторона встаёт на ближайшую к курсору границу столбцов.
                    var column = new Vector2Int(Mathf.RoundToInt(hit.x) - (handle.Dx > 0 ? 1 : 0), Mathf.RoundToInt(hit.z) - (handle.Dz > 0 ? 1 : 0));
                    crew.Zones.DragEdges(level, handle.Dx, handle.Dz, column, orders);
                    break;
                case ZoneHandleKind.Level when zone is LevelZone level:
                    crew.Zones.SetLevel(level, Mathf.Max(0, grabLevel + Mathf.RoundToInt(hit.y - grabHeight)), orders);
                    break;
                case ZoneHandleKind.Top when zone is LevelZone level:
                    crew.Zones.SetTop(level, Mathf.Max(0, grabLevel + Mathf.RoundToInt(hit.y - grabHeight)), orders);
                    break;
                case ZoneHandleKind.Bottom when zone is LevelZone level:
                    crew.Zones.SetBottom(level, Mathf.Max(0, grabLevel + Mathf.RoundToInt(hit.y - grabHeight)), orders);
                    break;
                case ZoneHandleKind.Move when zone is LevelZone level:
                    crew.Zones.Move(level, new Vector2Int(Mathf.RoundToInt(hit.x - grabOffset.x), Mathf.RoundToInt(hit.z - grabOffset.y)), orders);
                    break;
                case ZoneHandleKind.Move when zone is ChopZone chop:
                    crew.Zones.Move(chop, new Vector3Int(Mathf.RoundToInt(hit.x - grabOffset.x), chop.Cell.y, Mathf.RoundToInt(hit.z - grabOffset.y)), orders);
                    break;
                case ZoneHandleKind.Radius when zone is ChopZone chop:
                    crew.Zones.SetRadiusTo(chop, new Vector2(hit.x, hit.z), orders);
                    break;
            }
        }

        /// <summary>Ручки выбранной зоны: квадратики (перенос — крест, уровень — стрелка), под курсором и в руке — другим цветом.</summary>
        private void DrawHandles()
        {
            if (!PanelShown)
            {
                hoveredHandle = draggedHandle = -1;
                lines[HandleLines].SetActive(false);
                lines[HoverHandleLines].SetActive(false);
                return;
            }
            ZoneHandleLayout.Build(crew.SelectedZone, crew.GroundHeight, handles);
            var active = draggedHandle >= 0 ? draggedHandle : hoveredHandle;
            foreach (var (index, hot) in new[] { (HandleLines, false), (HoverHandleLines, true) })
            {
                Begin();
                for (var i = 0; i < handles.Count; i++)
                {
                    if ((i == active) == hot)
                        AppendHandle(handles[i]);
                }
                End(lines[index]);
            }
        }

        private void AppendHandle(ZoneHandle handle)
        {
            var p = handle.Position;
            const float s = HandleSize;
            switch (handle.Kind)
            {
                case ZoneHandleKind.Level:
                case ZoneHandleKind.Top:
                case ZoneHandleKind.Bottom:
                    // Стрелка вверх-вниз.
                    AppendSegment(p + Vector3.down * 0.7f, p + Vector3.up * 0.7f);
                    AppendSegment(p + Vector3.up * 0.7f, p + new Vector3(-s, 0.7f - s, 0f));
                    AppendSegment(p + Vector3.up * 0.7f, p + new Vector3(s, 0.7f - s, 0f));
                    AppendSegment(p + Vector3.down * 0.7f, p + new Vector3(-s, -0.7f + s, 0f));
                    AppendSegment(p + Vector3.down * 0.7f, p + new Vector3(s, -0.7f + s, 0f));
                    break;
                case ZoneHandleKind.Move:
                    AppendSegment(p + new Vector3(-s * 2f, 0f, 0f), p + new Vector3(s * 2f, 0f, 0f));
                    AppendSegment(p + new Vector3(0f, 0f, -s * 2f), p + new Vector3(0f, 0f, s * 2f));
                    AppendSquare(p, s);
                    break;
                default:
                    AppendSquare(p, s);
                    break;
            }
        }

        private void AppendSquare(Vector3 p, float s)
        {
            AppendSegment(p + new Vector3(-s, 0f, -s), p + new Vector3(s, 0f, -s));
            AppendSegment(p + new Vector3(s, 0f, -s), p + new Vector3(s, 0f, s));
            AppendSegment(p + new Vector3(s, 0f, s), p + new Vector3(-s, 0f, s));
            AppendSegment(p + new Vector3(-s, 0f, s), p + new Vector3(-s, 0f, -s));
        }

        private void AppendSegment(Vector3 a, Vector3 b)
        {
            indices.Add(vertices.Count);
            vertices.Add(a);
            indices.Add(vertices.Count);
            vertices.Add(b);
        }

        /// <summary>Объёмы зон копки: новые — завести, ушедшие — убрать, изменившиеся (зона или мир) — пересобрать.</summary>
        private void UpdateVolumes()
        {
            foreach (var zone in crew.Zones.All)
            {
                if (zone is not LevelZone level)
                    continue;
                if (!volumes.TryGetValue(level, out var volume))
                    volumes[level] = volume = CreateVolume($"Dig Zone Volume {volumes.Count + 1}");
                Place(volume.Renderer.transform);
                SetDraftLook(volume, level.IsDraft);
                Rebuild(volume, level.Min, level.Size, level.Level, level.Top, level.Bottom, level.Mode);
            }
            goneVolumes.Clear();
            foreach (var pair in volumes)
            {
                if (!crew.Zones.Contains(pair.Key))
                    goneVolumes.Add(pair.Key);
            }
            foreach (var zone in goneVolumes)
            {
                DestroyVolume(volumes[zone]);
                volumes.Remove(zone);
            }
        }

        /// <summary>
        /// Пересобрать объём, только если прямоугольник, уровень, режим или мир поменялись с прошлой сборки, и показать его.
        /// Режим без копки не рисует жёлтое, без наращивания — голубое.
        /// </summary>
        private void Rebuild(Volume volume, Vector2Int min, Vector2Int size, int level, int top, int bottom, LevelZoneMode mode)
        {
            var key = (min, size, level, top, bottom, mode, worldStamp, crew.ForestVersion);
            if (!volume.Key.Equals(key))
            {
                volume.Key = key;
                var world = crew.World;
                var forest = crew.ForestModel;
                LevelZoneShape.Build(min, size, level, top, bottom, crew.GroundHeight,
                    column => LevelZone.TryTopToDig(world, forest, column.x, column.y, level, top, out _),
                    volumeVertices, digTriangles, fillTriangles, mode != LevelZoneMode.Fill, mode != LevelZoneMode.Dig,
                    column => LevelZone.FillFrom(world, forest, crew.Zones.IsBlocked, crew.GroundHeight, column.x, column.y, level, bottom));
                var mesh = volume.Mesh;
                mesh.Clear();
                mesh.subMeshCount = 2;
                mesh.SetVertices(volumeVertices);
                mesh.SetTriangles(digTriangles, 0, false);
                mesh.SetTriangles(fillTriangles, 1, false);
                mesh.RecalculateBounds();
                volume.HasGeometry = volumeVertices.Count > 0;
            }
            volume.Renderer.gameObject.SetActive(volume.HasGeometry);
        }

        /// <summary>Будущая зона под курсором: круг рубки или прямоугольник копки от начала протяжки до курсора с объёмами.</summary>
        private void AppendPreview()
        {
            preview ??= CreateVolume("Dig Zone Preview Volume");
            // Будущая зона встанет черновиком — и выглядит так же.
            SetDraftLook(preview, true);
            Place(preview.Renderer.transform);
            if (!AppendPreviewShape())
                preview.Renderer.gameObject.SetActive(false);
        }

        /// <summary>Линии будущей зоны; true — показан и объём будущей зоны копки.</summary>
        private bool AppendPreviewShape()
        {
            // Курсор на ручке выбранной зоны: новую зону не предлагаем.
            if (editor == null || editor.Hovered is not { } hit || hoveredHandle >= 0 || draggedHandle >= 0)
                return false;
            if (editor.Tool == BlockTool.ChopZone)
            {
                var cell = hit.Block + Vector3Int.up;
                if (crew.Zones.At(cell, WorkKind.Chop) == null)
                    AppendCircle(cell, crew.NewZoneRadius);
                return false;
            }
            if (editor.Tool != BlockTool.DigZone)
                return false;
            var from = editor.DigZoneDragFrom ?? hit.Block;
            if (editor.DigZoneDragFrom == null && crew.Zones.At(from, WorkKind.Dig) != null)
                return false;
            var min = new Vector2Int(Mathf.Min(from.x, hit.Block.x), Mathf.Min(from.z, hit.Block.z));
            var size = new Vector2Int(Mathf.Abs(hit.Block.x - from.x) + 1, Mathf.Abs(hit.Block.z - from.z) + 1);
            var level = from.y - 1;
            var (top, bottom) = LevelZone.DefaultBounds(crew.World, min, size, level);
            AppendBox(min, size, bottom, top, 0f);
            Rebuild(preview, min, size, level, top, bottom, LevelZoneMode.Level);
            return true;
        }

        private void OnDestroy()
        {
            if (editor != null)
            {
                editor.BlockChanged -= OnBlockChanged;
                editor.BlocksFell -= OnBlocksFell;
            }
            foreach (var volume in volumes.Values)
                DestroyVolume(volume);
            volumes.Clear();
            if (preview != null)
                DestroyVolume(preview);
            if (ownsVolumeMaterial && volumeMaterial != null)
                Destroy(volumeMaterial);
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
            if (zone.IsDraft)
                GUILayout.Label("Черновик: рабочие не придут, пока не нажмёшь «Начать»");
            if (zone is ChopZone chop)
            {
                GUILayout.Label("Зона рубки: деревья и пни в круге");
                var radius = Stepper("Радиус", chop.Radius.ToString(), chop.Radius > ChopZone.MinRadius, chop.Radius < ChopZone.MaxRadius);
                if (radius != 0)
                    crew.Zones.SetRadius(chop, chop.Radius + radius, crew.Orders);
            }
            else if (zone is LevelZone level)
            {
                GUILayout.Label(ModeHint(level.Mode));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Режим", GUILayout.Width(80f));
                foreach (var mode in Modes)
                {
                    var name = ModeVerb(mode);
                    var label = level.Mode == mode ? $"[{name}]" : name;
                    if (GUILayout.Button(label, GUILayout.Width(84f)) && level.Mode != mode)
                        crew.Zones.SetMode(level, mode, crew.Orders);
                }
                GUILayout.EndHorizontal();
                var length = Stepper("Длина", level.Size.x.ToString(), level.Size.x > LevelZone.MinSize, level.Size.x < LevelZone.MaxSize);
                if (length != 0)
                    crew.Zones.SetSize(level, level.Size + new Vector2Int(length, 0), crew.Orders);
                var width = Stepper("Ширина", level.Size.y.ToString(), level.Size.y > LevelZone.MinSize, level.Size.y < LevelZone.MaxSize);
                if (width != 0)
                    crew.Zones.SetSize(level, level.Size + new Vector2Int(0, width), crew.Orders);
                var top = Stepper("Верх", $"высота {level.Top}", level.Top > 0, true);
                if (top != 0)
                    crew.Zones.SetTop(level, level.Top + top, crew.Orders);
                var height = Stepper("Уровень", $"высота {level.Level}", level.Level > 0, true);
                if (height != 0)
                    crew.Zones.SetLevel(level, level.Level + height, crew.Orders);
                var bottom = Stepper("Низ", $"высота {level.Bottom}", level.Bottom > 0, true);
                if (bottom != 0)
                    crew.Zones.SetBottom(level, level.Bottom + bottom, crew.Orders);
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
            if (zone.IsDraft && GUILayout.Button("Начать (Enter)"))
                crew.StartZone(zone);
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

        private void Begin()
        {
            vertices.Clear();
            indices.Clear();
        }

        /// <summary>Граница зоны по земле; inset — насколько отступить внутрь (рамка выбранной зоны), dashed — пунктиром.</summary>
        private void AppendOutline(WorkZone zone, float inset, bool dashed = false)
        {
            if (zone is ChopZone chop)
                AppendCircle(chop.Cell, chop.Radius - inset, dashed);
            else if (zone is LevelZone level)
                AppendBox(level.Min, level.Size, level.Bottom, level.Top, inset, dashed);
        }

        /// <summary>
        /// Коробка зоны с уровнем: прямоугольник столбцов от низа (верх блока bottom) до верха (верх блока top) — видно,
        /// какие высоты захвачены. dashed — пунктиром (черновик).
        /// </summary>
        private void AppendBox(Vector2Int min, Vector2Int size, int bottom, int top, float inset, bool dashed = false)
        {
            var x0 = min.x + inset;
            var z0 = min.y + inset;
            var x1 = min.x + size.x - inset;
            var z1 = min.y + size.y - inset;
            var y0 = bottom + 1f + Lift;
            var y1 = top + 1f + Lift;
            var corners = new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                AppendEdge(new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), dashed);
                if (y1 > y0 + 1e-3f)
                {
                    AppendEdge(new Vector3(a.x, y1, a.y), new Vector3(b.x, y1, b.y), dashed);
                    AppendEdge(new Vector3(a.x, y0, a.y), new Vector3(a.x, y1, a.y), dashed);
                }
            }
        }

        /// <summary>Ребро; пунктиром — кусками по полблока через один.</summary>
        private void AppendEdge(Vector3 a, Vector3 b, bool dashed)
        {
            if (!dashed)
            {
                AppendSegment(a, b);
                return;
            }
            var steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) * 2f));
            for (var i = 0; i < steps; i += 2)
                AppendSegment(Vector3.Lerp(a, b, (float)i / steps), Vector3.Lerp(a, b, (float)(i + 1) / steps));
        }

        /// <summary>Круг по земле вокруг середины клетки center, в координатах мира блоков.</summary>
        private void AppendCircle(Vector3Int center, float radius, bool dashed = false)
        {
            var first = vertices.Count;
            for (var i = 0; i < Segments; i++)
            {
                var angle = i * Mathf.PI * 2f / Segments;
                var x = center.x + 0.5f + Mathf.Cos(angle) * radius;
                var z = center.z + 0.5f + Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, Ground(x, z), z));
                if (dashed && i % 2 == 1)
                    continue;
                indices.Add(first + i);
                indices.Add(first + (i + 1) % Segments);
            }
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
                ZoneLines => zoneColor,
                SelectedLines => selectedColor,
                DraftLines => draftColor,
                HandleLines => handleColor,
                HoverHandleLines => hoverHandleColor,
                _ => previewColor,
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
    
        private void Place(Transform target)
        {
            target.SetPositionAndRotation(space.position, space.rotation);
            target.localScale = space.lossyScale;
        }

        /// <summary>Объект объёма зоны копки: один меш, две части (жёлтая «выкопать», голубая «нарастить»).</summary>
        private Volume CreateVolume(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var shared = VolumeMaterial();
            renderer.sharedMaterials = new[] { shared, shared };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            var volume = new Volume(renderer, mesh);
            SetDraftLook(volume, false, true);
            return volume;
        }

        /// <summary>Цвета объёма: у черновика те же жёлтый и голубой, но бледнее.</summary>
        private void SetDraftLook(Volume volume, bool draft, bool force = false)
        {
            if (!force && volume.IsDraftLook == draft)
                return;
            volume.IsDraftLook = draft;
            var scale = draft ? DraftAlpha : 1f;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, new Color(digColor.r, digColor.g, digColor.b, digColor.a * scale));
            volume.Renderer.SetPropertyBlock(properties, 0);
            properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * scale));
            volume.Renderer.SetPropertyBlock(properties, 1);
        }

        /// <summary>
        /// Прозрачный материал объёмов: из сцены, а если не назначен — копия материала линий, переключённая в прозрачный
        /// режим URP Unlit.
        /// </summary>
        private Material VolumeMaterial()
        {
            if (volumeMaterial != null)
                return volumeMaterial;
            volumeMaterial = new Material(material) { name = "Work Zone Volume (runtime)", renderQueue = (int)RenderQueue.Transparent };
            volumeMaterial.SetFloat("_Surface", 1f);
            volumeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            volumeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            volumeMaterial.SetFloat("_ZWrite", 0f);
            volumeMaterial.SetOverrideTag("RenderType", "Transparent");
            volumeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ownsVolumeMaterial = true;
            return volumeMaterial;
        }

        private static void DestroyVolume(Volume volume)
        {
            if (volume.Renderer == null)
                return;
            Destroy(volume.Mesh);
            Destroy(volume.Renderer.gameObject);
        }

        private sealed class Volume
        {
            public Volume(MeshRenderer renderer, Mesh mesh)
            {
                Renderer = renderer;
                Mesh = mesh;
            }

            public MeshRenderer Renderer { get; }
            public Mesh Mesh { get; }
            public bool HasGeometry { get; set; }
            public bool IsDraftLook { get; set; }

            /// <summary>С чем объём собран в последний раз: прямоугольник, уровень, отметка мира и леса.</summary>
            public (Vector2Int, Vector2Int, int, int, int, LevelZoneMode, int, int) Key { get; set; } = (default, default, int.MinValue, 0, 0, default, -1, -1);
        }
}
}
