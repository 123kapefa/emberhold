using System;
using System.Collections.Generic;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Выпускает ползунов ночью: каждые несколько секунд ночи (время суток из DayNightCycle) один ползун
    /// появляется в тумане за границей света горящего огня. Каждая следующая ночь чуть опаснее.
    /// С рассветом все ползуны растворяются. В углу экрана — сколько их и сколько уже у огня.
    /// </summary>
    public sealed class CrawlerSpawner : MonoBehaviour
    {
        private const int SpawnAttempts = 12;

        [SerializeField] private CrawlerSettings settings;
        [SerializeField] private Crawler prefab;
        [Tooltip("Мир блоков. Если пусто — первый VoxelWorldView в сцене.")]
        [SerializeField] private VoxelWorldView worldView;
        [Tooltip("Источники света. Если пусто — первая BuildZone в сцене.")]
        [SerializeField] private BuildZone zone;
        [Tooltip("Смена дня и ночи. Если пусто — первая в сцене.")]
        [SerializeField] private DayNightCycle dayNight;

        private readonly List<Crawler> crawlers = new();
        private float nightTime;
        private float nextSpawnAt;

        /// <summary>Живые ползуны (растворяющиеся сюда не входят).</summary>
        public IReadOnlyList<Crawler> Crawlers => crawlers;

        public CrawlerSettings Settings => settings;

        /// <summary>Появился новый ползун.</summary>
        public event Action<Crawler> Spawned;

        private void OnEnable()
        {
            if (settings == null || prefab == null)
            {
                Debug.LogError("[Crawlers] Не назначены CrawlerSettings или префаб ползуна.", this);
                enabled = false;
                return;
            }
            if (worldView == null)
                worldView = FindAnyObjectByType<VoxelWorldView>();
            if (zone == null)
                zone = FindAnyObjectByType<BuildZone>();
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight != null)
            {
                dayNight.TimePassed += OnTimePassed;
                dayNight.PhaseChanged += OnPhaseChanged;
            }
            else
            {
                Debug.LogWarning("[Crawlers] В сцене нет DayNightCycle: ползуны сами не появятся.", this);
            }
            StartNight();
        }

        private void OnDisable()
        {
            if (dayNight != null)
            {
                dayNight.TimePassed -= OnTimePassed;
                dayNight.PhaseChanged -= OnPhaseChanged;
            }
        }

        private void OnTimePassed(DayPhase phase, float seconds)
        {
            if (phase != DayPhase.Night)
                return;
            nightTime += seconds;
            while (nightTime >= nextSpawnAt)
            {
                TrySpawn();
                nextSpawnAt += settings.SpawnInterval(dayNight.DayNumber);
            }
        }

        private void OnPhaseChanged(DayPhase phase)
        {
            if (phase == DayPhase.Night)
                StartNight();
            else if (phase == DayPhase.Dawn)
                DissolveAll();
        }

        private void StartNight()
        {
            nightTime = 0f;
            nextSpawnAt = settings.FirstSpawnDelay;
        }

        /// <summary>Все ползуны растворяются (рассвет).</summary>
        public void DissolveAll()
        {
            // Died убирает ползуна из списка, поэтому идём по копии.
            foreach (var crawler in crawlers.ToArray())
                crawler.Dissolve();
        }

        /// <summary>
        /// Выпустить ползуна в тумане за границей света случайного горящего огня. Не выходит, если живых
        /// уже максимум, ни один огонь не горит или рядом не нашлось земли вне света.
        /// </summary>
        public Crawler TrySpawn()
        {
            if (crawlers.Count >= settings.MaxAlive || zone == null || worldView == null || worldView.World == null)
                return null;
            var fire = RandomLitSource();
            if (fire == null)
                return null;

            var center = fire.transform.position;
            for (var i = 0; i < SpawnAttempts; i++)
            {
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var distance = fire.Radius + UnityEngine.Random.Range(settings.SpawnDistanceMin, settings.SpawnDistanceMax);
                var point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                var local = worldView.transform.InverseTransformPoint(point);
                if (!CrawlerFooting.TryTopSurface(worldView.World, local.x, local.z, out _) || zone.Contains(point))
                    continue;
                return Spawn(point);
            }
            return null;
        }

        /// <summary>Выпустить ползуна в точке (высота берётся по земле).</summary>
        public Crawler Spawn(Vector3 point)
        {
            var crawler = Instantiate(prefab, point, Quaternion.identity, transform);
            crawler.Init(settings, worldView, zone);
            crawler.Died += OnDied;
            crawlers.Add(crawler);
            Spawned?.Invoke(crawler);
            return crawler;
        }

        private void OnDied(Crawler crawler)
        {
            crawler.Died -= OnDied;
            crawlers.Remove(crawler);
        }

        private LightSource RandomLitSource()
        {
            LightSource chosen = null;
            var lit = 0;
            foreach (var source in zone.Sources)
            {
                // Равновероятный выбор за один проход.
                if (source.IsLit && UnityEngine.Random.Range(0, ++lit) == 0)
                    chosen = source;
            }
            return chosen;
        }

        private void OnGUI()
        {
            if (crawlers.Count == 0)
                return;
            var atFire = 0;
            foreach (var crawler in crawlers)
                atFire += crawler.AtFire ? 1 : 0;
            var text = $"Ползуны: {crawlers.Count}, гасят огонь: {atFire}   (ЛКМ по ползуну — ударить)";
            GUI.Label(new Rect(Screen.width - 410f, 54f, 400f, 22f), text);
        }
    }
}
