using UnityEngine;

namespace Emberhold.CameraRig
{
    /// <summary>
    /// Положение камеры, описанное через точку фокуса: камера смотрит на Focus
    /// под углами Yaw/Pitch с расстояния Distance.
    /// </summary>
    public struct CameraRigState
    {
        public Vector3 Focus;
        /// <summary>Поворот вокруг вертикальной оси, градусы. Не нормализуется, чтобы сглаживание не прыгало через 360.</summary>
        public float Yaw;
        /// <summary>Наклон вниз от горизонта, градусы.</summary>
        public float Pitch;
        public float Distance;

        public CameraRigState(Vector3 focus, float yaw, float pitch, float distance)
        {
            Focus = focus;
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
        }

        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);

        public Vector3 CameraPosition => Focus - Rotation * Vector3.forward * Distance;
    }
}
