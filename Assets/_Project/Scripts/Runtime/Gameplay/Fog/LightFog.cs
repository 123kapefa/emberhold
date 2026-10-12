using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Lighting;
using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Туман за пределами света. Круги берёт из той же BuildZone, что и стройка, поэтому граница
    /// тумана всегда совпадает с радиусом активных источников; густоту, цвет и границу по высоте — из FogSettings
    /// по темноте суток. Высота — мировая Y точки (мир блоков стоит в начале координат, один блок — одна единица).
    /// Рисует его полноэкранный проход URP (материал LightFog в рендерерах PC/Mobile): компонент только
    /// передаёт шейдеру глобальные параметры. Без компонента в сцене туман выключен.
    /// </summary>
    public sealed class LightFog : MonoBehaviour
    {
        /// <summary>Сколько источников видит шейдер; совпадает с размером массива в LightFog.shader.</summary>
        public const int MaxShaderLights = 16;

        private static readonly int FogColorId = Shader.PropertyToID("_EmberholdFogColor");
        private static readonly int FogParamsId = Shader.PropertyToID("_EmberholdFogParams");
        private static readonly int LightCirclesId = Shader.PropertyToID("_EmberholdLightCircles");
        private static readonly int LightCountId = Shader.PropertyToID("_EmberholdLightCount");
        private static readonly int FogHeightId = Shader.PropertyToID("_EmberholdFogHeight");

        [SerializeField] private FogSettings settings;
        [Tooltip("Источники света. Если пусто — первая BuildZone в сцене.")]
        [SerializeField] private BuildZone zone;
        [Tooltip("Смена дня и ночи. Если пусто — первая в сцене; без неё туман всегда дневной.")]
        [SerializeField] private DayNightCycle dayNight;
        [Tooltip("Особая ночь: туман поднимается выше обычного. Пока особых ночей нет, флаг ставится вручную для проверки.")]
        [SerializeField] private bool specialNight;

        private readonly LightZone circles = new();
        private readonly Vector4[] shaderCircles = new Vector4[MaxShaderLights];
        private bool warnedTooManyLights;

        /// <summary>Вид тумана в этом кадре.</summary>
        public FogLook Look { get; private set; }

        /// <summary>Особая ночь: туман поднимается выше (числа в FogSettings). Действует с этого кадра.</summary>
        public bool SpecialNight
        {
            get => specialNight;
            set
            {
                specialNight = value;
                if (isActiveAndEnabled)
                    Push();
            }
        }

        private void OnEnable()
        {
            if (settings == null)
            {
                Debug.LogError("[Fog] Не назначены FogSettings.", this);
                enabled = false;
                return;
            }
            if (zone == null)
                zone = FindAnyObjectByType<BuildZone>();
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            Push();
        }

        private void OnDisable() => Shader.SetGlobalVector(FogParamsId, Vector4.zero);

        // После Update: к этому моменту время суток уже продвинулось, а источники могли сдвинуться.
        private void LateUpdate() => Push();

        /// <summary>
        /// Плотность тумана в точке мира, от 0 (внутри света или выше тумана) до непрозрачности текущей фазы.
        /// Учитывает и свет, и высоту точки. Рабочим и существам: чем гуще, тем страшнее и опаснее.
        /// </summary>
        public float DensityAt(Vector3 worldPoint)
        {
            if (zone != null)
                zone.CollectCircles(circles);
            else
                circles.Clear();
            return Look.DensityAt(circles.DistanceOutside(worldPoint), worldPoint.y);
        }

        private void Push()
        {
            Look = settings.LookAt(dayNight != null && dayNight.isActiveAndEnabled ? dayNight.Darkness : 0f, specialNight);

            if (zone != null)
                zone.CollectCircles(circles);
            else
                circles.Clear();

            var count = Mathf.Min(circles.Count, MaxShaderLights);
            if (circles.Count > MaxShaderLights && !warnedTooManyLights)
            {
                warnedTooManyLights = true;
                Debug.LogWarning($"[Fog] Источников света {circles.Count}, шейдер тумана видит только {MaxShaderLights}.", this);
            }
            for (var i = 0; i < MaxShaderLights; i++)
            {
                if (i < count)
                {
                    var circle = circles.Circles[i];
                    shaderCircles[i] = new Vector4(circle.Center.x, circle.Center.z, circle.Radius, 0f);
                }
                else
                {
                    shaderCircles[i] = Vector4.zero;
                }
            }

            Shader.SetGlobalColor(FogColorId, Look.Color);
            Shader.SetGlobalVector(FogParamsId, new Vector4(Look.Opacity, 1f / Look.Falloff, 1f, 0f));
            Shader.SetGlobalVector(FogHeightId, new Vector4(Look.DenseBelow, Look.ClearAbove, 0f, 0f));
            Shader.SetGlobalVectorArray(LightCirclesId, shaderCircles);
            Shader.SetGlobalFloat(LightCountId, count);
        }
    }
}
