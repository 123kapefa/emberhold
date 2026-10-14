using System;
using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Карта кампании ассетом: горный хребет, котловины локаций и ущелья между ними. Подключается в
    /// <see cref="TerrainSettings"/>, тогда рельеф строится по карте, а не по одному профилю с зонами.
    /// Координаты в блоках сетки мира: X на восток, Z на север, (0, 0) — юго-западный угол.
    /// </summary>
    [CreateAssetMenu(fileName = "CampaignMap", menuName = "Emberhold/World/Campaign Map")]
    public sealed class CampaignMap : ScriptableObject
    {
        [Header("Хребет")]
        [Tooltip("Самые низкие места хребта вне локаций и ущелий, в блоках.")]
        [SerializeField] private float ridgeMinHeight = 36f;
        [Tooltip("Самые высокие гребни, в блоках.")]
        [SerializeField] private float ridgeMaxHeight = 46f;
        [Tooltip("Ниже этой высоты склоны хребта и ущелий травяные, выше — камень профиля хребта. 0 — без травы.")]
        [SerializeField] private float ridgeGrassLine = 30f;
        [Tooltip("Горы хребта из скалы, которая не копается: под травой склонов скала до самого дна.")]
        [SerializeField] private bool ridgeCliff = true;
        [Tooltip("Профиль хребта и склонов ущелий: террасы, скалы, материалы, лес. Пусто — как у долины по умолчанию.")]
        [SerializeField] private TerrainProfile ridgeProfile;

        [Header("Поверхность")]
        [Tooltip("Гладкая земля: склоны любой крутизны по высотам карты, без террас; нетронутая земля рисуется сплошной поверхностью. Выкл — скосы до 45° и террасы.")]
        [SerializeField] private bool smoothSurface = true;

        [SerializeField] private List<Location> locations = new();
        [SerializeField] private List<Gorge> gorges = new();

        [Header("Высокие горы")]
        [Tooltip("Пики и гребни выше хребта. Не помещаются в мир — срезаются по его высоте.")]
        [SerializeField] private List<Peak> peaks = new();
        [Tooltip("Выше этой высоты (в блоках) скала гор под снегом; граница гуляет по шуму, на крутых гранях снега нет.")]
        [SerializeField] private float snowLine = 54f;
        [Tooltip("Граница леса в блоках: выше неё деревья не растут; граница гуляет по шуму на несколько блоков.")]
        [SerializeField] private float treeLine = 46f;

        public IReadOnlyList<Location> Locations => locations;
        public IReadOnlyList<Gorge> Gorges => gorges;
        public IReadOnlyList<Peak> Peaks => peaks;

        /// <summary>Снеговая линия в блоках: выше неё скала гор рисуется под снегом.</summary>
        public float SnowLine => snowLine;

        /// <summary>Граница леса в блоках: выше неё деревья не растут.</summary>
        public float TreeLine => treeLine;

        /// <summary>
        /// Схема для генератора. Ущелье с неизвестной локацией на конце пропускается с предупреждением.
        /// </summary>
        public CampaignLayout ToLayout()
        {
            var layout = new CampaignLayout
            {
                SmoothSurface = smoothSurface,
                Ridge = new CampaignRidge
                {
                    MinHeight = ridgeMinHeight,
                    MaxHeight = Mathf.Max(ridgeMinHeight, ridgeMaxHeight),
                    GrassLine = ridgeGrassLine,
                    Cliff = ridgeCliff,
                    Profile = ridgeProfile != null ? ridgeProfile.Parameters : TerrainProfileParameters.Valley,
                    Forest = ridgeProfile != null ? ridgeProfile.Forest : null,
                    GrassTint = ridgeProfile != null ? ridgeProfile.GrassTint : Color.white,
                    GrassKind = ridgeProfile != null ? ridgeProfile.GrassKind : GrassKind.Meadow,
                },
            };
            var indices = new Dictionary<string, int>();
            foreach (var location in locations)
            {
                indices[location.id] = layout.Locations.Count;
                layout.Locations.Add(location.ToShape());
            }
            foreach (var gorge in gorges)
            {
                if (!indices.TryGetValue(gorge.from, out var from) || !indices.TryGetValue(gorge.to, out var to))
                {
                    Debug.LogWarning($"[CampaignMap] Ущелье «{gorge.name}»: нет локации «{gorge.from}» или «{gorge.to}».", this);
                    continue;
                }
                layout.Gorges.Add(gorge.ToShape(from, to));
            }
            foreach (var peak in peaks)
                if (peak.points != null && peak.points.Length > 0)
                    layout.Peaks.Add(peak.ToShape());
            return layout;
        }

        [Serializable]
        public sealed class Peak
        {
            [Tooltip("Название, как в дизайн-документе.")]
            public string name;
            [Tooltip("Вершина (одна точка) или линия гребня (несколько) в блоках сетки мира (X — восток, Y здесь — Z, север).")]
            public Vector2[] points = Array.Empty<Vector2>();
            [Tooltip("Высота вершины в блоках.")]
            public float height = 64f;
            [Tooltip("Радиус подножия в блоках: на таком расстоянии от вершины гора сходит в хребет.")]
            [Min(1f)]
            public float radius = 35f;
            [Tooltip("Зубцы гребня: 0 — ровный, больше — острые пики с седловинами между ними.")]
            [Range(0f, 1f)]
            public float teeth;

            public CampaignPeakShape ToShape() => new()
            {
                Name = name,
                Points = points,
                Height = height,
                Radius = Mathf.Max(radius, 1f),
                Teeth = teeth,
            };
        }

        [Serializable]
        public sealed class Location
        {
            [Tooltip("Короткий ключ латиницей: по нему ущелья ссылаются на локацию.")]
            public string id;
            [Tooltip("Название, как в дизайн-документе.")]
            public string name;
            [Tooltip("Центр в блоках сетки мира (X — восток, Y здесь — Z, север).")]
            public Vector2 center;
            [Tooltip("Полуоси основного эллипса по X и Z в блоках. Края добавляют ещё несколько эллипсов поменьше.")]
            public Vector2 radius = new(30f, 30f);
            [Tooltip("Профиль местности дна: форма (гора, чаша), шум, террасы, материалы, лес.")]
            public TerrainProfile profile;
            [Tooltip("Высота дна в блоках (заменяет базовую высоту профиля).")]
            public float floorHeight = 12f;
            [Tooltip("На сколько блоков дно поднимается от одного края локации к другому.")]
            public float rise;
            [Tooltip("Куда поднимается дно, градусы: 0 — на север, 90 — на восток.")]
            public float riseDirection;
            [Tooltip("Неровность краёв: 0 — ровный эллипс, 1 — как в наброске, больше — ещё рваннее.")]
            [Range(0f, 2f)]
            public float roughness = 1f;
            [Tooltip("Свой оттенок травы; выключено — оттенок профиля.")]
            public bool overrideGrassTint;
            [Tooltip("Оттенок травы локации (умножается на текстуру травы, можно больше 1 — ярче); альфа — насыщенность: 1 — как есть, меньше — блёклее.")]
            [ColorUsage(true, true)]
            public Color grassTint = Color.white;

            [Tooltip("Свой вид травы; выключено — вид профиля.")]
            public bool overrideGrassKind;
            [Tooltip("Вид травы локации: своя текстура травы (Топи, Мёртвый город, Горы), остальные — луговая.")]
            public GrassKind grassKind;

            /// <summary>Оттенок травы: свой, если задан, иначе профиля, иначе нейтральный.</summary>
            public Color GrassTint => overrideGrassTint ? grassTint : profile != null ? profile.GrassTint : Color.white;

            /// <summary>Вид травы: свой, если задан, иначе профиля, иначе луговая.</summary>
            public GrassKind GrassKind => overrideGrassKind ? grassKind : profile != null ? profile.GrassKind : GrassKind.Meadow;

            public CampaignLocationShape ToShape() => new()
            {
                Name = name,
                Center = center,
                Radius = Vector2.Max(radius, Vector2.one),
                FloorHeight = floorHeight,
                Rise = rise,
                RiseDirection = riseDirection,
                Roughness = roughness,
                Profile = profile != null ? profile.Parameters : FlatFloor,
                Forest = profile != null ? profile.Forest : null,
                GrassTint = GrassTint,
                GrassKind = GrassKind,
            };
        }

        [Serializable]
        public sealed class Gorge
        {
            [Tooltip("Название, если у ущелья есть своё (Карниз); иначе можно оставить пустым.")]
            public string name;
            [Tooltip("Ключ локации, откуда идёт ущелье.")]
            public string from;
            [Tooltip("Ключ локации, куда идёт ущелье.")]
            public string to;
            [Tooltip("Промежуточные точки пути в блоках, без центров локаций.")]
            public Vector2[] waypoints = Array.Empty<Vector2>();
            [Tooltip("Средняя ширина дна у начала и у конца, в блоках. По пути ширина гуляет от половины до полутора.")]
            public float widthStart = 12f;
            public float widthEnd = 12f;
            [Tooltip("Высота дна у начала и у конца, в блоках.")]
            public float heightStart = 12f;
            public float heightEnd = 12f;
            [Tooltip("Подъём дна в середине пути (перевал), в блоках.")]
            public float bump;
            [Tooltip("Насколько линия уходит в сторону между точками, в блоках.")]
            [Min(0f)]
            public float wiggle = 18f;

            public CampaignGorgeShape ToShape(int fromIndex, int toIndex) => new()
            {
                Name = string.IsNullOrEmpty(name) ? $"{from} → {to}" : name,
                From = fromIndex,
                To = toIndex,
                Waypoints = waypoints ?? Array.Empty<Vector2>(),
                WidthStart = Mathf.Max(widthStart, 1f),
                WidthEnd = Mathf.Max(widthEnd, 1f),
                HeightStart = heightStart,
                HeightEnd = heightEnd,
                Bump = bump,
                Wiggle = Mathf.Max(wiggle, 0f),
            };
        }

        /// <summary>Дно без профиля: ровное, с лёгким шумом, трава.</summary>
        private static TerrainProfileParameters FlatFloor
        {
            get
            {
                var p = TerrainProfileParameters.Valley;
                p.Shape = TerrainShape.Flat;
                p.NoiseAmplitude = 1.5f;
                p.TerraceHeight = 0f;
                p.CliffAmount = 0f;
                return p;
            }
        }

        private void OnValidate() => ridgeMaxHeight = Mathf.Max(ridgeMinHeight, ridgeMaxHeight);
    }
}
