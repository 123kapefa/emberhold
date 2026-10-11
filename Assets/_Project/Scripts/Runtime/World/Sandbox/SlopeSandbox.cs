using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Песочница склонов: строит макеты <see cref="SlopeShowcase"/> и показывает их через <see cref="VoxelWorldView"/>
    /// (у которого не назначена долина). Подписывает площадки и пересобирает их при изменении настроек в Play.
    /// </summary>
    public sealed class SlopeSandbox : MonoBehaviour
    {
        [SerializeField] private SlopeSandboxSettings settings;
        [SerializeField] private VoxelWorldView worldView;
        [Tooltip("Камера для подписей. Если пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera labelCamera;

        private bool rebuildRequested;
        private GUIStyle labelStyle;

        private void Awake()
        {
            if (settings == null || worldView == null)
            {
                Debug.LogError("[SlopeSandbox] Не назначены SlopeSandboxSettings или VoxelWorldView.", this);
                enabled = false;
                return;
            }

            worldView.SmoothShading = settings.SmoothShading;
            worldView.Show(SlopeShowcase.Build(settings));
        }

        private void OnEnable()
        {
            if (settings != null)
                settings.Changed += RequestRebuild;
        }

        private void OnDisable()
        {
            if (settings != null)
                settings.Changed -= RequestRebuild;
        }

        // OnValidate приходит посреди отрисовки инспектора, поэтому пересобираем в ближайшем Update.
        private void RequestRebuild() => rebuildRequested = true;

        private void Update()
        {
            if (!rebuildRequested || worldView.World == null)
                return;
            rebuildRequested = false;
            worldView.SmoothShading = settings.SmoothShading;
            SlopeShowcase.Fill(worldView.World, settings.HeightStep, settings.SnapToLayers, settings.Steepness,
                settings.BaseHeight, settings.DirtDepth, settings.Seed);
        }

        private void OnGUI()
        {
            var cam = labelCamera != null ? labelCamera : UnityEngine.Camera.main;
            if (cam == null || worldView.World == null)
                return;

            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            for (var pad = 0; pad < SlopeShowcase.Pads.Length; pad++)
            {
                var center = SlopeShowcase.PadCenter(pad);
                // Подпись над ближним краем площадки, чуть выше земли.
                var local = new Vector3(center.x, settings.BaseHeight + 8f, center.y - SlopeShowcase.PadSize * 0.4f);
                var screen = cam.WorldToScreenPoint(worldView.transform.TransformPoint(local));
                if (screen.z <= 0f)
                    continue;
                var rect = new Rect(screen.x - 100f, Screen.height - screen.y - 12f, 200f, 24f);
                GUI.Label(rect, $"{pad + 1}. {SlopeShowcase.Pads[pad].Name}", labelStyle);
            }
        }
    }
}
