using UnityEngine;

namespace Emberhold.World.Bushes
{
    /// <summary>
    /// Вид куста ассетом (#88): сколько ударов держит, сколько травы даёт за срез, за сколько суток отрастает и как
    /// выглядит. Срезанный куст не исчезает: стоит голым и через <see cref="RegrowDays"/> снова зелёный. Образец лежит в Data/Bushes.
    /// </summary>
    [CreateAssetMenu(fileName = "BushSpecies", menuName = "Emberhold/World/Bush Species")]
    public sealed class BushSpecies : ScriptableObject
    {
        [Tooltip("Название вида для подсказок.")]
        [SerializeField] private string displayName = "Куст";

        [Header("Сбор")]
        [Tooltip("Прочность в ХП: рабочий снимает DigHitDamage за удар (25 — два удара на 50).")]
        [Min(1f)]
        [SerializeField] private float durability = 50f;
        [Tooltip("Id ресурса, который падает со срезанного куста.")]
        [SerializeField] private string itemId = "grass";
        [Tooltip("Сколько травы падает за один срез.")]
        [Min(1)]
        [SerializeField] private int grassYield = 3;
        [Tooltip("Через сколько игровых суток голый куст снова даёт траву.")]
        [Min(0.01f)]
        [SerializeField] private float regrowDays = 2f;

        [Header("Вид")]
        [Tooltip("Высота зелёного куста в блоках.")]
        [Range(0.2f, 1.5f)]
        [SerializeField] private float height = 0.7f;
        [Tooltip("Ширина куста в блоках.")]
        [Range(0.2f, 1.2f)]
        [SerializeField] private float width = 0.85f;
        [Tooltip("Цвет листвы (красится оттенком травы локации).")]
        [SerializeField] private Color leafColor = new(0.36f, 0.55f, 0.24f);
        [Tooltip("Цвет веток: голый куст — только ветки.")]
        [SerializeField] private Color twigColor = new(0.42f, 0.31f, 0.2f);
        [Tooltip("Сколько разных форм у вида: куст берёт одну по своей клетке.")]
        [Range(1, 8)]
        [SerializeField] private int variants = 3;

        public string DisplayName => displayName;
        public float Durability => durability;
        public string ItemId => string.IsNullOrEmpty(itemId) ? "grass" : itemId;
        public int GrassYield => Mathf.Max(1, grassYield);
        public float RegrowDays => Mathf.Max(0.01f, regrowDays);
        public float Height => height;
        public float Width => width;
        public Color LeafColor => leafColor;
        public Color TwigColor => twigColor;
        public int Variants => Mathf.Max(1, variants);

        /// <summary>Высота голого куста: ветки ниже листвы.</summary>
        public float BareHeight => height * 0.6f;

        /// <summary>Вид для тестов без ассета.</summary>
        public static BushSpecies Create(string name, float durability, int grassYield, float regrowDays = 2f, float height = 0.7f,
            float width = 0.85f)
        {
            var species = CreateInstance<BushSpecies>();
            species.name = name;
            species.displayName = name;
            species.durability = durability;
            species.grassYield = grassYield;
            species.regrowDays = regrowDays;
            species.height = height;
            species.width = width;
            return species;
        }
    }
}
