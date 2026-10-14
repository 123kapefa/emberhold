using System.Collections.Generic;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Схема карты кампании обычными данными, без ассетов: хребет, локации и ущелья между ними.
    /// Генератор (<see cref="CampaignHeights"/>) и тесты работают с ней; из ассета её собирает <see cref="CampaignMap"/>.
    /// Координаты — блоки сетки мира: X на восток, Z на север.
    /// </summary>
    public sealed class CampaignLayout
    {
        /// <summary>Горный хребет: всё, что не локация и не ущелье.</summary>
        public CampaignRidge Ridge = CampaignRidge.Default;

        /// <summary>
        /// Гладкая поверхность (<see cref="Voxels.TerrainSurface"/>): склоны любой крутизны по высотам карты,
        /// без террас; нетронутая земля рисуется сплошной поверхностью. false — склоны из скосов до 45° и террасы.
        /// </summary>
        public bool SmoothSurface;

        public readonly List<CampaignLocationShape> Locations = new();
        public readonly List<CampaignGorgeShape> Gorges = new();

        /// <summary>Высокие горы над хребтом: отдельные пики и гребни выше <see cref="CampaignRidge.MaxHeight"/>.</summary>
        public readonly List<CampaignPeakShape> Peaks = new();
    }

    /// <summary>Горный хребет вокруг локаций: высота гребней, граница травы и профиль (террасы, скалы, материалы, лес).</summary>
    public struct CampaignRidge
    {
        /// <summary>Самые низкие места хребта в блоках.</summary>
        public float MinHeight;

        /// <summary>Самые высокие гребни в блоках.</summary>
        public float MaxHeight;

        /// <summary>
        /// Ниже этой высоты (в блоках) склоны хребта и ущелий покрыты травой, выше — материалами профиля
        /// хребта (камень). Граница гуляет по шуму на несколько блоков. 0 — трава не растёт.
        /// </summary>
        public float GrassLine;

        /// <summary>
        /// Горы хребта из скалы (<see cref="TerrainMaterials.Mountain"/>): под травой и землёй склонов — скала до дна,
        /// каменные места — скала целиком. Скала не копается, поэтому хребет и стены котловин не прорыть.
        /// </summary>
        public bool Cliff;

        public TerrainProfileParameters Profile;
        public ForestMix Forest;
        /// <summary>Оттенок травы на хребте и его склонах (rgb — множитель, a — насыщенность); null — нейтральный.</summary>
        public Color? GrassTint;
        /// <summary>Вид травы на хребте и его склонах.</summary>
        public GrassKind GrassKind;

        public static CampaignRidge Default => new()
        {
            MinHeight = 36f,
            MaxHeight = 46f,
            GrassLine = 0f,
            Profile = TerrainProfileParameters.Valley,
        };
    }

    /// <summary>
    /// Локация: котловина в хребте. Основа — эллипс с полуосями <see cref="Radius"/>, к нему прилипают
    /// ещё несколько эллипсов поменьше, а граница искажается шумом, поэтому края неровные.
    /// Дно на <see cref="FloorHeight"/>, форма дна (гора, чаша), шум, террасы, материалы и лес — из профиля.
    /// </summary>
    public struct CampaignLocationShape
    {
        public string Name;

        /// <summary>Центр в блоках сетки мира.</summary>
        public Vector2 Center;

        /// <summary>Полуоси основного эллипса по X и Z в блоках.</summary>
        public Vector2 Radius;

        /// <summary>Высота дна в блоках; заменяет базовую высоту профиля.</summary>
        public float FloorHeight;

        /// <summary>На сколько блоков дно поднимается от одного края локации к другому.</summary>
        public float Rise;

        /// <summary>Куда поднимается дно, в градусах: 0 — на север (+Z), 90 — на восток (+X).</summary>
        public float RiseDirection;

        /// <summary>Неровность краёв: 0 — ровный эллипс, 1 — как в наброске карты, больше — ещё рваннее.</summary>
        public float Roughness;

        public TerrainProfileParameters Profile;
        public ForestMix Forest;
        /// <summary>Оттенок травы локации (rgb — множитель, a — насыщенность); null — нейтральный.</summary>
        public Color? GrassTint;
        /// <summary>Вид травы локации: своя текстура травы.</summary>
        public GrassKind GrassKind;
    }

    /// <summary>
    /// Ущелье между двумя локациями: проходит через центры локаций и промежуточные точки. Линия петляет:
    /// между точками добавляются боковые отклонения до <see cref="Wiggle"/> блоков, а ширина дна
    /// меняется по шуму вокруг заданной. Дно идёт от высоты на одном конце к высоте на другом.
    /// </summary>
    public struct CampaignGorgeShape
    {
        public string Name;

        /// <summary>Номера локаций в <see cref="CampaignLayout.Locations"/>, откуда и куда идёт ущелье.</summary>
        public int From;
        public int To;

        /// <summary>Промежуточные точки пути в блоках сетки мира, без центров локаций.</summary>
        public Vector2[] Waypoints;

        /// <summary>Ширина дна у начала и у конца в блоках (средняя: местами в полтора раза шире, местами вдвое уже).</summary>
        public float WidthStart;
        public float WidthEnd;

        /// <summary>Высота дна у начала и у конца в блоках.</summary>
        public float HeightStart;
        public float HeightEnd;

        /// <summary>Подъём дна в середине пути (перевал) в блоках, 0 — без подъёма.</summary>
        public float Bump;

        /// <summary>Насколько линия уходит в сторону между точками, в блоках.</summary>
        public float Wiggle;
    }

    /// <summary>
    /// Высокая гора над хребтом: одна точка — острый пик, несколько — гребень по ломаной через них.
    /// Склоны вогнутые, к вершине сходятся остро, по граням — скальные выступы. Гора поднимает только хребет:
    /// котловины локаций и ущелья вырезаются из неё так же, как из хребта, и дно у них остаётся своим.
    /// </summary>
    public struct CampaignPeakShape
    {
        public string Name;

        /// <summary>Вершина или линия гребня в блоках сетки мира.</summary>
        public Vector2[] Points;

        /// <summary>Высота вершины в блоках. Вдоль гребня высота гуляет на десятую часть вниз.</summary>
        public float Height;

        /// <summary>Радиус подножия в блоках: на таком расстоянии от вершины гора сходит в хребет.</summary>
        public float Radius;

        /// <summary>
        /// Неровность кромки гребня, 0..1: 0 — кромка почти ровная, без жандармов; больше — выше острые жандармы
        /// со сколами (при 1 кромка между ними до 16 блоков ниже их вершин) и глубже седловины.
        /// </summary>
        public float Teeth;
    }
}
