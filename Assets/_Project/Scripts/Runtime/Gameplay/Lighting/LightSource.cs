using Emberhold.Core;
using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Источник света: задаёт круг, внутри которого можно строить и копать. Отдельный компонент,
    /// а не часть постройки, — его можно повесить на костёр, башню или экспедицию.
    /// Ставится на землю под собой, светит точечным светом и рисует границу радиуса по рельефу.
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

        private readonly Vector3[] ringPoints = new Vector3[RingSegments];

        public float Radius => settings != null ? settings.Radius : 0f;

        public LightCircle Circle => new(transform.position, Radius);

        private void OnEnable()
        {
            if (zone == null)
                zone = FindAnyObjectByType<BuildZone>();
            if (zone != null)
                zone.Register(this);
            else
                Debug.LogWarning("[Light] В сцене нет BuildZone: свет не ограничивает строительство.", this);
        }

        private void OnDisable()
        {
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
                pointLight.range = settings.Radius;
                pointLight.color = settings.LightColor;
                pointLight.intensity = settings.LightIntensity;
                pointLight.transform.localPosition = Vector3.up * settings.LightHeight;
            }

            if (ring != null)
                BuildRing();
        }

        /// <summary>Граница света: окружность радиуса, каждая точка положена на рельеф.</summary>
        public void BuildRing()
        {
            var center = transform.position;
            for (var i = 0; i < RingSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / RingSegments;
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * settings.Radius;
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
