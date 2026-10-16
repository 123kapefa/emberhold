using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Туманница: медленный шаг на четырёх ногах (диагональные пары вместе), мешок с туманом дышит. Атака act — фаза цикла:
    /// вдох (мешок раздувается) до 0.45, выдох 0.45–0.9 — хобот поднимается, туман идёт из хобота и труб (его пускает сцена).
    /// </summary>
    public sealed class MistmotherRig : CreatureRig
    {
        private readonly Transform body;
        private readonly Transform sac;
        private readonly Transform head;
        private readonly Transform trunk;
        private readonly Leg[] legs = new Leg[4];

        private readonly struct Leg
        {
            public Leg(int side, int hind, Transform hip, Transform knee)
            {
                Side = side;
                Hind = hind;
                Hip = hip;
                Knee = knee;
            }

            public int Side { get; }
            public int Hind { get; }
            public Transform Hip { get; }
            public Transform Knee { get; }
        }

        public MistmotherRig(int level, CreatureMaterials templates) : base(CreatureKind.Mistmother, level, templates)
        {
            body = Pivot(Root, new Vector3(0f, 0.95f, -0.15f));
            sac = Pivot(body, new Vector3(0f, 1.25f, -0.45f));
            head = Pivot(body, new Vector3(0f, 1.3f, 0.2f));
            trunk = Pivot(head, new Vector3(0f, 1.27f, 0.4f));
            // Ноги: передняя и задняя с каждой стороны, шарнир в тазу и в колене.
            var n = 0;
            foreach (var s in new[] { -1, 1 })
            {
                for (var k = 0; k < 2; k++)
                {
                    var hip = Pivot(Root, k == 1 ? new Vector3(s * 0.25f, 0.85f, -0.4f) : new Vector3(s * 0.3f, 0.9f, 0f));
                    var knee = Pivot(hip, k == 1 ? new Vector3(s * 0.46f, 0.58f, -0.55f) : new Vector3(s * 0.48f, 0.62f, 0.1f));
                    legs[n++] = new Leg(s, k, hip, knee);
                }
            }
            BuildModel(e =>
            {
                var p = e.Center;
                var ax = Mathf.Abs(p.x);
                if (ax > 0.27f && p.y < 0.95f)
                {
                    var leg = legs[(p.x < 0f ? 0 : 2) + (p.z > -0.2f ? 0 : 1)];
                    return p.y < 0.6f ? leg.Knee : leg.Hip;
                }
                if (p.y > 1.15f && p.z < -0.1f)
                    return sac;
                if (p.z > 0.38f && p.y < 1.3f && ax < 0.22f)
                    return trunk;
                return p.z > 0.15f && p.y > 1.15f ? head : body;
            });
            CenterHeight = 1.3f;
        }

        /// <summary>Конец хобота в мире: отсюда идёт основной выдох.</summary>
        public Vector3 TrunkTip => World(trunk, new Vector3(0f, 0.78f, 0.52f));

        /// <summary>Раструбы труб на мешке в мире.</summary>
        public Vector3[] PipeTips => new[] { World(sac, new Vector3(-0.5f, 2.28f, -0.62f)), World(sac, new Vector3(0.5f, 2.28f, -0.62f)) };

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 3f);
            const float swing = 0.25f, legLength = 0.85f;
            var w = Mathf.Min(1f, Speed / 0.2f);
            if (Speed > 0.01f)
                Gait += dt * Speed / (4f * legLength * Mathf.Sin(swing));
            var q = Gait * Mathf.PI * 2f;
            float k = pose.Act, r = pose.Recoil, f = pose.Fold;
            var inhale = k > 0f ? Segment(k, 0f, 0.4f) * (1f - Segment(k, 0.48f, 0.9f)) : 0f;
            var exhale = k > 0f ? Segment(k, 0.42f, 0.5f) * (1f - Segment(k, 0.85f, 1f)) : 0f;
            foreach (var leg in legs)
            {
                // Диагональные пары шагают вместе.
                var qi = q + (leg.Side < 0 ? (leg.Hind == 1 ? Mathf.PI : 0f) : (leg.Hind == 1 ? 0f : Mathf.PI));
                var sign = leg.Hind == 1 ? -1f : 1f;
                leg.Hip.localRotation = Rot(-swing * w * Mathf.Sin(qi), 0f, leg.Side * 0.45f * f);
                leg.Knee.localRotation = Rot(0.55f * w * Mathf.Max(0f, Mathf.Cos(qi)) * sign + 0.4f * f * sign, 0f, 0f);
            }
            var br = Mathf.Sin(t * 1.1f);
            body.localPosition = new Vector3(0f, 0.95f + 0.025f * w * Mathf.Abs(Mathf.Sin(q * 2f)) + 0.01f * br - 0.45f * f, -0.15f);
            body.localRotation = Rot(-0.06f * inhale + 0.08f * exhale + 0.25f * f, 0f,
                0.05f * w * Mathf.Sin(q) + Mathf.Sin(t * 0.5f) * 0.02f + Mathf.Sin(t * 35f) * 0.03f * r);
            var sc = (1f + 0.04f * br + 0.28f * inhale - 0.06f * exhale + Mathf.Sin(t * 25f) * 0.05f * r) * (1f - 0.35f * f);
            sac.localScale = new Vector3(sc, sc * 1.03f, sc);
            head.localRotation = Rot(0.1f + Mathf.Sin(t * 0.5f) * 0.06f - 0.3f * inhale + 0.1f * exhale - 0.3f * r + 0.5f * f,
                Mathf.Sin(t * 0.31f) * 0.2f * (1f - w) * (1f - Mathf.Min(1f, k * 3f)), 0f);
            trunk.localRotation = Rot(Mathf.Sin(t * 0.9f) * 0.12f * (1f - w) + 0.1f * w * Mathf.Sin(q * 2f) - 0.25f * inhale - 0.75f * exhale + 0.4f * f, 0f,
                Mathf.Sin(t * 0.7f) * 0.1f * (1f - exhale));
        }
    }
}
