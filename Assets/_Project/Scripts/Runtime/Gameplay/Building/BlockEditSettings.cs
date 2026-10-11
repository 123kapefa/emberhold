using Emberhold.Core;
using UnityEngine;

namespace Emberhold.Gameplay.Building
{
    [CreateAssetMenu(fileName = "BlockEditSettings", menuName = "Emberhold/Block Edit Settings")]
    public sealed class BlockEditSettings : ScriptableObject
    {
        [Header("Копание")]
        [Tooltip("Сколько ХП снимает один клик инструментом «Копать». Прочность блоков — в BlockDurability.")]
        [Min(0.01f)]
        [SerializeField] private float hitDamage = 25f;
        [Tooltip("Сколько этапов трещин показывать до разрушения.")]
        [Range(1, BlockCrackMesh.MaxStages)]
        [SerializeField] private int crackStages = 3;

        [Header("Луч")]
        [Range(10f, 1000f)]
        [SerializeField] private float maxRayDistance = 500f;
        [SerializeField] private LayerMask groundMask = Layers.GroundMask;

        [Header("Подсветка")]
        [SerializeField] private Color digColor = new(1f, 0.75f, 0.2f);
        [SerializeField] private Color removeColor = new(1f, 0.25f, 0.2f);
        [SerializeField] private Color buildColor = new(0.35f, 0.9f, 0.45f);
        [Tooltip("На сколько рамка выступает за блок, чтобы не мерцать на его гранях.")]
        [Range(0f, 0.1f)]
        [SerializeField] private float outlinePadding = 0.01f;
        [Tooltip("Цвет трещин на повреждённом блоке.")]
        [SerializeField] private Color crackColor = new(0.08f, 0.06f, 0.05f);

        public float HitDamage => hitDamage;
        public int CrackStages => crackStages;
        public float MaxRayDistance => maxRayDistance;
        public LayerMask GroundMask => groundMask;
        public Color DigColor => digColor;
        public Color RemoveColor => removeColor;
        public Color BuildColor => buildColor;
        public float OutlinePadding => outlinePadding;
        public Color CrackColor => crackColor;
    }
}
