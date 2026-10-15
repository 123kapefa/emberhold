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
        /// <summary>Подол ниже пояса и всё, что висит на поясе — мягкая часть (<see cref="WorkerSkin"/>).</summary>
        Hips,
        FootLeft,
        FootRight,
        /// <summary>Пелерина — мягкая часть.</summary>
        Cape,
    }

    /// <summary>
    /// Поза рабочего: повороты частей относительно родителя (градусы, Эйлер в порядке Unity), сдвиг корня и где ноша.
    /// Знаки: у корпуса и головы +X — наклон вперёд; у бедра и плеча −X — вынос вперёд; у голени +X — сгиб колена назад;
    /// у предплечья −X — сгиб локтя вперёд-вверх; Z у руки — отвести в сторону (умножать на сторону: −1 левая, +1 правая).
    /// Стопы ставит скелет сам (<see cref="WorkerSkeleton.Evaluate"/>); подол и пелерину гнёт <see cref="WorkerSkin"/>.
    /// </summary>
    public struct WorkerPose
    {
        public const int PartCount = 14;

        /// <summary>Повороты частей, по индексу <see cref="WorkerPart"/>.</summary>
        public Vector3[] Angles;

        /// <summary>Сдвиг корня по X и Z, доли роста; высоту ставит прижатие к земле.</summary>
        public Vector2 Shift;

        /// <summary>Где ноша, в системе рабочего (ноги в нуле, блоки).</summary>
        public Vector3 LoadPosition;

        public Quaternion LoadRotation;

        /// <summary>
        /// Насколько корзина на спине: 1 — на спине, лямки на плечах; 0 — снята, лямки втянуты (<see cref="WorkerCarryBuilder.Shape"/>).
        /// </summary>
        public float LoadOnBack;

        public static WorkerPose Rest() => new() { Angles = new Vector3[PartCount], LoadRotation = Quaternion.identity };

        /// <summary>Сбросить в покой, не выделяя память (массив поворотов тот же).</summary>
        public void Reset()
        {
            if (Angles == null)
                Angles = new Vector3[PartCount];
            else
                System.Array.Clear(Angles, 0, PartCount);
            Shift = Vector2.zero;
            LoadPosition = Vector3.zero;
            LoadRotation = Quaternion.identity;
            LoadOnBack = 0f;
        }

        /// <summary>Скопировать позу other в эту, не выделяя память.</summary>
        public void CopyFrom(in WorkerPose other)
        {
            Angles ??= new Vector3[PartCount];
            System.Array.Copy(other.Angles, Angles, PartCount);
            Shift = other.Shift;
            LoadPosition = other.LoadPosition;
            LoadRotation = other.LoadRotation;
            LoadOnBack = other.LoadOnBack;
        }

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
            Lerp(a, b, t, ref result);
            return result;
        }

        /// <summary>Смешать позы a и b в уже готовую позу result (без выделения памяти; result не должен быть a или b).</summary>
        public static void Lerp(in WorkerPose a, in WorkerPose b, float t, ref WorkerPose result)
        {
            result.Angles ??= new Vector3[PartCount];
            for (var i = 0; i < PartCount; i++)
            {
                result.Angles[i] = new Vector3(Mathf.LerpAngle(a.Angles[i].x, b.Angles[i].x, t), Mathf.LerpAngle(a.Angles[i].y, b.Angles[i].y, t),
                    Mathf.LerpAngle(a.Angles[i].z, b.Angles[i].z, t));
            }
            result.Shift = Vector2.Lerp(a.Shift, b.Shift, t);
            result.LoadPosition = Vector3.Lerp(a.LoadPosition, b.LoadPosition, t);
            result.LoadRotation = Quaternion.Slerp(a.LoadRotation, b.LoadRotation, t);
            result.LoadOnBack = Mathf.Lerp(a.LoadOnBack, b.LoadOnBack, t);
        }
    }

    /// <summary>
    /// Скелет рабочего: точки вращения частей (в долях роста, ноги в нуле, лицо в +Z), иерархия и расчёт матриц частей
    /// по позе. Стопы всегда ровно (параллельно земле). Подол и пелерина — мягкие части: их матрица — матрица корпуса,
    /// а гнёт их <see cref="WorkerSkin"/> по бёдрам и плечам. Корень опускается так, чтобы самая низкая опора — подошвы или таз
    /// сидящего — касалась земли. Руку к точке ставит <see cref="ReachArm"/>.
    /// </summary>
    public static class WorkerSkeleton
    {
        private static readonly Vector3[] Pivots =
        {
            new(0f, 0.515f, 0f), // корпус — талия
            new(0f, 0.775f, 0f), // голова — шея
            new(-0.05f, 0.44f, 0f), new(0.05f, 0.44f, 0f), // бёдра
            new(-0.055f, 0.265f, 0.002f), new(0.055f, 0.265f, 0.002f), // колени
            new(-0.135f, 0.695f, -0.002f), new(0.135f, 0.695f, -0.002f), // плечи
            new(-0.175f, 0.55f, -0.008f), new(0.175f, 0.55f, -0.008f), // локти
            new(0f, 0.515f, 0f), // подол — талия
            new(-0.058f, 0.05f, 0.007f), new(0.058f, 0.05f, 0.007f), // щиколотки
            new(0f, 0.515f, 0f), // пелерина — как корпус
        };

        /// <summary>Родитель части; −1 — корень.</summary>
        private static readonly int[] Parents =
        {
            -1, (int)WorkerPart.Torso, -1, -1, (int)WorkerPart.ThighLeft, (int)WorkerPart.ThighRight,
            (int)WorkerPart.Torso, (int)WorkerPart.Torso, (int)WorkerPart.UpperArmLeft, (int)WorkerPart.UpperArmRight,
            (int)WorkerPart.Torso, (int)WorkerPart.ShinLeft, (int)WorkerPart.ShinRight, (int)WorkerPart.Torso,
        };

        /// <summary>Опоры стопы в системе стопы: пятка и носок на подошве.</summary>
        private static readonly Vector3[] SoleOffsets = { new(0f, -0.05f, -0.045f), new(0f, -0.05f, 0.075f) };

        /// <summary>Таз сидящего в системе подола (он движется с корпусом).</summary>
        private static readonly Vector3 SeatOffset = new(0f, -0.14f, -0.035f);

        private static readonly Matrix4x4[] Scratch = new Matrix4x4[WorkerPose.PartCount];
        private static readonly Vector3[] Effective = new Vector3[WorkerPose.PartCount];

        /// <summary>Центр кулака руки side (−1 левая) в собранной сетке, в блоках.</summary>
        public static Vector3 FistCenter(float side, float height) => WorkerGearBuilder.FistCenter(side) * height;

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
        /// Возвращает, на сколько опущен корень.
        /// </summary>
        public static float Evaluate(WorkerPose pose, float height, Matrix4x4[] result)
        {
            Solve(pose, height, Vector3.zero, result);
            var lowest = LowestSupport(result, height);
            var root = new Vector3(pose.Shift.x * height, -lowest, pose.Shift.y * height);
            Solve(pose, height, root, result);
            return -lowest;
        }

        /// <summary>Самая низкая точка опоры (подошвы, таз) по готовым матрицам частей.</summary>
        public static float LowestSupport(Matrix4x4[] parts, float height)
        {
            var lowest = float.MaxValue;
            foreach (var offset in SoleOffsets)
            {
                lowest = Mathf.Min(lowest, parts[(int)WorkerPart.FootLeft].MultiplyPoint3x4(offset * height).y);
                lowest = Mathf.Min(lowest, parts[(int)WorkerPart.FootRight].MultiplyPoint3x4(offset * height).y);
            }
            return Mathf.Min(lowest, parts[(int)WorkerPart.Hips].MultiplyPoint3x4(SeatOffset * height).y);
        }

        /// <summary>
        /// Поставить кулак руки side (−1 левая) в точку target (система рабочего, блоки), локоть — в сторону pole.
        /// Остальная поза берётся как есть; weight от 0 до 1 смешивает с прежним положением руки.
        /// </summary>
        public static void ReachArm(ref WorkerPose pose, float side, Vector3 target, Vector3 pole, float height, float weight)
        {
            if (weight <= 0f)
                return;
            var upperPart = side < 0f ? WorkerPart.UpperArmLeft : WorkerPart.UpperArmRight;
            var forePart = side < 0f ? WorkerPart.ForearmLeft : WorkerPart.ForearmRight;
            Evaluate(pose, height, Scratch);
            var torso = Scratch[(int)WorkerPart.Torso];
            var torsoRotation = torso.rotation;
            var shoulder = Scratch[(int)upperPart].GetColumn(3);
            var restUpper = (Pivots[(int)forePart] - Pivots[(int)upperPart]) * height;
            var restFore = (WorkerGearBuilder.FistCenter(side) - Pivots[(int)forePart]) * height;
            var upperLength = restUpper.magnitude;
            var foreLength = restFore.magnitude;

            var toTarget = target - (Vector3)shoulder;
            var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - foreLength) + 1e-4f, upperLength + foreLength - 1e-4f);
            var direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;
            var reach = (Vector3)shoulder + direction * distance;
            var along = (upperLength * upperLength - foreLength * foreLength + distance * distance) / (2f * distance);
            var bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var poleDirection = Vector3.ProjectOnPlane(pole - (Vector3)shoulder, direction);
            poleDirection = poleDirection.sqrMagnitude > 1e-8f ? poleDirection.normalized : Vector3.ProjectOnPlane(Vector3.down, direction).normalized;
            var elbow = (Vector3)shoulder + direction * along + poleDirection * bend;

            var upperWorld = Quaternion.FromToRotation(torsoRotation * restUpper, elbow - (Vector3)shoulder) * torsoRotation;
            var foreWorld = Quaternion.FromToRotation(upperWorld * restFore, reach - elbow) * upperWorld;
            var upperLocal = Quaternion.Inverse(torsoRotation) * upperWorld;
            var foreLocal = Quaternion.Inverse(upperWorld) * foreWorld;
            pose[upperPart] = Wrap(Quaternion.Slerp(Quaternion.Euler(pose[upperPart]), upperLocal, weight).eulerAngles);
            pose[forePart] = Wrap(Quaternion.Slerp(Quaternion.Euler(pose[forePart]), foreLocal, weight).eulerAngles);
        }

        /// <summary>Матрица корпуса в позе (для ноши на плече).</summary>
        public static Matrix4x4 Torso(WorkerPose pose, float height)
        {
            Evaluate(pose, height, Scratch);
            return Scratch[(int)WorkerPart.Torso];
        }

        private static void Solve(WorkerPose pose, float height, Vector3 root, Matrix4x4[] result)
        {
            var angles = pose.Angles;
            for (var i = 0; i < WorkerPose.PartCount; i++)
                Effective[i] = angles != null ? angles[i] : Vector3.zero;

            for (var i = 0; i < WorkerPose.PartCount; i++)
            {
                var parent = Parents[i];
                var rotation = Quaternion.Euler(Effective[i]);
                if (parent < 0)
                {
                    result[i] = Matrix4x4.TRS(root + Pivots[i] * height, rotation, Vector3.one);
                    continue;
                }
                var position = result[parent].MultiplyPoint3x4((Pivots[i] - Pivots[parent]) * height);
                // Стопы держатся ровно: их поворот — от земли, а не от голени.
                var world = i == (int)WorkerPart.FootLeft || i == (int)WorkerPart.FootRight ? rotation : result[parent].rotation * rotation;
                result[i] = Matrix4x4.TRS(position, world, Vector3.one);
            }
        }

        private static Vector3 Wrap(Vector3 euler) =>
            new(Mathf.DeltaAngle(0f, euler.x), Mathf.DeltaAngle(0f, euler.y), Mathf.DeltaAngle(0f, euler.z));
    }
}
