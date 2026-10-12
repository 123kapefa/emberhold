using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Items
{
    /// <summary>
    /// Ресурсы на земле: слушает разрушение блоков копанием (<see cref="BlockDamage.Destroyed"/>),
    /// по таблице BlockDrops кладёт выпавший ресурс на место блока и показывает кучку стопкой кубиков.
    /// Каждый кадр кладёт предметы на опору заново, чтобы они не висели в воздухе, когда блок под ними выкопали.
    /// Если задан <see cref="BlockFallView"/>, опустившаяся кучка падает с тем же ускорением, что и блоки,
    /// поэтому ресурс на упавшем блоке летит вместе с ним; данные (<see cref="DroppedItems"/>) меняются сразу.
    /// </summary>
    public sealed class DroppedItemsView : MonoBehaviour
    {
        [SerializeField] private BlockEditController editor;
        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private BlockDropTable drops;
        [Tooltip("Материал кубика ресурса.")]
        [SerializeField] private Material itemMaterial;
        [Tooltip("Размер кубика ресурса в блоках.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float itemSize = 0.35f;
        [Tooltip("Сколько кубиков в стопке показывать у одной кучки, не больше.")]
        [Min(1)]
        [SerializeField] private int maxStack = 5;
        [Tooltip("Падение блоков: с его ускорением падают и кучки, потерявшие опору. Пусто — кучки переставляются сразу.")]
        [SerializeField] private BlockFallView blockFalls;

        private readonly List<GameObject> visuals = new();
        private readonly Dictionary<DroppedItem, float> heightsBefore = new();
        // Падающие кучки: с какой высоты и когда начали падать.
        private readonly Dictionary<DroppedItem, (float from, float start)> falling = new();
        private readonly List<DroppedItem> landed = new();
        private int shownVersion = -1;
        private BlockDamage damage;

        /// <summary>Лежащие на земле ресурсы; рабочие потом будут забирать их отсюда.</summary>
        public DroppedItems Items { get; private set; }

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

            // Кучка — стопка кубиков, по одному на единицу ресурса (не выше maxStack).
            var shown = 0;
            var space = worldView.transform;
            foreach (var item in Items.Items)
            {
                var count = Mathf.Min(item.Drop.Count, maxStack);
                for (var k = 0; k < count; k++, shown++)
                {
                    if (shown == visuals.Count)
                        visuals.Add(CreateVisual());
                    var visual = visuals[shown];
                    var local = new Vector3(item.Position.x, ShownHeight(item), item.Position.z)
                                + Vector3.up * (itemSize * (k + 0.5f));
                    visual.transform.SetPositionAndRotation(space.TransformPoint(local), space.rotation);
                    visual.transform.localScale = space.lossyScale * itemSize;
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
            go.transform.SetParent(transform, false);
            if (itemMaterial != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = itemMaterial;
            return go;
        }
    }
}
