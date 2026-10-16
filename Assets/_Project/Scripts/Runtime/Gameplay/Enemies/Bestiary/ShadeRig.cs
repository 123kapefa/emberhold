using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Тень: парит, подол трепещет и тянется назад в полёте. Атака act — поднимается над огнём и тянет к нему руки
    /// (из ладоней идёт чёрный дым, его пускает сцена). Видимость vis задаёт сцена: тень видна только в ярком свете.
    /// </summary>
    public sealed class ShadeRig : CreatureRig
    {
        private const float Float = 0.12f;

        private readonly Transform body;
        private readonly Transform hem;
        private readonly Transform head;
        private readonly Transform[] shoulder;
        private readonly Transform[] elbow;

        public ShadeRig(int level, CreatureMaterials templates) : base(CreatureKind.Shade, level, templates)
        {
            body = Pivot(Root, new Vector3(0f, 0.9f, 0f));
            hem = Pivot(body, new Vector3(0f, 0.62f, 0f));
            head = Pivot(body, new Vector3(0f, 1.74f, 0f));
            shoulder = new[] { Pivot(body, new Vector3(-0.25f, 1.62f, 0.05f)), Pivot(body, new Vector3(0.25f, 1.62f, 0.05f)) };
            elbow = new[] { Pivot(shoulder[0], new Vector3(-0.48f, 1.2f, 0.2f)), Pivot(shoulder[1], new Vector3(0.48f, 1.2f, 0.2f)) };
            BuildModel(e =>
            {
                var p = e.Center;
                var ax = Mathf.Abs(p.x);
                var i = p.x < 0f ? 0 : 1;
                if ((ax > 0.42f && p.y > 0.3f && p.y < 1.2f) || (ax > 0.33f && p.y >= 1.15f && p.y < 1.55f))
                    return p.y > 1.2f ? shoulder[i] : elbow[i];
                if (p.y > 1.72f && ax < 0.3f)
                    return head;
                return p.y < 0.62f ? hem : body;
            });
            CenterHeight = 1.4f;
        }

        /// <summary>Ладони в мире: из них к огню тянется чёрный дым.</summary>
        public Vector3[] Hands => new[] { World(elbow[0], new Vector3(-0.62f, 0.5f, 0.4f)), World(elbow[1], new Vector3(0.62f, 0.5f, 0.4f)) };

        protected override void Animate(float dt, float speed)
        {
            var t = Time;
            var pose = Pose;
            Speed += (speed - Speed) * Mathf.Min(1f, dt * 3f);
            var w = Mathf.Min(1f, Speed / 0.4f);
            float a = pose.Act, f = pose.Fold, r = pose.Recoil;
            body.localPosition = new Vector3(0f, 0.9f + Float + Mathf.Sin(t * 1.4f) * 0.04f + 0.12f * a + Mathf.Sin(t * 3f) * 0.03f * a - 0.3f * f, -0.15f * r);
            body.localRotation = Rot(0.28f * w + 0.35f * a - 0.3f * r + 0.3f * f, 0f, Mathf.Sin(t * 0.8f) * 0.04f * (1f - w) + Mathf.Sin(t * 40f) * 0.05f * r);
            body.localScale = new Vector3(1f + 0.1f * f, 1f - 0.4f * f, 1f + 0.1f * f);
            // Подол: тянется назад в полёте, трепещет, над огнём расходится.
            hem.localRotation = Rot(0.3f * w + Mathf.Sin(t * 4.2f) * 0.06f + Mathf.Sin(t * 9f) * 0.04f * w - 0.2f * a, 0f, Mathf.Sin(t * 3.1f) * 0.05f);
            var flare = 1f + 0.25f * a + 0.1f * r + 0.3f * f;
            hem.localScale = new Vector3(flare, 1f - 0.3f * f, flare);
            // Голова: медленно склоняется набок, над огнём смотрит вниз.
            head.localRotation = Rot(0.1f * w + 0.35f * a - 0.4f * r + 0.5f * f, Mathf.Sin(t * 0.37f) * 0.3f * (1f - w) * (1f - a),
                Mathf.Sin(t * 0.6f) * 0.18f * (1f - w) * (1f - a));
            for (var i = 0; i < 2; i++)
            {
                var s = i == 1 ? 1f : -1f;
                shoulder[i].localRotation = Rot(0.55f * w - 1.05f * a + Mathf.Sin(t * 1.2f + i) * 0.05f + 0.3f * r - 0.2f * f, 0f,
                    s * (0.08f + 0.35f * a + 0.3f * r - 0.05f * f));
                elbow[i].localRotation = Rot(-0.45f * a - 0.15f * w + Mathf.Sin(t * 2f + i * 1.3f) * 0.08f * (1f - a) + Mathf.Sin(t * 9f + i) * 0.04f * a + 0.3f * f, 0f, 0f);
            }
        }
    }
}
