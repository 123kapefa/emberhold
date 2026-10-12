using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    [CreateAssetMenu(fileName = "Workers", menuName = "Emberhold/Worker Settings")]
    public sealed class WorkerSettings : ScriptableObject
    {
        [Header("Отряд")]
        [Tooltip("Сколько рабочих появляется у первого источника света.")]
        [Range(0, 8)]
        [SerializeField] private int count = 2;

        [Header("Ходьба")]
        [Tooltip("Скорость, блоков в секунду.")]
        [Range(0.5f, 10f)]
        [SerializeField] private float moveSpeed = 2.5f;
        [Tooltip("Самый большой перепад высот между соседними столбцами, на который рабочий забирается, в блоках.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float maxStep = 1f;
        [Tooltip("Сколько столбцов поиск пути обходит самое большее.")]
        [Range(64, 65536)]
        [SerializeField] private int searchLimit = 4096;

        [Header("Досягаемость")]
        [Tooltip("На сколько блоков выше ног рабочий дотягивается до блока из соседнего столбца.")]
        [Range(1, 4)]
        [SerializeField] private int reachUp = 3;
        [Tooltip("На сколько блоков ниже ног рабочий дотягивается до блока из соседнего столбца.")]
        [Range(1, 4)]
        [SerializeField] private int reachDown = 2;

        [Header("Работа")]
        [Tooltip("Сколько секунд рабочий поднимает ресурс с земли.")]
        [Range(0f, 5f)]
        [SerializeField] private float pickUpSeconds = 0.4f;
        [Tooltip("Сколько секунд рабочий ставит блок, когда всё принесено.")]
        [Range(0f, 10f)]
        [SerializeField] private float buildSeconds = 1.5f;
        [Tooltip("Длина анимации копки: один удар за анимацию.")]
        [Range(0.1f, 5f)]
        [SerializeField] private float digHitSeconds = 1f;
        [Tooltip("Сколько ХП снимает один удар рабочего. Прочность блоков — в BlockDurability.")]
        [Min(0.01f)]
        [SerializeField] private float digHitDamage = 25f;
        [Tooltip("Как часто свободный рабочий ищет задачу, в секундах.")]
        [Range(0.05f, 5f)]
        [SerializeField] private float idleSearchSeconds = 0.5f;

        public int Count => count;
        public float MoveSpeed => moveSpeed;
        public float MaxStep => maxStep;
        public int SearchLimit => searchLimit;
        public int ReachUp => reachUp;
        public int ReachDown => reachDown;
        public float PickUpSeconds => pickUpSeconds;
        public float BuildSeconds => buildSeconds;
        public float DigHitSeconds => digHitSeconds;
        public float DigHitDamage => digHitDamage;
        public float IdleSearchSeconds => idleSearchSeconds;
    }
}
