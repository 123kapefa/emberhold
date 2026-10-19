using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Мотыльки: каждый летит сам, стайка держится облаком в покое и вытягивается потоком в полёте. Атака act — облепляют
    /// фонарь (<see cref="Cling"/>, мир) со всех сторон и медленно раскрывают крылья. Удар разбрасывает их, при смерти
    /// складывают крылья и падают, кружась.
    /// </summary>
    public sealed class MothsRig : CreatureRig
    {
        private readonly List<Moth> moths = new();

        private sealed class Moth
        {
            public Transform Body;
            public readonly List<(Transform pivot, int side, float amplitude)> Wings = new();
            public float Phase;
            public float Pace;
            public Vector3 Position;
            public Vector3 Previous;
            public float Yaw;
            public float Roll;
            public float Pitch;
            public float Fall;
            public Vector3 Spot;
        }

        public MothsRig(int level, CreatureMaterials templates) : base(CreatureKind.Moths, level, templates)
        {
            var random = new System.Random(77);
            var wingPivots = new Transform[Model.Wings.Count];
            for (var g = 0; g < Model.Groups.Count; g++)
            {
                var matrix = Model.Groups[g];
                var body = new GameObject("Moth").transform;
                body.SetParent(Root, false);
                body.localPosition = matrix.GetColumn(3);
                body.localRotation = matrix.rotation;
                body.localScale = Vector3.one * matrix.GetColumn(0).magnitude;
                Register(body, Vector3.zero);
                var forward = matrix.rotation * Vector3.forward;
                var moth = new Moth
                {
                    Body = body, Phase = (float)random.NextDouble() * Mathf.PI * 2f, Pace = 0.8f + (float)random.NextDouble() * 0.4f,
                    Position = body.localPosition, Previous = body.localPosition, Yaw = Mathf.Atan2(forward.x, forward.z),
                };
                for (var w = 0; w < Model.Wings.Count; w++)
                {
                    var wing = Model.Wings[w];
                    if (wing.Group != g)
                        continue;
                    wingPivots[w] = Pivot(body, wing.Root);
                    moth.Wings.Add((wingPivots[w], wing.Side, wing.Amplitude));
                }
                moths.Add(moth);
            }
            // Места на облепленном фонаре: равномерно вокруг.
            for (var i = 0; i < moths.Count; i++)
            {
                var a = (float)i / moths.Count * Mathf.PI * 2f;
                moths[i].Spot = new Vector3(Mathf.Cos(a), i % 2 == 1 ? 0.35f : -0.25f, Mathf.Sin(a)).normalized;
            }
            BuildModel(e => e.Group < 0 ? null : e.Wing >= 0 ? wingPivots[e.Wing] : moths[e.Group].Body);
            CenterHeight = 1.6f;
        }

        /// <summary>Фонарь, на который садится стайка (мир); null — лететь облаком.</summary>
        public Vector3? Cling { get; set; }

        public override Vector3 Center
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var moth in moths)
                    sum += moth.Body.position;
                return sum / moths.Count;
            }
        }

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 3f);
            var w = Mathf.Min(1f, Speed / 0.5f);
            float a = pose.Act, r = pose.Recoil, f = pose.Fold;
            var n = moths.Count;
            Vector3? cling = Cling is { } world && a > 0f ? Root.InverseTransformPoint(world) : null;
            for (var i = 0; i < n; i++)
            {
                var m = moths[i];
                var ph = m.Phase;
                var tt = t * m.Pace;
                // Облако в покое, вытянутый поток в полёте.
                var cloud = new Vector3(Mathf.Cos(tt * 0.6f + ph) * 0.6f, 1.6f + Mathf.Sin(tt * 1.1f + ph * 2f) * 0.25f, Mathf.Sin(tt * 0.6f + ph) * 0.5f);
                var stream = new Vector3(Mathf.Sin(tt * 2f + ph) * 0.35f, 1.6f + Mathf.Sin(tt * 3f + ph) * 0.15f, (i - (n - 1) * 0.5f) * 0.3f + Mathf.Sin(tt * 1.3f + ph) * 0.1f);
                var target = Vector3.Lerp(cloud, stream, w);
                if (cling is { } c)
                    target = Vector3.Lerp(target, c + m.Spot * (0.3f + Mathf.Sin(t * 1.5f + i) * 0.015f), Ease(a));
                if (r > 0f)
                    target += new Vector3(Mathf.Cos(ph) * 1.1f, 0.4f, Mathf.Sin(ph) * 1.1f) * r;
                m.Previous = m.Position;
                if (f > 0f)
                {
                    m.Fall = Mathf.Min(1f, m.Fall + dt * (1.2f + i % 3 * 0.3f));
                    m.Position.y = Mathf.Max(0.06f, m.Position.y - dt * 3.5f * m.Fall);
                    if (m.Position.y > 0.07f)
                        m.Roll += dt * 4f * (i % 2 == 1 ? 1f : -1f);
                }
                else
                {
                    m.Fall = 0f;
                    m.Position = Vector3.Lerp(m.Position, target, 1f - Mathf.Exp(-dt * (cling != null ? 4f : 2.5f + r * 6f)));
                }
                var velocity = (m.Position - m.Previous) / Mathf.Max(dt, 1e-4f);
                // Разворот по направлению полёта; на фонаре — мордой к свету.
                var want = m.Yaw;
                if (cling is { } at && a > 0.5f)
                {
                    var d = at - m.Position;
                    want = Mathf.Atan2(d.x, d.z);
                }
                else if (velocity.sqrMagnitude > 0.02f)
                {
                    want = Mathf.Atan2(velocity.x, velocity.z);
                }
                var dy = want - m.Yaw;
                dy = Mathf.Atan2(Mathf.Sin(dy), Mathf.Cos(dy));
                m.Yaw += dy * Mathf.Min(1f, dt * 5f);
                m.Body.localPosition = m.Position + new Vector3(0f, Mathf.Sin(t * 9f * m.Pace + ph) * 0.02f * (1f - a) * (1f - f), 0f);
                if (f == 0f)
                {
                    m.Roll = -dy * 0.8f;
                    m.Pitch = Mathf.Clamp(-velocity.y * 0.3f, -0.5f, 0.5f) - 0.5f * Ease(a);
                }
                m.Body.localRotation = Rot(m.Pitch, m.Yaw, m.Roll);
                // Крылья: быстрые взмахи в полёте, медленно раскрываются на фонаре, сложены при падении.
                foreach (var (pivot, side, amplitude) in m.Wings)
                {
                    var fast = Mathf.Sin(t * (14f + 6f * r) * m.Pace + ph) * amplitude * (0.7f + 0.3f * w);
                    var slow = (Mathf.Sin(t * 1.8f + i) * 0.35f + 0.1f) * 0.6f;
                    var z = Mathf.Lerp(0.15f + fast, slow, Ease(a)) * (1f - f) + 1.3f * f;
                    pivot.localRotation = Rot(0f, 0f, side * z);
                }
            }
        }
    }
}
