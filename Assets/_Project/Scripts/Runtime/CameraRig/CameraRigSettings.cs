using Emberhold.Core;
using UnityEngine;

namespace Emberhold.CameraRig
{
    [CreateAssetMenu(fileName = "CameraRigSettings", menuName = "Emberhold/Camera Rig Settings")]
    public sealed class CameraRigSettings : ScriptableObject
    {
        [Header("Старт")]
        [Range(0f, 360f)]
        [SerializeField] private float startYaw = 45f;
        [Range(0f, 89f)]
        [SerializeField] private float startPitch = 50f;
        [Range(1f, 200f)]
        [SerializeField] private float startDistance = 30f;

        [Header("Панорамирование")]
        [Tooltip("Ед./с с клавиатуры при дистанции Pan Reference Distance.")]
        [Range(1f, 100f)]
        [SerializeField] private float panSpeed = 20f;
        [Range(1f, 200f)]
        [SerializeField] private float panReferenceDistance = 30f;
        [Tooltip("Границы точки фокуса по X/Z.")]
        [SerializeField] private Vector2 focusBoundsMin = new(-100f, -100f);
        [SerializeField] private Vector2 focusBoundsMax = new(100f, 100f);

        [Header("Вращение")]
        [Tooltip("Градусы/с с клавиатуры (Q/E).")]
        [Range(10f, 360f)]
        [SerializeField] private float rotateSpeed = 90f;
        [Tooltip("Градусы на пиксель при перетаскивании правой кнопкой.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float mouseOrbitSensitivity = 0.25f;
        [Range(0f, 89f)]
        [SerializeField] private float minPitch = 25f;
        [Range(0f, 89f)]
        [SerializeField] private float maxPitch = 80f;

        [Header("Зум")]
        [Range(1f, 200f)]
        [SerializeField] private float minDistance = 8f;
        [Range(1f, 200f)]
        [SerializeField] private float maxDistance = 80f;
        [Tooltip("Доля дистанции, на которую приближает один щелчок колеса.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float zoomStepFraction = 0.12f;
        [Tooltip("Шагов зума в секунду при удержании R/F.")]
        [Range(1f, 20f)]
        [SerializeField] private float zoomKeySteps = 6f;

        [Header("Плавность")]
        [Tooltip("Чем больше, тем быстрее камера догоняет цель. 0 — без сглаживания.")]
        [Range(0f, 30f)]
        [SerializeField] private float sharpness = 12f;

        [Header("Высота земли")]
        [Tooltip("Слои, по которым точка фокуса ищет высоту земли (обычно только Ground). Если луч не попал, высота не меняется.")]
        [SerializeField] private LayerMask groundMask = Layers.GroundMask;
        [Range(10f, 1000f)]
        [SerializeField] private float groundProbeHeight = 200f;

        public float StartYaw => startYaw;
        public float StartPitch => startPitch;
        public float StartDistance => startDistance;
        public float PanSpeed => panSpeed;
        public float PanReferenceDistance => panReferenceDistance;
        public Vector2 FocusBoundsMin => focusBoundsMin;
        public Vector2 FocusBoundsMax => focusBoundsMax;
        public float RotateSpeed => rotateSpeed;
        public float MouseOrbitSensitivity => mouseOrbitSensitivity;
        public float MinPitch => minPitch;
        public float MaxPitch => maxPitch;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public float ZoomStepFraction => zoomStepFraction;
        public float ZoomKeySteps => zoomKeySteps;
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
