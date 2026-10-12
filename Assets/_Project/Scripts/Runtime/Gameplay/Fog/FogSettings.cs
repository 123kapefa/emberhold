using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Вид тумана за пределами света днём и ночью; между ними смешивается по темноте суток.
    /// Плотность у края света нулевая и растёт с расстоянием: falloff — на скольких блоках
    /// за краем туман набирает примерно две трети своей непрозрачности.
    /// </summary>
    [CreateAssetMenu(fileName = "FogSettings", menuName = "Emberhold/Fog Settings")]
    public sealed class FogSettings : ScriptableObject
    {
        [Header("День")]
        [SerializeField] private Color dayColor = new(0.62f, 0.68f, 0.76f);
        [Tooltip("Непрозрачность густого тумана вдали от света.")]
        [Range(0f, 1f)] [SerializeField] private float dayOpacity = 0.6f;
        [Tooltip("Блоков за краем света, за которые туман густеет (меньше — резче граница).")]
        [Min(0.1f)] [SerializeField] private float dayFalloff = 24f;

        [Header("Ночь")]
        [SerializeField] private Color nightColor = new(0.03f, 0.04f, 0.065f);
        [Range(0f, 1f)] [SerializeField] private float nightOpacity = 0.97f;
        [Min(0.1f)] [SerializeField] private float nightFalloff = 5f;

        public Color DayColor => dayColor;
        public float DayOpacity => dayOpacity;
        public float DayFalloff => dayFalloff;
        public Color NightColor => nightColor;
        public float NightOpacity => nightOpacity;
        public float NightFalloff => nightFalloff;

        /// <summary>Вид тумана при темноте от 0 (день) до 1 (ночь).</summary>
        public FogLook LookAt(float darkness)
        {
            var t = Mathf.Clamp01(darkness);
            return new FogLook(
                Color.Lerp(dayColor, nightColor, t),
                Mathf.Lerp(dayOpacity, nightOpacity, t),
                Mathf.Lerp(dayFalloff, nightFalloff, t));
        }
    }
}
