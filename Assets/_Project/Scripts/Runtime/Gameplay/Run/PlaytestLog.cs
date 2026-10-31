using System;
using System.Globalization;
using System.IO;
using System.Text;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Lighting;
using UnityEngine;

namespace Emberhold.Gameplay.Run
{
    /// <summary>
    /// Журнал плейтеста: на каждом рассвете и при проигрыше дописывает строку об этих сутках в CSV-файл
    /// (в редакторе — папка Logs/Playtest рядом с Assets, в сборке — Application.persistentDataPath/Playtest)
    /// и в консоль. В batchmode (прогоны тестов) файл не пишется. Считает через NightLog; механику не меняет, только смотрит на костёр, сутки и ползунов.
    /// </summary>
    public sealed class PlaytestLog : MonoBehaviour
    {
        private static readonly string[] Columns =
        {
            "night", "survived", "fuel_night_start", "fuel_end", "fuel_min_night",
            "brought_day", "brought_night", "burned_fire", "eaten_crawlers",
            "low_seconds", "out_seconds", "crawlers_spawned", "crawlers_killed", "crawlers_max_alive",
        };

        [Tooltip("Ход забега: главный костёр и проигрыш. Если пусто — первый в сцене.")]
        [SerializeField] private RunController run;
        [Tooltip("Смена дня и ночи. Если пусто — первая в сцене.")]
        [SerializeField] private DayNightCycle dayNight;
        [Tooltip("Спаунер ползунов. Если пусто — первый в сцене.")]
        [SerializeField] private CrawlerSpawner spawner;

        private NightLog log;
        private LightSource campfire;
        private string path;

        /// <summary>Файл журнала этого забега.</summary>
        public string FilePath => path;

        private void OnEnable()
        {
            if (run == null)
                run = FindAnyObjectByType<RunController>();
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (spawner == null)
                spawner = FindAnyObjectByType<CrawlerSpawner>();
            if (dayNight != null)
            {
                dayNight.TimePassed += OnTimePassed;
                dayNight.PhaseChanged += OnPhaseChanged;
            }
            if (spawner != null)
            {
                spawner.Spawned += OnSpawned;
                spawner.Killed += OnKilled;
            }
            if (run != null)
                run.Defeated += OnDefeated;
        }

        private void OnDisable()
        {
            if (dayNight != null)
            {
                dayNight.TimePassed -= OnTimePassed;
                dayNight.PhaseChanged -= OnPhaseChanged;
            }
            if (spawner != null)
            {
                spawner.Spawned -= OnSpawned;
                spawner.Killed -= OnKilled;
            }
            if (run != null)
                run.Defeated -= OnDefeated;
        }

        private void Start()
        {
            // Главный костёр RunController находит в своём Start; к нашему он может ещё не успеть, поэтому берём лениво.
            var folder = Application.isEditor
                ? Path.Combine(Application.dataPath, "..", "Logs", "Playtest")
                : Path.Combine(Application.persistentDataPath, "Playtest");
            // Прогоны тестов (batchmode) тоже грузят сцену; их ночи в журнал плейтеста не пишем, только в консоль.
            if (Application.isBatchMode)
                return;
            path = Path.GetFullPath(Path.Combine(folder, $"nights-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));
            Debug.Log($"[Playtest] Журнал ночей: {path}", this);
        }

        private bool TryCampfire(out LightSource fire)
        {
            if (campfire == null && run != null)
                campfire = run.Campfire;
            fire = campfire;
            if (fire == null || fire.Fuel == null)
                return false;
            log ??= new NightLog(dayNight != null ? dayNight.DayNumber : 1, fire.Fuel.Amount);
            return true;
        }

        private void OnTimePassed(DayPhase phase, float seconds)
        {
            if (!TryCampfire(out var fire))
                return;
            var low = fire.IsLit && fire.Radius < fire.FullRadius - 0.001f;
            var alive = spawner != null ? spawner.Crawlers.Count : 0;
            log.Sample(phase, seconds, fire.Fuel.Amount, fire.IsLit, low, fire.BurnPerSecond, alive);
        }

        private void OnPhaseChanged(DayPhase phase)
        {
            if (!TryCampfire(out var fire))
                return;
            if (phase == DayPhase.Night)
                log.StartNight(fire.Fuel.Amount);
            else if (phase == DayPhase.Dawn && (run == null || !run.IsGameOver))
                Write(log.Close(true, fire.Fuel.Amount));
        }

        private void OnSpawned(Crawler crawler)
        {
            if (TryCampfire(out _))
                log.OnCrawlerSpawned();
        }

        private void OnKilled(Crawler crawler)
        {
            if (TryCampfire(out _))
                log.OnCrawlerKilled();
        }

        private void OnDefeated()
        {
            if (TryCampfire(out var fire))
                Write(log.Close(false, fire.Fuel.Amount));
        }

        /// <summary>Строка CSV об одних сутках: числа через точку, поля в порядке Columns.</summary>
        public static string ToCsv(NightRecord r)
        {
            var c = CultureInfo.InvariantCulture;
            string F(float value) => value.ToString("0.##", c);
            return string.Join(",",
                r.Night.ToString(c), r.Survived ? "1" : "0", F(r.FuelAtNightStart), F(r.FuelAtEnd), F(r.MinFuelAtNight),
                F(r.BroughtByDay), F(r.BroughtAtNight), F(r.BurnedByFire), F(r.EatenByCrawlers),
                F(r.LowSeconds), F(r.OutSeconds), r.CrawlersSpawned.ToString(c), r.CrawlersKilled.ToString(c),
                r.MaxCrawlersAlive.ToString(c));
        }

        public static string CsvHeader => string.Join(",", Columns);

        private void Write(NightRecord record)
        {
            var line = ToCsv(record);
            Debug.Log($"[Playtest] Ночь {record.Night}{(record.Survived ? "" : " (проигрыш)")}: {CsvHeader}\n{line}", this);
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = File.Exists(path) ? line + "\n" : CsvHeader + "\n" + line + "\n";
                File.AppendAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Playtest] Не записать журнал {path}: {e.Message}", this);
            }
        }
    }
}
