using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Часть рабочего, которая двигается целиком. Левая — со стороны −X.</summary>
    public enum WorkerPart
    {
        Torso,
        Head,
        ThighLeft,
        ThighRight,
        ShinLeft,
        ShinRight,
        UpperArmLeft,
        UpperArmRight,
        ForearmLeft,
        ForearmRight,
    }

    /// <summary>
    /// Поза рабочего: повороты частей относительно родителя (градусы, Эйлер в порядке Unity) и сдвиг корня.
    /// Знаки: у корпуса и головы +X — наклон вперёд; у бедра и плеча −X — вынос вперёд; у голени +X — сгиб колена назад;
    /// у предплечья −X — сгиб локтя вперёд-вверх; Z у руки — отвести в сторону (умножать на сторону: −1 левая, +1 правая).
    /// </summary>
    public struct WorkerPose
    {
        public const int PartCount = 10;

        /// <summary>Повороты частей, по индексу <see cref="WorkerPart"/>.</summary>
        public Vector3[] Angles;

        /// <summary>Сдвиг корня по X и Z, доли роста; высоту ставит прижатие к земле.</summary>
        public Vector2 Shift;

        public static WorkerPose Rest() => new() { Angles = new Vector3[PartCount] };

        public Vector3 this[WorkerPart part]
        {
            get => Angles[(int)part];
            set => Angles[(int)part] = value;
        }

        /// <summary>Добавить к повороту части.</summary>
        public void Add(WorkerPart part, Vector3 degrees) => Angles[(int)part] += degrees;

        public static WorkerPose Lerp(WorkerPose a, WorkerPose b, float t)
        {
            var result = Rest();
            for (var i = 0; i < PartCount; i++)
                result.Angles[i] = Vector3.Lerp(a.Angles[i], b.Angles[i], t);
            result.Shift = Vector2.Lerp(a.Shift, b.Shift, t);
            return result;
        }
    }

    /// <summary>
    /// Скелет рабочего: точки вращения частей (в долях роста, ноги в нуле, лицо в +Z), иерархия и расчёт матриц частей
    /// по позе. Корень опускается так, чтобы самая низкая из опор — подошвы или таз сидящего — касалась земли.
    /// </summary>
    public static class WorkerSkeleton
    {
        private static readonly Vector3[] Pivots =
        {
            new(0f, 0.45f, 0f), // корпус — над тазом
            new(0f, 0.775f, 0f), // голова — шея
            new(-0.05f, 0.44f, 0f), new(0.05f, 0.44f, 0f), // бёдра
            new(-0.055f, 0.265f, 0.002f), new(0.055f, 0.265f, 0.002f), // колени
            new(-0.135f, 0.695f, -0.002f), new(0.135f, 0.695f, -0.002f), // плечи
            new(-0.175f, 0.55f, -0.008f), new(0.175f, 0.55f, -0.008f), // локти
        };

        /// <summary>Родитель части; −1 — корень.</summary>
        private static readonly int[] Parents =
        {
            -1, (int)WorkerPart.Torso, -1, -1, (int)WorkerPart.ThighLeft, (int)WorkerPart.ThighRight,
            (int)WorkerPart.Torso, (int)WorkerPart.Torso, (int)WorkerPart.UpperArmLeft, (int)WorkerPart.UpperArmRight,
        };

        /// <summary>Опоры стопы в системе голени: пятка и носок на подошве.</summary>
        private static readonly Vector3[] SoleOffsets = { new(0f, -0.265f, -0.03f), new(0f, -0.265f, 0.085f) };

        /// <summary>Таз сидящего в системе корпуса.</summary>
        private static readonly Vector3 SeatOffset = new(0f, -0.075f, -0.035f);

        /// <summary>Точка вращения части в собранной сетке, в блоках.</summary>
        public static Vector3 Pivot(WorkerPart part, float height) => Pivots[(int)part] * height;

        public static Vector3[] PivotsFor(float height)
        {
            var result = new Vector3[WorkerPose.PartCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = Pivots[i] * height;
            return result;
        }

        /// <summary>
        /// Матрицы частей в системе рабочего (ноги в нуле) по позе. result — массив длины PartCount.
        /// Возвращает, на сколько опущен корень (для отладки и тестов).
        /// </summary>
        public static float Evaluate(WorkerPose pose, float height, Matrix4x4[] result)
        {
            Solve(pose, height, Vector3.zero, result);
            var lowest = Lowest(pose, height, result);
            var root = new Vector3(pose.Shift.x * height, -lowest, pose.Shift.y * height);
            Solve(pose, height, root, result);
            return -lowest;
        }

        private static void Solve(WorkerPose pose, float height, Vector3 root, Matrix4x4[] result)
        {
            for (var i = 0; i < WorkerPose.PartCount; i++)
            {
                var parent = Parents[i];
                var rotation = Quaternion.Euler(pose.Angles?[i] ?? Vector3.zero);
                var local = parent < 0
                    ? Matrix4x4.TRS(root + Pivots[i] * height, rotation, Vector3.one)
                    : result[parent] * Matrix4x4.TRS((Pivots[i] - Pivots[parent]) * height, rotation, Vector3.one);
                result[i] = local;
            }
        }

        private static float Lowest(WorkerPose pose, float height, Matrix4x4[] parts)
        {
            var lowest = float.MaxValue;
            foreach (var shin in new[] { WorkerPart.ShinLeft, WorkerPart.ShinRight })
            {
                foreach (var offset in SoleOffsets)
                    lowest = Mathf.Min(lowest, parts[(int)shin].MultiplyPoint3x4(offset * height).y);
            }
            lowest = Mathf.Min(lowest, parts[(int)WorkerPart.Torso].MultiplyPoint3x4(SeatOffset * height).y);
            return lowest;
        }
    }
}
