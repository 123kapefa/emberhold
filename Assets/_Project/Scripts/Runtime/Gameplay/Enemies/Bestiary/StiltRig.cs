using System;
using UnityEngine;
using static Emberhold.Gameplay.Enemies.Bestiary.CreaturePalette;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Ходуля: маленькое тело на четырёх длинных ногах. Ступни стоят на земле (мир) и переставляются диагональными
    /// парами, колено — двухзвенная IK от таза. Через препятствие (<see cref="Obstacle"/>) нога переносится выше него,
    /// тело над стеной приподнимается (over). Атака act — раздвигает ноги и садится прямо на костёр, жар светит в брюхе.
    /// </summary>
    public sealed class StiltRig : CreatureRig
    {
        private const float Thigh = 0.85f;
        private const float Shin = 3.15f;
        private const float Reach = 1.5f;

        private readonly Transform body;
        private readonly Transform head;
        private readonly Transform dangle;
        private readonly Light belly;
        private readonly Leg[] legs = new Leg[4];

        private sealed class Leg
        {
            public int Index;
            public float Angle;
            public Vector3 Out;
            public int Group;
            public Vector3 Hip;
            public Vector3 Rest;
            public Vector3? Plant;
            public Vector3 From;
            public Vector3 To;
            public bool Swing;
            public float K;
            public float Duration;
            public float Lift;
            public Vector3 Foot;
            public Transform Femur;
            public Transform Tibia;
            public Transform Knee;
            public Transform[] Middle;
            public Transform Spike;
            public Transform[] Claws;
        }

        public StiltRig(int level, CreatureMaterials templates) : base(CreatureKind.Stilt, level, templates)
        {
            body = Pivot(Root, new Vector3(0f, 2.85f, 0f));
            head = Pivot(body, new Vector3(0f, 2.78f, 0.2f));
            dangle = Pivot(body, new Vector3(0f, 2.75f, 0f));
            BuildModel(e =>
            {
                var p = e.Center;
                var rad = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (rad > 0.42f)
                    return null;                                                      // ноги модели: собираются заново ниже
                if (e.LevelAdd && p.y < 2.6f)
                {
                    e.Offset = new Vector3(0f, 2.83f, 0f) - p;                        // ледяное ядро древнего — внутрь тела
                    return body;
                }
                if (p.y < 2.72f && rad < 0.3f && p.y > 1.4f)
                    return dangle;
                return p.z > 0.15f && p.y < 2.9f && p.y > 2.4f ? head : body;
            });

            var femurMesh = new[] { SegmentMesh(0.05f, 0.035f, 6, FogBody), SegmentMesh(0.05f, 0.035f, 6, 0x262d38) };
            var tibiaMesh = new[] { SegmentMesh(0.035f, 0.012f, 6, FogBody), SegmentMesh(0.035f, 0.012f, 6, 0x262d38) };
            var kneeMesh = BallMesh(0.06f, 0x6c7480);
            var middleMesh = BallMesh(0.032f, 0x6c7480);
            var spikeMesh = ConeMesh(0.025f, 0.16f, 4, Bone);
            var clawMesh = ConeMesh(0.012f, 0.08f, 4, Claw);
            for (var i = 0; i < 4; i++)
            {
                var angle = i / 4f * Mathf.PI * 2f + Mathf.PI / 4f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                legs[i] = new Leg
                {
                    Index = i, Angle = angle, Out = new Vector3(ca, 0f, sa), Group = i % 2, Hip = new Vector3(ca * 0.2f, 0f, sa * 0.2f),
                    Rest = new Vector3(ca * Reach, 0.02f, sa * Reach),
                    Femur = Piece(femurMesh[i % 2]), Tibia = Piece(tibiaMesh[(i + 1) % 2]), Knee = Piece(kneeMesh),
                    Middle = new[] { Piece(middleMesh), Piece(middleMesh) }, Spike = Piece(spikeMesh),
                    Claws = new[] { Piece(clawMesh), Piece(clawMesh), Piece(clawMesh) },
                };
            }
            belly = AddLight(body, new Vector3(0f, 2.6f, 0f), Hex(0xff8a3c), 0f, 3f * Scale);
            CenterHeight = 2.85f;
        }

        /// <summary>Высота препятствия в точке мира (стена, которую надо перешагнуть); задаёт сцена.</summary>
        public Func<Vector3, float> Obstacle { get; set; } = _ => 0f;

        public override Vector3 Center => body.position;

        /// <summary>Забыть, где стоят ступни: после переноса существа они встанут заново в точки покоя.</summary>
        public void ResetFeet()
        {
            foreach (var leg in legs)
            {
                leg.Plant = null;
                leg.Swing = false;
            }
        }

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 4f);
            var v = Speed;
            var w = Mathf.Min(1f, v / 0.3f);
            var moving = v > 0.02f;
            const float cycle = 0.9f, stance = 0.6f;
            var forward = Root.rotation * Vector3.forward;
            if (legs[0].Plant == null)
            {
                foreach (var leg in legs)
                {
                    leg.Plant = RestWorld(leg, 0f);
                    leg.Foot = leg.Plant.Value;
                }
            }
            if (moving)
                Gait = (Gait + dt / cycle) % 1f;
            var stepping = Array.Exists(legs, l => l.Swing);
            foreach (var leg in legs)
            {
                var phase = (Gait + leg.Group * 0.5f) % 1f;
                var plant = leg.Plant.Value;
                if (moving && phase < 1f - stance && !leg.Swing && phase < 0.15f)
                {
                    // Отрыв: выбрать, куда встать.
                    StartStep(leg, plant, ClearOf(RestWorld(leg, v * cycle * stance * 0.5f + v * cycle * (1f - stance)), forward), cycle * (1f - stance));
                }
                else if (!moving && !leg.Swing && !stepping && pose.Act == 0f && pose.Fold == 0f)
                {
                    // Стоя: переставить отставшую лапу.
                    var want = RestWorld(leg, 0f);
                    if (Vector3.Distance(want, plant) > 0.5f * Root.localScale.x)
                    {
                        StartStep(leg, plant, ClearOf(want, forward), 0.45f);
                        stepping = true;
                    }
                }
                if (!leg.Swing && (pose.Act > 0f || pose.Fold > 0f))
                    leg.Plant = Vector3.Lerp(plant, RestWorld(leg, 0f), Mathf.Min(1f, dt * 3f));
                if (leg.Swing)
                {
                    leg.K += dt / leg.Duration;
                    if (leg.K >= 1f)
                    {
                        leg.Swing = false;
                        leg.Plant = leg.To;
                        leg.Foot = leg.To;
                    }
                    else
                    {
                        leg.Foot = Vector3.Lerp(leg.From, leg.To, Ease(leg.K));
                        leg.Foot.y += Mathf.Sin(leg.K * Mathf.PI) * leg.Lift;
                    }
                }
                else
                {
                    leg.Foot = leg.Plant.Value;
                }
            }
            // Тело: над стеной приподнимается, на костре опускается, при смерти падает.
            var bob = moving ? Mathf.Sin(Gait * Mathf.PI * 4f) * 0.04f : Mathf.Sin(t * 1.6f) * 0.02f;
            body.localPosition = new Vector3(0f, 2.85f + bob + 0.35f * pose.Over - 2.15f * Ease(pose.Act) - 2.25f * Ease(pose.Fold) + 0.15f * pose.Recoil
                                                 + Mathf.Sin(t * 2.2f) * 0.03f * pose.Act, 0f);
            body.localRotation = Rot(0.06f * w - 0.1f * pose.Recoil + 0.2f * pose.Fold, 0f, Mathf.Sin(t * 40f) * 0.05f * pose.Recoil + 0.3f * pose.Fold);
            head.localRotation = Rot(0.3f * pose.Act - 0.3f * pose.Recoil,
                Mathf.Sin(t * 0.5f) * 0.4f * (1f - w) * (1f - pose.Act) * Mathf.Max(0f, Mathf.Sin(t * 0.27f + 2f)), 0f);
            dangle.localRotation = Rot(-0.25f * w + Mathf.Sin(t * 1.7f) * 0.08f, 0f, Mathf.Sin(t * 1.3f) * 0.08f);
            dangle.localScale = new Vector3(1f, Mathf.Max(0.01f, 1f - 0.75f * pose.Act - 0.7f * pose.Fold), 1f);
            belly.intensity = pose.Glow * 3f;
            // Ноги: двухзвенная IK от таза к ступне, колено вверх и наружу.
            var bodyMatrix = Matrix4x4.TRS(body.localPosition, body.localRotation, body.localScale);
            foreach (var leg in legs)
            {
                var hip = bodyMatrix.MultiplyPoint3x4(leg.Hip);
                var foot = Root.InverseTransformPoint(leg.Foot);
                if (pose.Recoil > 0f)
                    foot.y += Mathf.Max(0f, Mathf.Sin(t * 30f + leg.Index * 2f)) * 0.15f * pose.Recoil;
                var d0 = foot - hip;
                var d = Mathf.Clamp(d0.magnitude, Shin - Thigh + 0.01f, Thigh + Shin - 0.001f);
                var dir = d0.normalized;
                var a = (Thigh * Thigh - Shin * Shin + d * d) / (2f * d);
                var h = Mathf.Sqrt(Mathf.Max(0f, Thigh * Thigh - a * a));
                var raw = leg.Out * 0.5f + Vector3.up;
                var pole = (raw - dir * Vector3.Dot(dir, raw)).normalized;
                var knee = hip + dir * a + pole * h;
                var end = hip + dir * d;
                Place(leg.Femur, hip, knee);
                Place(leg.Tibia, knee, end);
                leg.Knee.localPosition = knee;
                leg.Middle[0].localPosition = Vector3.Lerp(knee, end, 0.3f);
                leg.Middle[1].localPosition = Vector3.Lerp(knee, end, 0.6f);
                leg.Spike.localPosition = knee + pole * 0.06f;
                leg.Spike.localRotation = Quaternion.FromToRotation(Vector3.up, (pole + leg.Out * 0.4f).normalized);
                for (var j = 0; j < 3; j++)
                {
                    var ta = leg.Angle + (j - 1) * 0.7f;
                    var cd = new Vector3(Mathf.Cos(ta), -0.6f, Mathf.Sin(ta)).normalized;
                    leg.Claws[j].localPosition = end + cd * 0.035f;
                    leg.Claws[j].localRotation = Quaternion.FromToRotation(Vector3.up, cd);
                }
            }
        }

        private void StartStep(Leg leg, Vector3 from, Vector3 to, float duration)
        {
            leg.Swing = true;
            leg.K = 0f;
            leg.Duration = duration;
            leg.From = from;
            leg.To = to;
            leg.Lift = LiftFor(from, to);
        }

        // Куда ставить ступню (мир): точка покоя, раздвинутая при посадке и смерти и вынесенная вперёд на ходу.
        private Vector3 RestWorld(Leg leg, float ahead)
        {
            var spread = 1f + 0.85f * Pose.Act + 0.7f * Pose.Fold;
            return Root.TransformPoint(Vector3.Scale(leg.Rest, new Vector3(spread, 1f, spread)) + new Vector3(0f, 0f, ahead));
        }

        // Высота переноса: над препятствием с запасом, иначе невысоко.
        private float LiftFor(Vector3 a, Vector3 b)
        {
            var height = 0f;
            for (var k = 0; k <= 8; k++)
                height = Mathf.Max(height, Obstacle(Vector3.Lerp(a, b, k / 8f)));
            return height > 0f ? height + 0.6f : 0.5f * Root.localScale.x;
        }

        // Ступня не встаёт на стену: сдвигается вперёд, пока не окажется за ней.
        private Vector3 ClearOf(Vector3 point, Vector3 direction)
        {
            for (var n = 0; n < 20 && Obstacle(point) > 0f; n++)
                point += direction * 0.25f;
            if (Obstacle(point - direction * 0.35f) > 0f)
                point += direction * 0.35f;
            return point;
        }
    }
}
