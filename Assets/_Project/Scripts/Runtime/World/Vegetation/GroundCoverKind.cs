using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>Форма мелочи на земле (<see cref="GroundCoverMeshBuilder"/>).</summary>
    public enum GroundCoverShape
    {
        /// <summary>Пучок изогнутых травинок (трава, высокая и сухая трава, бурьян).</summary>
        Grass,
        /// <summary>Несколько стеблей с гранёными головками и пучок травы вокруг.</summary>
        Flowers,
        /// <summary>Розетка дугой изогнутых листьев.</summary>
        Fern,
        /// <summary>Кучка грибов: ножки и гранёные шляпки.</summary>
        Mushrooms,
        /// <summary>Высокие прямые стебли, часть с бурой головкой (рогоз).</summary>
        Reeds,
        /// <summary>Низкие гранёные подушки.</summary>
        Moss,
        /// <summary>Плоские пятна на земле или камне; с цветами — горные цветы.</summary>
        Lichen,
    }

    /// <summary>
    /// Вид мелочи на земле ассетом: форма, высота и цвета. Только для вида: не мешает ходить и строить,
    /// пропадает, если под ней копают или в её клетку ставят блок. Образцы лежат в Data/GroundCover.
    /// </summary>
    [CreateAssetMenu(fileName = "GroundCoverKind", menuName = "Emberhold/World/Ground Cover Kind")]
    public sealed class GroundCoverKind : ScriptableObject
    {
        [SerializeField] private GroundCoverShape shape = GroundCoverShape.Grass;
        [Tooltip("Высота самой высокой части в блоках.")]
        [Range(0.05f, 1.5f)]
        [SerializeField] private float height = 0.45f;
        [Tooltip("Цвета основной части (травинки, листья, мох, пятна лишайника): у каждого пучка один из них.")]
        [SerializeField] private Color[] colors = { new(0.541f, 0.604f, 0.275f) };
        [Tooltip("Цвета головок цветов, шляпок грибов, головок рогоза; у лишайника — цветы на нём (пусто — без цветов).")]
        [SerializeField] private Color[] accents = { };
        [Tooltip("Крапинки на шляпках (мухомор); альфа 0 — без крапинок.")]
        [SerializeField] private Color spotColor = new(0.925f, 0.902f, 0.847f, 0f);
        [Tooltip("Основная часть подстраивается под оттенок травы локации, как крона деревьев. Сухая трава и лишайник — нет.")]
        [SerializeField] private bool tintedByGrass = true;
        [Tooltip("Сколько разных форм у вида: пучок берёт одну по своей клетке.")]
        [Range(1, 8)]
        [SerializeField] private int variants = 4;

        public GroundCoverShape Shape => shape;
        public float Height => height;
        public Color[] Colors => colors ?? System.Array.Empty<Color>();
        public Color[] Accents => accents ?? System.Array.Empty<Color>();
        public Color SpotColor => spotColor;
        public bool TintedByGrass => tintedByGrass;
        public int Variants => Mathf.Max(1, variants);

        /// <summary>Вид для тестов без ассета.</summary>
        public static GroundCoverKind Create(string name, GroundCoverShape shape, float height = 0.45f, params Color[] accents)
        {
            var kind = CreateInstance<GroundCoverKind>();
            kind.name = name;
            kind.shape = shape;
            kind.height = height;
            kind.accents = accents;
            return kind;
        }
    }
}
