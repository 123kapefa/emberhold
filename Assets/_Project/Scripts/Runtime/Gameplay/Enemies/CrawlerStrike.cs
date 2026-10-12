using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Удар по ползуну щелчком ЛКМ (карта ввода «Enemies», действия Point и Strike), пока ползунов не бьют
    /// рабочие или башни. Строительство в этот щелчок не копает блок под ползуном (<see cref="Crawler.TryPick"/>).
    /// </summary>
    public sealed class CrawlerStrike : MonoBehaviour
    {
        private const string ActionMapName = "Enemies";

        [Tooltip("Сколько здоровья снимает один удар.")]
        [Min(0.1f)]
        [SerializeField] private float damage = 1f;
        [Tooltip("Дальше этого щелчок не достаёт, в метрах от камеры.")]
        [Min(1f)]
        [SerializeField] private float maxDistance = 200f;
        [Tooltip("Камера, из которой пускаем луч. Если пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Если пусто — проектные действия (InputSystem.actions).")]
        [SerializeField] private InputActionAsset actions;

        private InputActionMap map;
        private InputAction point;
        private InputAction strike;

        private void OnEnable()
        {
            if (targetCamera == null)
                targetCamera = UnityEngine.Camera.main;
            var asset = actions != null ? actions : InputSystem.actions;
            map = asset != null ? asset.FindActionMap(ActionMapName) : null;
            point = map?.FindAction("Point");
            strike = map?.FindAction("Strike");
            map?.Enable();
        }

        private void OnDisable()
        {
            map?.Disable();
            map = null;
            point = null;
            strike = null;
        }

        private void Update()
        {
            if (targetCamera == null || point == null || strike == null || !strike.WasPressedThisFrame())
                return;
            var ray = targetCamera.ScreenPointToRay(point.ReadValue<Vector2>());
            if (Crawler.TryPick(ray, maxDistance, out var crawler))
                crawler.Hit(damage);
        }
    }
}
