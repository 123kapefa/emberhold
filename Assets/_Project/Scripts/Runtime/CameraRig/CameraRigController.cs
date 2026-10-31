using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberhold.CameraRig
{
    /// <summary>
    /// Камера стратегии: смотрит сверху под углом на точку фокуса, вращается вокруг неё,
    /// приближается колесом и панорамирует, сдвигая точку фокуса.
    /// Ввод берётся из карты «Camera» в EmberholdControls.
    /// </summary>
    public sealed class CameraRigController : MonoBehaviour
    {
        private const string ActionMapName = "Camera";
        // После подвисания (загрузка, брейкпоинт) удержанная клавиша не должна унести камеру далеко.
        private const float MaxDeltaTime = 0.1f;

        [SerializeField] private CameraRigSettings settings;
        [Tooltip("Камера, которой управляет риг. Если пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private InputActionMap map;
        private InputAction pan;
        private InputAction rotate;
        private InputAction zoomKeys;
        private InputAction zoom;
        private InputAction pointerDelta;
        private InputAction orbitHold;
        private InputAction panHold;

        private CameraRigState current;
        private CameraRigState target;

        public CameraRigState Target => target;

        /// <summary>Мгновенно навести камеру на точку, например на костёр в начале партии.</summary>
        public void SnapTo(Vector3 focus)
        {
            if (settings == null)
                return;
            target.Focus = ClampFocus(focus);
            target.Focus.y = SampleGroundHeight(target.Focus);
            current = target;
            ApplyPose(current);
        }

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError("[CameraRig] Не назначен CameraRigSettings.", this);
                enabled = false;
                return;
            }

            if (targetCamera == null)
                targetCamera = UnityEngine.Camera.main;

            target = new CameraRigState(transform.position, settings.StartYaw, settings.StartPitch, settings.StartDistance);
            target.Focus = ClampFocus(target.Focus);
            target.Focus.y = SampleGroundHeight(target.Focus);
            current = target;
        }

        private void OnEnable()
        {
            if (settings == null)
            {
                enabled = false;
                return;
            }

            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            if (map == null)
            {
                Debug.LogError($"[CameraRig] Нет карты ввода «{ActionMapName}».", this);
                enabled = false;
                return;
            }

            pan = map.FindAction("Pan", true);
            rotate = map.FindAction("Rotate", true);
            zoomKeys = map.FindAction("ZoomKeys", true);
            zoom = map.FindAction("Zoom", true);
            pointerDelta = map.FindAction("PointerDelta", true);
            orbitHold = map.FindAction("OrbitHold", true);
            panHold = map.FindAction("PanHold", true);
            map.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
        }

        private void LateUpdate()
        {
            if (map == null || targetCamera == null)
                return;

            // Камера должна работать и на паузе, поэтому время без масштаба.
            var dt = Mathf.Min(Time.unscaledDeltaTime, MaxDeltaTime);
            ReadInput(dt);

            target.Focus = ClampFocus(target.Focus);
            target.Focus.y = SampleGroundHeight(target.Focus);

            current = CameraRigMath.Damp(current, target, settings.Sharpness, dt);
            ApplyPose(current);
        }

        private void ReadInput(float dt)
        {
            // Направление и масштаб панорамы берём из того, что игрок видит сейчас, а не из цели сглаживания.
            var delta = pointerDelta.ReadValue<Vector2>();

            var keyPan = pan.ReadValue<Vector2>();
            if (keyPan != Vector2.zero)
            {
                var speed = CameraRigMath.ScaledPanSpeed(settings.PanSpeed, current.Distance, settings.PanReferenceDistance);
                target.Focus += CameraRigMath.PanOffset(Vector2.ClampMagnitude(keyPan, 1f), current.Yaw, speed * dt);
            }

            if (panHold.IsPressed())
            {
                // Перетаскивание «держит» землю: курсор вправо — мир едет вправо, фокус влево.
                var unitsPerPixel = CameraRigMath.WorldUnitsPerPixel(current.Distance, targetCamera.fieldOfView, Screen.height);
                target.Focus -= CameraRigMath.DragPanOffset(delta, current.Yaw, current.Pitch, unitsPerPixel);
            }

            target.Yaw += rotate.ReadValue<float>() * settings.RotateSpeed * dt;

            if (orbitHold.IsPressed())
            {
                target.Yaw += delta.x * settings.MouseOrbitSensitivity;
                target.Pitch -= delta.y * settings.MouseOrbitSensitivity;
            }
            target.Pitch = Mathf.Clamp(target.Pitch, settings.MinPitch, settings.MaxPitch);

            // Колесо даёт шаги за кадр, R/F — плавный зум при удержании.
            var zoomSteps = CameraRigMath.NormalizeScroll(zoom.ReadValue<float>())
                            + zoomKeys.ReadValue<float>() * settings.ZoomKeySteps * dt;
            target.Distance = CameraRigMath.Zoom(target.Distance, zoomSteps, settings.ZoomStepFraction,
                settings.MinDistance, settings.MaxDistance);
        }

        private Vector3 ClampFocus(Vector3 focus) =>
            CameraRigMath.ClampToBounds(focus, settings.FocusBoundsMin, settings.FocusBoundsMax);

        /// <summary>
        /// Высота земли под фокусом лучом сверху вниз. Мир будет из блоков с неполной высотой и склонами,
        /// поэтому y=0 не предполагаем; если луч ничего не нашёл, высота остаётся прежней.
        /// </summary>
        private float SampleGroundHeight(Vector3 focus)
        {
            var origin = new Vector3(focus.x, focus.y + settings.GroundProbeHeight, focus.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, settings.GroundProbeHeight * 2f,
                    settings.GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return focus.y;
        }

        private void ApplyPose(CameraRigState state)
        {
            transform.position = state.Focus;
            if (targetCamera == null)
                return;
            // Сама поза рига не меняется: подъезд и подъём над землёй только для камеры в этом кадре.
            var desired = state.CameraPosition;
            var position = CameraTerrainClearance.Resolve(state.Focus, desired, settings.GroundMask,
                settings.CameraCollisionRadius, settings.CameraClearance, settings.GroundProbeHeight);
            var rotation = state.Rotation;
            var toFocus = state.Focus - position;
            if ((position - desired).sqrMagnitude > 1e-6f && toFocus.sqrMagnitude > 1e-6f)
                rotation = Quaternion.LookRotation(toFocus);
            targetCamera.transform.SetPositionAndRotation(position, rotation);
        }

        private void OnDrawGizmosSelected()
        {
            var focus = Application.isPlaying ? target.Focus : transform.position;
            Gizmos.color = new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireSphere(focus, 0.5f);
        }
    }
}
