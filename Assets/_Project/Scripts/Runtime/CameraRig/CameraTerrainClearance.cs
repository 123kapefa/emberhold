using UnityEngine;

namespace Emberhold.CameraRig
{
    /// <summary>
    /// Не даёт камере уйти под рельеф. Если между фокусом и камерой есть земля (стенка ямы, холм), риг сначала
    /// смотрит круче сверху (<see cref="PitchLift"/>) — так камера остаётся на своём расстоянии и не утыкается в стенку;
    /// только если и сверху не видно, камера подъезжает к фокусу (<see cref="Resolve"/>).
    /// Если под камерой земля выше допустимого, камера поднимается над ней с зазором.
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

        /// <summary>
        /// На сколько градусов круче посмотреть сверху, чтобы от фокуса до камеры не было земли: перебираем наклон
        /// шагами step до maxPitch. 0 — путь и так свободен; если не помогает и maxPitch, возвращается подъём до maxPitch.
        /// </summary>
        public static float PitchLift(CameraRigState state, float maxPitch, LayerMask mask, float radius, float clearance, float step = 5f)
        {
            var room = Mathf.Max(0f, maxPitch - state.Pitch);
            for (var lift = 0f; lift < room + step; lift += step)
            {
                var tried = Mathf.Min(lift, room);
                var probe = state;
                probe.Pitch += tried;
                if (IsClear(state.Focus, probe.CameraPosition, mask, radius, clearance))
                    return tried;
            }
            return room;
        }

        /// <summary>От фокуса (с зазором над ним) до камеры нет земли.</summary>
        public static bool IsClear(Vector3 focus, Vector3 camera, LayerMask mask, float radius, float clearance)
        {
            var origin = focus + Vector3.up * clearance;
            var toCamera = camera - origin;
            var length = toCamera.magnitude;
            return length <= 1e-4f || !Physics.SphereCast(origin, radius, toCamera / length, out _, length, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Высота камеры не ниже земли под ней плюс зазор.</summary>
        public static float LiftAbove(float cameraY, float groundY, float clearance) =>
            Mathf.Max(cameraY, groundY + clearance);
    }
}
