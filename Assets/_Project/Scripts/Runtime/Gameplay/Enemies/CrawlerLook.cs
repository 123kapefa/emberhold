using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Enemies.Bestiary;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Вид ползуна: гранёная модель и процедурные анимации из бестиария (<see cref="CrawlerRig"/>) поверх
    /// <see cref="Crawler"/>. Только смотрит на его состояние и ничего в нём не меняет: ползёт — бежит лапами, добрался
    /// до горящего огня — присасывается и светится жаром, удар — отшатывается со вспышкой, смерть — складывается и
    /// бледнеет, пока ползун растворяется. Уровень облика (обычный, матёрый, древний) берётся по номеру ночи.
    /// </summary>
    [RequireComponent(typeof(Crawler))]
    public sealed class CrawlerLook : MonoBehaviour
    {
        [Tooltip("Материалы врагов: непрозрачный (EnemyBody) и прозрачный (EnemyGlass).")]
        [SerializeField] private CreatureMaterials materials;
        [Tooltip("С какой ночи ползуны матёрые: изморозь, трещины, холодный свет.")]
        [Min(1)]
        [SerializeField] private int seasonedFromNight = 4;
        [Tooltip("С какой ночи ползуны древние: ледяное ядро и корона.")]
        [Min(1)]
        [SerializeField] private int ancientFromNight = 7;

        private Crawler crawler;
        private CrawlerRig rig;
        private readonly CrawlerPoseDriver driver = new();
        private Vector3 lastPosition;

        /// <summary>Риг модели (null, пока ползун не появился в сцене).</summary>
        public CrawlerRig Rig => rig;

        /// <summary>Уровень облика для ночи number (с 1): 0 — обычный, 1 — матёрый, 2 — древний.</summary>
        public int LevelForNight(int number) => number >= ancientFromNight ? 2 : number >= seasonedFromNight ? 1 : 0;

        private void Start()
        {
            crawler = GetComponent<Crawler>();
            var cycle = FindAnyObjectByType<DayNightCycle>();
            rig = CreatureRig.Create(CreatureKind.Crawler, LevelForNight(cycle != null ? cycle.DayNumber : 1), materials) as CrawlerRig;
            rig.Root.SetParent(transform, false);
            lastPosition = transform.position;
            driver.Reset(crawler.Health);
        }

        private void LateUpdate()
        {
            if (rig == null)
                return;
            var dt = Time.deltaTime;
            var moved = transform.position - lastPosition;
            lastPosition = transform.position;
            moved.y = 0f;
            var size = Mathf.Max(rig.Root.lossyScale.x, 1e-4f);
            var latched = crawler.IsAlive && crawler.AtFire && crawler.Target != null && crawler.Target.IsLit;
            driver.Update(dt, crawler.IsAlive, latched, crawler.Health, rig.Pose);
            var speed = crawler.IsAlive && dt > 0f ? moved.magnitude / dt / size : 0f;
            rig.Tick(dt, Mathf.Min(speed, CrawlerPoseDriver.MaxSpeed));
        }

        private void OnDestroy()
        {
            rig?.Destroy();
            rig = null;
        }
    }
}
