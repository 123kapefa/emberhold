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
    /// по таблице BlockDrops кладёт выпавший ресурс на место блока и показывает его кубиком.
    /// Каждый кадр кладёт предметы на опору заново, чтобы они не висели в воздухе, когда блок под ними выкопали.
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

        private readonly List<GameObject> visuals = new();
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
            Items.Settle(worldView.World);
            if (shownVersion == Items.Version)
                return;
            shownVersion = Items.Version;

            while (visuals.Count < Items.Items.Count)
                visuals.Add(CreateVisual());
            for (var i = 0; i < visuals.Count; i++)
            {
                var active = i < Items.Items.Count;
                visuals[i].SetActive(active);
                if (!active)
                    continue;
                var space = worldView.transform;
                var local = Items.Items[i].Position + Vector3.up * (itemSize * 0.5f);
                visuals[i].transform.SetPositionAndRotation(space.TransformPoint(local), space.rotation);
                visuals[i].transform.localScale = space.lossyScale * itemSize;
            }
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
