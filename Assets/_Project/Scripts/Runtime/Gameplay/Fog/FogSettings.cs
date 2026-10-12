using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Вид тумана за пределами света днём и ночью; между ними смешивается по темноте суток.
    /// Плотность у края света нулевая и растёт с расстоянием: falloff — на скольких блоках
    /// за краем туман набирает примерно две трети своей непрозрачности.
    /// Туман стелется по низу: ниже denseBelow он в полную силу, выше clearAbove его нет.
    /// Ночью обе границы поднимаются на nightRise, в особые ночи ещё на specialNightRise
    /// (по темноте суток, так что в сумерки граница ползёт вверх постепенно).
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

        [Header("Высота, блоки")]
        [Tooltip("Ниже этой высоты туман густой, в полную силу.")]
        [SerializeField] private float denseBelow = 8f;
        [Tooltip("Выше этой высоты тумана нет. Между границами он плавно редеет.")]
        [SerializeField] private float clearAbove = 16f;
        [Tooltip("На сколько блоков туман поднимается ночью.")]
        [Min(0f)] [SerializeField] private float nightRise = 6f;
        [Tooltip("На сколько блоков ещё выше он поднимается в особые ночи.")]
        [Min(0f)] [SerializeField] private float specialNightRise = 8f;

        public Color DayColor => dayColor;
        public float DayOpacity => dayOpacity;
        public float DayFalloff => dayFalloff;
        public Color NightColor => nightColor;
        public float NightOpacity => nightOpacity;
        public float NightFalloff => nightFalloff;
        public float DenseBelow => denseBelow;
        public float ClearAbove => clearAbove;
        public float NightRise => nightRise;
        public float SpecialNightRise => specialNightRise;

        /// <summary>
        /// Вид тумана при темноте от 0 (день) до 1 (ночь). В особую ночь граница по высоте поднимается выше обычного.
        /// </summary>
        public FogLook LookAt(float darkness, bool specialNight = false)
        {
            var t = Mathf.Clamp01(darkness);
            var rise = t * (nightRise + (specialNight ? specialNightRise : 0f));
            return new FogLook(
                Color.Lerp(dayColor, nightColor, t),
                Mathf.Lerp(dayOpacity, nightOpacity, t),
                Mathf.Lerp(dayFalloff, nightFalloff, t),
                denseBelow + rise,
                clearAbove + rise);
        }
    }
}
