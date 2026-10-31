using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.DayNight
{
    /// <summary>
    /// Смена дня и ночи: ведёт время суток, меняет солнце, окружающий свет и отражения.
    /// Другие системы читают Phase, IsNight и Darkness или подписываются на PhaseChanged
    /// (топливо костра тратится ночью, ползуны выходят ночью, туман густеет по Darkness).
    /// Клавиша N (карта ввода «DayNight») сразу переключает на следующую фазу — для проверки.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        private const string ActionMapName = "DayNight";

        [SerializeField] private DayNightSettings settings;
        [Tooltip("Направленный свет сцены: днём солнце, ночью луна.")]
        [SerializeField] private Light sun;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private DayCycle cycle;
        private float baseYaw;
        private InputActionMap map;
        private InputAction nextPhase;

        /// <summary>Фаза сменилась; аргумент — новая фаза. Подписчики отписываются в OnDisable.</summary>
        public event Action<DayPhase> PhaseChanged;

        /// <summary>
        /// Прошло время суток: фаза и сколько секунд в ней. Шаг через границу фаз приходит частями,
        /// по одной на фазу, поэтому ночной расход (топливо) считается точно. Пропуск фазы клавишей N времени не тратит.
        /// </summary>
        public event Action<DayPhase, float> TimePassed;

        public DayCycle Cycle => cycle;

        /// <summary>Секунды от начала текущих суток.</summary>
        public float TimeOfDay { get; private set; }

        /// <summary>Номер суток, с 1. Растёт на каждом новом дне — пригодится, чтобы каждая ночь была сложнее.</summary>
        public int DayNumber { get; private set; } = 1;

        public DayPhase Phase { get; private set; }

        /// <summary>Доля прошедшей текущей фазы, от 0 до 1.</summary>
        public float PhaseProgress { get; private set; }

        /// <summary>Темнота от 0 (день) до 1 (ночь), плавно меняется в сумерки и на рассвете.</summary>
        public float Darkness { get; private set; }

        public bool IsNight => Phase == DayPhase.Night;

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogError("[DayNight] Не назначены DayNightSettings.", this);
                enabled = false;
                return;
            }

            cycle = settings.CreateCycle();
            if (sun != null)
                baseYaw = sun.transform.eulerAngles.y;
            Phase = settings.StartPhase;
            TimeOfDay = cycle.StartOf(Phase);
            Refresh();
        }

        private void OnEnable()
        {
            if (cycle == null)
                return;
            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            nextPhase = map?.FindAction("NextPhase");
            map?.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
            nextPhase = null;
        }

        private void Update()
        {
            if (nextPhase != null && nextPhase.WasPressedThisFrame())
            {
                SkipToNextPhase();
                return;
            }
            Advance(Time.deltaTime * settings.TimeScale);
        }

        /// <summary>Продвинуть время на столько секунд суток. Фазы, пройденные за раз, сообщаются по порядку.</summary>
        public void Advance(float seconds)
        {
            if (seconds <= 0f)
                return;
            // Шагаем по границам фаз, чтобы подписчики не пропустили ночь даже при большом шаге.
            var remaining = seconds;
            while (true)
            {
                var toEnd = cycle.StartOf(Phase) + cycle.Duration(Phase) - TimeOfDay;
                if (remaining < toEnd)
                {
                    TimeOfDay += remaining;
                    TimePassed?.Invoke(Phase, remaining);
                    break;
                }
                var spent = Mathf.Max(0f, toEnd);
                remaining -= spent;
                if (spent > 0f)
                    TimePassed?.Invoke(Phase, spent);
                EnterPhase(DayCycle.Next(Phase));
            }
            Refresh();
        }

        /// <summary>Сразу перейти к началу следующей фазы.</summary>
        public void SkipToNextPhase()
        {
            EnterPhase(DayCycle.Next(Phase));
            Refresh();
        }

        private void EnterPhase(DayPhase phase)
        {
            if (phase == DayPhase.Day)
                DayNumber++;
            Phase = phase;
            TimeOfDay = cycle.StartOf(phase);
            PhaseChanged?.Invoke(phase);
        }

        private void Refresh()
        {
            var duration = cycle.Duration(Phase);
            PhaseProgress = duration > 0f ? Mathf.Clamp01((TimeOfDay - cycle.StartOf(Phase)) / duration) : 1f;
            Darkness = DayCycle.Darkness(Phase, PhaseProgress);
            ApplyLighting();
        }

        private void ApplyLighting()
        {
            var t = Darkness;
            if (sun != null)
            {
                sun.color = Color.Lerp(settings.SunDayColor, settings.SunNightColor, t);
                sun.intensity = Mathf.Lerp(settings.SunDayIntensity, settings.SunNightIntensity, t);
                var elevation = Mathf.Lerp(settings.SunDayElevation, settings.SunNightElevation, t);
                var yaw = baseYaw + settings.SunYawPerDay * (TimeOfDay / cycle.Length);
                sun.transform.rotation = Quaternion.Euler(elevation, yaw, 0f);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(settings.AmbientDay, settings.AmbientNight, t);
            RenderSettings.reflectionIntensity = Mathf.Lerp(settings.ReflectionDay, settings.ReflectionNight, t);
        }

        private void OnGUI()
        {
            if (cycle == null)
                return;
            var name = Phase switch
            {
                DayPhase.Day => "день",
                DayPhase.Dusk => "сумерки",
                DayPhase.Night => "ночь",
                _ => "рассвет",
            };
            var left = cycle.Duration(Phase) * (1f - PhaseProgress) / Mathf.Max(settings.TimeScale, 0.0001f);
            var text = $"Сутки {DayNumber}, {name}, ещё {(int)left / 60}:{(int)left % 60:00}   (N — следующая фаза)";
            HudText.Right(HudText.DayRow, text);
        }
    }
}
