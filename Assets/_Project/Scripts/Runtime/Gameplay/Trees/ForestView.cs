using System.Collections.Generic;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using Emberhold.World.Trees;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Trees
{
    /// <summary>
    /// Лес в сцене: при старте рассаживает деревья по профилям местности мира (фон и зоны, <see cref="ForestScatter"/>),
    /// показывает каждое стволом и кроной из кубиков и убирает упавшие. Упавшее дерево кладёт поленья
    /// в те же ресурсы на земле, что и камень (<see cref="DroppedItemsView"/>). Рубят деревья рабочие (WorkerCrew),
    /// приказ рубить ставит игрок инструментом 5 (BlockEditController).
    /// </summary>
    public sealed class ForestView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private DroppedItemsView droppedItems;
        [Tooltip("Вокруг огней в этом радиусе (в блоках) деревья не растут: место для лагеря.")]
        [Min(0f)]
        [SerializeField] private float clearAroundFire = 3f;

        [Header("Вид")]
        [SerializeField] private Material trunkMaterial;
        [SerializeField] private Material crownMaterial;

        private readonly Dictionary<TreeStand, GameObject> visuals = new();
        private MaterialPropertyBlock properties;
        private Forest forest;
        private int shownVersion = -1;

        /// <summary>
        /// Деревья мира. Рассаживаются при первом обращении: мир и ресурсы на земле создаются в Awake,
        /// поэтому из любого Start лес уже готов. Null, если сцена собрана не до конца.
        /// </summary>
        public Forest Forest
        {
            get
            {
                if (forest == null && worldView != null && worldView.World != null && droppedItems != null && droppedItems.Items != null)
                    Plant();
                return forest;
            }
        }

        private void Start()
        {
            if (Forest == null)
            {
                Debug.LogError("[Forest] Не назначены VoxelWorldView или DroppedItemsView.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (forest != null)
                forest.Felled -= OnFelled;
            foreach (var visual in visuals.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
        }

        private void LateUpdate()
        {
            if (forest == null)
                return;
            // Под деревом могли выкопать землю или поставить блок в его клетку.
            forest.Settle(worldView.World);
            if (shownVersion == forest.Version)
                return;
            shownVersion = forest.Version;
            foreach (var tree in forest.Trees)
            {
                if (!visuals.ContainsKey(tree))
                    visuals[tree] = CreateVisual(tree);
            }
        }

        /// <summary>
        /// Дерево, в которое попадает луч из сцены, ближе maxDistance; distance — в единицах сцены.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out TreeStand tree, out float distance)
        {
            tree = null;
            distance = maxDistance;
            if (Forest == null)
                return false;
            var space = worldView.transform;
            // Мир не масштабируется, поэтому расстояние в блоках совпадает с расстоянием в сцене.
            return forest.Raycast(worldView.World, space.InverseTransformPoint(ray.origin),
                space.InverseTransformDirection(ray.direction), maxDistance, out tree, out distance);
        }

        /// <summary>Коробка дерева для рамки: от основания до макушки, шириной с крону; в долях клетки дерева.</summary>
        public (Vector3 min, Vector3 max) LocalBounds(TreeStand tree)
        {
            var half = Mathf.Max(tree.Species.CrownWidth, tree.Species.TrunkWidth) * 0.5f;
            var bottom = Forest.BaseHeight(worldView.World, tree) - tree.Cell.y;
            return (new Vector3(0.5f - half, bottom, 0.5f - half), new Vector3(0.5f + half, bottom + tree.Species.Height, 0.5f + half));
        }

        private void Plant()
        {
            forest = new Forest(droppedItems.Items);
            forest.Felled += OnFelled;
            var terrain = worldView.Terrain;
            if (terrain == null)
                return;
            var fires = new List<Vector3>();
            foreach (var source in FindObjectsByType<LightSource>())
                fires.Add(worldView.transform.InverseTransformPoint(source.transform.position));
            var clear = clearAroundFire * clearAroundFire;
            var planted = ForestScatter.Scatter(worldView.World, terrain.Seed, worldView.ForestAt, forest, column =>
            {
                foreach (var fire in fires)
                {
                    var d = new Vector2(column.x + 0.5f - fire.x, column.y + 0.5f - fire.z);
                    if (d.sqrMagnitude <= clear)
                        return true;
                }
                return false;
            });
            Debug.Log($"[Forest] Посажено деревьев: {planted}.", this);
        }

        private void OnFelled(TreeStand tree)
        {
            if (!visuals.Remove(tree, out var visual))
                return;
            Destroy(visual);
        }

        /// <summary>Дерево из двух кубиков: ствол и крона, цвета вида через MaterialPropertyBlock.</summary>
        private GameObject CreateVisual(TreeStand tree)
        {
            properties ??= new MaterialPropertyBlock();
            var species = tree.Species;
            var root = new GameObject($"Tree {species.DisplayName}");
            root.transform.SetParent(transform, false);
            var space = worldView.transform;
            var bottom = Forest.BaseHeight(worldView.World, tree);
            root.transform.SetPositionAndRotation(space.TransformPoint(new Vector3(tree.Cell.x + 0.5f, bottom, tree.Cell.z + 0.5f)),
                space.rotation * Quaternion.Euler(0f, (tree.Cell.x * 37 + tree.Cell.z * 61) % 90, 0f));

            var crownHeight = species.Height - species.TrunkHeight;
            // Ствол заходит в крону, чтобы между ними не было щели.
            AddPart(root.transform, "Trunk", trunkMaterial, species.TrunkColor,
                new Vector3(0f, (species.TrunkHeight + crownHeight * 0.3f) * 0.5f, 0f),
                new Vector3(species.TrunkWidth, species.TrunkHeight + crownHeight * 0.3f, species.TrunkWidth));
            AddPart(root.transform, "Crown", crownMaterial, species.CrownColor,
                new Vector3(0f, species.TrunkHeight + crownHeight * 0.5f, 0f),
                new Vector3(species.CrownWidth, crownHeight, species.CrownWidth));
            return root;
        }

        private void AddPart(Transform parent, string name, Material material, Color color, Vector3 position, Vector3 size)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            // Без коллайдера: дерево выбирается лучом по коробке (Forest.Raycast) и не мешает лучу выбора блока.
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            var renderer = part.GetComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            properties.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
