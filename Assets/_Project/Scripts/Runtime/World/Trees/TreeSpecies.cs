using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Вид дерева ассетом: сколько ударов держит, сколько поленьев даёт и как выглядит.
    /// Ресурс у всех видов один (поленья), виды отличаются прочностью и выходом. Образцы лежат в Data/Trees.
    /// </summary>
    [CreateAssetMenu(fileName = "TreeSpecies", menuName = "Emberhold/World/Tree Species")]
    public sealed class TreeSpecies : ScriptableObject
    {
        [Tooltip("Название вида для подсказок.")]
        [SerializeField] private string displayName = "Берёза";

        [Header("Рубка")]
        [Tooltip("Прочность в ХП: рабочий снимает DigHitDamage за удар (25 — четыре удара на 100).")]
        [Min(1f)]
        [SerializeField] private float durability = 75f;
        [Tooltip("Id ресурса, который падает со срубленного дерева.")]
        [SerializeField] private string itemId = "wood";
        [Tooltip("Сколько поленьев падает со срубленного дерева.")]
        [Min(1)]
        [SerializeField] private int woodYield = 3;

        [Header("Вид")]
        [Tooltip("Высота дерева в блоках, от земли до макушки.")]
        [Min(0.5f)]
        [SerializeField] private float height = 3f;
        [Tooltip("Высота ствола до кроны в блоках.")]
        [Min(0.1f)]
        [SerializeField] private float trunkHeight = 1.2f;
        [Tooltip("Толщина ствола в блоках.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float trunkWidth = 0.25f;
        [Tooltip("Ширина кроны в блоках.")]
        [Range(0.2f, 3f)]
        [SerializeField] private float crownWidth = 1.1f;
        [SerializeField] private Color trunkColor = new(0.85f, 0.82f, 0.75f);
        [SerializeField] private Color crownColor = new(0.45f, 0.7f, 0.3f);

        public string DisplayName => displayName;
        public float Durability => durability;
        public string ItemId => string.IsNullOrEmpty(itemId) ? "wood" : itemId;
        public int WoodYield => Mathf.Max(1, woodYield);
        public float Height => height;
        public float TrunkHeight => Mathf.Min(trunkHeight, height);
        public float TrunkWidth => trunkWidth;
        public float CrownWidth => crownWidth;
        public Color TrunkColor => trunkColor;
        public Color CrownColor => crownColor;

        /// <summary>Вид для тестов без ассета.</summary>
        public static TreeSpecies Create(string name, float durability, int woodYield, float height = 3f, float crownWidth = 1f)
        {
            var species = CreateInstance<TreeSpecies>();
            species.name = name;
            species.displayName = name;
            species.durability = durability;
            species.woodYield = woodYield;
            species.height = height;
            species.crownWidth = crownWidth;
            return species;
        }
    }
}
