using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Макет для просмотра текстур земли: строит <see cref="GroundTexturesShowcase"/> и показывает его через
    /// <see cref="VoxelWorldView"/> (у которого не назначена долина). Подписывает площадки.
    /// </summary>
    public sealed class GroundTexturesSandbox : MonoBehaviour
    {
        [SerializeField] private VoxelWorldView worldView;
        [Tooltip("Камера для подписей. Если пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera labelCamera;
        [SerializeField] private bool showLabels = true;

        private GUIStyle labelStyle;

        private void Awake()
        {
            if (worldView == null)
            {
                Debug.LogError("[GroundTexturesSandbox] Не назначен VoxelWorldView.", this);
                enabled = false;
                return;
            }

            worldView.SmoothShading = true;
            worldView.Show(GroundTexturesShowcase.Build());
        }

        private void OnGUI()
        {
            var cam = labelCamera != null ? labelCamera : UnityEngine.Camera.main;
            if (!showLabels || cam == null || worldView.World == null)
                return;

            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            foreach (var area in GroundTexturesShowcase.Areas)
            {
                var center = area.Rect.center;
                var local = new Vector3(center.x, GroundTexturesShowcase.Height((int)center.x, (int)center.y) + 3f, center.y);
                var screen = cam.WorldToScreenPoint(worldView.transform.TransformPoint(local));
                if (screen.z <= 0f)
                    continue;
                var rect = new Rect(screen.x - 120f, Screen.height - screen.y - 12f, 240f, 24f);
                GUI.Label(rect, area.Name, labelStyle);
            }
        }
    }
}
