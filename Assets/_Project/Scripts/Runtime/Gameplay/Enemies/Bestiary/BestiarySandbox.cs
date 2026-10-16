using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Тестовая сцена «Бестиарий»: шесть базовых врагов тумана с тремя уровнями и пятью анимациями (покой, бег, атака по
    /// роли, удар, смерть) и сценка «выходит из тумана и делает своё». Перенос страницы-прототипа анимаций: у костра стоит
    /// рабочий ростом 1.9 блока для масштаба, громила ломает стену, ходуля её перешагивает, тень обходит фонарь, мотыльки
    /// облепляют его. Механики боя нет: удары считаются только для сценки. Мышь — крутить камеру, колесо — зум.
    /// </summary>
    public sealed class BestiarySandbox : MonoBehaviour
    {
        private enum Mode
        {
            Idle,
            Run,
            Attack,
            Scene,
        }

        private enum OneShot
        {
            None,
            Hit,
            Death,
        }

        private sealed class Conf
        {
            public string Name;
            public float Run;
            public int Hp;
            public float Circle;
            public Vector3 Idle;
            public float[] Camera;
            public float FoldTime;
            public Vector3 Dust;
            public string Act;
            public string[] Text;
        }

        // Камера с той стороны, где виден и костёр, и путь врага: k — отдаление, h — высота цели.
        private static float[] CamAt(float k, float h = 0f) => new[] { 3.4f - 1.9f * k, 1.2f + h + 4.8f * k, -1.1f + 11.1f * k, 3.4f, 1.2f + h, -1.1f };

        private static readonly Dictionary<CreatureKind, Conf> Confs = new()
        {
            [CreatureKind.Crawler] = new Conf
            {
                Name = "Ползун", Run = 2.5f, Hp = 2, Circle = 2.4f, Idle = new Vector3(1.6f, 0.4f, -1.9f), Camera = new[] { 3.2f, 3.0f, 5.6f, 0.6f, 0.35f, 0f },
                FoldTime = 0.6f, Dust = new Vector3(0.5f, 0.4f, 0.1f), Act = "3. Гасит огонь",
                Text = new[]
                {
                    "Стоит у огня: дышит грудью, оглядывается, жвалы подёргиваются, хвост покачивается, изредка переставляет лапу.",
                    "Бежит со скоростью из игры (2.5 блока/с) походкой «треногой», как насекомое: три лапы опираются, три переносятся вперёд. Тело покачивается, брюшко виляет.",
                    "Подбегает, приседает и обнимает костёр передними лапами, жуёт жвалами. Искры жара летят ему в пасть, брюхо разгорается, огонь тускнеет и гаснет.",
                    "вздрагивает, вспыхивает, отскакивает назад, лапы дёргаются", "поджимает лапы, падает и рассыпается в туман",
                },
            },
            [CreatureKind.Brute] = new Conf
            {
                Name = "Громила", Run = 1.3f, Hp = 4, Circle = 3.6f, Idle = new Vector3(3.8f, 1.3f, -1.9f), Camera = CamAt(1.2f, 0.8f), FoldTime = 1f,
                Dust = new Vector3(1.4f, 2.6f, 0.3f), Act = "3. Ломает стену",
                Text = new[]
                {
                    "Тяжело дышит, плечи ходят вверх-вниз, руки висят до земли. Медленно поворачивает голову, иногда фыркает.",
                    "Идёт медленно и тяжело (1.3 блока/с): шаг под скорость, корпус переваливается, руки качаются навстречу ногам.",
                    "Подходит к стене, заносит оба кулака над головой и бьёт сверху. Блоки разлетаются, после третьего удара стены нет, и он идёт дальше к огню.",
                    "почти не замечает: дёргает головой, вспыхивает, чуть отшатывается", "падает на колени, валится вперёд на кулаки и рассыпается",
                },
            },
            [CreatureKind.Shade] = new Conf
            {
                Name = "Тень", Run = 1.8f, Hp = 2, Circle = 3.2f, Idle = new Vector3(2.4f, 0.7f, -1.9f), Camera = CamAt(0.9f), FoldTime = 0.8f,
                Dust = new Vector3(0.6f, 2.4f, 0.2f), Act = "3. Крадётся к огню",
                Text = new[]
                {
                    "Парит над землёй, подол колышется, голова медленно склоняется набок. В темноте почти не видна.",
                    "Скользит без шагов: наклоняется вперёд, руки и подол тянутся назад.",
                    "Обходит фонарь по краю света, чтобы не проявиться. У костра поднимается, вытягивает руки к огню, и из ладоней к нему тянется чёрный дым: огонь задыхается, мечется и гаснет. Рядом с ярким огнём становится видна.",
                    "отшатывается, вспыхивает и дрожит", "оседает в подол, складывается и рассыпается",
                },
            },
            [CreatureKind.Moths] = new Conf
            {
                Name = "Мотыльки", Run = 3f, Hp = 2, Circle = 3.4f, Idle = new Vector3(2.4f, 0.5f, -1.9f), Camera = CamAt(0.95f), FoldTime = 0.8f,
                Dust = new Vector3(1.2f, 2.6f, 0.12f), Act = "3. Облепляют фонарь",
                Text = new[]
                {
                    "Стайка кружит облаком, каждый мотылёк машет крыльями сам.", "Летят вытянутым потоком, на поворотах кренятся.",
                    "Летят на свет, садятся на фонарь со всех сторон и медленно раскрывают крылья. Фонарь тускнеет.",
                    "разлетаются в стороны и снова собираются", "складывают крылья и падают, кружась",
                },
            },
            [CreatureKind.Mistmother] = new Conf
            {
                Name = "Туманница", Run = 1f, Hp = 3, Circle = 3.6f, Idle = new Vector3(2.8f, 0.8f, -1.9f), Camera = CamAt(1f), FoldTime = 1f,
                Dust = new Vector3(1.2f, 2.6f, 0.25f), Act = "3. Выдыхает туман",
                Text = new[]
                {
                    "Мешок с туманом дышит, хобот покачивается, голова опущена.",
                    "Медленно идёт на четырёх ногах (1 блок/с), диагональные пары шагают вместе, мешок покачивается.",
                    "Останавливается поодаль, раздувает мешок, поднимает хобот и выдыхает туман. Туман ползёт к огню, огонь тускнеет, рабочий бросает работу и пятится.",
                    "мешок трясётся, она вспыхивает", "ноги разъезжаются, мешок сдувается, она оседает и рассыпается",
                },
            },
            [CreatureKind.Stilt] = new Conf
            {
                Name = "Ходуля", Run = 2f, Hp = 3, Circle = 4.2f, Idle = new Vector3(4.2f, 0.8f, -1.9f), Camera = CamAt(1.35f, 1.2f), FoldTime = 1f,
                Dust = new Vector3(1.6f, 4.4f, 0.3f), Act = "3. Садится на костёр",
                Text = new[]
                {
                    "Тело покачивается высоко над землёй, голова оглядывается. Если ступня отстала, переставляет её.",
                    "Шагает диагональными парами, ступни стоят на земле и не скользят.",
                    "Подходит к стене в три блока и переступает её высоко поднятыми ногами, тело проходит над стеной. Над костром раздвигает ноги и садится прямо на огонь.",
                    "тело подпрыгивает, ноги дёргаются", "ноги разъезжаются, тело падает на землю и рассыпается",
                },
            },
        };

        private const float PathAngle = -0.35f;
        private const float CrawlerReach = 1.45f;
        private const float Step = 1f / 60f;
        private static readonly Vector3 PathDirection = new(Mathf.Cos(PathAngle), 0f, Mathf.Sin(PathAngle));
        private static readonly Vector3 Lateral = new(-Mathf.Sin(PathAngle), 0f, Mathf.Cos(PathAngle));
        private static readonly Vector3 WorkerSpot = new(-1.6f, 0f, -0.6f);
        private const float WorkerYaw = 0.6f;

        [SerializeField] private CreatureMaterials materials = new();
        [Tooltip("Материал рабочего у костра (Worker.mat).")]
        [SerializeField] private Material workerMaterial;
        [SerializeField] private Light sun;

        private BestiaryProps props;
        private CreatureRig rig;
        private Transform worker;
        private UnityEngine.Camera view;
        private CreatureKind creature = CreatureKind.Crawler;
        private Mode mode = Mode.Scene;
        private int level;
        private float playSpeed = 1f;
        private bool night = true;

        // Враг и сценка.
        private Vector3 actorPosition;
        private float actorYaw;
        private string phase = "idle";
        private float timer;
        private OneShot oneShot;
        private float oneShotTime;
        private int blows;
        private float bruteReach = 2.6f;
        private bool fadeIn;
        private int hits;
        private float fuel = 1f;
        private float choke;
        private float fear;
        private float simTime;
        private float shake;

        // Камера по кругу вокруг цели.
        private Vector3 cameraTarget;
        private float cameraYaw;
        private float cameraPitch;
        private float cameraDistance = 10f;
        private GUIStyle infoStyle;

        private Conf Current => Confs[creature];
        private float LevelScale => CreaturePalette.LevelScale[level];

        private static Vector3 Along(float distance, float side = 0f) => PathDirection * distance + Lateral * side;
        private static Vector3 Flat(Vector3 p) => new(p.x, 0f, p.z);
        private float FlatDistance(Vector3 p) => Vector3.Distance(Flat(p), Flat(actorPosition));
        private static float Rnd(float span) => (Random.value - 0.5f) * span;
        private Color FogColor => CreaturePalette.Hex(night ? 0x7d90a8u : 0xd6dee6u);

        private void Awake()
        {
            if (materials.Opaque == null || materials.Transparent == null)
            {
                Debug.LogError("[BestiarySandbox] Не назначены материалы существ.", this);
                enabled = false;
                return;
            }
            view = UnityEngine.Camera.main;
            props = new BestiaryProps(transform, materials);
            BuildWorker();
            ApplyTime();
            MakeRig();
            SetCamera(Current.Camera, LevelScale);
            SetMode(mode);
        }

        private void OnDestroy() => rig?.Destroy();

        private void Update()
        {
            var dt = Mathf.Min(0.05f, UnityEngine.Time.deltaTime) * playSpeed;
            while (dt > 1e-4f)
            {
                var s = Mathf.Min(Step, dt);
                Simulate(s);
                dt -= s;
            }
        }

        private void LateUpdate()
        {
            if (view == null)
                return;
            var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            view.transform.SetPositionAndRotation(cameraTarget - rotation * Vector3.forward * cameraDistance
                                                  + new Vector3(Rnd(shake), Rnd(shake), Rnd(shake)), rotation);
            props.Step(0f, view.transform.rotation);
        }

        // ---------- враг ----------

        private void MakeRig()
        {
            rig?.Destroy();
            rig = CreatureRig.Create(creature, level, materials);
            rig.Root.SetParent(transform, false);
            if (rig is StiltRig stilt)
                stilt.Obstacle = p => props.CurrentWall?.HeightAt(p) ?? 0f;
            // Громила встаёт так, чтобы кулаки в момент удара доставали до стены.
            if (rig is BruteRig brute)
            {
                brute.Pose.Act = BruteRig.ImpactPhase;
                brute.Tick(0f, 0f);
                var fists = brute.Fists;
                bruteReach = (fists[0].z + fists[1].z) * 0.5f - transform.position.z + 0.35f;
                brute.Pose.Act = 0f;
                brute.Tick(0f, 0f);
            }
        }

        private void SetMode(Mode next)
        {
            mode = next;
            hits = 0;
            oneShot = OneShot.None;
            fadeIn = false;
            rig.Pose.Fade = 0f;
            rig.Pose.Fold = 0f;
            BuildProps();
            ResetActor();
        }

        private void BuildProps()
        {
            props.ClearStaged();
            if (mode != Mode.Attack && mode != Mode.Scene)
                return;
            switch (creature)
            {
                case CreatureKind.Brute: props.MakeWall(Along(4.6f), -PathAngle - Mathf.PI / 2f, 3, 2); break;
                case CreatureKind.Stilt: props.MakeWall(Along(4.2f), -PathAngle - Mathf.PI / 2f, 5, 3); break;
                case CreatureKind.Shade: props.MakeLamp(Along(4.6f)); break;
                case CreatureKind.Moths: props.MakeLamp(Along(3.6f, 0.9f)); break;
            }
        }

        private void ResetActor()
        {
            var pose = rig.Pose;
            pose.Latch = pose.Glow = pose.Recoil = pose.Flash = pose.Act = pose.Over = 0f;
            pose.Vis = 1f;
            fuel = 1f;
            blows = 0;
            switch (mode)
            {
                case Mode.Idle:
                    actorPosition = new Vector3(Current.Idle.x, 0f, Current.Idle.y);
                    actorYaw = Current.Idle.z;
                    SetPhase("idle");
                    break;
                case Mode.Run:
                    SetPhase("circle");
                    break;
                default:
                    var distance = creature == CreatureKind.Crawler ? 6.5f : creature == CreatureKind.Stilt ? 10f : 9f;
                    actorPosition = Along(distance);
                    actorYaw = Mathf.Atan2(-PathDirection.x, -PathDirection.z);
                    SetPhase(mode == Mode.Scene ? "emerge" : "go");
                    pose.Fade = mode == Mode.Scene ? 1f : 0f;
                    break;
            }
            if (rig is StiltRig stilt)
                stilt.ResetFeet();
            if (rig is MothsRig moths)
                moths.Cling = null;
        }

        private void SetPhase(string next)
        {
            phase = next;
            timer = 0f;
        }

        private void StartOneShot(OneShot kind)
        {
            if (oneShot == OneShot.Death)
                return;
            oneShot = kind;
            oneShotTime = 0f;
            if (kind == OneShot.Hit)
            {
                hits++;
                if (mode == Mode.Scene && hits >= Current.Hp)
                    oneShot = OneShot.Death;
            }
        }

        private void TurnTo(float want, float k)
        {
            var d = want - actorYaw;
            d = Mathf.Atan2(Mathf.Sin(d), Mathf.Cos(d));
            actorYaw += d * Mathf.Min(1f, k);
        }

        private void FaceFire(float k) => TurnTo(Mathf.Atan2(-actorPosition.x, -actorPosition.z), k);

        // Шаг к цели; возвращает скорость в блоках в секунду (0 — на месте).
        private float MoveTo(Vector3 target, float reach, float maxSpeed, float dt)
        {
            var d = Flat(target) - Flat(actorPosition);
            var distance = d.magnitude;
            if (distance > 0.05f)
                TurnTo(Mathf.Atan2(d.x, d.z), dt * 3f);
            if (distance <= reach + 0.02f)
                return 0f;
            var v = Mathf.Min(maxSpeed, (distance - reach) * 3f + 0.3f);
            actorPosition += d.normalized * Mathf.Min(v * dt, distance - reach);
            return v;
        }

        private void Done(float after)
        {
            if (mode == Mode.Attack && timer > after)
            {
                BuildProps();
                ResetActor();
            }
        }

        // Атака по роли: каждое существо ведёт себя по-своему.
        private float Behave(float dt, CreaturePose pose)
        {
            var c = Current;
            switch (creature)
            {
                case CreatureKind.Crawler:
                {
                    if (phase == "go")
                    {
                        var v = MoveTo(Vector3.zero, CrawlerReach, c.Run, dt);
                        if (v == 0f)
                            SetPhase("latch");
                        return v;
                    }
                    // Присосался: тянет жар.
                    FaceFire(dt * 5f);
                    pose.Latch = Mathf.Min(1f, pose.Latch + dt * 2.5f);
                    if (fuel > 0f)
                    {
                        fuel = Mathf.Max(0f, fuel - dt * 0.12f);
                        pose.Glow = Mathf.Min(1f, pose.Glow + dt * 0.4f);
                        var crawler = (CrawlerRig)rig;
                        if (Random.value < dt * 14f * fuel)
                            props.Spark(new Vector3(Rnd(0.3f), 0.35f, Rnd(0.3f)), () => crawler.MouthWorld);
                    }
                    if (fuel <= 0f)
                        Done(10f);
                    return 0f;
                }
                case CreatureKind.Brute:
                {
                    var wall = props.CurrentWall;
                    if (phase == "go")
                    {
                        var v = MoveTo(wall.Center, bruteReach, c.Run, dt);
                        if (v == 0f)
                            SetPhase("smash");
                        return v;
                    }
                    if (phase == "smash")
                    {
                        const float period = 1.9f;
                        var k0 = pose.Act;
                        var k = timer / period;
                        if (k >= 1f)
                        {
                            blows++;
                            timer = 0f;
                            k = 0f;
                            if (wall.Alive == 0)
                            {
                                pose.Act = 0f;
                                SetPhase("go2");
                                return 0f;
                            }
                        }
                        if (k0 < BruteRig.ImpactPhase && k >= BruteRig.ImpactPhase)
                        {
                            // Удар кулаками: блоки разлетаются, пыль, тряска.
                            var fists = ((BruteRig)rig).Fists;
                            var at = (fists[0] + fists[1]) * 0.5f;
                            props.BreakBlocks(at, 0.8f + blows * 0.5f, blows >= 2, PathDirection, Lateral);
                            for (var i = 0; i < 10; i++)
                                props.Puff(at + new Vector3(Rnd(1.2f), Rnd(0.6f), Rnd(1.2f)), 0.35f, new Vector3(Rnd(1f), 0.4f + Random.value * 0.5f, Rnd(1f)), FogColor,
                                    1.4f, 0.45f);
                            shake = 0.18f;
                        }
                        pose.Act = k;
                        return 0f;
                    }
                    if (phase == "go2")
                    {
                        var v = MoveTo(Vector3.zero, 2.6f * LevelScale, c.Run, dt);
                        if (v == 0f)
                            SetPhase("stand");
                        return v;
                    }
                    FaceFire(dt * 3f);
                    Done(4f);
                    return 0f;
                }
                case CreatureKind.Shade:
                {
                    var detour = Along(4.6f, 2.9f);
                    if (phase == "go")
                    {
                        var v = MoveTo(detour, 0f, c.Run, dt);
                        if (FlatDistance(detour) < 1f)
                            SetPhase("go2");
                        return v;
                    }
                    if (phase == "go2")
                    {
                        var v = MoveTo(Vector3.zero, 1.2f * LevelScale, c.Run, dt);
                        if (v == 0f)
                            SetPhase("smother");
                        return v;
                    }
                    FaceFire(dt * 3f);
                    pose.Act = Mathf.Min(1f, pose.Act + dt * 0.8f);
                    if (pose.Act > 0.6f && fuel > 0f)
                    {
                        // Из рук к огню тянется чёрный дым, огонь задыхается.
                        var pulse = 0.6f + 0.4f * Mathf.Sin(timer * 5f);
                        foreach (var hand in ((ShadeRig)rig).Hands)
                        {
                            if (Random.value >= dt * 30f * pulse)
                                continue;
                            var to = new Vector3(Rnd(0.25f), 0.35f + Random.value * 0.2f, Rnd(0.25f)) - hand;
                            var life = to.magnitude / 1.5f + 0.25f;
                            props.Puff(hand, 0.13f * LevelScale, to / (life - 0.25f) + new Vector3(Rnd(0.3f), 0f, Rnd(0.3f)),
                                CreaturePalette.Hex(night ? 0x07090du : 0x15181eu), life, 0.8f, 0.9f, 0f);
                        }
                        fuel = Mathf.Max(0f, fuel - dt * 0.1f);
                        choke = Mathf.Min(1f, choke + dt * 2f);
                    }
                    else
                    {
                        choke = Mathf.Max(0f, choke - dt);
                    }
                    if (fuel <= 0f)
                        Done(10f);
                    return 0f;
                }
                case CreatureKind.Moths:
                {
                    var lamp = props.CurrentLamp;
                    ((MothsRig)rig).Cling = lamp.Top;
                    if (phase == "go")
                    {
                        var v = MoveTo(lamp.Base, 0.1f, c.Run, dt);
                        if (FlatDistance(lamp.Base) < 1.8f)
                            pose.Act = Mathf.Min(1f, pose.Act + dt * 1.2f);
                        if (v == 0f)
                            SetPhase("cling");
                        return v;
                    }
                    pose.Act = Mathf.Min(1f, pose.Act + dt * 1.2f);
                    lamp.Dim = Mathf.Max(0.25f, lamp.Dim - dt * 0.12f);
                    if (lamp.Dim <= 0.25f)
                        Done(4f);
                    return 0f;
                }
                case CreatureKind.Mistmother:
                {
                    if (phase == "go")
                    {
                        var v = MoveTo(Vector3.zero, 4.2f * LevelScale, c.Run, dt);
                        if (v == 0f)
                            SetPhase("exhale");
                        return v;
                    }
                    FaceFire(dt * 3f);
                    const float period = 3.4f;
                    var k = timer / period % 1f;
                    pose.Act = Mathf.Max(0.001f, k);
                    if (k > 0.45f && k < 0.9f)
                    {
                        // Выдох: туман тянется к огню, огонь тускнеет, рабочий пятится.
                        var mist = (MistmotherRig)rig;
                        var pipes = mist.PipeTips;
                        var sources = new[] { mist.TrunkTip, pipes[0], pipes[1] };
                        for (var j = 0; j < sources.Length; j++)
                        {
                            if (Random.value >= dt * (j > 0 ? 6f : 16f))
                                continue;
                            var to = new Vector3(Rnd(1f), 0.45f + Random.value * 0.4f, Rnd(1f)) - sources[j];
                            var life = to.magnitude / 1.4f + 0.6f;
                            props.Puff(sources[j], (j > 0 ? 0.12f : 0.18f) * LevelScale, to / (life - 0.6f), FogColor, life, 0.3f, 0.35f);
                        }
                        fuel = Mathf.Max(0.4f, fuel - dt * 0.06f);
                        fear = Mathf.Min(1f, fear + dt * 0.5f);
                    }
                    Done(14f);
                    return 0f;
                }
                default:
                {
                    var wall = props.CurrentWall;
                    if (phase == "go")
                    {
                        var over = wall != null && (wall.HeightAt(actorPosition) > 0f || wall.HeightAt(actorPosition - PathDirection * 1.5f) > 0f
                                                                                      || wall.HeightAt(actorPosition + PathDirection * 1.5f) > 0f);
                        pose.Over += ((over ? 1f : 0f) - pose.Over) * Mathf.Min(1f, dt * 2f);
                        var near = wall != null && Mathf.Abs(Flat(actorPosition).magnitude - 4.2f) < 3.2f;
                        var v = MoveTo(Vector3.zero, 0f, near ? 1.1f : c.Run, dt);
                        if (v == 0f || Flat(actorPosition).magnitude < 0.05f)
                            SetPhase("sit");
                        return v;
                    }
                    pose.Over = Mathf.Max(0f, pose.Over - dt);
                    pose.Act = Mathf.Min(1f, pose.Act + dt * 0.55f);
                    if (pose.Act > 0.7f)
                    {
                        fuel = Mathf.Max(0f, fuel - dt * 0.2f);
                        pose.Glow = Mathf.Min(1f, fuel * 1.5f) * pose.Act;
                    }
                    else
                    {
                        pose.Glow = pose.Act * 0.5f;
                    }
                    if (fuel <= 0f)
                        Done(3f);
                    return 0f;
                }
            }
        }

        private void Simulate(float dt)
        {
            simTime += dt;
            var pose = rig.Pose;
            var c = Current;
            timer += dt;
            var v = 0f;
            if (oneShot != OneShot.Death)
            {
                switch (phase)
                {
                    case "idle":
                        break;
                    case "circle":
                    {
                        var a = timer * c.Run / c.Circle;
                        var o = creature == CreatureKind.Crawler ? Vector3.zero : Along(5f, -2f);
                        actorPosition = new Vector3(o.x + Mathf.Cos(a) * c.Circle, 0f, o.z + Mathf.Sin(a) * c.Circle);
                        actorYaw = Mathf.Atan2(-Mathf.Sin(a), Mathf.Cos(a));
                        v = c.Run;
                        break;
                    }
                    case "emerge":
                        pose.Fade = Mathf.Max(0f, 1f - timer / 1.2f);
                        if (Random.value < dt * 20f)
                            props.Puff(actorPosition + new Vector3(Rnd(c.Dust.x), Random.value * c.Dust.y * 0.5f, Rnd(c.Dust.x)), c.Dust.z * 2f, null, FogColor);
                        if (timer > 1.2f)
                            SetPhase("go");
                        break;
                    default:
                        v = Behave(dt, pose);
                        break;
                }
            }
            // Тень видна только в ярком свете: у фонаря и у горящего костра.
            if (creature == CreatureKind.Shade)
            {
                var bright = night ? 0f : 1f;
                if (props.CurrentLamp != null)
                    bright = Mathf.Max(bright, 1f - (Vector3.Distance(Flat(props.CurrentLamp.Base), Flat(actorPosition)) - 1.6f) / 1.2f);
                bright = Mathf.Max(bright, (1f - (Flat(actorPosition).magnitude - 2.2f) / 1.5f) * Mathf.Min(1f, fuel * 2f));
                pose.Vis += (Mathf.Clamp(bright, 0.12f, 1f) - pose.Vis) * Mathf.Min(1f, dt * 3f);
            }
            // Разовые: удар и смерть.
            if (oneShot == OneShot.Hit)
            {
                var first = oneShotTime == 0f;
                oneShotTime += dt;
                var k = oneShotTime / 0.5f;
                pose.Recoil = Mathf.Max(0f, Mathf.Sin(Mathf.Min(1f, k) * Mathf.PI));
                pose.Flash = Mathf.Max(0f, 1f - k * 2.2f);
                if (first)
                {
                    var at = rig.Center;
                    for (var i = 0; i < 6; i++)
                        props.Puff(at + new Vector3(Rnd(0.3f), Rnd(0.3f), Rnd(0.3f)), c.Dust.z * 0.8f, null, FogColor);
                }
                if (k >= 1f)
                {
                    oneShot = OneShot.None;
                    pose.Recoil = 0f;
                    pose.Flash = 0f;
                }
            }
            if (oneShot == OneShot.Death)
            {
                oneShotTime += dt;
                var t = oneShotTime;
                var fold = c.FoldTime;
                pose.Latch = Mathf.Max(0f, pose.Latch - dt * 3f);
                pose.Recoil = Mathf.Max(0f, 0.6f - t * 2f);
                pose.Flash = Mathf.Max(0f, 1f - t * 3f);
                pose.Act = creature is CreatureKind.Brute or CreatureKind.Mistmother ? 0f : Mathf.Max(0f, pose.Act - dt * 1.5f);
                pose.Fold = Mathf.Min(1f, t / fold);
                if (t > fold)
                {
                    pose.Fade = Mathf.Min(1f, (t - fold) / 1f);
                    if (Random.value < dt * 35f * (1f - pose.Fade * 0.5f))
                        props.Puff(actorPosition + new Vector3(Rnd(c.Dust.x), 0.1f + Random.value * c.Dust.y * 0.4f, Rnd(c.Dust.x)), c.Dust.z * (1f + Random.value),
                            null, FogColor);
                }
                if (t > fold + 2.2f)
                {
                    oneShot = OneShot.None;
                    pose.Fold = 0f;
                    hits = 0;
                    if (mode is Mode.Scene or Mode.Attack)
                    {
                        BuildProps();
                        ResetActor();
                    }
                    else
                    {
                        pose.Fade = 1f;
                        fadeIn = true;
                    }
                }
            }
            if (fadeIn)
            {
                pose.Fade = Mathf.Max(0f, pose.Fade - dt * 1.2f);
                if (pose.Fade == 0f)
                    fadeIn = false;
            }
            rig.Root.localPosition = actorPosition;
            rig.Root.localRotation = Quaternion.AngleAxis(actorYaw * Mathf.Rad2Deg, Vector3.up);
            rig.Tick(dt, v / rig.Scale);

            // Костёр, фонарь, рабочий и частицы.
            if (creature != CreatureKind.Shade || mode is Mode.Idle or Mode.Run)
                choke = 0f;
            props.UpdateFire(fuel, choke, simTime, night);
            if (creature != CreatureKind.Mistmother || mode is Mode.Idle or Mode.Run)
                fear = Mathf.Max(0f, fear - dt * 0.5f);
            if (worker != null)
            {
                worker.localPosition = WorkerSpot + new Vector3(-fear * 0.8f, 0f, -fear * 0.5f);
                worker.localRotation = CreatureBuilder.EulerXYZ(new Vector3(0f, WorkerYaw + fear * 2.4f, Mathf.Sin(simTime * 30f) * 0.02f * fear));
            }
            props.Step(dt, view != null ? view.transform.rotation : Quaternion.identity);
            shake = Mathf.Max(0f, shake - dt * 0.6f);
        }

        // ---------- сцена ----------

        private void BuildWorker()
        {
            if (workerMaterial == null)
                return;
            var look = new WorkerLook(new Color(0.91f, 0.75f, 0.6f), new Color(0.23f, 0.17f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                new Color(0.36f, 0.42f, 0.3f), true);
            var go = new GameObject("Worker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = WorkerMeshBuilder.Build(look, 1.9f);
            go.AddComponent<MeshRenderer>().sharedMaterial = workerMaterial;
            worker = go.transform;
        }

        private void ApplyTime()
        {
            var background = CreaturePalette.Hex(night ? 0x141c28u : 0xaab8c4u);
            if (view != null)
            {
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = background;
            }
            RenderSettings.fog = true;
            if (night)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.04f;
                RenderSettings.fogColor = CreaturePalette.Hex(0x1c2635);
            }
            else
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 30f;
                RenderSettings.fogEndDistance = 80f;
                RenderSettings.fogColor = background;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = CreaturePalette.Hex(night ? 0x6f88a8u : 0xc4d0dcu) * (night ? 0.8f : 1f);
            RenderSettings.ambientEquatorColor = CreaturePalette.Hex(night ? 0x3a4656u : 0x8a917fu) * (night ? 0.8f : 1f);
            RenderSettings.ambientGroundColor = CreaturePalette.Hex(night ? 0x1b2026u : 0x4c5232u);
            if (sun != null)
            {
                sun.intensity = night ? 0.6f : 2f;
                sun.color = CreaturePalette.Hex(night ? 0x8fa8d8u : 0xfff0dcu);
            }
        }

        // Камера отъезжает от цели пропорционально уровню: древний в 1.36 раза крупнее.
        private void SetCamera(float[] c, float k)
        {
            cameraTarget = transform.TransformPoint(new Vector3(c[3], c[4] * k, c[5]));
            var offset = new Vector3(c[0] - c[3], c[1] - c[4], c[2] - c[5]) * k;
            cameraDistance = offset.magnitude;
            var direction = -offset / cameraDistance;
            cameraPitch = Mathf.Asin(-direction.y) * Mathf.Rad2Deg;
            cameraYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        // ---------- интерфейс ----------

        private void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.MouseDrag && e.button == 0 && e.mousePosition.y > 150f)
            {
                cameraYaw += e.delta.x * 0.3f;
                cameraPitch = Mathf.Clamp(cameraPitch + e.delta.y * 0.3f, 3f, 88f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                cameraDistance = Mathf.Clamp(cameraDistance * (1f + e.delta.y * 0.05f), 1.5f, 40f);
                e.Use();
            }

            var x = 16f;
            var y = 12f;
            Row("Существо", ref y, x, () =>
            {
                foreach (CreatureKind kind in System.Enum.GetValues(typeof(CreatureKind)))
                {
                    if (Toggle(kind == creature, Confs[kind].Name))
                    {
                        creature = kind;
                        fear = 0f;
                        MakeRig();
                        SetCamera(Current.Camera, LevelScale);
                        SetMode(mode);
                    }
                }
            });
            Row("Состояние", ref y, x, () =>
            {
                if (Toggle(mode == Mode.Idle, "1. Покой"))
                    SetMode(Mode.Idle);
                if (Toggle(mode == Mode.Run, "2. Бег"))
                    SetMode(Mode.Run);
                if (Toggle(mode == Mode.Attack, Current.Act))
                    SetMode(Mode.Attack);
                if (GUILayout.Button("4. Удар"))
                    StartOneShot(OneShot.Hit);
                if (GUILayout.Button("5. Смерть"))
                    StartOneShot(OneShot.Death);
                if (Toggle(mode == Mode.Scene, "Сценка"))
                    SetMode(Mode.Scene);
            });
            Row("Уровень", ref y, x, () =>
            {
                var names = new[] { "Обычный", "Матёрый", "Древний" };
                for (var l = 0; l < 3; l++)
                {
                    if (Toggle(level == l, names[l]))
                    {
                        level = l;
                        MakeRig();
                        SetCamera(Current.Camera, LevelScale);
                        SetMode(mode);
                    }
                }
            });
            Row("Скорость и время", ref y, x, () =>
            {
                if (Toggle(Mathf.Approximately(playSpeed, 1f), "1×"))
                    playSpeed = 1f;
                if (Toggle(Mathf.Approximately(playSpeed, 0.25f), "¼×"))
                    playSpeed = 0.25f;
                GUILayout.Space(12f);
                if (Toggle(!night, "День"))
                {
                    night = false;
                    ApplyTime();
                }
                if (Toggle(night, "Ночь"))
                {
                    night = true;
                    ApplyTime();
                }
            });

            infoStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 13, richText = true };
            var conf = Current;
            var head = mode switch { Mode.Idle => "1. Покой", Mode.Run => "2. Бег", Mode.Attack => conf.Act, _ => "Сценка" };
            var body = mode == Mode.Scene
                ? $"Выходит из тумана и делает своё ({conf.Act.Substring(3).ToLowerInvariant()}). Нажимай «Удар»: после {conf.Hp}-го удара умирает, затем выходит следующий."
                : conf.Text[(int)mode];
            var tail = $"4. Удар: {conf.Text[3]}. 5. Смерть: {conf.Text[4]}." + (mode == Mode.Scene ? $" Ударов: {hits} из {conf.Hp}." : "");
            var width = Mathf.Min(560f, Screen.width - 32f);
            GUI.Box(new Rect(16f, Screen.height - 130f, width, 116f), $"<b>{head}</b>\n{body}\n{tail}", infoStyle);
            GUI.Label(new Rect(Screen.width - 420f, Screen.height - 30f, 410f, 24f), "Рабочий у костра ростом 1.9 блока · тянуть — крутить, колесо — зум");
        }

        private static void Row(string title, ref float y, float x, System.Action buttons)
        {
            GUILayout.BeginArea(new Rect(x, y, Screen.width - 2f * x, 30f));
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(title, GUILayout.Width(120f));
            buttons();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            y += 32f;
        }

        private static bool Toggle(bool on, string label)
        {
            var color = GUI.backgroundColor;
            if (on)
                GUI.backgroundColor = new Color(0.94f, 0.64f, 0.29f);
            var pressed = GUILayout.Button(label);
            GUI.backgroundColor = color;
            return pressed && !on;
        }
    }
}
