using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.World.Generation;
using Emberhold.World.Meshing;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Emberhold.World
{
    /// <summary>
    /// Показывает воксельный мир в сцене: на каждый чанк один GameObject с мешем и коллайдером на слое Ground.
    /// В Awake генерирует рельеф по настройкам и зонам местности сцены; каждый кадр перестраивает только изменённые чанки.
    /// Вне Play тот же рельеф строится по кнопке инспектора (<see cref="BuildPreview"/>) во временные объекты,
    /// которые не сохраняются в сцену.
    /// Отдельные блоки можно спрятать из картинки (<see cref="HideBlock"/>), пока их показывает кто-то другой,
    /// например падающий блок: данные мира и коллайдер при этом не меняются.
    /// </summary>
    public sealed class VoxelWorldView : MonoBehaviour
    {
        [Tooltip("Рельеф, который сгенерировать при старте. Пусто — мир передаст другой компонент через Show (например, песочница).")]
        [FormerlySerializedAs("valley")]
        [SerializeField] private TerrainSettings terrain;
        [Tooltip("Материал для каждого номера материала блока: 0 — воздух (не используется), 1 — трава, 2 — земля, 3 — камень, 4 — дерево.")]
        [SerializeField] private Material[] blockMaterials;
        [Tooltip("Сдвинуть мир так, чтобы центр мира оказался в начале координат сцены.")]
        [SerializeField] private bool centerOnOrigin = true;
        [Tooltip("Сглаженное освещение склонов: нормали верха блоков усредняются по соседям.")]
        [SerializeField] private bool smoothShading;

        private readonly Dictionary<Vector3Int, ChunkView> chunkViews = new();
        private readonly Dictionary<Collider, ChunkView> viewsByCollider = new();
        private readonly List<Vector3Int> dirtyBuffer = new();
        private readonly List<Material> sharedMaterials = new();
        // Спрятанные блоки и сколько раз каждый спрятан: один и тот же блок могут прятать двое.
        private readonly Dictionary<Vector3Int, int> hiddenBlocks = new();
        private readonly List<(Vector3Int block, Block value)> hiddenNearChunk = new();
        // Чанки, у которых изменилась только картинка (спрятали или вернули блок): коллайдер у них прежний.
        private readonly HashSet<Vector3Int> visualDirty = new();
        private readonly List<Color> groundColors = new();
        private Color? rockColor;
        private ChunkMeshData meshData;
        private ChunkMeshData colliderData;
        private readonly SurfaceNormals surfaceNormals = new();
        private bool missingMaterialReported;
        /// <summary>Куда кладутся чанки: сам вид в Play, временный объект предпросмотра вне Play.</summary>
        private Transform chunkParent;
        private List<TerrainZoneArea> zones = new();
        private CampaignLayout campaignLayout;
        private CampaignHeights campaign;
        private float[] zoneWeights = new float[1];

        /// <summary>Имя временного объекта с чанками предпросмотра.</summary>
        public const string PreviewName = "Предпросмотр (не сохраняется)";
        /// <summary>Предпросмотр не сохраняется и не правится руками: его нельзя случайно сдвинуть или унести из-под мира.</summary>
        private const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

        public VoxelWorld World { get; private set; }

        /// <summary>Настройки рельефа, по которым построен мир; null, если мир передали через Show.</summary>
        public TerrainSettings Terrain => terrain;

        /// <summary>Зоны местности, с которыми сгенерирован мир при старте (по ним рассаживается лес).</summary>
        public IReadOnlyList<TerrainZoneArea> Zones => zones;

        /// <summary>Сглаженное освещение склонов; при переключении весь мир перестраивается.</summary>
        public bool SmoothShading
        {
            get => smoothShading;
            set
            {
                if (smoothShading == value)
                    return;
                smoothShading = value;
                World?.MarkAllDirty();
            }
        }

        /// <summary>Есть ли настройки рельефа, по которым можно построить предпросмотр.</summary>
        public bool CanGenerate => terrain != null;

        /// <summary>Построен ли предпросмотр вне Play (в том числе оставшийся после перекомпиляции скриптов).</summary>
        public bool HasPreview => FindPreviewRoot() != null;

        private void Awake()
        {
            // Предпросмотр мог остаться в сцене, если Play запустили в обход редактора.
            ClearPreview();
            if (terrain != null)
                Show(Generate());
        }

        private VoxelWorld Generate()
        {
            zones = CollectZones(terrain.SizeInChunks * ChunkData.Size);
            zoneWeights = new float[zones.Count + 1];
            campaign = null;
            campaignLayout = null;
            if (terrain.Map == null)
                return terrain.Generate(zones);
            // С картой кампании зоны сцены не действуют: ни на рельеф, ни на лес.
            zones.Clear();
            campaignLayout = terrain.Map.ToLayout();
            return TerrainGenerator.GenerateCampaign(campaignLayout, terrain.Seed, terrain.SizeInChunks, out campaign);
        }

        /// <summary>
        /// Лес столбца мира, построенного при старте: у локации карты кампании, иначе у главного профиля
        /// (зона или фон). null — деревья не растут.
        /// </summary>
        public ForestMix ForestAt(Vector2Int column)
        {
            if (terrain == null)
                return null;
            if (campaign == null)
                return ForestScatter.MixAt(terrain.Forest, zones, new Vector2(column.x + 0.5f, column.y + 0.5f), zoneWeights);
            var owner = campaign.ColumnOwner(column.x, column.y);
            return owner == CampaignHeights.Ridge ? campaignLayout.Ridge.Forest : campaignLayout.Locations[owner - 1].Forest;
        }

        /// <summary>
        /// Вне Play: сгенерировать рельеф по настройкам и зонам сцены, как при старте, и показать его во временном
        /// объекте. Сам вид не сдвигается, сцена не меняется. Повторный вызов перестраивает предпросмотр.
        /// </summary>
        public void BuildPreview()
        {
            if (Application.isPlaying)
                return;
            ClearPreview();
            if (terrain == null)
                return;

            var world = Generate();
            var root = new GameObject(PreviewName) { hideFlags = PreviewFlags };
            root.transform.SetParent(transform, false);
            if (centerOnOrigin)
                root.transform.position = WorldOrigin(world.SizeInBlocks);
            Display(world, root.transform);
        }

        /// <summary>Убрать предпросмотр вне Play. В Play ничего не делает с показанным миром.</summary>
        public void ClearPreview()
        {
            var root = FindPreviewRoot();
            if (root == null)
                return;
            if (chunkParent == root)
            {
                ClearChunkViews();
                World = null;
                chunkParent = null;
            }
            ReleasePreview(root);
        }

        /// <summary>
        /// Убрать все предпросмотры в открытых сценах, в том числе оставшиеся без вида (компонент удалили,
        /// объект мира выключен). Редактор зовёт перед запуском Play.
        /// </summary>
        public static void ClearAllPreviews()
        {
            foreach (var view in FindObjectsByType<VoxelWorldView>(FindObjectsInactive.Include))
                view.ClearPreview();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t != null && IsPreviewRoot(t) && t.gameObject.scene.IsValid())
                    ReleasePreview(t);
        }

        private Transform FindPreviewRoot()
        {
            foreach (Transform child in transform)
                if (IsPreviewRoot(child))
                    return child;
            return null;
        }

        private static bool IsPreviewRoot(Transform t) =>
            t.name == PreviewName && (t.hideFlags & HideFlags.DontSaveInEditor) != 0;

        /// <summary>Удалить объект предпросмотра с мешами: после перекомпиляции скриптов словарь чанков пуст, а объекты остались.</summary>
        private static void ReleasePreview(Transform root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                Release(filter.sharedMesh);
            Release(root.gameObject);
        }

        /// <summary>Destroy в Play, DestroyImmediate вне Play (там Destroy не работает).</summary>
        private static void Release(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        /// <summary>Зоны местности сцены в координатах сетки мира, который встанет на место этого вида.</summary>
        private List<TerrainZoneArea> CollectZones(Vector3Int sizeInBlocks)
        {
            var result = new List<TerrainZoneArea>();
            var origin = centerOnOrigin ? WorldOrigin(sizeInBlocks) : transform.position;
            foreach (var zone in FindObjectsByType<TerrainZone>())
                if (zone.TryGetArea(origin, out var area))
                    result.Add(area);
            return result;
        }

        /// <summary>Где окажется угол (0, 0) сетки мира, если центр мира в начале координат.</summary>
        private static Vector3 WorldOrigin(Vector3Int sizeInBlocks) =>
            new(-sizeInBlocks.x * 0.5f, 0f, -sizeInBlocks.z * 0.5f);

        /// <summary>Показать мир: старые чанки убираются, все чанки нового мира строятся сразу.</summary>
        public void Show(VoxelWorld world)
        {
            ClearPreview();
            if (centerOnOrigin)
            {
                transform.position = WorldOrigin(world.SizeInBlocks);
            }
            Display(world, transform);
        }

        private void Display(VoxelWorld world, Transform parent)
        {
            ClearChunkViews();
            hiddenBlocks.Clear();
            visualDirty.Clear();
            HiddenVersion++;
            World = world;
            chunkParent = parent;

            meshData ??= new ChunkMeshData();
            if (sharedMaterials.Count == 0)
                BuildSharedMaterials();
            World.MarkAllDirty();
            RebuildDirtyChunks();
        }

        private void LateUpdate()
        {
            if (World != null && (World.DirtyCount > 0 || visualDirty.Count > 0))
                RebuildDirtyChunks();
        }

        private void OnDestroy() => ClearChunkViews();

        private void ClearChunkViews()
        {
            foreach (var view in chunkViews.Values)
            {
                Release(view.Mesh);
                if (view.ColliderMesh != null)
                    Release(view.ColliderMesh);
                Release(view.Collider.gameObject);
            }
            chunkViews.Clear();
            viewsByCollider.Clear();
        }

        private void RebuildDirtyChunks()
        {
            dirtyBuffer.Clear();
            World.TakeDirtyChunks(dirtyBuffer);
            foreach (var chunk in dirtyBuffer)
            {
                visualDirty.Remove(chunk);
                RebuildChunk(chunk, true);
            }
            foreach (var chunk in visualDirty)
                RebuildChunk(chunk, false);
            visualDirty.Clear();
        }

        /// <summary>
        /// Не рисовать блок в чанке, пока его не вернут <see cref="ShowBlock"/>. Мир, луч мыши и коллайдер
        /// по-прежнему видят блок; меш перестроится в LateUpdate этого кадра.
        /// </summary>
        public void HideBlock(Vector3Int block)
        {
            hiddenBlocks.TryGetValue(block, out var count);
            hiddenBlocks[block] = count + 1;
            if (count == 0)
                MarkAround(block);
        }

        /// <summary>Снова рисовать блок, спрятанный <see cref="HideBlock"/> (столько же раз, сколько прятали).</summary>
        public void ShowBlock(Vector3Int block)
        {
            if (!hiddenBlocks.TryGetValue(block, out var count))
                return;
            if (count > 1)
            {
                hiddenBlocks[block] = count - 1;
                return;
            }
            hiddenBlocks.Remove(block);
            MarkAround(block);
        }

        public bool IsHidden(Vector3Int block) => hiddenBlocks.ContainsKey(block);

        /// <summary>Номер изменения набора спрятанных блоков: растёт, когда блок спрятали или вернули.</summary>
        public int HiddenVersion { get; private set; }

        /// <summary>Перерисовать чанк блока и соседние (у соседей могли открыться или закрыться грани).</summary>
        private void MarkAround(Vector3Int block)
        {
            HiddenVersion++;
            if (World == null)
                return;
            MarkVisualDirty(ChunkOf(block));
            MarkVisualDirty(ChunkOf(block + Vector3Int.left));
            MarkVisualDirty(ChunkOf(block + Vector3Int.right));
            MarkVisualDirty(ChunkOf(block + Vector3Int.down));
            MarkVisualDirty(ChunkOf(block + Vector3Int.up));
            MarkVisualDirty(ChunkOf(block + Vector3Int.back));
            MarkVisualDirty(ChunkOf(block + Vector3Int.forward));
        }

        private void MarkVisualDirty(Vector3Int chunk)
        {
            if (World.IsInsideChunks(chunk))
                visualDirty.Add(chunk);
        }

        private static Vector3Int ChunkOf(Vector3Int block) => new(
            Mathf.FloorToInt(block.x / (float)ChunkData.Size),
            Mathf.FloorToInt(block.y / (float)ChunkData.Size),
            Mathf.FloorToInt(block.z / (float)ChunkData.Size));

        /// <param name="rebuildCollider">Изменились данные мира; false — только картинка, коллайдер прежний.</param>
        private void RebuildChunk(Vector3Int chunk, bool rebuildCollider)
        {
            // Спрятанные блоки на время меширования картинки становятся воздухом прямо в данных чанка
            // (без пометки чанков), затем возвращаются. Коллайдер строится по настоящему миру.
            var hidden = TakeHiddenNear(chunk);
            try
            {
                ChunkMesher.Build(World, chunk, meshData, smoothShading ? surfaceNormals : null);
            }
            finally
            {
                if (hidden)
                    RestoreHidden();
            }

            var separateCollider = rebuildCollider && hidden;
            if (separateCollider)
            {
                colliderData ??= new ChunkMeshData();
                ChunkMesher.Build(World, chunk, colliderData);
            }

            if (!chunkViews.TryGetValue(chunk, out var view))
            {
                // Спрятать можно только настоящий блок, а у чанка с блоками вид уже есть.
                if (!rebuildCollider || (separateCollider ? colliderData : meshData).IsEmpty)
                    return;
                view = CreateChunkView(chunk);
                chunkViews.Add(chunk, view);
                viewsByCollider.Add(view.Collider, view);
            }

            // Перерисовываем только картинку, а коллайдер стоит на том же меше: отдаём ему этот меш целиком,
            // а картинку пишем во второй, иначе коллайдер и номера его треугольников разойдутся.
            if (!rebuildCollider && view.Collider.sharedMesh == view.Mesh)
            {
                (view.Mesh, view.ColliderMesh) = (view.ColliderMesh != null ? view.ColliderMesh : NewChunkMesh(view.Mesh.name), view.Mesh);
                view.Filter.sharedMesh = view.Mesh;
            }
            FillMesh(view.Mesh, meshData, true);
            ReportUnmappedMaterials();
            view.Renderer.enabled = !meshData.IsEmpty;
            if (rebuildCollider)
                ApplyCollider(view, separateCollider);
        }

        /// <summary>
        /// Спрятанные блоки, от которых зависит меш чанка, — заменить воздухом. Берём столбцы чанка с запасом
        /// по краям на всю высоту: сглаженные нормали ищут поверхность по всему столбцу.
        /// </summary>
        private bool TakeHiddenNear(Vector3Int chunk)
        {
            hiddenNearChunk.Clear();
            if (hiddenBlocks.Count == 0)
                return false;
            const int margin = 2;
            var min = chunk * ChunkData.Size - new Vector3Int(margin, 0, margin);
            var max = (chunk + Vector3Int.one) * ChunkData.Size + new Vector3Int(margin, 0, margin);
            foreach (var block in hiddenBlocks.Keys)
            {
                if (block.x < min.x || block.x >= max.x || block.z < min.z || block.z >= max.z || !World.IsInsideBlocks(block))
                    continue;
                hiddenNearChunk.Add((block, World.GetBlock(block)));
            }
            foreach (var (block, _) in hiddenNearChunk)
                SetQuietly(block, Block.Air);
            return hiddenNearChunk.Count > 0;
        }

        private void RestoreHidden()
        {
            foreach (var (block, value) in hiddenNearChunk)
                SetQuietly(block, value);
            hiddenNearChunk.Clear();
        }

        /// <summary>Записать блок в данные чанка, не помечая чанки изменёнными.</summary>
        private void SetQuietly(Vector3Int block, Block value)
        {
            var chunk = ChunkOf(block);
            var local = block - chunk * ChunkData.Size;
            World.GetChunk(chunk).Set(local.x, local.y, local.z, value);
        }

        private ChunkView CreateChunkView(Vector3Int chunk)
        {
            var go = new GameObject($"Chunk {chunk.x},{chunk.y},{chunk.z}")
            {
                layer = Layers.Ground,
                hideFlags = chunkParent.hideFlags,
            };
            go.transform.SetParent(chunkParent, false);
            go.transform.localPosition = chunk * ChunkData.Size;

            var mesh = NewChunkMesh(go.name);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.SetSharedMaterials(sharedMaterials);
            return new ChunkView(chunk, mesh, filter, renderer, go.AddComponent<MeshCollider>());
        }

        private static Mesh NewChunkMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            return mesh;
        }

        /// <param name="separateCollider">Коллайдер строится по <see cref="colliderData"/>, а не по мешу картинки.</param>
        private void ApplyCollider(ChunkView view, bool separateCollider)
        {
            var collider = separateCollider ? colliderData : meshData;
            view.TriangleBlocks.Clear();
            for (var subMesh = 0; subMesh < sharedMaterials.Count; subMesh++)
            {
                // Коллайдер нумерует треугольники подряд по подмешам, в том же порядке.
                view.TriangleBlocks.AddRange(collider.TriangleBlocks[subMesh + 1]);
            }
            Mesh colliderMesh = view.Mesh;
            if (separateCollider)
            {
                if (view.ColliderMesh == null)
                    view.ColliderMesh = NewChunkMesh(view.Mesh.name);
                FillMesh(view.ColliderMesh, colliderData, false);
                colliderMesh = view.ColliderMesh;
            }
            // Коллайдер берёт меш при назначении, поэтому переназначаем после каждой перестройки.
            view.Collider.sharedMesh = null;
            if (!collider.IsEmpty)
                view.Collider.sharedMesh = colliderMesh;
        }

        /// <summary>
        /// Смешивание земли в меш: UV3 (вес камня, бахрома травы) и оттенок травы в цвете вершин.
        /// colors — список для переиспользования.
        /// </summary>
        public static void SetGroundBlend(Mesh mesh, ChunkMeshData data, List<Color> colors)
        {
            mesh.SetUVs(3, data.Blends);
            colors.Clear();
            // Цвет материала Unity переводит в линейное пространство сам, цвет вершин — нет.
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var tint in data.Colors)
                colors.Add(linear ? new Color(Mathf.GammaToLinearSpace(tint.r), Mathf.GammaToLinearSpace(tint.g),
                    Mathf.GammaToLinearSpace(tint.b), tint.a) : tint);
            mesh.SetColors(colors);
        }

        /// <param name="visual">Меш картинки: ему нужны вес камня, бахрома травы (UV3) и оттенок травы (цвет вершин); коллайдеру — нет.</param>
        private void FillMesh(Mesh mesh, ChunkMeshData data, bool visual)
        {
            mesh.Clear();
            mesh.indexFormat = data.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uvs);
            // Гладкая земля смешивает траву с камнем: вес в UV3.x, цвет камня — общее свойство шейдера.
            // Верх блоков земли и камня у травы получает бахрому травы: маска соседей и место в ячейке в UV3.yzw.
            // Оттенок травы своего места — в цвете вершин.
            if (visual)
            {
                Shader.SetGlobalColor(GroundRockColorId, RockColor());
                SetGroundBlend(mesh, data, groundColors);
            }

            // Подмеш на каждый материал, даже пустой: у рендерера всегда один и тот же список материалов.
            // Если список менять при каждой перестройке, GPU Resident Drawer может остаться со старым
            // сопоставлением подмешей и материалов и нарисовать землю камнем до следующей перестройки.
            mesh.subMeshCount = sharedMaterials.Count;
            for (var subMesh = 0; subMesh < sharedMaterials.Count; subMesh++)
                mesh.SetTriangles(data.Triangles[subMesh + 1], subMesh, false);
            mesh.RecalculateBounds();
        }

        /// <summary>Цвет камня для перехода гладкой земли из травы в камень: основной цвет материала камня.</summary>
        /// <remarks>Общее свойство шейдера: Unity переводит его в линейное пространство сам, как цвет материала.</remarks>
        private Color RockColor()
        {
            if (rockColor.HasValue)
                return rockColor.Value;
            var stone = blockMaterials != null && BlockMaterials.Stone < blockMaterials.Length ? blockMaterials[BlockMaterials.Stone] : null;
            rockColor = stone != null && stone.HasProperty(BaseColorId) ? stone.GetColor(BaseColorId) : Color.gray;
            return rockColor.Value;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GroundRockColorId = Shader.PropertyToID("_GroundRockColor");

        /// <summary>Материалы для подмешей: подмеш i — материал блока i + 1 (воздух не рисуется).</summary>
        private void BuildSharedMaterials()
        {
            sharedMaterials.Clear();
            var count = Mathf.Max(BlockMaterials.Count, blockMaterials != null ? blockMaterials.Length : 0);
            for (var material = 1; material < count; material++)
                sharedMaterials.Add(MaterialFor(material));
        }

        private void ReportUnmappedMaterials()
        {
            for (var material = sharedMaterials.Count + 1; material < meshData.Triangles.Length; material++)
            {
                if (meshData.Triangles[material].Count > 0)
                    MaterialFor(material);
            }
        }

        /// <summary>Материал, которым рисуется блок с этим номером материала (для предпросмотра постановки).</summary>
        public Material BlockMaterial(byte material) => MaterialFor(material);

        private Material MaterialFor(int material)
        {
            if (blockMaterials != null && material < blockMaterials.Length && blockMaterials[material] != null)
                return blockMaterials[material];
            if (!missingMaterialReported)
            {
                missingMaterialReported = true;
                Debug.LogWarning($"[VoxelWorld] Нет материала для блока {material}.", this);
            }
            return null;
        }

        /// <summary>
        /// Блок, которому принадлежит треугольник меша чанка, по попаданию луча (RaycastHit.collider и
        /// RaycastHit.triangleIndex). Точнее, чем считать по точке: на склоне точка у низкого края лежит
        /// прямо на полу ячейки.
        /// </summary>
        public bool TryGetBlock(Collider collider, int triangleIndex, out Vector3Int block)
        {
            block = default;
            if (collider == null || !viewsByCollider.TryGetValue(collider, out var view)
                || (uint)triangleIndex >= (uint)view.TriangleBlocks.Count)
                return false;

            block = view.Chunk * ChunkData.Size + ChunkData.Position(view.TriangleBlocks[triangleIndex]);
            return true;
        }

        private sealed class ChunkView
        {
            public readonly Vector3Int Chunk;
            /// <summary>Меш картинки (в MeshFilter).</summary>
            public Mesh Mesh;
            public readonly MeshFilter Filter;
            public readonly MeshRenderer Renderer;
            public readonly MeshCollider Collider;
            /// <summary>
            /// Второй меш чанка: коллайдер берёт его, когда картинка отличается от мира (спрятаны блоки);
            /// иначе коллайдер берёт <see cref="Mesh"/>, а этот лежит про запас.
            /// </summary>
            public Mesh ColliderMesh;
            /// <summary>Индекс блока в чанке для каждого треугольника меша по порядку.</summary>
            public readonly List<int> TriangleBlocks = new();

            public ChunkView(Vector3Int chunk, Mesh mesh, MeshFilter filter, MeshRenderer renderer, MeshCollider collider)
            {
                Chunk = chunk;
                Mesh = mesh;
                Filter = filter;
                Renderer = renderer;
                Collider = collider;
            }
        }
    }
}
