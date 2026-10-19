using UnityEngine;
using static Emberhold.Gameplay.Enemies.Bestiary.CreaturePalette;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Ползун: тело делится на грудь, голову, жвалы, брюшко и хвост, шесть лап собираются заново — бедро и голень,
    /// колено считается двухзвенной IK от тазобедренного сустава до ступни. Ступни ставятся «треногой», как у насекомых.
    /// Атака — присосался к огню (latch): передние лапы обнимают костёр, жвалы жуют, брюхо светится жаром (glow).
    /// </summary>
    public sealed class CrawlerRig : CreatureRig
    {
        private const float Femur = 0.3f;
        private const float Shin = 0.42f;
        private const float FootX = 0.52f;
        private static readonly float[] Hips = { 0.14f, 0f, -0.14f };
        private static readonly float[] Feet = { 0.4f, 0.03f, -0.34f };
        private static readonly int[,] Tripod = { { 0, 1, 0 }, { 1, 0, 1 } };
        private static readonly Vector3 Mouth = new(0f, 0.24f, 0.42f);

        private readonly Transform body;
        private readonly Transform thorax;
        private readonly Transform head;
        private readonly Transform[] jaw;
        private readonly Transform abdomen;
        private readonly Transform tail;
        private readonly Leg[] legs = new Leg[6];

        private sealed class Leg
        {
            public int Side;
            public int Index;
            public int Group;
            public Vector3 Hip;
            public Vector3 Rest;
            public Vector3 Foot;
            public Transform Femur;
            public Transform Tibia;
            public Transform Knee;
            public Transform Spike;
            public Transform[] Claws;
        }

        public CrawlerRig(int level, CreatureMaterials templates) : base(CreatureKind.Crawler, level, templates)
        {
            body = Pivot(Root, new Vector3(0f, 0.3f, 0f));
            thorax = Pivot(body, new Vector3(0f, 0.3f, 0f));
            head = Pivot(body, new Vector3(0f, 0.3f, 0.22f));
            jaw = new[] { Pivot(head, new Vector3(-0.05f, 0.27f, 0.33f)), Pivot(head, new Vector3(0.05f, 0.27f, 0.33f)) };
            abdomen = Pivot(body, new Vector3(0f, 0.3f, -0.04f));
            tail = Pivot(abdomen, new Vector3(0f, 0.3f, -0.46f));
            BuildModel(e =>
            {
                var p = e.Center;
                if (Mathf.Abs(p.x) > 0.13f && p.y < 0.5f && p.z > -0.22f && p.z < 0.27f && !e.Tiny)
                    return null; // лапы модели: собираются заново ниже
                if (p.z > 0.2f)
                    return Mathf.Abs(p.x) >= 0.03f && p.y < 0.29f && p.z > 0.33f ? jaw[p.x < 0f ? 0 : 1] : head;
                if (p.z < -0.45f)
                    return tail;
                return p.z < -0.02f ? abdomen : thorax;
            });

            // Новые лапы: бедро, колено, голень, шип колена, два когтя.
            var femurMesh = new[] { SegmentMesh(0.035f, 0.026f, 5, FogBody), SegmentMesh(0.035f, 0.026f, 5, FogDark) };
            var tibiaMesh = new[] { SegmentMesh(0.026f, 0.012f, 5, FogBody), SegmentMesh(0.026f, 0.012f, 5, FogDark) };
            var kneeMesh = BallMesh(0.034f, 0x3e4856);
            var spikeMesh = ConeMesh(0.012f, 0.06f, 4, 0x6c7682);
            var clawMesh = ConeMesh(0.01f, 0.045f, 4, Claw);
            var n = 0;
            foreach (var side in new[] { -1, 1 })
            {
                for (var i = 0; i < 3; i++)
                {
                    var rest = new Vector3(side * FootX, 0.02f, Feet[i]);
                    legs[n++] = new Leg
                    {
                        Side = side, Index = i, Group = Tripod[side < 0 ? 0 : 1, i], Hip = new Vector3(side * 0.12f, 0.3f, Hips[i]), Rest = rest, Foot = rest,
                        Femur = Piece(femurMesh[i % 2]), Tibia = Piece(tibiaMesh[(i + 1) % 2]), Knee = Piece(kneeMesh), Spike = Piece(spikeMesh),
                        Claws = new[] { Piece(clawMesh), Piece(clawMesh) },
                    };
                }
            }
            SolveLegs();
        }

        /// <summary>Пасть в мире: сюда летят искры жара.</summary>
        public Vector3 MouthWorld => World(head, Mouth);

        public override Vector3 Center => MouthWorld;

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            // Походка: цикл треноги, частота растёт со скоростью.
            var run = Mathf.Min(1f, speed / 1.2f);
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 8f);
            const float cycle = 0.3f, stance = 0.55f;
            var stride = Speed * cycle * stance;
            var moving = Speed > 0.02f;
            if (moving)
                Gait = (Gait + dt / cycle) % 1f;
            foreach (var leg in legs)
            {
                var phase = (Gait + leg.Group * 0.5f) % 1f;
                var target = leg.Rest;
                if (moving)
                {
                    if (phase < 1f - stance)
                    {
                        // Перенос лапы вперёд по дуге.
                        var k = phase / (1f - stance);
                        target.z += stride * (k - 0.5f);
                        target.y += Mathf.Sin(k * Mathf.PI) * (0.06f + 0.1f * run);
                    }
                    else
                    {
                        // Опора: лапа уходит назад.
                        var k = (phase - (1f - stance)) / stance;
                        target.z += stride * (0.5f - k);
                    }
                }
                else
                {
                    // Покой: изредка переставляет одну лапу.
                    var shuffle = Mathf.Sin(t * 0.9f + leg.Index * 2.1f + leg.Side);
                    if (shuffle > 0.97f)
                        target.y += (shuffle - 0.97f) * 2.5f;
                }
                // Присосался: передние лапы обнимают костёр, остальные шире.
                if (pose.Latch > 0f)
                {
                    if (leg.Index == 0)
                    {
                        target.z += 0.18f * pose.Latch;
                        target.x -= leg.Side * 0.12f * pose.Latch;
                        target.y += 0.05f * pose.Latch;
                    }
                    else
                    {
                        target.x += leg.Side * 0.05f * pose.Latch;
                    }
                }
                // Удар: лапы дёргаются.
                if (pose.Recoil > 0f)
                {
                    target.x += Mathf.Sin(t * 45f + leg.Index * 3f + leg.Side) * 0.05f * pose.Recoil;
                    target.y += Mathf.Max(0f, Mathf.Sin(t * 38f + leg.Index)) * 0.08f * pose.Recoil;
                }
                // Смерть: лапы поджимаются под тело.
                if (pose.Fold > 0f)
                    target = Vector3.Lerp(target, new Vector3(leg.Side * 0.2f, 0.28f, leg.Hip.z * 0.8f), pose.Fold * 0.85f);
                leg.Foot = target;
            }
            // Тело: покачивание на бегу, дыхание в покое, приседание при ударе и смерти.
            var breath = Mathf.Sin(t * 2.2f);
            var wave = Mathf.Sin(Gait * Mathf.PI * 2f);
            var bob = moving ? Mathf.Abs(wave) * 0.025f : breath * 0.006f;
            body.localPosition = new Vector3(0f, 0.3f + bob - 0.07f * pose.Latch - 0.22f * pose.Fold + 0.03f * pose.Recoil, -0.12f * pose.Recoil);
            body.localRotation = Rot(0.08f * pose.Latch - 0.35f * pose.Recoil + (moving ? 0.04f : 0f) - 0.1f * pose.Fold, 0f,
                (moving ? wave * 0.05f : 0f) + Mathf.Sin(t * 50f) * 0.06f * pose.Recoil + 0.5f * pose.Fold * Mathf.Sin(1.3f));
            thorax.localScale = new Vector3(1f + breath * 0.015f, 1f + breath * 0.02f, 1f);
            // Голова: оглядывается в покое, опущена у огня, жвалы жуют.
            var look = moving || pose.Latch > 0f ? 0f : Mathf.Sin(t * 0.7f) * 0.35f * Mathf.Max(0f, Mathf.Sin(t * 0.31f + 1f));
            head.localRotation = Rot(0.25f * pose.Latch + Mathf.Sin(t * 9f) * 0.05f * pose.Latch - 0.3f * pose.Recoil + 0.3f * pose.Fold, look * (1f - pose.Fold), 0f);
            var chew = pose.Latch > 0f
                ? (0.35f + Mathf.Sin(t * 14f) * 0.3f) * pose.Latch
                : (Mathf.Sin(t * 3.3f) > 0.9f ? 0.2f : 0f) + 0.4f * pose.Recoil;
            jaw[0].localRotation = Rot(0f, chew, 0f);
            jaw[1].localRotation = Rot(0f, -chew, 0f);
            // Брюшко: сзади раскачивается, у огня пульсирует жаром, хвост поджимается.
            var pulse = pose.Latch > 0f ? 1f + Mathf.Max(0f, Mathf.Sin(t * 6f)) * 0.1f * pose.Latch : 1f;
            abdomen.localScale = new Vector3(pulse, pulse, 1f + (pulse - 1f) * 0.5f);
            abdomen.localRotation = Rot(-0.1f * pose.Recoil + 0.2f * pose.Fold, moving ? wave * 0.12f : Mathf.Sin(t * 1.3f) * 0.05f, 0f);
            tail.localRotation = Rot(Mathf.Sin(t * 2.5f) * 0.12f + 0.5f * pose.Latch + 0.6f * pose.Recoil + 0.8f * pose.Fold, 0f, 0f);
            // Свечение брюха от вытянутого жара.
            SetLayerEmission(CreatureLayer.Heat, new Color(0.35f + pose.Glow * 0.65f, 0.52f + pose.Glow * 0.2f, 0.66f - pose.Glow * 0.4f), 0.4f + pose.Glow * 1.6f);
            SolveLegs();
        }

        private void SolveLegs()
        {
            // Тазы заданы в координатах модели: в систему груди и обратно в корень.
            var matrix = Matrix4x4.TRS(body.localPosition, body.localRotation, body.localScale)
                         * Matrix4x4.TRS(thorax.localPosition, thorax.localRotation, thorax.localScale)
                         * Matrix4x4.Translate(new Vector3(0f, -0.3f, 0f));
            foreach (var leg in legs)
                SolveLeg(leg, matrix.MultiplyPoint3x4(leg.Hip));
        }

        // Двухзвенная IK: колено поднято вверх и наружу.
        private static void SolveLeg(Leg leg, Vector3 hip)
        {
            var d0 = leg.Foot - hip;
            var d = Mathf.Min(d0.magnitude, Femur + Shin - 0.001f);
            var dir = d0.normalized;
            var a = (Femur * Femur - Shin * Shin + d * d) / (2f * d);
            var h = Mathf.Sqrt(Mathf.Max(0f, Femur * Femur - a * a));
            var raw = new Vector3(leg.Side * 0.9f, 0.8f, 0f);
            var pole = (raw - dir * Vector3.Dot(dir, raw)).normalized;
            var knee = hip + dir * a + pole * h;
            var foot = hip + dir * d;
            Place(leg.Femur, hip, knee);
            Place(leg.Tibia, knee, foot);
            leg.Knee.localPosition = knee;
            leg.Spike.localPosition = knee + pole * 0.03f;
            leg.Spike.localRotation = Quaternion.FromToRotation(Vector3.up, (pole + new Vector3(leg.Side * 0.3f, 0f, 0f)).normalized);
            for (var k = 0; k < 2; k++)
            {
                var cd = new Vector3(leg.Side * 0.4f, -0.6f, k == 1 ? 0.6f : -0.6f).normalized;
                leg.Claws[k].localPosition = foot + cd * 0.02f;
                leg.Claws[k].localRotation = Quaternion.FromToRotation(Vector3.up, cd);
            }
        }
    }
}
