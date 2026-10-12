using System;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Ползун: мелкое быстрое существо из тумана. Ползёт по земле к ближайшему горящему огню и, добравшись,
    /// гасит его — отнимает топливо, пока огонь не погаснет или ползуна не убьют. Путь ищет по столбцам блоков
    /// (<see cref="CrawlerPaths"/>): забирается на уступ не выше <see cref="CrawlerSettings.MaxStep"/>, обходит стены
    /// и ложбины. Если пути нет — сплошная стена в два блока вокруг огня, — ползёт к огню напрямую и, упёршись
    /// в стену, вдоль неё, но за стену не попадает.
    /// Создаёт и убирает ползунов <see cref="CrawlerSpawner"/>; игрок бьёт их щелчком (<see cref="CrawlerStrike"/>).
    /// </summary>
    public sealed class Crawler : MonoBehaviour
    {
        // Сколько раз за шаг ползун может свернуть на пути: дойдя до центра столбца, сразу ползёт к следующему.
        private const int PathTurnsPerStep = 3;
        // Куда пробовать ползти, если прямо к огню нельзя: всё дальше в сторону, сначала в привычную.
        private static readonly float[] DetourAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };
        // Запас к дальности: подползая ровно на reach, ползун из-за округления никогда бы её не достиг.
        private const float ReachTolerance = 0.01f;

        [SerializeField] private CrawlerSettings settings;

        private VoxelWorldView worldView;
        private BuildZone zone;
        private CrawlerPaths paths;
        private Collider body;
        private Vector3 baseScale = Vector3.one;
        private float footing;
        private float sideSign = 1f;
        private float dissolve;

        public CrawlerSettings Settings => settings;

        /// <summary>Сколько ударов осталось выдержать.</summary>
        public float Health { get; private set; }

        /// <summary>Жив: не убит и не растворяется. Мёртвого нельзя ударить, он не гасит огонь.</summary>
        public bool IsAlive { get; private set; }

        /// <summary>Убит ударами, а не растворился с рассветом.</summary>
        public bool WasKilled { get; private set; }

        /// <summary>Огонь, к которому ползун ползёт (или который гасит).</summary>
        public LightSource Target { get; private set; }

        /// <summary>Добрался до огня и гасит его.</summary>
        public bool AtFire { get; private set; }

        /// <summary>В этом шаге ползти было некуда: со всех сторон уступы выше шага.</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>Ползун умер или начал растворяться (с рассветом). Он ещё в сцене, пока не растворится.</summary>
        public event Action<Crawler> Died;

        /// <summary>
        /// Подготовить к работе в мире: ставит на землю под собой. Вызывает спаунер сразу после создания и отдаёт
        /// общие для всех ползунов пути (crawlerPaths); без них ползун ищет пути сам.
        /// </summary>
        public void Init(CrawlerSettings crawlerSettings, VoxelWorldView view, BuildZone buildZone, CrawlerPaths crawlerPaths = null)
        {
            if (crawlerSettings != null)
                settings = crawlerSettings;
            worldView = view;
            zone = buildZone;
            paths = crawlerPaths ?? new CrawlerPaths(view.World, settings.MaxStep);
            Health = settings.Health;
            IsAlive = true;
            body = GetComponent<Collider>();
            baseScale = transform.localScale;

            var local = ToLocal(transform.position);
            if (CrawlerFooting.TryTopSurface(worldView.World, local.x, local.z, out var ground))
                footing = ground;
            transform.position = ToWorld(new Vector3(local.x, footing, local.z));
        }

        private void Update() => Step(Time.deltaTime);

        /// <summary>Прожить dt секунд: проползти, погасить огонь или раствориться. Тесты зовут напрямую.</summary>
        public void Step(float dt)
        {
            if (settings == null || worldView == null || worldView.World == null)
                return;
            if (!IsAlive)
            {
                Dissolving(dt);
                return;
            }

            ChooseTarget();
            if (Target == null)
                return;

            var toFire = Target.transform.position - transform.position;
            toFire.y = 0f;
            var distance = toFire.magnitude;
            if (distance <= settings.Reach + ReachTolerance)
            {
                AtFire = true;
                IsBlocked = false;
                if (Target.IsLit)
                    Target.DrainFuel(settings.DrainPerSecond * dt);
                Face(toFire);
                SettleHeight(dt);
                return;
            }

            AtFire = false;
            var step = Mathf.Min(settings.Speed * dt, distance - settings.Reach);
            if (!FollowPath(step))
                Crawl(toFire / distance, step);
            SettleHeight(dt);
        }

        /// <summary>Удар игрока. Возвращает true, если удар добил ползуна.</summary>
        public bool Hit(float damage)
        {
            if (!IsAlive || damage <= 0f)
                return false;
            Health -= damage;
            if (Health > 0f)
                return false;
            WasKilled = true;
            Dissolve();
            return true;
        }

        /// <summary>Начать растворяться: ползун сразу считается мёртвым, а через dissolveSeconds исчезает.</summary>
        public void Dissolve()
        {
            if (!IsAlive)
                return;
            IsAlive = false;
            AtFire = false;
            if (body != null)
                body.enabled = false;
            Died?.Invoke(this);
            if (settings.DissolveSeconds <= 0f)
                Destroy(gameObject);
        }

        /// <summary>Ползун под лучом (ближе земли и прочего). Строительство не копает там, куда игрок бьёт ползуна.</summary>
        public static bool TryPick(Ray ray, float maxDistance, out Crawler crawler)
        {
            crawler = null;
            if (!Physics.Raycast(ray, out var hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                return false;
            crawler = hit.collider.GetComponentInParent<Crawler>();
            return crawler != null && crawler.IsAlive;
        }

        /// <summary>Ближайший горящий огонь; если все погасли — прежняя цель (ползун сидит у погасшего костра).</summary>
        private void ChooseTarget()
        {
            if (zone == null)
                return;
            LightSource best = null;
            var bestDistance = float.PositiveInfinity;
            foreach (var source in zone.Sources)
            {
                if (!source.IsLit)
                    continue;
                var d = (source.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = source;
                }
            }
            if (best != null)
                Target = best;
        }

        /// <summary>
        /// Проползти distance по пути к огню: от центра столбца к центру следующего, в столбце огня — прямо к нему.
        /// false — пути к огню нет (или шаг по нему не удался): тогда ползун ползёт напрямую с обходом.
        /// </summary>
        private bool FollowPath(float distance)
        {
            if (distance <= 0f)
            {
                IsBlocked = false;
                return true;
            }
            var fire = WalkSurface.ColumnOf(ToLocal(Target.transform.position));
            var moved = false;
            for (var turn = 0; turn < PathTurnsPerStep && distance > 0.0001f; turn++)
            {
                var local = ToLocal(transform.position);
                var column = WalkSurface.ColumnOf(local);
                if (!paths.TryNext(fire, column, out var next))
                    break;
                var aim = next == column ? ToLocal(Target.transform.position) : ColumnCenter(next);
                if (!TryMoveTowards(local, aim, ref distance)
                    // Появившись у края столбца, ползун может задеть угол соседнего уступа — сперва к центру своего.
                    && !TryMoveTowards(local, ColumnCenter(column), ref distance))
                    break;
                moved = true;
            }
            if (moved)
                IsBlocked = false;
            return moved;
        }

        private bool TryMoveTowards(Vector3 local, Vector3 aim, ref float distance)
        {
            var offset = aim - local;
            offset.y = 0f;
            var length = offset.magnitude;
            if (length < 0.0001f)
                return false;
            var dir = offset / length;
            var move = Mathf.Min(distance, length);
            if (!TryMove(dir, move))
                return false;
            distance -= move;
            Face(dir);
            return true;
        }

        private static Vector3 ColumnCenter(Vector2Int column) => new(column.x + 0.5f, 0f, column.y + 0.5f);

        private void Crawl(Vector3 direction, float distance)
        {
            IsBlocked = true;
            if (distance <= 0f)
            {
                IsBlocked = false;
                return;
            }
            foreach (var angle in DetourAngles)
            {
                var dir = Quaternion.Euler(0f, angle * sideSign, 0f) * direction;
                if (!TryMove(dir, distance))
                    continue;
                // Обходя стену, держимся той же стороны, иначе ползун топчется на месте.
                if (angle != 0f)
                    sideSign = Mathf.Sign(angle) * sideSign;
                IsBlocked = false;
                Face(dir);
                return;
            }
        }

        private bool TryMove(Vector3 dir, float distance)
        {
            var local = ToLocal(transform.position);
            var next = local + dir * distance;
            var probe = next + dir * settings.BodyRadius;
            var world = worldView.World;
            if (!CrawlerFooting.TryStep(world, probe.x, probe.z, footing, settings.MaxStep, out _)
                || !CrawlerFooting.TryStep(world, next.x, next.z, footing, settings.MaxStep, out var ground))
                return false;
            footing = ground;
            transform.position = ToWorld(new Vector3(next.x, ToLocal(transform.position).y, next.z));
            return true;
        }

        /// <summary>Тело плавно догоняет землю под собой: на уступ забирается, с обрыва спускается.</summary>
        private void SettleHeight(float dt)
        {
            var local = ToLocal(transform.position);
            local.y = Mathf.MoveTowards(local.y, footing, settings.ClimbSpeed * dt);
            transform.position = ToWorld(local);
        }

        private void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private void Dissolving(float dt)
        {
            var duration = Mathf.Max(settings.DissolveSeconds, 0.0001f);
            dissolve += dt / duration;
            transform.localScale = baseScale * Mathf.Max(0f, 1f - dissolve);
            if (dissolve >= 1f)
                Destroy(gameObject);
        }

        private Vector3 ToLocal(Vector3 worldPoint) => worldView.transform.InverseTransformPoint(worldPoint);

        private Vector3 ToWorld(Vector3 localPoint) => worldView.transform.TransformPoint(localPoint);
    }
}
