using System;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Enemies;
using Emberhold.Gameplay.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Emberhold.Gameplay.Run
{
    /// <summary>
    /// Ход забега: счёт пережитых ночей и убитых ползунов в углу экрана и проигрыш. Главный костёр погас —
    /// по центру экрана идёт отсчёт; не разожгли снова, пока он не кончился, — игра встаёт на паузу
    /// и показывает экран проигрыша со счётом. Кнопка «Заново» или Enter (карта ввода «Run», действие Restart)
    /// загружает сцену с начала.
    /// </summary>
    public sealed class RunController : MonoBehaviour
    {
        private const string ActionMapName = "Run";

        [Tooltip("Сколько секунд даётся, чтобы снова разжечь погасший костёр.")]
        [Min(0f)]
        [SerializeField] private float defeatSeconds = 10f;
        [Tooltip("Главный костёр: проигрыш, когда он погас. Если пусто — первый источник света в BuildZone.")]
        [SerializeField] private LightSource campfire;
        [Tooltip("Смена дня и ночи: рассвет закрывает пережитую ночь. Если пусто — первая в сцене.")]
        [SerializeField] private DayNightCycle dayNight;
        [Tooltip("Спаунер ползунов: считает убитых. Если пусто — первый в сцене.")]
        [SerializeField] private CrawlerSpawner spawner;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private InputActionMap map;
        private InputAction restart;
        private bool paused;
        private GUIStyle bigLabel;
        private GUIStyle bigButton;

        public RunScore Score { get; } = new();

        public DefeatCountdown Countdown { get; private set; }

        public LightSource Campfire => campfire;

        public bool IsGameOver => Countdown != null && Countdown.IsDefeated;

        /// <summary>Отсчёт кончился, игра проиграна.</summary>
        public event Action Defeated;

        private void Awake()
        {
            Countdown = new DefeatCountdown(defeatSeconds);
        }

        private void OnEnable()
        {
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight != null)
                dayNight.PhaseChanged += Score.OnPhaseChanged;
            if (spawner == null)
                spawner = FindAnyObjectByType<CrawlerSpawner>();
            if (spawner != null)
                spawner.Killed += OnCrawlerKilled;

            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            restart = map?.FindAction("Restart");
            map?.Enable();
        }

        private void OnDisable()
        {
            if (dayNight != null)
                dayNight.PhaseChanged -= Score.OnPhaseChanged;
            if (spawner != null)
                spawner.Killed -= OnCrawlerKilled;
            map?.Disable();
            map = null;
            restart = null;
        }

        private void Start()
        {
            // Источники регистрируются в BuildZone в своём OnEnable, к Start все уже там.
            if (campfire == null)
            {
                var zone = FindAnyObjectByType<BuildZone>();
                if (zone != null && zone.Sources.Count > 0)
                    campfire = zone.Sources[0];
            }
            if (campfire == null)
                Debug.LogWarning("[Run] Не найден главный костёр: проиграть нельзя.", this);
        }

        private void OnDestroy()
        {
            // Пауза проигрыша не должна пережить сцену (перезапуск, выход из Play, тесты).
            if (paused)
                Time.timeScale = 1f;
        }

        private void Update()
        {
            if (IsGameOver)
            {
                if (restart != null && restart.WasPressedThisFrame())
                    Restart();
                return;
            }
            Step(Time.deltaTime);
        }

        /// <summary>Прожить dt секунд: отсчёт, пока костёр не горит. Тесты зовут напрямую.</summary>
        public void Step(float dt)
        {
            if (campfire == null || !Countdown.Tick(campfire.IsLit, dt))
                return;
            Score.Finish();
            paused = true;
            Time.timeScale = 0f;
            Defeated?.Invoke();
        }

        /// <summary>Начать забег заново: снять паузу и загрузить сцену с начала.</summary>
        public void Restart()
        {
            paused = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnCrawlerKilled(Crawler crawler) => Score.OnCrawlerKilled();

        private void OnGUI()
        {
            GUI.Label(new Rect(Screen.width - 410f, 76f, 400f, 22f),
                $"Пережито ночей: {Score.NightsSurvived}   Убито ползунов: {Score.CrawlersKilled}");

            if (Countdown == null)
                return;
            bigLabel ??= new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bigButton ??= new GUIStyle(GUI.skin.button) { fontSize = 22 };

            if (Countdown.IsCounting)
            {
                var left = Mathf.CeilToInt(Countdown.Remaining);
                GUI.Label(new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.25f, 600f, 80f),
                    $"Костёр погас! Разожгите его за {left} с\n(L — подбросить полено)", bigLabel);
            }
            else if (IsGameOver)
            {
                var box = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 140f, 440f, 280f);
                GUI.Box(box, GUIContent.none);
                GUI.Box(box, GUIContent.none); // Второй слой — фон плотнее.
                GUI.Label(new Rect(box.x, box.y + 20f, box.width, 50f), "Костёр погас. Поражение", bigLabel);
                GUI.Label(new Rect(box.x, box.y + 80f, box.width, 40f), $"Пережито ночей: {Score.NightsSurvived}", bigLabel);
                GUI.Label(new Rect(box.x, box.y + 120f, box.width, 40f), $"Убито ползунов: {Score.CrawlersKilled}", bigLabel);
                if (GUI.Button(new Rect(box.x + 120f, box.y + 190f, 200f, 50f), "Заново (Enter)", bigButton))
                    Restart();
            }
        }
    }
}
