using System;
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
        [Header("Топливо")]
        [Tooltip("Id ресурса, которым рабочие кормят огонь.")]
        [SerializeField] private string fuelItemId = "wood";
        [Tooltip("Сколько поленьев рабочий несёт в огонь за одну ходку, самое большее.")]
        [Range(1, 10)]
        [SerializeField] private int fuelPerTrip = 2;

        [Header("Поиск задач")]
        [Tooltip("Как часто свободный рабочий ищет задачу, в секундах.")]
        [Range(0.05f, 5f)]
        [SerializeField] private float idleSearchSeconds = 0.5f;

        [Header("Вид")]
        [Tooltip("Рост рабочего, блоков. Дверь — 2 блока в высоту.")]
        [Range(0.5f, 1.99f)]
        [SerializeField] private float height = 1.9f;
        [Tooltip("Цвет откинутого капюшона и пелерины, если у роли он не задан ниже.")]
        [SerializeField] private Color hoodColor = new(0.26f, 0.36f, 0.3f);
        [Tooltip("Цвет капюшона и пелерины по ролям: по нему роль видно издалека.")]
        [SerializeField] private RoleHood[] roleHoods =
        {
            new(WorkerRole.Free, new Color(0.6f, 0.56f, 0.46f)),
            new(WorkerRole.Woodcutter, new Color(0.26f, 0.36f, 0.3f)),
            new(WorkerRole.Digger, new Color(0.62f, 0.47f, 0.2f)),
            new(WorkerRole.Builder, new Color(0.24f, 0.34f, 0.5f)),
            new(WorkerRole.Stoker, new Color(0.78f, 0.42f, 0.12f)),
            new(WorkerRole.Swordsman, new Color(0.5f, 0.15f, 0.15f)),
            new(WorkerRole.Archer, new Color(0.5f, 0.15f, 0.15f)),
            new(WorkerRole.Spearman, new Color(0.5f, 0.15f, 0.15f)),
        };
        [Tooltip("Цвета кожи: каждому рабочему достаётся один по его номеру.")]
        [SerializeField] private Color[] skinColors = { new(0.910f, 0.753f, 0.604f), new(0.851f, 0.655f, 0.486f), new(0.769f, 0.541f, 0.376f), new(0.941f, 0.812f, 0.682f) };
        [Tooltip("Цвета волос, бровей и бороды.")]
        [SerializeField] private Color[] hairColors = { new(0.227f, 0.165f, 0.110f), new(0.420f, 0.290f, 0.165f), new(0.627f, 0.478f, 0.282f), new(0.165f, 0.149f, 0.133f), new(0.541f, 0.518f, 0.471f) };
        [Tooltip("Цвета рубахи.")]
        [SerializeField] private Color[] tunicColors = { new(0.541f, 0.353f, 0.212f), new(0.478f, 0.384f, 0.282f), new(0.431f, 0.314f, 0.251f), new(0.549f, 0.416f, 0.267f) };
        [Tooltip("Доля рабочих с бородой.")]
        [Range(0f, 1f)]
        [SerializeField] private float beardChance = 0.5f;

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
        public string FuelItemId => string.IsNullOrEmpty(fuelItemId) ? "wood" : fuelItemId;
        public int FuelPerTrip => fuelPerTrip;
        public float Height => height;

        /// <summary>Облик рабочего с номером index: один номер всегда даёт один облик. Роль — Свободный.</summary>
        public WorkerLook LookFor(int index) => LookFor(index, WorkerRole.Free);

        /// <summary>Облик рабочего с номером index в роли role: лицо от номера, капюшон и снаряжение от роли.</summary>
        public WorkerLook LookFor(int index, WorkerRole role) =>
            WorkerLook.Pick(index, skinColors, hairColors, tunicColors, hoodColor, beardChance).WithRole(role, HoodFor(role));

        /// <summary>Цвет капюшона роли; если для роли нет записи — общий hoodColor.</summary>
        public Color HoodFor(WorkerRole role)
        {
            if (roleHoods != null)
            {
                foreach (var entry in roleHoods)
                {
                    if (entry.Role == role)
                        return entry.Hood;
                }
            }
            return hoodColor;
        }

        [Serializable]
        private struct RoleHood
        {
            [SerializeField] private WorkerRole role;
            [SerializeField] private Color hood;

            public RoleHood(WorkerRole role, Color hood)
            {
                this.role = role;
                this.hood = hood;
            }

            public WorkerRole Role => role;
            public Color Hood => hood;
        }
    }
}
