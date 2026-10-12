using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>Параметры ползуна и того, как часто они выходят из тумана.</summary>
    [CreateAssetMenu(fileName = "CrawlerSettings", menuName = "Emberhold/Crawler Settings")]
    public sealed class CrawlerSettings : ScriptableObject
    {
        [Header("Движение")]
        [Tooltip("Скорость по земле, метров в секунду.")]
        [Min(0.1f)]
        [SerializeField] private float speed = 2.5f;
        [Tooltip("На какой уступ ползун забирается, в блоках. При 1.1 стена в два блока его останавливает.")]
        [Min(0f)]
        [SerializeField] private float maxStep = 1.1f;
        [Tooltip("Радиус тела: на столько вперёд ползун проверяет, можно ли пройти.")]
        [Min(0.05f)]
        [SerializeField] private float bodyRadius = 0.3f;
        [Tooltip("С какой скоростью тело поднимается или опускается к земле, метров в секунду.")]
        [Min(0.1f)]
        [SerializeField] private float climbSpeed = 5f;

        [Header("Огонь")]
        [Tooltip("С какого расстояния от центра огня ползун начинает его гасить.")]
        [Min(0.1f)]
        [SerializeField] private float reach = 1.3f;
        [Tooltip("Сколько поленьев в секунду гасит один ползун у огня.")]
        [Min(0f)]
        [SerializeField] private float drainPerSecond = 0.2f;

        [Header("Здоровье")]
        [Tooltip("Сколько ударов выдерживает (удар игрока снимает 1).")]
        [Min(0.1f)]
        [SerializeField] private float health = 2f;
        [Tooltip("Сколько секунд ползун растворяется после смерти или с рассветом.")]
        [Min(0f)]
        [SerializeField] private float dissolveSeconds = 0.6f;

        [Header("Появление (время суток, секунды)")]
        [Tooltip("Через сколько секунд ночи выходит первый ползун.")]
        [Min(0f)]
        [SerializeField] private float firstSpawnDelay = 10f;
        [Tooltip("Раз во сколько секунд ночи выходит следующий.")]
        [Min(0.5f)]
        [SerializeField] private float spawnInterval = 20f;
        [Tooltip("На сколько чаще они выходят с каждой следующей ночью: 0.25 — вторая ночь на четверть чаще первой.")]
        [Min(0f)]
        [SerializeField] private float extraPerNight = 0.25f;
        [Tooltip("Больше живых ползунов одновременно не бывает.")]
        [Min(1)]
        [SerializeField] private int maxAlive = 6;
        [Tooltip("Ближе всего к границе света, где ползун появляется, в метрах за ней.")]
        [Min(0.5f)]
        [SerializeField] private float spawnDistanceMin = 6f;
        [Tooltip("Дальше всего от границы света, где ползун появляется, в метрах за ней.")]
        [Min(0.5f)]
        [SerializeField] private float spawnDistanceMax = 12f;

        public float Speed => speed;
        public float MaxStep => maxStep;
        public float BodyRadius => bodyRadius;
        public float ClimbSpeed => climbSpeed;
        public float Reach => reach;
        public float DrainPerSecond => drainPerSecond;
        public float Health => health;
        public float DissolveSeconds => dissolveSeconds;
        public float FirstSpawnDelay => firstSpawnDelay;
        public int MaxAlive => maxAlive;
        public float SpawnDistanceMin => spawnDistanceMin;
        public float SpawnDistanceMax => Mathf.Max(spawnDistanceMin, spawnDistanceMax);

        /// <summary>Промежуток между ползунами в ночь номер night (с 1): каждая следующая ночь чуть опаснее.</summary>
        public float SpawnInterval(int night) => spawnInterval / (1f + extraPerNight * Mathf.Max(0, night - 1));
    }
}
