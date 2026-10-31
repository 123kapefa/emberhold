using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Топливо костров для игрока (заглушка интерфейса). Над каждым костром на экране — полоска топлива: жёлтая, на исходе — оранжевая,
    /// мигает красным, пока ползуны гасят огонь, у погасшего — серая. В углу — итог по всем кострам зоны: сколько горят,
    /// сколько топлива всего, сколько под атакой, и расход за ночь. Поленья в костры носят рабочие, сначала в самый пустой;
    /// клавиша L (карта ввода «Lighting», действие AddFuel) подбрасывает полено из ниоткуда в главный костёр — только для отладки.
    /// Стоит только на главном костре: у построенных копий снимается.
    /// </summary>
    public sealed class CampfireFuelControls : MonoBehaviour
    {
        private const string ActionMapName = "Lighting";

        [SerializeField] private LightSource source;
        [Tooltip("Сколько поленьев подбрасывает одно нажатие.")]
        [Min(0.1f)]
        [SerializeField] private float fuelPerPress = 1f;
        [Tooltip("На сколько выше огня полоска топлива, в единицах сцены.")]
        [SerializeField] private float barLift = 1.6f;
        [Tooltip("Ниже этой доли запаса полоска оранжевая.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowFraction = 0.3f;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private const float BarWidth = 60f;
        private const float BarHeight = 7f;

        private InputActionMap map;
        private GUIStyle numberStyle;
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
            var zone = source.Zone;
            var camera = UnityEngine.Camera.main;
            int fires = 0, lit = 0, attacked = 0;
            float amount = 0f, capacity = 0f;
            if (zone != null)
            {
                foreach (var fire in zone.Sources)
                {
                    if (fire == null || fire.Fuel == null)
                        continue;
                    fires++;
                    lit += fire.IsLit ? 1 : 0;
                    attacked += fire.UnderAttack ? 1 : 0;
                    amount += fire.Fuel.Amount;
                    capacity += fire.Fuel.Capacity;
                    if (camera != null)
                        DrawBar(camera, fire);
                }
            }
            else
            {
                fires = 1;
                lit = source.IsLit ? 1 : 0;
                attacked = source.UnderAttack ? 1 : 0;
                amount = source.Fuel.Amount;
                capacity = source.Fuel.Capacity;
                if (camera != null)
                    DrawBar(camera, source);
            }

            var burn = source.BurnPerSecond * 60f;
            var text = $"Костры: горят {lit} из {fires}, топливо {amount:0.0} / {capacity:0}";
            if (attacked > 0)
                text += $", гасят ползуны: {attacked}";
            text += $"   ночь {source.Night}: {burn:0.#} в мин на костёр   (L — полено, отладка)";
            HudText.Right(HudText.FuelRow, text, attacked > 0 ? AttackColor() : Color.white);
        }

        /// <summary>Полоска топлива над огнём и число поленьев под ней; огонь за камерой не рисуется.</summary>
        private void DrawBar(UnityEngine.Camera camera, LightSource fire)
        {
            var screen = camera.WorldToScreenPoint(fire.transform.position + Vector3.up * barLift);
            if (screen.z <= 0f)
                return;
            var fuel = fire.Fuel;
            var fraction = fuel.Capacity > 0f ? Mathf.Clamp01(fuel.Amount / fuel.Capacity) : 0f;
            var x = screen.x - BarWidth * 0.5f;
            var y = Screen.height - screen.y - BarHeight;
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(x - 1f, y - 1f, BarWidth + 2f, BarHeight + 2f), Texture2D.whiteTexture);
            GUI.color = !fire.IsLit ? new Color(0.5f, 0.5f, 0.5f)
                : fire.UnderAttack ? AttackColor()
                : fraction < lowFraction ? new Color(1f, 0.5f, 0.1f)
                : new Color(1f, 0.85f, 0.25f);
            GUI.DrawTexture(new Rect(x, y, BarWidth * fraction, BarHeight), Texture2D.whiteTexture);
            GUI.color = previous;

            numberStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 11 };
            var number = fire.IsLit ? $"{fuel.Amount:0.#} / {fuel.Capacity:0}" : "погас";
            GUI.Label(new Rect(x - 20f, y + BarHeight + 1f, BarWidth + 40f, 16f), number, numberStyle);
        }

        /// <summary>Красный, мигает: ползуны гасят огонь.</summary>
        private static Color AttackColor() => Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(1f, 0.6f, 0.5f), Mathf.PingPong(Time.unscaledTime * 3f, 1f));
    }
}
