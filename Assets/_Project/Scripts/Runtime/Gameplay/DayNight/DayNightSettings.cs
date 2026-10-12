using UnityEngine;

namespace Emberhold.Gameplay.DayNight
{
    /// <summary>
    /// Длительности фаз суток и освещение днём и ночью. Между ними всё смешивается по темноте
    /// (DayCycle.DarknessAt). Туман настраивается отдельно, в FogSettings.
    /// </summary>
    [CreateAssetMenu(fileName = "DayNightSettings", menuName = "Emberhold/Day Night Settings")]
    public sealed class DayNightSettings : ScriptableObject
    {
        [Header("Сутки, секунды")]
        [Min(0f)] [SerializeField] private float daySeconds = 360f;
        [Min(0f)] [SerializeField] private float duskSeconds = 60f;
        [Min(0f)] [SerializeField] private float nightSeconds = 240f;
        [Min(0f)] [SerializeField] private float dawnSeconds = 60f;
        [Tooltip("С какой фазы начинается игра.")]
        [SerializeField] private DayPhase startPhase = DayPhase.Day;
        [Tooltip("Ускорение времени суток: 1 — как в дизайн-документе, 10 — сутки за 72 секунды.")]
        [Min(0f)] [SerializeField] private float timeScale = 1f;

        [Header("Солнце днём")]
        [SerializeField] private Color sunDayColor = new(1f, 0.95f, 0.85f);
        [Min(0f)] [SerializeField] private float sunDayIntensity = 2f;
        [Tooltip("Высота солнца над горизонтом днём, градусы.")]
        [Range(0f, 90f)] [SerializeField] private float sunDayElevation = 50f;

        [Header("Луна ночью")]
        [SerializeField] private Color sunNightColor = new(0.35f, 0.45f, 0.75f);
        [Min(0f)] [SerializeField] private float sunNightIntensity = 0.3f;
        [Range(0f, 90f)] [SerializeField] private float sunNightElevation = 35f;

        [Header("Направление")]
        [Tooltip("На сколько градусов свет поворачивается вокруг вертикали за сутки (тени ползут).")]
        [Range(0f, 360f)] [SerializeField] private float sunYawPerDay = 180f;

        [Header("Окружение")]
        [SerializeField] private Color ambientDay = new(0.55f, 0.58f, 0.62f);
        [SerializeField] private Color ambientNight = new(0.1f, 0.11f, 0.16f);
        [Tooltip("Отражения неба днём и ночью: ночью гасим, иначе блоки блестят.")]
        [Range(0f, 1f)] [SerializeField] private float reflectionDay = 1f;
        [Range(0f, 1f)] [SerializeField] private float reflectionNight = 0.1f;

        public float DaySeconds => daySeconds;
        public float DuskSeconds => duskSeconds;
        public float NightSeconds => nightSeconds;
        public float DawnSeconds => dawnSeconds;
        public DayPhase StartPhase => startPhase;
        public float TimeScale => timeScale;
        public Color SunDayColor => sunDayColor;
        public float SunDayIntensity => sunDayIntensity;
        public float SunDayElevation => sunDayElevation;
        public Color SunNightColor => sunNightColor;
        public float SunNightIntensity => sunNightIntensity;
        public float SunNightElevation => sunNightElevation;
        public float SunYawPerDay => sunYawPerDay;
        public Color AmbientDay => ambientDay;
        public Color AmbientNight => ambientNight;
        public float ReflectionDay => reflectionDay;
        public float ReflectionNight => reflectionNight;

        public DayCycle CreateCycle() => new(daySeconds, duskSeconds, nightSeconds, dawnSeconds);
    }
}
