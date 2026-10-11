using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Таблица прочности материалов. Прочность не зависит от заполнения: блок на четверть и полный
    /// разрушаются за одинаковое число ударов. Удар (клик игрока или анимация рабочего) снимает ХП,
    /// снятые ХП сохраняются, пока блок не разрушен.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockDurability", menuName = "Emberhold/Block Durability")]
    public sealed class BlockDurabilityTable : ScriptableObject, IBlockDurability
    {
        [Tooltip("Прочность по номеру материала: 0 — воздух (не используется), 1 — трава, 2 — земля, 3 — камень.")]
        [SerializeField] private float[] hitPoints = { 0f, 100f, 100f, 100f };
        [Tooltip("Прочность материалов, которых нет в списке.")]
        [Min(1f)]
        [SerializeField] private float fallbackHitPoints = 100f;

        public float HitPoints(byte material) =>
            hitPoints != null && material < hitPoints.Length && hitPoints[material] > 0f
                ? hitPoints[material]
                : fallbackHitPoints;
    }
}
