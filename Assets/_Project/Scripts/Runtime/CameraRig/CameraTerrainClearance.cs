using UnityEngine;

namespace Emberhold.CameraRig
{
    /// <summary>
    /// Не даёт камере уйти под рельеф: если между фокусом и камерой есть земля, камера подъезжает к фокусу,
    /// а если под камерой земля выше допустимого, камера поднимается над ней с зазором.
    /// Положение рига (фокус, углы, дистанция) не меняется: как только место освободится, камера вернётся сама.
    /// </summary>
    public static class CameraTerrainClearance
    {
        /// <summary>Куда поставить камеру вместо желаемой точки.</summary>
        /// <param name="focus">Точка фокуса на земле.</param>
        /// <param name="desired">Желаемое положение камеры.</param>
        /// <param name="radius">Радиус шара, которым проверяем путь от фокуса до камеры.</param>
        /// <param name="clearance">Зазор над землёй под камерой; луч к камере начинается на столько же выше фокуса.</param>
        /// <param name="probeHeight">С какой высоты над камерой ищем землю под ней.</param>
        public static Vector3 Resolve(Vector3 focus, Vector3 desired, LayerMask mask, float radius, float clearance, float probeHeight)
        {
            // Фокус лежит на самой поверхности; шар, начатый внутри коллайдера, его не видит, поэтому стартуем выше.
            var origin = focus + Vector3.up * clearance;
            var toCamera = desired - origin;
            var length = toCamera.magnitude;
            var position = desired;
            if (length > 1e-4f)
            {
                var direction = toCamera / length;
                if (Physics.SphereCast(origin, radius, direction, out var hit, length, mask, QueryTriggerInteraction.Ignore))
                    position = origin + direction * hit.distance;
            }

            var probeOrigin = new Vector3(position.x, position.y + probeHeight, position.z);
            if (Physics.Raycast(probeOrigin, Vector3.down, out var ground, probeHeight * 2f, mask, QueryTriggerInteraction.Ignore))
                position.y = LiftAbove(position.y, ground.point.y, clearance);
            return position;
        }

        /// <summary>Высота камеры не ниже земли под ней плюс зазор.</summary>
        public static float LiftAbove(float cameraY, float groundY, float clearance) =>
            Mathf.Max(cameraY, groundY + clearance);
    }
}
