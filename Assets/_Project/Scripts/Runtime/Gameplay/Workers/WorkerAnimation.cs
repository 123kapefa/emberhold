using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Общие анимации поселенцев.</summary>
    public enum WorkerClip
    {
        /// <summary>Стоит: дышит, иногда оглядывается или переминается.</summary>
        Idle,
        Walk,
        /// <summary>Бег к огню.</summary>
        Run,
        /// <summary>Присесть и поднять ресурс.</summary>
        PickUp,
        /// <summary>Идти с ношей: бревно на левом плече (левая рука его придерживает) или корзина на спине (руки свободны).</summary>
        Carry,
        /// <summary>Поставить блок перед собой.</summary>
        Place,
        /// <summary>Положить: снять ношу с плеча, присесть и опустить её на землю (в огонь).</summary>
        Put,
        /// <summary>Переступать на месте, пока разворачивается.</summary>
        Turn,
        /// <summary>Вздрогнуть и оглянуться, когда пропал свет.</summary>
        Startle,
        /// <summary>Греться: руки к огню.</summary>
        Warm,
        /// <summary>Сидеть у огня.</summary>
        Sit,
        /// <summary>Лесоруб рубит: косой удар топором из-за правого плеча, цикл <see cref="WorkerAnimation.WorkCycle"/>.</summary>
        Chop,
        /// <summary>Копатель бьёт киркой: замах над головой, удар вниз перед собой.</summary>
        Dig,
        /// <summary>Строитель: три коротких удара молотком перед грудью.</summary>
        Hammer,
        /// <summary>Истопник: берёт полено из вязанки, кладёт в огонь перед собой и шевелит угли кочергой.</summary>
        Stoke,
    }

    /// <summary>
    /// Позы общих анимаций кодом: каждая — функция времени. Циклы (стойка, ходьба, бег, ноша, поворот, у огня) берут
    /// время в секундах, разовые (поднять, поставить, положить, испуг) — долю от 0 до 1. Ходьба и бег привязаны
    /// к скорости: шаг тем чаще, чем быстрее идёт, и ноги не скользят по земле.
    /// </summary>
    public static class WorkerAnimation
    {
        /// <summary>Сколько блоков проходит за полный цикл шагов (два шага) при росте 1,9.</summary>
        private const float WalkCycleBlocks = 1.45f;
        private const float RunCycleBlocks = 2.3f;

        public const float DefaultWalkSpeed = 2.5f;
        public const float DefaultRunSpeed = 4.5f;

        /// <summary>Длительность разовых анимаций по умолчанию, секунды (в игре их задают таймеры рабочего).</summary>
        public static float DefaultDuration(WorkerClip clip) => clip switch
        {
            WorkerClip.PickUp => 0.7f,
            WorkerClip.Place => 1.5f,
            WorkerClip.Put => 1.2f,
            WorkerClip.Startle => 0.45f,
            _ => 0f,
        };

        /// <summary>Разовая анимация: время — доля от 0 до 1.</summary>
        public static bool IsOneShot(WorkerClip clip) => DefaultDuration(clip) > 0f;

        /// <summary>Длина цикла рабочей анимации роли, секунды; у остальных — 0.</summary>
        public static float WorkCycle(WorkerClip clip) => clip switch
        {
            WorkerClip.Chop => 1.2f,
            WorkerClip.Dig => 1.3f,
            WorkerClip.Hammer => 1.5f,
            WorkerClip.Stoke => 3f,
            _ => 0f,
        };

        /// <summary>Доля цикла «рубить» и «копать», на которой инструмент бьёт (ключ удара в ChopKeys и DigKeys).</summary>
        public const float SwingImpact = 0.6f;

        /// <summary>Чья это рабочая анимация (у неё в руке нужный инструмент); null — общая, для всех.</summary>
        public static WorkerRole? RoleOf(WorkerClip clip) => clip switch
        {
            WorkerClip.Chop => WorkerRole.Woodcutter,
            WorkerClip.Dig => WorkerRole.Digger,
            WorkerClip.Hammer => WorkerRole.Builder,
            WorkerClip.Stoke => WorkerRole.Stoker,
            _ => null,
        };

        /// <summary>
        /// Поза анимации clip в момент time (секунды для циклов, доля для разовых). Из motion: Seed разводит фазы у разных
        /// рабочих, Speed — скорость (блоков в секунду), RightBusy и LeftBusy — в руке инструмент, она почти не качается,
        /// Carry — что несёт.
        /// </summary>
        public static WorkerPose Sample(WorkerClip clip, float time, in WorkerMotion motion)
        {
            var pose = WorkerPose.Rest();
            Sample(clip, time, motion, ref pose);
            return pose;
        }

        /// <summary>То же, но в уже готовую позу pose: она перезаписывается, память не выделяется.</summary>
        public static void Sample(WorkerClip clip, float time, in WorkerMotion motion, ref WorkerPose pose)
        {
            pose.Reset();
            Relaxed(ref pose);
            switch (clip)
            {
                case WorkerClip.Idle:
                    Idle(ref pose, time, motion);
                    break;
                case WorkerClip.Walk:
                    Steps(ref pose, time, motion, Speed(motion, DefaultWalkSpeed) / Cycle(motion, WalkCycleBlocks), 24f, 38f, 22f, 3f);
                    break;
                case WorkerClip.Run:
                    Steps(ref pose, time, motion, Speed(motion, DefaultRunSpeed) / Cycle(motion, RunCycleBlocks), 38f, 75f, 34f, 13f);
                    // Локти согнуты; рука с инструментом или оружием — меньше, иначе оно упирается в плечо.
                    pose.Add(WorkerPart.ForearmLeft, new Vector3(motion.LeftBusy ? -35f : -70f, 0f, 0f));
                    pose.Add(WorkerPart.ForearmRight, new Vector3(motion.RightBusy ? -35f : -70f, 0f, 0f));
                    break;
                case WorkerClip.Carry:
                    if (motion.Speed > 0.05f)
                        Steps(ref pose, time, motion, Speed(motion, DefaultWalkSpeed) / Cycle(motion, WalkCycleBlocks), 21f, 32f, 16f, 2f);
                    else
                        Breathe(ref pose, time, motion);
                    // Под ношей на левом плече корпус чуть клонится влево; корзина на спине — посередине.
                    if (WorkerCarryBuilder.HeldWhileCarried(motion.Carry))
                    {
                        pose.Add(WorkerPart.Torso, new Vector3(0f, 0f, -3f));
                        pose.Add(WorkerPart.Head, new Vector3(0f, 0f, -4f));
                    }
                    break;
                case WorkerClip.Turn:
                    Steps(ref pose, time, motion, 1.8f, 9f, 24f, 6f, 1f);
                    break;
                case WorkerClip.PickUp:
                    // Поднять — то же, что положить, в обратную сторону: присел, взял с земли, закинул на плечо.
                    PutDown(1f - time, motion, ref pose);
                    return;
                case WorkerClip.Put:
                    PutDown(time, motion, ref pose);
                    return;
                case WorkerClip.Place:
                    Place(ref pose, time);
                    break;
                case WorkerClip.Startle:
                    Startle(ref pose, time);
                    break;
                case WorkerClip.Warm:
                    Warm(ref pose, time, motion);
                    break;
                case WorkerClip.Sit:
                    Sit(ref pose, time, motion);
                    break;
                case WorkerClip.Chop:
                case WorkerClip.Dig:
                case WorkerClip.Hammer:
                    Work(ref pose, clip, Mathf.Repeat(time / WorkCycle(clip), 1f));
                    break;
                case WorkerClip.Stoke:
                    Stoke(Mathf.Repeat(time / WorkCycle(clip), 1f), motion, ref pose);
                    return;
            }
            SetLoad(ref pose, motion, holdWeight: clip == WorkerClip.Carry && WorkerCarryBuilder.HeldWhileCarried(motion.Carry) ? 1f : 0f);
        }

        /// <summary>
        /// Ноша на плече или спине (по матрице корпуса в этой позе); holdWeight — насколько левая рука её придерживает.
        /// Если ничего не несёт — считать нечего (скелет лишний раз не считается).
        /// </summary>
        private static void SetLoad(ref WorkerPose pose, in WorkerMotion motion, float holdWeight)
        {
            if (motion.Carry == WorkerCarry.None)
                return;
            var carry = motion.Carry;
            var load = WorkerSkeleton.Torso(pose, motion.Height) * WorkerCarryBuilder.ShoulderAnchor(carry, motion.Height);
            pose.LoadPosition = load.GetColumn(3);
            pose.LoadRotation = load.rotation;
            pose.LoadOnBack = 1f;
            if (holdWeight > 0f)
                HoldLoad(ref pose, carry, load, motion.Height, holdWeight);
        }

        /// <summary>Левая рука держит ношу за точку хвата; локоть вниз и наружу.</summary>
        private static void HoldLoad(ref WorkerPose pose, WorkerCarry carry, Matrix4x4 load, float height, float weight)
        {
            var grip = load.MultiplyPoint3x4(WorkerCarryBuilder.Grip(height));
            WorkerSkeleton.ReachArm(ref pose, -1f, grip, grip + WorkerCarryBuilder.ElbowHint(height), height, weight);
        }

        /// <summary>
        /// Положить ношу (t от 0 до 1): снять с плеча и перехватить перед собой, присесть с ровными стопами и опустить
        /// на землю, отпустить, выпрямиться. Ноша остаётся лежать на земле до конца.
        /// </summary>
        private static void PutDown(float t, in WorkerMotion motion, ref WorkerPose pose)
        {
            var carry = motion.Carry;
            var height = motion.Height;
            // Присед: начинается, когда ноша сошла с плеча, глубже всего с 0,45 до 0,75. Без ноши — только присел.
            var crouch = Bump(Mathf.InverseLerp(0.2f, 1f, t), 0.32f, 0.3f);
            Crouch(ref pose, crouch);
            if (carry == WorkerCarry.None)
                return;
            if (WorkerCarryBuilder.HeldWhileCarried(carry))
                pose.Add(WorkerPart.Torso, new Vector3(0f, 0f, -3f * (1f - crouch)));

            // Ноша идёт по пути, который обходит тело: плечо → … → земля, за первые 0,6 анимации, каждый отрезок плавно.
            var shoulder = WorkerSkeleton.Torso(pose, height) * WorkerCarryBuilder.ShoulderAnchor(carry, height);
            var path = WorkerCarryBuilder.PathDown(carry, height);
            var travel = Mathf.Clamp01(t / 0.6f) * path.Length;
            var segment = Mathf.Min(path.Length - 1, Mathf.FloorToInt(travel));
            var from = segment == 0 ? shoulder : path[segment - 1];
            var load = Blend(from, path[segment], Mathf.SmoothStep(0f, 1f, travel - segment));
            // Лямки корзины втягиваются в начале, пока она едва сдвинулась со спины.
            pose.LoadOnBack = segment == 0 ? 1f - Mathf.SmoothStep(0f, 1f, travel * 2.5f) : 0f;
            pose.LoadPosition = load.GetColumn(3);
            pose.LoadRotation = load.rotation;
            // Рука держит ношу до 0,7, потом отпускает и уходит в покой.
            var hold = t < 0.65f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.15f);
            if (WorkerCarryBuilder.HeldWhileCarried(carry))
                HoldLoad(ref pose, carry, load, height, hold);
            else
            {
                // Со спины корзину разворачивают на левой лямке; левая рука подхватывает её у левого бока,
                // правая — когда она уже перед грудью.
                var left = hold * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.17f, t));
                var right = hold * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.5f, t));
                HoldBasket(ref pose, load, height, left, right);
            }
        }

        /// <summary>
        /// Руки держат корзину за бока у края: левая слева, правая справа (по сторонам рабочего); локти вниз и наружу.
        /// Пока корзина сбоку слева, левая рука держит её за ближний к телу бок — иначе рука ложится поперёк корзины.
        /// </summary>
        private static void HoldBasket(ref WorkerPose pose, Matrix4x4 load, float height, float left, float right)
        {
            var side = WorkerCarryBuilder.BasketSideGrip(height);
            Vector3 center = load.GetColumn(3);
            var aside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f * height, -0.2f * height, center.x));
            var top = load.MultiplyPoint3x4(Vector3.up * side.y);
            if (left > 0f)
            {
                var grip = top + new Vector3(Mathf.Lerp(-side.x, side.x, aside), 0f, 0f);
                WorkerSkeleton.ReachArm(ref pose, -1f, grip, grip + new Vector3(-0.25f, -0.3f, 0f) * height, height, left);
            }
            if (right > 0f)
            {
                var grip = top + new Vector3(side.x, 0f, 0f);
                WorkerSkeleton.ReachArm(ref pose, 1f, grip, grip + new Vector3(0.25f, -0.3f, 0f) * height, height, right);
            }
        }

        /// <summary>
        /// Переход от позы from к позе to (t от 0 до 1). Тело смешивается, а ноша не летит по прямой сквозь тело
        /// (с плеча на землю и обратно): она держится корпуса так же, как в позе to.
        /// </summary>
        public static WorkerPose Blend(WorkerPose from, WorkerPose to, float t, float height)
        {
            var pose = WorkerPose.Rest();
            Blend(from, to, t, height, ref pose);
            return pose;
        }

        /// <summary>То же, но в уже готовую позу pose (не from и не to): память не выделяется.</summary>
        public static void Blend(in WorkerPose from, in WorkerPose to, float t, float height, ref WorkerPose pose)
        {
            WorkerPose.Lerp(from, to, t, ref pose);
            var load = WorkerSkeleton.Torso(pose, height) * WorkerSkeleton.Torso(to, height).inverse *
                       Matrix4x4.TRS(to.LoadPosition, to.LoadRotation, Vector3.one);
            pose.LoadPosition = load.GetColumn(3);
            pose.LoadRotation = load.rotation;
            pose.LoadOnBack = to.LoadOnBack;
        }

        private static Matrix4x4 Blend(Matrix4x4 a, Matrix4x4 b, float t) =>
            Matrix4x4.TRS(Vector3.Lerp(a.GetColumn(3), b.GetColumn(3), t), Quaternion.Slerp(a.rotation, b.rotation, t), Vector3.one);

        private static float Speed(in WorkerMotion motion, float fallback) => motion.Speed > 0.05f ? motion.Speed : fallback;

        private static float Cycle(in WorkerMotion motion, float blocks) => blocks * Mathf.Max(0.3f, motion.Height) / 1.9f;

        /// <summary>Руки свободно висят, чуть отведены от тела и согнуты в локтях.</summary>
        private static void Relaxed(ref WorkerPose pose)
        {
            pose.Add(WorkerPart.UpperArmLeft, new Vector3(0f, 0f, -4f));
            pose.Add(WorkerPart.UpperArmRight, new Vector3(0f, 0f, 4f));
            pose.Add(WorkerPart.ForearmLeft, new Vector3(-8f, 0f, 0f));
            pose.Add(WorkerPart.ForearmRight, new Vector3(-8f, 0f, 0f));
        }

        private static void Breathe(ref WorkerPose pose, float time, in WorkerMotion motion)
        {
            var breath = Mathf.Sin(2f * Mathf.PI * (time / 3.2f + motion.Seed));
            pose.Add(WorkerPart.Torso, new Vector3(1.2f * breath, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(-0.8f * breath, 0f, 0f));
            pose.Add(WorkerPart.UpperArmLeft, new Vector3(0f, 0f, -0.8f * breath));
            pose.Add(WorkerPart.UpperArmRight, new Vector3(0f, 0f, 0.8f * breath));
        }

        private static void Idle(ref WorkerPose pose, float time, in WorkerMotion motion)
        {
            Breathe(ref pose, time, motion);
            // Раз в 7 секунд (у каждого со своим сдвигом) — две секунды «живой» вставки: оглянуться или переступить.
            const float period = 7f;
            var local = time + motion.Seed * period;
            var cycle = Mathf.FloorToInt(local / period);
            var into = local - cycle * period;
            if (into > 2f)
                return;
            var weight = Mathf.Sin(Mathf.PI * into / 2f);
            var sign = (cycle & 1) == 0 ? 1f : -1f;
            if (Mathf.Abs(Mathf.Sin(cycle * 12.9898f + motion.Seed * 78.233f)) < 0.5f)
            {
                pose.Add(WorkerPart.Head, new Vector3(0f, 32f * sign * Mathf.Sin(Mathf.PI * into), 0f));
                pose.Add(WorkerPart.Torso, new Vector3(0f, 6f * sign * Mathf.Sin(Mathf.PI * into), 0f));
            }
            else
            {
                // Перенёс вес на одну ногу: таз в сторону, другое колено чуть согнуто.
                pose.Shift += new Vector2(0.014f * sign * weight, 0f);
                pose.Add(WorkerPart.Torso, new Vector3(0f, 0f, -2.5f * sign * weight));
                pose.Add(WorkerPart.Head, new Vector3(0f, 0f, 2f * sign * weight));
                var bent = sign > 0f ? WorkerPart.ShinLeft : WorkerPart.ShinRight;
                var thigh = sign > 0f ? WorkerPart.ThighLeft : WorkerPart.ThighRight;
                pose.Add(thigh, new Vector3(-6f * weight, 0f, 0f));
                pose.Add(bent, new Vector3(12f * weight, 0f, 0f));
            }
        }

        /// <summary>Шаги: бёдра в противофазе, колено сгибается, пока нога идёт вперёд; руки против ног.</summary>
        private static void Steps(ref WorkerPose pose, float time, in WorkerMotion motion, float cyclesPerSecond, float hip, float knee,
            float arm, float lean)
        {
            var phase = 2f * Mathf.PI * (time * cyclesPerSecond + motion.Seed);
            var swing = Mathf.Sin(phase);
            var lift = Mathf.Cos(phase);
            pose.Add(WorkerPart.ThighLeft, new Vector3(-hip * swing, 0f, 0f));
            pose.Add(WorkerPart.ThighRight, new Vector3(hip * swing, 0f, 0f));
            pose.Add(WorkerPart.ShinLeft, new Vector3(5f + knee * Mathf.Max(0f, lift), 0f, 0f));
            pose.Add(WorkerPart.ShinRight, new Vector3(5f + knee * Mathf.Max(0f, -lift), 0f, 0f));
            pose.Add(WorkerPart.UpperArmLeft, new Vector3(arm * swing * (motion.LeftBusy ? 0.35f : 1f), 0f, 0f));
            pose.Add(WorkerPart.UpperArmRight, new Vector3(-arm * swing * (motion.RightBusy ? 0.35f : 1f), 0f, 0f));
            pose.Add(WorkerPart.Torso, new Vector3(lean, 4f * swing, 0f));
            pose.Add(WorkerPart.Head, new Vector3(-lean * 0.6f, -3f * swing, 0f));
        }

        /// <summary>Присесть на корточки, стопы ровно на земле, корпус наклонён: глубина amount от 0 до 1.</summary>
        private static void Crouch(ref WorkerPose pose, float amount)
        {
            pose.Add(WorkerPart.ThighLeft, new Vector3(-78f * amount, 0f, 0f));
            pose.Add(WorkerPart.ThighRight, new Vector3(-78f * amount, 0f, 0f));
            pose.Add(WorkerPart.ThighLeft, new Vector3(0f, 0f, -8f * amount));
            pose.Add(WorkerPart.ThighRight, new Vector3(0f, 0f, 8f * amount));
            pose.Add(WorkerPart.ShinLeft, new Vector3(118f * amount, 0f, 0f));
            pose.Add(WorkerPart.ShinRight, new Vector3(118f * amount, 0f, 0f));
            pose.Add(WorkerPart.Torso, new Vector3(34f * amount, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(-16f * amount, 0f, 0f));
            pose.Shift += new Vector2(0f, -0.05f * amount);
        }

        private static void Place(ref WorkerPose pose, float t)
        {
            var amount = Bump(t, 0.35f, 0.3f);
            // На пике — короткий толчок вперёд: блок встал.
            var push = t > 0.35f && t < 0.7f ? Mathf.Sin(Mathf.PI * (t - 0.35f) / 0.35f) : 0f;
            pose.Add(WorkerPart.ThighLeft, new Vector3(-18f * amount, 0f, 0f));
            pose.Add(WorkerPart.ThighRight, new Vector3(-18f * amount, 0f, 0f));
            pose.Add(WorkerPart.ShinLeft, new Vector3(30f * amount, 0f, 0f));
            pose.Add(WorkerPart.ShinRight, new Vector3(30f * amount, 0f, 0f));
            pose.Add(WorkerPart.Torso, new Vector3(24f * amount + 4f * push, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(-10f * amount, 0f, 0f));
            // Плечи вперёд не выше 45°, дальше тянется предплечье: так плечо не прорывает пелерину.
            pose.Add(WorkerPart.UpperArmLeft, new Vector3(-42f * amount - 6f * push, 0f, 0f));
            pose.Add(WorkerPart.UpperArmRight, new Vector3(-42f * amount - 6f * push, 0f, 0f));
            pose.Add(WorkerPart.ForearmLeft, new Vector3(-38f * amount, 0f, 0f));
            pose.Add(WorkerPart.ForearmRight, new Vector3(-38f * amount, 0f, 0f));
        }

        private static void Startle(ref WorkerPose pose, float t)
        {
            var amount = Bump(t, 0.2f, 0.55f);
            pose.Add(WorkerPart.Torso, new Vector3(-10f * amount, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(-6f * amount, 38f * Mathf.Sin(2f * Mathf.PI * t), 0f));
            pose.Add(WorkerPart.UpperArmLeft, new Vector3(-32f * amount, 0f, -22f * amount));
            pose.Add(WorkerPart.UpperArmRight, new Vector3(-32f * amount, 0f, 22f * amount));
            pose.Add(WorkerPart.ForearmLeft, new Vector3(-62f * amount, 0f, 0f));
            pose.Add(WorkerPart.ForearmRight, new Vector3(-62f * amount, 0f, 0f));
            pose.Add(WorkerPart.ShinLeft, new Vector3(16f * amount, 0f, 0f));
            pose.Add(WorkerPart.ShinRight, new Vector3(16f * amount, 0f, 0f));
            pose.Add(WorkerPart.ThighLeft, new Vector3(-8f * amount, 0f, 0f));
            pose.Add(WorkerPart.ThighRight, new Vector3(-8f * amount, 0f, 0f));
        }

        private static void Warm(ref WorkerPose pose, float time, in WorkerMotion motion)
        {
            Breathe(ref pose, time, motion);
            // Руки вытянуты к огню ладонями вперёд, иногда потирает ладони.
            var rub = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (time / 5f + motion.Seed))) * Mathf.Sin(2f * Mathf.PI * time * 2.2f);
            pose.Add(WorkerPart.Torso, new Vector3(6f, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(4f, 0f, 0f));
            pose[WorkerPart.UpperArmLeft] = new Vector3(-44f, 0f, 0f);
            pose[WorkerPart.UpperArmRight] = new Vector3(-44f, 0f, 0f);
            pose[WorkerPart.ForearmLeft] = new Vector3(-42f + 6f * rub, 6f * rub, 0f);
            pose[WorkerPart.ForearmRight] = new Vector3(-42f - 6f * rub, -6f * rub, 0f);
        }

        private static void Sit(ref WorkerPose pose, float time, in WorkerMotion motion)
        {
            Breathe(ref pose, time, motion);
            // Сидит на земле, колени подняты и чуть врозь, корпус немного откинут назад — бёдра не входят в живот;
            // руки лежат на коленях.
            pose.Add(WorkerPart.ThighLeft, new Vector3(-90f, 0f, -10f));
            pose.Add(WorkerPart.ThighRight, new Vector3(-90f, 0f, 10f));
            pose.Add(WorkerPart.ShinLeft, new Vector3(62f, 0f, 0f));
            pose.Add(WorkerPart.ShinRight, new Vector3(62f, 0f, 0f));
            pose.Add(WorkerPart.Torso, new Vector3(-14f, 0f, 0f));
            pose.Add(WorkerPart.Head, new Vector3(20f, 0f, 0f));
            pose[WorkerPart.UpperArmLeft] = new Vector3(-42f, 0f, -8f);
            pose[WorkerPart.UpperArmRight] = new Vector3(-42f, 0f, 8f);
            pose[WorkerPart.ForearmLeft] = new Vector3(-38f, 0f, 0f);
            pose[WorkerPart.ForearmRight] = new Vector3(-38f, 0f, 0f);
        }

        // ── Рабочие анимации ролей ──────────────────────────────────────────────────────

        /// <summary>Ключевая поза: в доле цикла at — повороты частей (Torso, Head, плечо и предплечье правой и левой руки, бёдра, голени).</summary>
        private readonly struct Key
        {
            public readonly float At;
            public readonly Vector3 Torso, Head, UpperRight, ForeRight, UpperLeft, ForeLeft, Thighs, Shins;

            public Key(float at, Vector3 torso, Vector3 head, Vector3 upperRight, Vector3 foreRight, Vector3 upperLeft, Vector3 foreLeft,
                Vector3 thighs = default, Vector3 shins = default)
            {
                At = at;
                Torso = torso;
                Head = head;
                UpperRight = upperRight;
                ForeRight = foreRight;
                UpperLeft = upperLeft;
                ForeLeft = foreLeft;
                Thighs = thighs;
                Shins = shins;
            }
        }

        // Лесоруб: стойка → замах (корпус повёрнут вправо, топор за правым плечом) → быстрый косой удар перед собой
        // на уровне пояса → отвод обратно.
        private static readonly Key[] ChopKeys =
        {
            new(0f, new Vector3(6f, 0f, 0f), Vector3.zero, new Vector3(-35f, 0f, 12f), new Vector3(-55f, 0f, 0f), new Vector3(-18f, 0f, -6f),
                new Vector3(-30f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(14f, 0f, 0f)),
            new(0.45f, new Vector3(-4f, 18f, 0f), new Vector3(0f, -14f, 0f), new Vector3(-104f, 0f, 24f), new Vector3(-100f, 0f, 0f),
                new Vector3(-30f, 0f, -8f), new Vector3(-40f, 0f, 0f), new Vector3(-6f, 0f, 0f), new Vector3(10f, 0f, 0f)),
            new(0.6f, new Vector3(20f, -16f, 0f), new Vector3(-10f, 12f, 0f), new Vector3(-58f, 0f, -6f), new Vector3(-14f, 0f, 0f),
                new Vector3(-12f, 0f, -10f), new Vector3(-25f, 0f, 0f), new Vector3(-14f, 0f, 0f), new Vector3(22f, 0f, 0f)),
            new(0.72f, new Vector3(20f, -14f, 0f), new Vector3(-10f, 10f, 0f), new Vector3(-56f, 0f, -4f), new Vector3(-16f, 0f, 0f),
                new Vector3(-12f, 0f, -10f), new Vector3(-25f, 0f, 0f), new Vector3(-14f, 0f, 0f), new Vector3(22f, 0f, 0f)),
            new(1f, new Vector3(6f, 0f, 0f), Vector3.zero, new Vector3(-35f, 0f, 12f), new Vector3(-55f, 0f, 0f), new Vector3(-18f, 0f, -6f),
                new Vector3(-30f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(14f, 0f, 0f)),
        };

        // Копатель: стойка → кирка над головой (корпус чуть назад) → удар вниз перед собой с наклоном и согнутыми коленями.
        private static readonly Key[] DigKeys =
        {
            new(0f, new Vector3(10f, 0f, 0f), Vector3.zero, new Vector3(-40f, 0f, 10f), new Vector3(-60f, 0f, 0f), new Vector3(-20f, 0f, -6f),
                new Vector3(-35f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(14f, 0f, 0f)),
            new(0.45f, new Vector3(-6f, 6f, 0f), new Vector3(-10f, 0f, 0f), new Vector3(-150f, 0f, 14f), new Vector3(-75f, 0f, 0f),
                new Vector3(-30f, 0f, -8f), new Vector3(-45f, 0f, 0f), new Vector3(-4f, 0f, 0f), new Vector3(8f, 0f, 0f)),
            new(0.6f, new Vector3(26f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(-60f, 0f, 3f), new Vector3(-10f, 0f, 0f),
                new Vector3(-14f, 0f, -10f), new Vector3(-30f, 0f, 0f), new Vector3(-18f, 0f, 0f), new Vector3(30f, 0f, 0f)),
            new(0.74f, new Vector3(25f, 0f, 0f), new Vector3(-11f, 0f, 0f), new Vector3(-58f, 0f, 3f), new Vector3(-12f, 0f, 0f),
                new Vector3(-14f, 0f, -10f), new Vector3(-30f, 0f, 0f), new Vector3(-18f, 0f, 0f), new Vector3(30f, 0f, 0f)),
            new(1f, new Vector3(10f, 0f, 0f), Vector3.zero, new Vector3(-40f, 0f, 10f), new Vector3(-60f, 0f, 0f), new Vector3(-20f, 0f, -6f),
                new Vector3(-35f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(14f, 0f, 0f)),
        };

        // Строитель: наклонился, левая рука придерживает перед собой, правая — один удар молотком (замах и удар);
        // за цикл таких ударов три.
        private static readonly Key[] HammerKeys =
        {
            new(0f, new Vector3(18f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(-48f, 0f, 6f), new Vector3(-45f, 0f, 0f),
                new Vector3(-50f, 0f, -12f), new Vector3(-40f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(20f, 0f, 0f)),
            new(0.55f, new Vector3(16f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(-72f, 0f, 14f), new Vector3(-112f, 0f, 0f),
                new Vector3(-50f, 0f, -12f), new Vector3(-40f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(20f, 0f, 0f)),
            new(0.75f, new Vector3(19f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(-46f, 0f, 4f), new Vector3(-40f, 0f, 0f),
                new Vector3(-50f, 0f, -12f), new Vector3(-40f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(20f, 0f, 0f)),
            new(1f, new Vector3(18f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(-48f, 0f, 6f), new Vector3(-45f, 0f, 0f),
                new Vector3(-50f, 0f, -12f), new Vector3(-40f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(20f, 0f, 0f)),
        };

        /// <summary>Рубить, копать, строить: поза по ключам цикла (phase от 0 до 1); у строителя в цикле три удара.</summary>
        private static void Work(ref WorkerPose pose, WorkerClip clip, float phase)
        {
            var keys = clip switch
            {
                WorkerClip.Chop => ChopKeys,
                WorkerClip.Dig => DigKeys,
                _ => HammerKeys,
            };
            if (clip == WorkerClip.Hammer)
                phase = Mathf.Repeat(phase * 3f, 1f);
            ApplyKeys(ref pose, keys, phase);
            // Стойка чуть шире: левая нога впереди.
            pose.Add(WorkerPart.ThighLeft, new Vector3(-6f, 0f, -3f));
            pose.Add(WorkerPart.ThighRight, new Vector3(4f, 0f, 3f));
        }

        /// <summary>Добавить к позе ключевую позу из keys в доле phase; между ключами — плавно.</summary>
        private static void ApplyKeys(ref WorkerPose pose, Key[] keys, float phase)
        {
            var i = 1;
            while (i < keys.Length - 1 && phase > keys[i].At)
                i++;
            var a = keys[i - 1];
            var b = keys[i];
            var t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.At, b.At, phase));
            pose.Add(WorkerPart.Torso, Vector3.Lerp(a.Torso, b.Torso, t));
            pose.Add(WorkerPart.Head, Vector3.Lerp(a.Head, b.Head, t));
            // Руки задаются целиком: в стойке они чуть отведены (Relaxed), здесь — своя поза.
            pose[WorkerPart.UpperArmRight] = Vector3.Lerp(a.UpperRight, b.UpperRight, t);
            pose[WorkerPart.ForearmRight] = Vector3.Lerp(a.ForeRight, b.ForeRight, t);
            pose[WorkerPart.UpperArmLeft] = Vector3.Lerp(a.UpperLeft, b.UpperLeft, t);
            pose[WorkerPart.ForearmLeft] = Vector3.Lerp(a.ForeLeft, b.ForeLeft, t);
            var thighs = Vector3.Lerp(a.Thighs, b.Thighs, t);
            var shins = Vector3.Lerp(a.Shins, b.Shins, t);
            pose.Add(WorkerPart.ThighLeft, thighs);
            pose.Add(WorkerPart.ThighRight, thighs);
            pose.Add(WorkerPart.ShinLeft, shins);
            pose.Add(WorkerPart.ShinRight, shins);
        }

        /// <summary>Откуда истопник берёт полено: торец вязанки на спине слева, у бедра; система рабочего, доли роста.</summary>
        private static readonly Vector3 BilletAtBundle = new(-0.25f, 0.53f, -0.1f);

        /// <summary>Куда кладёт: в огонь перед собой на земле.</summary>
        private static readonly Vector3 BilletInFire = new(0.02f, 0.03f, 0.44f);

        /// <summary>
        /// Истопник у огня (phase от 0 до 1): тянется левой рукой к вязанке за поленом, приседает и кладёт полено в огонь
        /// перед собой, потом дважды шевелит угли кочергой в правой руке и выпрямляется. Полено — ноша
        /// <see cref="WorkerCarry.Billet"/>: в руке, потом лежит в огне.
        /// </summary>
        private static void Stoke(float phase, in WorkerMotion motion, ref WorkerPose pose)
        {
            var height = motion.Height;
            // Присед: от полена в руке до конца кочерги, потом выпрямиться.
            var crouch = Bump(Mathf.InverseLerp(0.14f, 0.95f, phase), 0.25f, 0.12f) * 0.42f;
            Crouch(ref pose, crouch);
            // Сначала повернулся к вязанке влево, потом к огню.
            var turn = Bump(Mathf.InverseLerp(0f, 0.25f, phase), 0.4f, 0.4f);
            pose.Add(WorkerPart.Torso, new Vector3(0f, -22f * turn, 0f));
            pose.Add(WorkerPart.Head, new Vector3(0f, -18f * turn, 0f));

            // Левая рука: к вязанке, с поленом к огню, отпустить.
            var carry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.38f, phase));
            var reach = Bump(Mathf.InverseLerp(0.02f, 0.5f, phase), 0.2f, 0.18f);
            var billet = Vector3.Lerp(BilletAtBundle, BilletInFire, carry) * height;
            // По дуге: выше и в сторону от бедра, чтобы не задеть ногу и подол.
            billet.y = Mathf.Lerp(BilletAtBundle.y, BilletInFire.y, carry) * height + Mathf.Sin(Mathf.PI * carry) * 0.16f * height;
            billet.x -= Mathf.Sin(Mathf.PI * carry) * 0.08f * height;
            // Полено в руке с той доли, когда рука дотянулась до вязанки, до той, когда положила его в огонь.
            var holding = phase > 0.12f && phase < 0.4f;
            var hand = billet + Vector3.up * (0.035f * height);
            WorkerSkeleton.ReachArm(ref pose, -1f, hand, hand + new Vector3(-0.25f, -0.2f, -0.05f) * height, height, reach);

            // Правая рука с кочергой: после того как полено легло — два тычка в угли.
            var poke = Bump(Mathf.InverseLerp(0.42f, 0.92f, phase), 0.15f, 0.15f);
            var jab = Mathf.Max(0f, Mathf.Sin(Mathf.PI * 4f * Mathf.InverseLerp(0.5f, 0.86f, phase)));
            pose[WorkerPart.UpperArmRight] = Vector3.Lerp(pose[WorkerPart.UpperArmRight], new Vector3(-72f - 14f * jab, 0f, 6f), poke);
            pose[WorkerPart.ForearmRight] = Vector3.Lerp(pose[WorkerPart.ForearmRight], new Vector3(-8f + 8f * jab, 0f, 0f), poke);

            // Полено: в руке — вдоль ладони поперёк хода, в огне — лежит.
            var lying = Quaternion.LookRotation(Vector3.right);
            pose.LoadPosition = holding ? billet : new Vector3(BilletInFire.x, WorkerCarryBuilder.BilletRadius, BilletInFire.z) * height;
            pose.LoadRotation = lying;
            pose.LoadOnBack = 1f;
        }

        /// <summary>Плавно вверх за rise доли, держит, плавно вниз за fall доли (t — доля от 0 до 1).</summary>
        private static float Bump(float t, float rise, float fall)
        {
            t = Mathf.Clamp01(t);
            if (t < rise)
                return Mathf.SmoothStep(0f, 1f, t / rise);
            if (t > 1f - fall)
                return Mathf.SmoothStep(0f, 1f, (1f - t) / fall);
            return 1f;
        }
    }

    /// <summary>Что о движении рабочего нужно анимации.</summary>
    public readonly struct WorkerMotion
    {
        public WorkerMotion(float seed, float speed, float height, bool rightBusy, bool leftBusy, WorkerCarry carry = WorkerCarry.None)
        {
            Carry = carry;
            Seed = seed;
            Speed = speed;
            Height = height;
            RightBusy = rightBusy;
            LeftBusy = leftBusy;
        }

        /// <summary>Сдвиг фазы от 0 до 1: у каждого рабочего свой, чтобы толпа не двигалась в такт.</summary>
        public float Seed { get; }

        /// <summary>Скорость, блоков в секунду; 0 — стоит.</summary>
        public float Speed { get; }

        public float Height { get; }
        public bool RightBusy { get; }
        public bool LeftBusy { get; }

        /// <summary>Что несёт: от этого зависит, где ноша и как её держит рука.</summary>
        public WorkerCarry Carry { get; }

        public static WorkerMotion For(WorkerLook look, float height, float speed, float seed, WorkerCarry carry = WorkerCarry.None) =>
            new(seed, speed, height, WorkerGearBuilder.GripAxis(look.Role, 1f) != null, WorkerGearBuilder.GripAxis(look.Role, -1f) != null, carry);
    }

    /// <summary>
    /// Проигрыватель анимаций одного рабочего: держит текущую анимацию и время, при смене плавно переводит позу
    /// за BlendSeconds. Разовые анимации идут за заданную длительность и замирают в конце.
    /// </summary>
    public sealed class WorkerAnimator
    {
        private const float BlendSeconds = 0.25f;
        private const float SitBlendSeconds = 0.8f;

        // Позы в своих буферах: каждый кадр они перезаписываются, память не выделяется.
        private WorkerPose from = WorkerPose.Rest();
        private WorkerPose target = WorkerPose.Rest();
        private WorkerPose pose = WorkerPose.Rest();
        private float blend = 1f;
        private float blendSeconds = BlendSeconds;
        private float duration;

        public WorkerClip Clip { get; private set; } = WorkerClip.Idle;

        /// <summary>Секунды с начала анимации.</summary>
        public float Time { get; set; }

        /// <summary>Текущая поза; её массив перезаписывается следующим <see cref="Update"/>.</summary>
        public WorkerPose Pose => pose;

        /// <summary>Сменить анимацию; та же анимация продолжается. duration — длительность разовой (0 — по умолчанию).</summary>
        public void Play(WorkerClip clip, float duration = 0f)
        {
            this.duration = duration > 0f ? duration : WorkerAnimation.DefaultDuration(clip);
            if (clip == Clip)
                return;
            from.CopyFrom(pose);
            blend = 0f;
            blendSeconds = clip == WorkerClip.Sit || Clip == WorkerClip.Sit ? SitBlendSeconds : BlendSeconds;
            Clip = clip;
            Time = 0f;
        }

        /// <summary>
        /// Начать ту же разовую анимацию заново (следующий удар, следующий блок). Время, ушедшее за конец, переносится
        /// в новый раз, чтобы удары не сбивались по фазе.
        /// </summary>
        public void Restart() => Time = duration > 0f ? Mathf.Repeat(Time, duration) : 0f;

        /// <summary>Доля разовой анимации от 0 до 1; у цикла — 0.</summary>
        public float Progress => WorkerAnimation.IsOneShot(Clip) && duration > 0f ? Mathf.Clamp01(Time / duration) : 0f;

        public WorkerPose Update(float deltaTime, in WorkerMotion motion)
        {
            Time += deltaTime;
            blend = Mathf.Min(1f, blend + deltaTime / blendSeconds);
            var time = WorkerAnimation.IsOneShot(Clip) ? Progress : Time;
            WorkerAnimation.Sample(Clip, time, motion, ref target);
            if (blend >= 1f)
                pose.CopyFrom(target);
            else
                WorkerAnimation.Blend(from, target, Mathf.SmoothStep(0f, 1f, blend), motion.Height, ref pose);
            return pose;
        }
    }
}
