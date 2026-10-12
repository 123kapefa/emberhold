using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>Параметры типа источника света (костёр, позже фонарь, жаровня), включая топливо.</summary>
    [CreateAssetMenu(fileName = "LightSourceSettings", menuName = "Emberhold/Light Source Settings")]
    public sealed class LightSourceSettings : ScriptableObject
    {
        [Tooltip("Радиус света в блоках. Внутри можно строить и копать.")]
        [Min(1f)]
        [SerializeField] private float radius = 12f;

        [Header("Свет")]
        [SerializeField] private Color lightColor = new(1f, 0.6f, 0.25f);
        [Min(0f)]
        [SerializeField] private float lightIntensity = 6f;
        [Tooltip("Высота огня над землёй.")]
        [Min(0f)]
        [SerializeField] private float lightHeight = 1f;

        [Header("Граница")]
        [Tooltip("Толщина линии границы света; цвет — в материале линии.")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float ringWidth = 0.12f;

        [Header("Топливо")]
        [Tooltip("Источник горит на топливе. Без него светит всегда.")]
        [SerializeField] private bool usesFuel = true;
        [Tooltip("Сколько поленьев помещается.")]
        [Min(0.1f)]
        [SerializeField] private float fuelCapacity = 10f;
        [Tooltip("Сколько поленьев в начале игры.")]
        [Min(0f)]
        [SerializeField] private float startFuel = 5f;
        [Tooltip("Сколько поленьев сгорает за минуту первой ночи (времени суток). Днём, в сумерки и на рассвете не тратится.")]
        [Min(0f)]
        [SerializeField] private float burnPerNightMinute = 2f;
        [Tooltip("На сколько растёт расход с каждой следующей ночью, доля расхода первой ночи: 0.25 — вторая ночь +25%, пятая вдвое.")]
        [Min(0f)]
        [SerializeField] private float burnGrowthPerNight = 0.25f;
        [Tooltip("Когда поленьев меньше, радиус начинает сжиматься.")]
        [Min(0.01f)]
        [SerializeField] private float lowFuel = 2f;
        [Tooltip("Доля радиуса на последних крохах топлива, перед тем как огонь погаснет.")]
        [Range(0f, 1f)]
        [SerializeField] private float minRadiusFraction = 0.4f;

        public float Radius => radius;
        public Color LightColor => lightColor;
        public float LightIntensity => lightIntensity;
        public float LightHeight => lightHeight;
        public float RingWidth => ringWidth;
        public bool UsesFuel => usesFuel;
        public float FuelCapacity => fuelCapacity;
        public float StartFuel => startFuel;
        /// <summary>Расход в первую ночь, поленьев в секунду времени суток.</summary>
        public float BurnPerSecond => burnPerNightMinute / 60f;
        public float BurnGrowthPerNight => burnGrowthPerNight;

        /// <summary>Расход в ночь номер night (с 1), поленьев в секунду времени суток.</summary>
        public float BurnPerSecondOnNight(int night) => BurnPerSecond * LightFuel.NightBurnFactor(night, burnGrowthPerNight);
        public float LowFuel => lowFuel;
        public float MinRadiusFraction => minRadiusFraction;
    }
}
