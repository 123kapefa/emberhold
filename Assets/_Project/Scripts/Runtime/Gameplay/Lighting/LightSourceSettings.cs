using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>Параметры типа источника света (костёр, позже фонарь, жаровня). Топливо — отдельная карточка.</summary>
    [CreateAssetMenu(fileName = "LightSourceSettings", menuName = "Emberhold/Light Source Settings")]
    public sealed class LightSourceSettings : ScriptableObject
    {
        [Tooltip("Радиус света в блоках. Внутри можно строить и копать.")]
        [Min(1f)]
        [SerializeField] private float radius = 12f;

        [Header("Свет")]
        [SerializeField] private Color lightColor = new(1f, 0.6f, 0.25f);
        [Min(0f)]
        [SerializeField] private float lightIntensity = 6f;
        [Tooltip("Высота огня над землёй.")]
        [Min(0f)]
        [SerializeField] private float lightHeight = 1f;

        [Header("Граница")]
        [Tooltip("Толщина линии границы света; цвет — в материале линии.")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float ringWidth = 0.12f;

        public float Radius => radius;
        public Color LightColor => lightColor;
        public float LightIntensity => lightIntensity;
        public float LightHeight => lightHeight;
        public float RingWidth => ringWidth;
    }
}
