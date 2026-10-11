using Emberhold.Core;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    [CreateAssetMenu(fileName = "BlockEditSettings", menuName = "Emberhold/Block Edit Settings")]
    public sealed class BlockEditSettings : ScriptableObject
    {
        [Header("Копание")]
        [Tooltip("Сколько шестнадцатых блока снимает один клик инструментом «Копать».")]
        [Range(1, 16)]
        [SerializeField] private int digStep = 4;

        [Header("Луч")]
        [Range(10f, 1000f)]
        [SerializeField] private float maxRayDistance = 500f;
        [SerializeField] private LayerMask groundMask = Layers.GroundMask;

        [Header("Подсветка")]
        [SerializeField] private Color digColor = new(1f, 0.75f, 0.2f);
        [SerializeField] private Color removeColor = new(1f, 0.25f, 0.2f);
        [Tooltip("На сколько рамка выступает за блок, чтобы не мерцать на его гранях.")]
        [Range(0f, 0.1f)]
        [SerializeField] private float outlinePadding = 0.01f;

        public int DigStep => digStep;
        public float MaxRayDistance => maxRayDistance;
        public LayerMask GroundMask => groundMask;
        public Color DigColor => digColor;
        public Color RemoveColor => removeColor;
        public float OutlinePadding => outlinePadding;
    }
}
