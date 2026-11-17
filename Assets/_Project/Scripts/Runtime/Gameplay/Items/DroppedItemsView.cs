using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Items
{
    /// <summary>
    /// Ресурсы на земле: слушает разрушение блоков копанием (<see cref="BlockDamage.Destroyed"/>),
    /// по таблице BlockDrops кладёт выпавший ресурс на место блока и показывает кучку стопкой кубиков. Траву (#88) показывает
    /// снопами на боку, той же сеткой, что рабочий носит на плече (<see cref="WorkerCarry.Sheaf"/>): не путать с блоками.
    /// Каждый кадр кладёт предметы на опору заново, чтобы они не висели в воздухе, когда блок под ними выкопали.
    /// Если задан <see cref="BlockFallView"/>, опустившаяся кучка падает с тем же ускорением, что и блоки,
    /// поэтому ресурс на упавшем блоке летит вместе с ним; данные (<see cref="DroppedItems"/>) меняются сразу.
    /// </summary>
    public sealed class DroppedItemsView : MonoBehaviour
    {
        /// <summary>Как выглядит кучка ресурса на земле.</summary>
        public enum ItemShape
        {
            /// <summary>Стопка кубиков, по одному на единицу.</summary>
            Cube,
            /// <summary>Снопы травы на боку, один на <see cref="unitsPerSheaf"/> единиц (#88).</summary>
            Sheaf,
        }

        [SerializeField] private BlockEditController editor;
        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private BlockDropTable drops;
        [Serializable]
        private struct ItemLook
        {
            [Tooltip("Id ресурса, например wood.")]
            public string itemId;
            [Tooltip("Для снопа — материал с цветом вершин, как у рабочих.")]
            public Material material;
            public ItemShape shape;
        }

        [Tooltip("Материал кубика ресурса, если для него нет своего в списке ниже.")]
        [SerializeField] private Material itemMaterial;
        [Tooltip("Свои материалы ресурсов: поленья не похожи на камень.")]
        [SerializeField] private ItemLook[] itemLooks = Array.Empty<ItemLook>();
        [Tooltip("Размер кубика ресурса в блоках.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float itemSize = 0.35f;
        [Tooltip("Сколько кубиков в стопке показывать у одной кучки, не больше.")]
        [Min(1)]
        [SerializeField] private int maxStack = 5;
        [Tooltip("Сколько единиц травы в одном снопе на земле: столько даёт куст и столько рабочий уносит за раз.")]
        [Min(1)]
        [SerializeField] private int unitsPerSheaf = 3;
        [Tooltip("Рост рабочего в блоках: сноп на земле того же размера, что у него на плече.")]
        [Min(0.1f)]
        [SerializeField] private float sheafWorkerHeight = 1.9f;
        [Tooltip("Падение блоков: с его ускорением падают и кучки, потерявшие опору. Пусто — кучки переставляются сразу.")]
        [SerializeField] private BlockFallView blockFalls;

        private readonly List<GameObject> visuals = new();
        private readonly Dictionary<DroppedItem, float> heightsBefore = new();
        // Падающие кучки: с какой высоты и когда начали падать.
        private readonly Dictionary<DroppedItem, (float from, float start)> falling = new();
        private readonly List<DroppedItem> landed = new();
        private int shownVersion = -1;
        private BlockDamage damage;
        private Mesh cubeMesh;
        private Mesh sheafMesh;
        private float sheafRadius;

        /// <summary>Лежащие на земле ресурсы; рабочие потом будут забирать их отсюда.</summary>
        public DroppedItems Items { get; private set; }

        /// <summary>Материал кубика ресурса itemId (им же рабочие показывают ношу).</summary>
        public Material MaterialFor(string itemId)
        {
            if (itemLooks != null)
            {
                foreach (var look in itemLooks)
                {
                    if (look.itemId == itemId && look.material != null)
                        return look.material;
                }
            }
            return itemMaterial;
        }

        /// <summary>Как выглядит кучка ресурса itemId: кубики или снопы.</summary>
        public ItemShape ShapeFor(string itemId)
        {
            if (itemLooks != null)
            {
                foreach (var look in itemLooks)
                {
                    if (look.itemId == itemId)
                        return look.shape;
                }
            }
            return ItemShape.Cube;
        }

        /// <summary>Сколько снопов показать у кучки из count единиц травы: не меньше одного и не больше max.</summary>
        public static int SheafCount(int count, int unitsPerSheaf, int max) =>
            Mathf.Clamp((count + Mathf.Max(1, unitsPerSheaf) - 1) / Mathf.Max(1, unitsPerSheaf), 1, Mathf.Max(1, max));

        /// <summary>
        /// Центр k-го снопа в кучке относительно точки на земле, в блоках: снизу в ряд до трёх (средний первым), сверху
        /// в ложбинах между ними ещё два. Снопы радиуса radius лежат вдоль Z и не проходят друг в друга и в землю.
        /// </summary>
        public static Vector3 SheafOffset(int k, float radius)
        {
            var step = radius * 2.1f;
            var top = radius + step * 0.8660254f;
            return (k % 5) switch
            {
                0 => new Vector3(0f, radius, 0f),
                1 => new Vector3(-step, radius, 0f),
                2 => new Vector3(step, radius, 0f),
                3 => new Vector3(-step * 0.5f, top, 0f),
                _ => new Vector3(step * 0.5f, top, 0f),
            };
        }

        private void Awake()
        {
            if (editor == null || worldView == null || drops == null)
            {
                Debug.LogError("[DroppedItems] Не назначены BlockEditController, VoxelWorldView или BlockDrops.", this);
                enabled = false;
                return;
            }
            Items = new DroppedItems(drops);
        }

        private void Start()
        {
            // Урон создаётся в Awake редактора, поэтому подписываемся в Start.
            damage = editor.Damage;
            if (damage == null)
            {
                Debug.LogError("[DroppedItems] У BlockEditController нет BlockDamage.", this);
                enabled = false;
                return;
            }
            damage.Destroyed += OnBlockDestroyed;
        }

        private void OnDestroy()
        {
            if (damage != null)
                damage.Destroyed -= OnBlockDestroyed;
            foreach (var visual in visuals)
                Destroy(visual);
            if (sheafMesh != null)
                Destroy(sheafMesh);
        }

        private void OnBlockDestroyed(Vector3Int block, Block destroyed)
        {
            if (worldView.World != null)
                Items.OnBlockDestroyed(worldView.World, block, destroyed);
        }

        private void LateUpdate()
        {
            if (worldView.World == null)
                return;
            heightsBefore.Clear();
            foreach (var item in Items.Items)
                heightsBefore[item] = item.Position.y;
            Items.Settle(worldView.World);
            var animating = falling.Count > 0;
            if (blockFalls != null)
                StartFalls();
            if (shownVersion == Items.Version && !animating && falling.Count == 0)
                return;
            shownVersion = Items.Version;

            // Кучка — стопка кубиков, по одному на единицу ресурса (не выше maxStack), трава — снопы на боку.
            var shown = 0;
            var space = worldView.transform;
            foreach (var item in Items.Items)
            {
                var sheaf = ShapeFor(item.Drop.ItemId) == ItemShape.Sheaf;
                var count = sheaf ? SheafCount(item.Drop.Count, unitsPerSheaf, maxStack) : Mathf.Min(item.Drop.Count, maxStack);
                var ground = new Vector3(item.Position.x, ShownHeight(item), item.Position.z);
                for (var k = 0; k < count; k++, shown++)
                {
                    if (shown == visuals.Count)
                        visuals.Add(CreateVisual());
                    var visual = visuals[shown];
                    var material = MaterialFor(item.Drop.ItemId);
                    var renderer = visual.GetComponent<MeshRenderer>();
                    if (material != null && renderer.sharedMaterial != material)
                        renderer.sharedMaterial = material;
                    var filter = visual.GetComponent<MeshFilter>();
                    var mesh = sheaf ? SheafMesh() : cubeMesh;
                    if (filter.sharedMesh != mesh)
                        filter.sharedMesh = mesh;
                    var local = sheaf ? ground + SheafOffset(k, sheafRadius) : ground + Vector3.up * (itemSize * (k + 0.5f));
                    visual.transform.SetPositionAndRotation(space.TransformPoint(local), space.rotation);
                    visual.transform.localScale = sheaf ? space.lossyScale : space.lossyScale * itemSize;
                    visual.SetActive(true);
                }
            }
            for (var i = shown; i < visuals.Count; i++)
                visuals[i].SetActive(false);
        }

        /// <summary>Кучки, которые Settle опустил в этом кадре, начинают падать с той высоты, где их видно сейчас.</summary>
        private void StartFalls()
        {
            foreach (var item in Items.Items)
            {
                if (!heightsBefore.TryGetValue(item, out var before))
                    continue;
                var height = item.Position.y;
                if (height < before - 0.001f)
                    falling[item] = (falling.ContainsKey(item) ? ShownHeight(item) : before, Time.time);
                else if (height > before + 0.001f)
                    falling.Remove(item);
            }

            // Долетевшие и исчезнувшие (слились с другой кучкой, подобраны) больше не падают.
            landed.Clear();
            foreach (var item in falling.Keys)
            {
                if (!Contains(item) || ShownHeight(item) <= item.Position.y)
                    landed.Add(item);
            }
            foreach (var item in landed)
                falling.Remove(item);
        }

        private bool Contains(DroppedItem item)
        {
            foreach (var other in Items.Items)
            {
                if (other == item)
                    return true;
            }
            return false;
        }

        /// <summary>Высота, на которой кучку видно сейчас: в полёте — выше точки опоры.</summary>
        private float ShownHeight(DroppedItem item)
        {
            if (blockFalls == null || !falling.TryGetValue(item, out var fall))
                return item.Position.y;
            var drop = FallMotion.Drop(fall.from - item.Position.y, Time.time - fall.start, blockFalls.Gravity);
            return Mathf.Max(item.Position.y, fall.from - drop);
        }

        private GameObject CreateVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Dropped Item";
            // Без коллайдера: предмет не должен мешать лучу выбора блока.
            Destroy(go.GetComponent<Collider>());
            cubeMesh ??= go.GetComponent<MeshFilter>().sharedMesh;
            go.transform.SetParent(transform, false);
            if (itemMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = itemMaterial;
            return go;
        }

        /// <summary>Сетка снопа на земле — та же, что у рабочего на плече; строится один раз.</summary>
        private Mesh SheafMesh()
        {
            if (sheafMesh == null)
            {
                sheafMesh = WorkerCarryBuilder.Build(WorkerCarry.Sheaf, sheafWorkerHeight, "Dropped Sheaf");
                sheafRadius = sheafMesh.bounds.extents.y;
            }
            return sheafMesh;
        }
    }
}
