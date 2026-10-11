using UnityEngine;

namespace Emberhold.CameraRig
{
    /// <summary>
    /// Чистая математика камеры без зависимости от сцены и ввода, чтобы её можно было проверять EditMode-тестами.
    /// </summary>
    public static class CameraRigMath
    {
        /// <summary>
        /// Сдвиг точки фокуса по земле. Input.x — вправо, Input.y — вперёд относительно направления камеры.
        /// </summary>
        public static Vector3 PanOffset(Vector2 input, float yaw, float worldDistance)
        {
            var heading = Quaternion.Euler(0f, yaw, 0f);
            var right = heading * Vector3.right;
            var forward = heading * Vector3.forward;
            return (right * input.x + forward * input.y) * worldDistance;
        }

        /// <summary>
        /// Сдвиг фокуса при перетаскивании мышью на delta пикселей. Земля видна под углом, поэтому
        /// вертикальный пиксель покрывает 1/sin(pitch) единиц по земле, а горизонтальный — одну.
        /// </summary>
        public static Vector3 DragPanOffset(Vector2 pixelDelta, float yaw, float pitch, float unitsPerPixel)
        {
            var sinPitch = Mathf.Max(Mathf.Sin(pitch * Mathf.Deg2Rad), 0.1f);
            return PanOffset(new Vector2(pixelDelta.x, pixelDelta.y / sinPitch), yaw, unitsPerPixel);
        }

        /// <summary>
        /// Скорость панорамирования растёт с отдалением, чтобы на любом зуме экран проезжал за одинаковое время.
        /// </summary>
        public static float ScaledPanSpeed(float baseSpeed, float distance, float referenceDistance)
        {
            if (referenceDistance <= 0f)
                return baseSpeed;
            return baseSpeed * distance / referenceDistance;
        }

        /// <summary>
        /// Сколько мировых единиц на уровне точки фокуса приходится на один пиксель экрана.
        /// </summary>
        public static float WorldUnitsPerPixel(float distance, float verticalFovDegrees, float screenHeightPixels)
        {
            if (screenHeightPixels <= 0f)
                return 0f;
            var visibleHeight = 2f * distance * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            return visibleHeight / screenHeightPixels;
        }

        /// <summary>
        /// Зум: каждый шаг колеса меняет дистанцию в одно и то же число раз, затем ограничение мин/макс.
        /// Положительный шаг приближает.
        /// </summary>
        public static float Zoom(float distance, float steps, float stepFraction, float minDistance, float maxDistance)
        {
            var factor = Mathf.Pow(1f - Mathf.Clamp(stepFraction, 0f, 0.95f), steps);
            return Mathf.Clamp(distance * factor, minDistance, maxDistance);
        }

        /// <summary>
        /// Колесо на разных платформах отдаёт от долей до ±120 за щелчок; приводим к не более чем одному шагу за кадр.
        /// </summary>
        public static float NormalizeScroll(float rawScroll) => Mathf.Clamp(rawScroll, -1f, 1f);

        public static Vector3 ClampToBounds(Vector3 focus, Vector2 boundsMin, Vector2 boundsMax)
        {
            focus.x = Mathf.Clamp(focus.x, boundsMin.x, boundsMax.x);
            focus.z = Mathf.Clamp(focus.z, boundsMin.y, boundsMax.y);
            return focus;
        }

        /// <summary>
        /// Коэффициент экспоненциального сглаживания, не зависящий от частоты кадров:
        /// два кадра по dt дают тот же результат, что один кадр 2·dt.
        /// </summary>
        public static float DampFactor(float sharpness, float deltaTime)
        {
            if (sharpness <= 0f)
                return 1f;
            return 1f - Mathf.Exp(-sharpness * deltaTime);
        }

        public static CameraRigState Damp(CameraRigState current, CameraRigState target, float sharpness, float deltaTime)
        {
            var t = DampFactor(sharpness, deltaTime);
            return new CameraRigState(
                Vector3.LerpUnclamped(current.Focus, target.Focus, t),
                Mathf.LerpUnclamped(current.Yaw, target.Yaw, t),
                Mathf.LerpUnclamped(current.Pitch, target.Pitch, t),
                Mathf.LerpUnclamped(current.Distance, target.Distance, t));
        }
    }
}
