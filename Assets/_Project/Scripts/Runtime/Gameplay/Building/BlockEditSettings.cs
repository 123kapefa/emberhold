using Emberhold.Core;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    [CreateAssetMenu(fileName = "BlockEditSettings", menuName = "Emberhold/Block Edit Settings")]
    public sealed class BlockEditSettings : ScriptableObject
    {
        [Header("Копание")]
        [Tooltip("За сколько кликов инструментом «Копать» блок разрушается целиком, независимо от заполнения.")]
        [Range(1, BlockCrackMesh.MaxStages + 1)]
        [SerializeField] private int hitsToBreak = 4;

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
        [Tooltip("Цвет трещин на повреждённом блоке.")]
        [SerializeField] private Color crackColor = new(0.08f, 0.06f, 0.05f);

        public int HitsToBreak => hitsToBreak;
        /// <summary>Сколько этапов трещин видно до разрушения: последний клик уже убирает блок.</summary>
        public int CrackStages => Mathf.Min(hitsToBreak - 1, BlockCrackMesh.MaxStages);
        public float MaxRayDistance => maxRayDistance;
        public LayerMask GroundMask => groundMask;
        public Color DigColor => digColor;
        public Color RemoveColor => removeColor;
        public float OutlinePadding => outlinePadding;
        public Color CrackColor => crackColor;
    }
}
