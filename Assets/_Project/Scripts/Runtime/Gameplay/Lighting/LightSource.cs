using System;
using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.Gameplay.DayNight;
using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Источник света: задаёт круг, внутри которого можно строить и копать. Отдельный компонент,
    /// а не часть постройки, — его можно повесить на костёр, башню или экспедицию.
    /// Ставится на землю под собой, светит точечным светом и рисует границу радиуса по рельефу.
    /// Горит на топливе: ночью оно сгорает (время берётся из DayNightCycle), и с каждой ночью быстрее; на последних крохах радиус
    /// сжимается, без топлива огонь гаснет — источник выпадает из зоны застройки и тумана.
    /// Граница рисуется только там, где её не накрывает свет других горящих источников: у пересекающихся кругов виден общий край.
    /// </summary>
    public sealed class LightSource : MonoBehaviour
    {
        private const float GroundProbeHeight = 200f;
        private const float RingLift = 0.08f;
        private const int RingSegments = 96;

        /// <summary>Точка границы на столько ближе края чужого круга, чтобы считаться накрытой: у совпавших кругов граница не пропадает.</summary>
        private const float CoverMargin = 0.01f;

        /// <summary>Сколько секунд после укуса ползуна огонь считается под атакой.</summary>
        private const float AttackMemory = 0.5f;

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
        private readonly bool[] ringCovered = new bool[RingSegments];
        private readonly List<LineRenderer> ringParts = new();
        private readonly List<Vector3> partPoints = new();
        private bool ringLit;
        private float lastDrainTime = float.NegativeInfinity;
        private Vector3 flameScale = Vector3.one;
        private float radiusFactor = 1f;
        private float ringRadius = -1f;
        private bool started;

        /// <summary>Текущий радиус света: полный, пока топлива хватает, меньше на последних крохах, 0 у погасшего огня.</summary>
        public float Radius => settings != null ? settings.Radius * radiusFactor : 0f;

        /// <summary>Радиус при достаточном топливе.</summary>
        public float FullRadius => settings != null ? settings.Radius : 0f;

        public LightCircle Circle => new(transform.position, Radius);

        /// <summary>Зона застройки, в которой зарегистрирован источник.</summary>
        public BuildZone Zone => zone;

        /// <summary>Запас топлива; null, если источник горит без топлива.</summary>
        public LightFuel Fuel { get; private set; }

        /// <summary>Огонь горит: источник включён и у него есть топливо (или оно ему не нужно).</summary>
        public bool IsLit => isActiveAndEnabled && radiusFactor > 0f;

        /// <summary>Ползуны гасят огонь прямо сейчас (отнимали топливо только что).</summary>
        public bool UnderAttack => IsLit && Time.time - lastDrainTime < AttackMemory;

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

        /// <summary>Отнять топливо (ползуны гасят огонь). Возвращает, сколько отнято; без топлива огонь гаснет.</summary>
        public float DrainFuel(float amount)
        {
            if (Fuel == null)
                return 0f;
            var drained = Fuel.Burn(1f, amount);
            if (drained > 0f)
            {
                lastDrainTime = Time.time;
                ApplyFuel();
            }
            return drained;
        }

        /// <summary>Номер текущей ночи, с 1: ночь идёт в те же сутки (DayNightCycle.DayNumber). Без смены суток — 1.</summary>
        public int Night => dayNight != null ? dayNight.DayNumber : 1;

        /// <summary>Расход топлива сейчас, поленьев в секунду ночи: растёт с каждой следующей ночью.</summary>
        public float BurnPerSecond => settings != null ? settings.BurnPerSecondOnNight(Night) : 0f;

        /// <summary>Сжечь топливо как за seconds секунд ночи, с расходом текущей ночи.</summary>
        public void BurnFuel(float seconds)
        {
            if (Fuel == null || Fuel.Burn(seconds, BurnPerSecond) <= 0f)
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
            // Снова включённый источник показывает границу, если горит; регистрация перерисует и её, и соседей.
            if (started)
                ringLit = radiusFactor > 0f;
            if (zone != null)
            {
                zone.LightChanged += RefreshRing;
                zone.Register(this);
            }
            else if (started)
                RefreshRing();
            else
                Debug.LogWarning("[Light] В сцене нет BuildZone: свет не ограничивает строительство.", this);
        }

        private void OnDisable()
        {
            if (dayNight != null)
                dayNight.TimePassed -= OnTimePassed;
            if (zone != null)
            {
                zone.LightChanged -= RefreshRing;
                zone.Unregister(this);
            }
            foreach (var part in ringParts)
            {
                if (part != null)
                    part.enabled = false;
            }
            ringLit = false;
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
            // Погасший с самого начала огонь границу не меняет, но линию из префаба надо спрятать.
            RefreshRing();
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
                // Перемеряем границу, только когда радиус заметно изменился: точки кладутся на рельеф лучами.
                var measure = lit && Mathf.Abs(Radius - ringRadius) > 0.05f;
                if (measure)
                    MeasureRing();
                if (measure || lit != ringLit)
                {
                    ringLit = lit;
                    // Свой круг поменялся — соседям тоже перерисовать, что у них накрыто.
                    if (zone != null)
                        zone.NotifyLightChanged();
                    else
                        RefreshRing();
                }
            }
        }

        /// <summary>Граница света: окружность радиуса, каждая точка положена на рельеф.</summary>
        private void MeasureRing()
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
        }

        /// <summary>
        /// Нарисовать границу: дуги, накрытые светом других горящих источников, пропускаются. Целый круг — одна замкнутая линия,
        /// иначе каждая видимая дуга — своя линия, а её концы досчитываются точно по краю чужого круга.
        /// </summary>
        private void RefreshRing()
        {
            if (ring == null)
                return;
            var shown = 0;
            if (started && ringLit && ringRadius > 0f)
            {
                var covered = 0;
                for (var i = 0; i < RingSegments; i++)
                {
                    ringCovered[i] = IsCovered(ringPoints[i]);
                    covered += ringCovered[i] ? 1 : 0;
                }
                if (covered == 0)
                {
                    partPoints.Clear();
                    partPoints.AddRange(ringPoints);
                    SetPart(shown++, true);
                }
                else if (covered < RingSegments)
                {
                    // Начинаем с накрытой точки, за которой открытая, чтобы ни одна дуга не разрезалась на стыке массива.
                    var start = 0;
                    while (!ringCovered[start] || ringCovered[(start + 1) % RingSegments])
                        start++;
                    for (var k = 1; k <= RingSegments; k++)
                    {
                        var index = (start + k) % RingSegments;
                        if (ringCovered[index])
                            continue;
                        var previous = (index + RingSegments - 1) % RingSegments;
                        var next = (index + 1) % RingSegments;
                        if (ringCovered[previous])
                        {
                            partPoints.Clear();
                            partPoints.Add(EdgePoint(index, -1));
                        }
                        partPoints.Add(ringPoints[index]);
                        if (ringCovered[next])
                        {
                            partPoints.Add(EdgePoint(index, 1));
                            SetPart(shown++, false);
                        }
                    }
                }
            }
            for (var i = shown; i < ringParts.Count; i++)
            {
                if (ringParts[i] != null)
                    ringParts[i].enabled = false;
            }
            if (shown == 0)
                ring.enabled = false;
        }

        /// <summary>Точку накрывает свет другого горящего источника этой же зоны.</summary>
        private bool IsCovered(Vector3 point)
        {
            if (zone == null)
                return false;
            foreach (var other in zone.Sources)
            {
                if (other == this || !other.IsLit)
                    continue;
                var offset = point - other.transform.position;
                offset.y = 0f;
                var reach = other.Radius - CoverMargin;
                if (offset.sqrMagnitude < reach * reach)
                    return true;
            }
            return false;
        }

        /// <summary>Где между открытой точкой index и соседней накрытой (step = ±1) кончается граница: делим отрезок дуги пополам.</summary>
        private Vector3 EdgePoint(int index, int step)
        {
            var center = transform.position;
            var from = ringPoints[index];
            var to = ringPoints[(index + step + RingSegments) % RingSegments];
            var angle = index * Mathf.PI * 2f / RingSegments;
            var delta = step * Mathf.PI * 2f / RingSegments;
            float open = 0f, shut = 1f;
            for (var i = 0; i < 10; i++)
            {
                var t = (open + shut) * 0.5f;
                var a = angle + delta * t;
                var probe = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringRadius;
                if (IsCovered(probe))
                    shut = t;
                else
                    open = t;
            }
            var edge = angle + delta * open;
            var point = center + new Vector3(Mathf.Cos(edge), 0f, Mathf.Sin(edge)) * ringRadius;
            point.y = Mathf.Lerp(from.y, to.y, open);
            return point;
        }

        /// <summary>Отдать линии номер index точки partPoints. Первая линия — своя из префаба, остальные — её копии.</summary>
        private void SetPart(int index, bool loop)
        {
            if (ringParts.Count == 0)
                ringParts.Add(ring);
            while (ringParts.Count <= index)
            {
                var copy = Instantiate(ring, ring.transform.parent);
                copy.name = $"{ring.name} {ringParts.Count + 1}";
                ringParts.Add(copy);
            }
            var part = ringParts[index];
            part.useWorldSpace = true;
            part.loop = loop;
            part.positionCount = partPoints.Count;
            for (var i = 0; i < partPoints.Count; i++)
                part.SetPosition(i, partPoints[i]);
            part.widthMultiplier = settings.RingWidth;
            part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            part.receiveShadows = false;
            part.enabled = true;
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
