using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Топливо костра для игрока: показывает запас и расход в углу экрана. Поленья в костёр носят рабочие;
    /// клавиша L (карта ввода «Lighting», действие AddFuel) подбрасывает полено из ниоткуда — только для отладки.
    /// </summary>
    public sealed class CampfireFuelControls : MonoBehaviour
    {
        private const string ActionMapName = "Lighting";

        [SerializeField] private LightSource source;
        [Tooltip("Сколько поленьев подбрасывает одно нажатие.")]
        [Min(0.1f)]
        [SerializeField] private float fuelPerPress = 1f;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private InputActionMap map;
        private InputAction addFuel;

        private void Awake()
        {
            if (source == null)
                source = GetComponent<LightSource>();
        }

        private void OnEnable()
        {
            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            addFuel = map?.FindAction("AddFuel");
            map?.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
            addFuel = null;
        }

        private void Update()
        {
            if (source != null && addFuel != null && addFuel.WasPressedThisFrame())
                source.AddFuel(fuelPerPress);
        }

        private void OnGUI()
        {
            if (source == null || source.Fuel == null)
                return;
            var fuel = source.Fuel;
            var state = source.IsLit ? $"Топливо костра: {fuel.Amount:0.0} / {fuel.Capacity:0}" : "Костёр погас";
            var burn = source.BurnPerSecond * 60f;
            GUI.Label(new Rect(Screen.width - 470f, 32f, 460f, 22f), $"{state}, ночь {source.Night}: {burn:0.#} в мин   (L — полено, отладка)");
        }
    }
}
