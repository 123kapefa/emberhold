using System;
using Emberhold.Core;
using Emberhold.Gameplay.DayNight;
using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Источник света: задаёт круг, внутри которого можно строить и копать. Отдельный компонент,
    /// а не часть постройки, — его можно повесить на костёр, башню или экспедицию.
    /// Ставится на землю под собой, светит точечным светом и рисует границу радиуса по рельефу.
    /// Горит на топливе: ночью оно сгорает (время берётся из DayNightCycle), на последних крохах радиус
    /// сжимается, без топлива огонь гаснет — источник выпадает из зоны застройки и тумана.
    /// </summary>
    public sealed class LightSource : MonoBehaviour
    {
        private const float GroundProbeHeight = 200f;
        private const float RingLift = 0.08f;
        private const int RingSegments = 96;

        [SerializeField] private LightSourceSettings settings;
        [Tooltip("Зона застройки. Если пусто — первая BuildZone в сцене.")]
        [SerializeField] private BuildZone zone;
        [Tooltip("Точечный свет огня; его дальность равна радиусу.")]
        [SerializeField] private Light pointLight;
        [Tooltip("Линия границы света на земле.")]
        [SerializeField] private LineRenderer ring;
        [Tooltip("При старте опустить источник на землю под ним.")]
        [SerializeField] private bool snapToGround = true;
        [Tooltip("Пламя: уменьшается вместе с радиусом и пропадает, когда огонь погас.")]
        [SerializeField] private Transform flame;
        [Tooltip("Смена дня и ночи; ночью тратится топливо. Если пусто — первая DayNightCycle в сцене.")]
        [SerializeField] private DayNightCycle dayNight;

        private readonly Vector3[] ringPoints = new Vector3[RingSegments];
        private Vector3 flameScale = Vector3.one;
        private float radiusFactor = 1f;
        private float ringRadius = -1f;
        private bool started;

        /// <summary>Текущий радиус света: полный, пока топлива хватает, меньше на последних крохах, 0 у погасшего огня.</summary>
        public float Radius => settings != null ? settings.Radius * radiusFactor : 0f;

        /// <summary>Радиус при достаточном топливе.</summary>
        public float FullRadius => settings != null ? settings.Radius : 0f;

        public LightCircle Circle => new(transform.position, Radius);

        /// <summary>Запас топлива; null, если источник горит без топлива.</summary>
        public LightFuel Fuel { get; private set; }

        /// <summary>Огонь горит: источник включён и у него есть топливо (или оно ему не нужно).</summary>
        public bool IsLit => isActiveAndEnabled && radiusFactor > 0f;

        /// <summary>Огонь погас или снова загорелся; аргумент — горит ли теперь.</summary>
        public event Action<bool> LitChanged;

        /// <summary>Подбросить топливо (рабочие, отладочная клавиша). Возвращает, сколько принято; погасший огонь загорается.</summary>
        public float AddFuel(float amount)
        {
            if (Fuel == null)
                return 0f;
            var accepted = Fuel.Add(amount);
            if (accepted > 0f)
                ApplyFuel();
            return accepted;
        }

        /// <summary>Сжечь топливо как за seconds секунд ночи.</summary>
        public void BurnFuel(float seconds)
        {
            if (Fuel == null || Fuel.Burn(seconds, settings.BurnPerSecond) <= 0f)
                return;
            ApplyFuel();
        }

        private void Awake()
        {
            if (settings != null && settings.UsesFuel)
            {
                Fuel = new LightFuel(settings.FuelCapacity, settings.StartFuel);
                radiusFactor = FuelRadiusFactor();
            }
            if (flame != null)
                flameScale = flame.localScale;
        }

        private void OnEnable()
        {
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight != null)
                dayNight.TimePassed += OnTimePassed;

            if (zone == null)
                zone = FindAnyObjectByType<BuildZone>();
            if (zone != null)
                zone.Register(this);
            else
                Debug.LogWarning("[Light] В сцене нет BuildZone: свет не ограничивает строительство.", this);
        }

        private void OnDisable()
        {
            if (dayNight != null)
                dayNight.TimePassed -= OnTimePassed;
            if (zone != null)
                zone.Unregister(this);
        }

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogError("[Light] Не назначены LightSourceSettings.", this);
                return;
            }

            // Чанки строятся в Awake мира, поэтому к Start их коллайдеры уже есть.
            Physics.SyncTransforms();
            if (snapToGround && TryGroundHeight(transform.position, out var ground))
                transform.position = new Vector3(transform.position.x, ground, transform.position.z);

            if (pointLight != null)
            {
                pointLight.type = LightType.Point;
                pointLight.color = settings.LightColor;
                pointLight.transform.localPosition = Vector3.up * settings.LightHeight;
            }

            started = true;
            ApplyLook();
        }

        private void OnTimePassed(DayPhase phase, float seconds)
        {
            if (phase == DayPhase.Night)
                BurnFuel(seconds);
        }

        private float FuelRadiusFactor() => LightFuel.RadiusFactor(Fuel.Amount, settings.LowFuel, settings.MinRadiusFraction);

        private void ApplyFuel()
        {
            var wasLit = radiusFactor > 0f;
            radiusFactor = FuelRadiusFactor();
            var lit = radiusFactor > 0f;
            if (started)
                ApplyLook();
            if (lit != wasLit)
                LitChanged?.Invoke(lit);
        }

        /// <summary>Свет, пламя и граница по текущему радиусу.</summary>
        private void ApplyLook()
        {
            var lit = radiusFactor > 0f;
            if (pointLight != null)
            {
                pointLight.enabled = lit;
                pointLight.range = Radius;
                pointLight.intensity = settings.LightIntensity * Mathf.Lerp(0.5f, 1f, radiusFactor);
            }
            if (flame != null)
            {
                flame.gameObject.SetActive(lit);
                flame.localScale = flameScale * Mathf.Lerp(0.4f, 1f, radiusFactor);
            }
            if (ring != null)
            {
                ring.enabled = lit;
                // Перестраиваем границу, только когда радиус заметно изменился: точки кладутся на рельеф лучами.
                if (lit && Mathf.Abs(Radius - ringRadius) > 0.05f)
                    BuildRing();
            }
        }

        /// <summary>Граница света: окружность радиуса, каждая точка положена на рельеф.</summary>
        public void BuildRing()
        {
            var center = transform.position;
            var radius = Radius;
            ringRadius = radius;
            for (var i = 0; i < RingSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / RingSegments;
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                point.y = (TryGroundHeight(point, out var ground) ? ground : center.y) + RingLift;
                ringPoints[i] = point;
            }

            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.SetPositions(ringPoints);
            ring.widthMultiplier = settings.RingWidth;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
        }

        private static bool TryGroundHeight(Vector3 point, out float height)
        {
            var origin = new Vector3(point.x, point.y + GroundProbeHeight, point.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, GroundProbeHeight * 2f, Layers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }
    }
}
