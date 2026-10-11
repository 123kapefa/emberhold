using UnityEngine;

namespace Emberhold.CameraRig
{
    [CreateAssetMenu(fileName = "CameraRigSettings", menuName = "Emberhold/Camera Rig Settings")]
    public sealed class CameraRigSettings : ScriptableObject
    {
        [Header("Старт")]
        [SerializeField] private float startYaw = 45f;
        [SerializeField] private float startPitch = 50f;
        [SerializeField] private float startDistance = 30f;

        [Header("Панорамирование")]
        [Tooltip("Ед./с с клавиатуры при дистанции Pan Reference Distance.")]
        [SerializeField] private float panSpeed = 20f;
        [SerializeField] private float panReferenceDistance = 30f;
        [Tooltip("Границы точки фокуса по X/Z.")]
        [SerializeField] private Vector2 focusBoundsMin = new(-100f, -100f);
        [SerializeField] private Vector2 focusBoundsMax = new(100f, 100f);

        [Header("Вращение")]
        [Tooltip("Градусы/с с клавиатуры (Q/E).")]
        [SerializeField] private float rotateSpeed = 90f;
        [Tooltip("Градусы/с наклона с клавиатуры (R — к горизонту, F — сверху вниз).")]
        [SerializeField] private float tiltSpeed = 60f;
        [Tooltip("Градусы на пиксель при перетаскивании правой кнопкой.")]
        [SerializeField] private float mouseOrbitSensitivity = 0.25f;
        [SerializeField] private float minPitch = 25f;
        [SerializeField] private float maxPitch = 80f;

        [Header("Зум")]
        [SerializeField] private float minDistance = 8f;
        [SerializeField] private float maxDistance = 80f;
        [Tooltip("Доля дистанции, на которую приближает один щелчок колеса.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float zoomStepFraction = 0.12f;

        [Header("Плавность")]
        [Tooltip("Чем больше, тем быстрее камера догоняет цель. 0 — без сглаживания.")]
        [SerializeField] private float sharpness = 12f;

        [Header("Высота земли")]
        [Tooltip("Слои, по которым точка фокуса ищет высоту земли. Если луч не попал, высота не меняется.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundProbeHeight = 200f;

        public float StartYaw => startYaw;
        public float StartPitch => startPitch;
        public float StartDistance => startDistance;
        public float PanSpeed => panSpeed;
        public float PanReferenceDistance => panReferenceDistance;
        public Vector2 FocusBoundsMin => focusBoundsMin;
        public Vector2 FocusBoundsMax => focusBoundsMax;
        public float RotateSpeed => rotateSpeed;
        public float TiltSpeed => tiltSpeed;
        public float MouseOrbitSensitivity => mouseOrbitSensitivity;
        public float MinPitch => minPitch;
        public float MaxPitch => maxPitch;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public float ZoomStepFraction => zoomStepFraction;
        public float Sharpness => sharpness;
        public LayerMask GroundMask => groundMask;
        public float GroundProbeHeight => groundProbeHeight;

        private void OnValidate()
        {
            minDistance = Mathf.Max(0.1f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            startDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
            minPitch = Mathf.Clamp(minPitch, 0f, 89f);
            maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
            startPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
            focusBoundsMax = Vector2.Max(focusBoundsMin, focusBoundsMax);
        }
    }
}
