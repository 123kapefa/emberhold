using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Громила: тяжёлый шаг под скорость (ступни не скользят), корпус переваливается, руки качаются навстречу ногам.
    /// Атака act — фаза одного удара двумя кулаками сверху: замах до 0.45, пауза, удар 0.58–0.66, возврат.
    /// </summary>
    public sealed class BruteRig : CreatureRig
    {
        /// <summary>Фаза удара, в которой кулаки бьют в землю.</summary>
        public const float ImpactPhase = 0.66f;

        private readonly Transform hips;
        private readonly Transform torso;
        private readonly Transform head;
        private readonly Transform[] shoulder;
        private readonly Transform[] elbow;
        private readonly Transform[] hip;
        private readonly Transform[] knee;

        public BruteRig(int level, CreatureMaterials templates) : base(CreatureKind.Brute, level, templates)
        {
            hips = Pivot(Root, new Vector3(0f, 0.86f, 0f));
            torso = Pivot(hips, new Vector3(0f, 0.92f, 0f));
            head = Pivot(torso, new Vector3(0f, 1.4f, 0.45f));
            shoulder = new[] { Pivot(torso, new Vector3(-0.88f, 1.62f, 0.12f)), Pivot(torso, new Vector3(0.88f, 1.62f, 0.12f)) };
            elbow = new[] { Pivot(shoulder[0], new Vector3(-1.08f, 0.95f, 0.45f)), Pivot(shoulder[1], new Vector3(1.08f, 0.95f, 0.45f)) };
            hip = new[] { Pivot(hips, new Vector3(-0.36f, 0.85f, -0.2f)), Pivot(hips, new Vector3(0.36f, 0.85f, -0.2f)) };
            knee = new[] { Pivot(hip[0], new Vector3(-0.42f, 0.48f, -0.05f)), Pivot(hip[1], new Vector3(0.42f, 0.48f, -0.05f)) };
            BuildModel(e =>
            {
                var p = e.Center;
                var ax = Mathf.Abs(p.x);
                var i = p.x < 0f ? 0 : 1;
                if (ax > 0.62f && p.y < 1.55f)
                    return p.y > 0.97f ? shoulder[i] : elbow[i];                    // руки: плечо и предплечье с кулаком
                if (ax > 0.27f && p.y < 0.8f)
                    return p.y > 0.5f ? hip[i] : knee[i];                           // ноги: бедро и голень
                if (p.z > 0.515f && p.y > 1.15f && ax < 0.45f)
                    return head;
                return p.y < 0.9f ? hips : torso;
            });
            CenterHeight = 1.3f;
        }

        /// <summary>Кулаки в мире.</summary>
        public Vector3[] Fists => new[] { World(elbow[0], new Vector3(-1.02f, 0.2f, 0.66f)), World(elbow[1], new Vector3(1.02f, 0.2f, 0.66f)) };

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 4f);
            const float swing = 0.3f, legLength = 0.78f;
            var w = Mathf.Min(1f, Speed / 0.25f);
            if (Speed > 0.01f)
                Gait += dt * Speed / (4f * legLength * Mathf.Sin(swing));                // частота шага под скорость: ступни не скользят
            var q = Gait * Mathf.PI * 2f;
            var br = Mathf.Sin(t * 1.5f);
            float k = pose.Act, r = pose.Recoil, f = pose.Fold;
            var raise = k > 0f ? Segment(k, 0f, 0.45f) * (1f - Segment(k, 0.58f, ImpactPhase)) : 0f;
            var slam = k > 0f ? Segment(k, 0.58f, ImpactPhase) * (1f - Segment(k, 0.72f, 1f)) : 0f;
            var look = (1f - w) * (1f - Mathf.Min(1f, k * 4f)) * Mathf.Sin(t * 0.45f) * 0.4f * Mathf.Max(0f, Mathf.Sin(t * 0.23f + 1f));
            var sq = Mathf.Sin(q);
            hips.localPosition = new Vector3(0f, 0.86f - 0.05f * w * sq * sq - 0.1f * slam - 0.42f * f, -0.08f * r);
            hips.localRotation = Rot(0.25f * f, 0f, 0f);
            torso.localRotation = Rot(0.1f * w + 0.02f * br - 0.22f * raise + 0.45f * slam - 0.2f * r + 0.55f * f, 0.12f * w * sq + 0.2f * r, 0.07f * w * sq);
            torso.localScale = new Vector3(1f + 0.012f * br, 1f + 0.02f * br, 1f + 0.012f * br);
            head.localRotation = Rot(-0.35f * raise + 0.2f * slam - 0.45f * r + 0.35f * f + (Mathf.Sin(t * 2.6f) > 0.97f ? -0.15f : 0f) * (1f - w),
                look - 0.08f * w * sq, 0f);
            for (var i = 0; i < 2; i++)
            {
                var s = i == 1 ? 1f : -1f;
                var qi = q + i * Mathf.PI;
                hip[i].localRotation = Rot(-swing * w * Mathf.Sin(qi) - 1.1f * f, 0f, 0f);
                knee[i].localRotation = Rot(0.75f * w * Mathf.Max(0f, Mathf.Cos(qi)) + 1.9f * f, 0f, 0f);
                shoulder[i].localRotation = Rot(0.25f * w * Mathf.Sin(qi) + 0.04f * Mathf.Sin(t * 1.5f + i) - 2.7f * raise - 1.3f * slam - 0.7f * f, 0f,
                    s * (0.05f + 0.03f * br + 0.15f * raise));
                elbow[i].localRotation = Rot(-0.15f * w - 0.5f * raise - 0.2f * f, 0f, 0f);
            }
        }
    }
}
