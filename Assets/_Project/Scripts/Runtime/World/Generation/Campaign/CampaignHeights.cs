using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Высоты карты кампании в углах сетки блоков, до террас и квантования. Хребет по всей карте, из него
    /// плавным минимумом вырезаются котловины локаций и ущелья, поэтому стыки скруглены. Для каждого угла
    /// запоминается, чья это земля (материалы и лес) и от какой высоты считать террасы склонов.
    /// Строки считаются параллельно: всё здесь — чистая арифметика без Unity API.
    /// </summary>
    public sealed class CampaignHeights
    {
        /// <summary>Номер хозяина угла: хребет. Локация i — номер i + 1.</summary>
        public const int Ridge = 0;

        // Насколько глубоко внутрь соседней поверхности уже считается «своей» (в блоках от края дна).
        private const float OwnMargin = 2f;
        // Дальше этого от края локация или ущелье на высоту не влияют.
        private const float LocationReach = 60f;
        private const float GorgeReach = 50f;
        // Самая крутая стенка высокой горы над краем дна котловины или ущелья, блоков на блок.
        private const float PeakWall = 2f;
        // Насколько пологие стенки массива бывают у котловин: от PeakWallLow до PeakWall блоков на блок по шуму.
        private const float PeakWallLow = 1.1f;
        // Стенка верхней части высокой горы над котловиной: почти отвесная скала, иначе котловины
        // рядом срезали бы вершину.
        private const float PeakWallHigh = 3.5f;
        // Горная цепь хребта: частота гребней (на блок), насколько они петляют (в блоках) и полоса от края карты,
        // где гребни встают до высоты вершин цепи.
        private const float ChainFrequency = 0.009f;
        private const float ChainWarp = 40f;
        private const float ChainBandNear = -20f;
        private const float ChainBandFar = 70f;
        // У самого края (до ChainLiftWidth блоков, граница гуляет) долины между гребнями поднимаются: дно долины
        // встаёт на долю до ChainLift от высоты гребней (местами меньше), чтобы долины цепи не выходили низиной
        // к обрезу мира, а заканчивались седловинами.
        private const float ChainLift = 0.35f;
        private const float ChainLiftWidth = 40f;
        // Вершины цепи вдоль краёв: шаг вдоль края, отступ от края (от RingInsetMin до +RingInsetSpread) и номер,
        // с которого считаются их зёрна шума (после пиков карты).
        private const float RingSpacing = 36f;
        private const float RingInsetMin = 16f;
        private const float RingInsetSpread = 34f;
        private const int RingIndex = 100;
        // У края карты котловины и ущелья не срезают горы: до RimFree блоков от края (граница гуляет) вершины
        // встают целиком, дальше до RimFade стенка срезает их всё сильнее; само дно (RimFloor блоков от его края)
        // остаётся своим. Так котловина у края упирается в массив, а не выходит низиной к обрезу мира.
        private const float RimFree = 30f;
        private const float RimFade = 70f;
        private const float RimFloor = 4f;
        // Полоса у самого края, где котловина закрыта бортом (граница гуляет).
        private const float RimFill = 16f;
        // На сколько блоков скруглён верх стенки массива над котловиной.
        private const float PeakWallRound = 8f;
        // Насколько быстрее рёбер опускаются грани пирамиды: посередине грани гора уже в (1 + PeakFaceDrop) раз.
        private const float PeakFaceDrop = 1f;
        // Мелкие рёбра и кулуары на гранях пирамиды, доля радиуса.
        private const float PeakFlutes = 0.12f;
        // Рёбра и грани проступают уже с этой доли радиуса от вершины: меньше — острее верх, без купола.
        private const float PeakSharpTop = 0.15f;
        // Профиль склона: больше 1 — склон вогнутый, у подножия положе.
        private const float PeakProfile = 1.15f;
        // Отроги от гребня вниз по склонам: через сколько блоков и насколько гребень между ними уже.
        private const float SpurSpacing = 18f;
        private const float SpurDrop = 0.45f;
        // Как часто по кромке гребня крупные подъёмы и седловины, на блок пути.
        private const float CrestFrequency = 0.1f;
        // Жандармы на кромке: через сколько блоков, насколько ниже вершин жандарма кромка между ними,
        // глубина сколов на макушке и насколько гребень уже между жандармами (всё при Teeth = 1).
        private const float GendarmeSpacing = 10f;
        private const float GendarmeDepth = 16f;
        private const float GendarmeChips = 2.5f;
        private const float GendarmePinch = 0.7f;

        /// <summary>Высота в блоках в углу (x, z); размер (ширина + 1) × (длина + 1).</summary>
        public readonly float[,] Heights;

        /// <summary>Чья земля в углу: <see cref="Ridge"/> или номер локации + 1. Ущелье отдаёт углы ближнему концу.</summary>
        public readonly int[,] Owners;

        /// <summary>По какому профилю класть террасы в углу: хребет или локация (номер, как у <see cref="Owners"/>).</summary>
        public readonly int[,] TerraceOwners;

        /// <summary>От какой высоты считать террасы: дно ближайшей локации или ущелья, чтобы у дна не было канавы.</summary>
        public readonly float[,] TerraceBases;

        /// <summary>
        /// Оттенок травы в углу: смесь оттенков локаций по близости (внутри дна — свой, за краем за
        /// <see cref="TintBlend"/> блоков сходит на нет), в ущелье — плавно от оттенка начала к оттенку конца,
        /// остаток — оттенок хребта. Поэтому на стыках, в ущельях и на склонах переход плавный.
        /// </summary>
        public readonly Color[,] GrassTints;

        /// <summary>
        /// Смесь видов травы в углу (<see cref="Voxels.GrassKinds.Weight"/>): смешивается так же, как <see cref="GrassTints"/>,
        /// поэтому трава одной локации плавно переходит в траву другой.
        /// </summary>
        public readonly Color[,] GrassKinds;

        /// <summary>За сколько блоков от края дна локации или ущелья их оттенок сходит на нет.</summary>
        public const float TintBlend = 16f;

        private CampaignHeights(int sizeX, int sizeZ)
        {
            Heights = new float[sizeX + 1, sizeZ + 1];
            Owners = new int[sizeX + 1, sizeZ + 1];
            TerraceOwners = new int[sizeX + 1, sizeZ + 1];
            TerraceBases = new float[sizeX + 1, sizeZ + 1];
            GrassTints = new Color[sizeX + 1, sizeZ + 1];
            GrassKinds = new Color[sizeX + 1, sizeZ + 1];
        }

        /// <summary>Чья земля в столбце (x, z): хозяин его юго-западного угла.</summary>
        public int ColumnOwner(int x, int z) => Owners[x, z];

        /// <summary>Посчитать высоты для мира sizeX × sizeZ блоков; maxHeight — потолок в блоках.</summary>
        public static CampaignHeights Build(CampaignLayout layout, int seed, int sizeX, int sizeZ, float maxHeight)
        {
            var result = new CampaignHeights(sizeX, sizeZ);
            var noise = new CampaignNoise(seed);
            var locations = new PreparedLocation[layout.Locations.Count];
            for (var i = 0; i < locations.Length; i++)
                locations[i] = new PreparedLocation(layout.Locations[i], i, noise);
            var gorges = new PreparedGorge[layout.Gorges.Count];
            for (var i = 0; i < gorges.Length; i++)
                gorges[i] = new PreparedGorge(layout, layout.Gorges[i], i, noise);
            var ring = layout.Ridge.PeakHeight > layout.Ridge.MaxHeight
                ? RingPeaks(layout.Ridge, sizeX, sizeZ, noise)
                : new List<CampaignPeakShape>();
            var peaks = new PreparedPeak[layout.Peaks.Count + ring.Count];
            for (var i = 0; i < layout.Peaks.Count; i++)
                peaks[i] = new PreparedPeak(layout.Peaks[i], i);
            for (var i = 0; i < ring.Count; i++)
                peaks[layout.Peaks.Count + i] = new PreparedPeak(ring[i], RingIndex + i);

            Parallel.For(0, sizeZ + 1, z =>
            {
                var floors = new float[locations.Length];
                var distances = new float[locations.Length];
                for (var x = 0; x <= sizeX; x++)
                    result.Compute(layout.Ridge, layout.SmoothSurface, locations, gorges, peaks, noise, x, z, sizeX, sizeZ, maxHeight,
                        floors, distances);
            });
            return result;
        }

        private void Compute(CampaignRidge ridge, bool smooth, PreparedLocation[] locations, PreparedGorge[] gorges, PreparedPeak[] peaks,
            CampaignNoise noise,
            int xi, int zi, int sizeX, int sizeZ, float maxHeight, float[] floors, float[] distances)
        {
            float x = xi, z = zi;
            // Хребет: гребни по «хребтовому» шуму, у края мира чуть выше, чтобы карта была закрыта.
            var edge = Math.Min(Math.Min(x, z), Math.Min(sizeX - x, sizeZ - z));
            float h;
            if (ridge.PeakHeight > ridge.MaxHeight)
            {
                h = ChainHeight(ridge, noise, x, z, edge) + (1f - CampaignNoise.SmoothStep(0f, 24f, edge)) * 4f;
                h = Math.Max(ridge.MinHeight, h);
            }
            else
            {
                var crest = CampaignNoise.Clamp01((noise.Ridged(x * 0.016f, z * 0.016f) - 0.12f) / 0.72f);
                h = CampaignNoise.Lerp(ridge.MinHeight, ridge.MaxHeight, crest)
                    + (noise.Fbm(x * 0.06f, z * 0.06f) - 0.5f) * 3f
                    + (1f - CampaignNoise.SmoothStep(0f, 24f, edge)) * 4f;
                h = Math.Max(ridge.MinHeight, Math.Min(ridge.MaxHeight, h));
            }
            var ridgeHeight = h;
            var rim = ridge.PeakHeight > ridge.MaxHeight
                ? 1f - CampaignNoise.SmoothStep(RimFree, RimFade,
                    edge + (noise.Fbm(x * 0.02f + 19f, z * 0.02f + 5f, 2) - 0.5f) * 30f)
                : 0f;

            // Оттенок: сумма оттенков с весами и сумма весов; остаток до 1 — хребет.
            var tint = new Color(0f, 0f, 0f, 0f);
            var kind = new Color(0f, 0f, 0f, 0f);
            var tintWeight = 0f;

            var owner = Ridge;
            var terraceOwner = Ridge;
            var terraceBase = 0f;
            var nearest = OwnMargin;
            var baseDistance = float.MaxValue;

            // Общее искажение координат: крупное (границы гуляют) и мелкое (рваный край).
            var ux = (noise.Fbm(x * 0.02f, z * 0.02f + 40f, 3) - 0.5f) * 2f;
            var uz = (noise.Fbm(x * 0.02f + 70f, z * 0.02f, 3) - 0.5f) * 2f;
            var vx = (noise.Value(x * 0.07f + 3f, z * 0.07f) - 0.5f) * 2f;
            var vz = (noise.Value(x * 0.07f, z * 0.07f + 9f) - 0.5f) * 2f;

            for (var i = 0; i < locations.Length; i++)
            {
                distances[i] = float.MaxValue;
                ref var location = ref locations[i];
                if (!location.Near(x, z))
                    continue;
                var d = location.Distance(x, z, ux, uz, vx, vz);
                if (d > LocationReach)
                    continue;
                // Крутизна стенок котловины, блоков на блок. У гладкой поверхности разброс шире:
                // пологие холмы и местами крутые скальные участки.
                var wall = smooth
                    ? 0.3f + 0.7f * noise.Fbm(x * 0.04f + 5f, z * 0.04f, 3)
                      + 1.6f * CampaignNoise.SmoothStep(0.55f, 0.75f, noise.Fbm(x * 0.025f + 33f, z * 0.025f, 3))
                    : 0.75f + 0.7f * noise.Fbm(x * 0.05f + 5f, z * 0.05f, 3);
                distances[i] = d;
                floors[i] = location.Floor(x, z, noise);
                var w = 1f - CampaignNoise.SmoothStep(0f, TintBlend, d);
                tint += w * (location.Shape.GrassTint ?? Color.white);
                kind += w * Voxels.GrassKinds.Weight(location.Shape.GrassKind);
                tintWeight += w;
                h = CampaignNoise.SmoothMin(h, floors[i] + Math.Max(d, 0f) * wall, 5f);
                if (d < nearest)
                {
                    nearest = d;
                    owner = terraceOwner = i + 1;
                }
                if (d < baseDistance)
                {
                    baseDistance = d;
                    terraceBase = location.Shape.FloorHeight;
                }
            }

            for (var i = 0; i < gorges.Length; i++)
            {
                ref var gorge = ref gorges[i];
                if (!gorge.Near(x, z))
                    continue;
                gorge.Distance(x + ux * 9f + vx * 4f, z + uz * 9f + vz * 4f, out var distance, out var t);
                var halfWidth = CampaignNoise.Lerp(gorge.Shape.WidthStart, gorge.Shape.WidthEnd, t) * 0.5f
                                * (0.45f + 1.1f * noise.Fbm(x * 0.035f + 11f, z * 0.035f, 3));
                var d = distance - halfWidth;
                if (d > GorgeReach)
                    continue;
                var floor = gorge.Floor(t) + (noise.Fbm(x * 0.06f, z * 0.06f, 3) - 0.5f) * 1.5f;
                // Внутри своих локаций ущелье не прорезает дно: иначе через гору или холмы к центру шла бы траншея.
                var end = t < 0.5f ? gorge.Shape.From : gorge.Shape.To;
                var into = CampaignNoise.SmoothStep(0f, 10f, -distances[end]);
                if (into >= 1f)
                    continue;
                if (into > 0f)
                    floor = Math.Max(floor, CampaignNoise.Lerp(floor, floors[end], into));
                var wall = 0.8f + 0.5f * noise.Fbm(x * 0.07f, z * 0.07f, 3);
                h = CampaignNoise.SmoothMin(h, floor + Math.Max(d, 0f) * wall, 4f);
                // Ущелье: оттенок плавно от локации начала к локации конца по середине пути.
                var gw = 1f - CampaignNoise.SmoothStep(0f, TintBlend, d);
                var fromTint = locations[gorge.Shape.From].Shape.GrassTint ?? Color.white;
                var toTint = locations[gorge.Shape.To].Shape.GrassTint ?? Color.white;
                var along = CampaignNoise.SmoothStep(0.2f, 0.8f, t);
                tint += gw * Color.LerpUnclamped(fromTint, toTint, along);
                kind += gw * Color.LerpUnclamped(Voxels.GrassKinds.Weight(locations[gorge.Shape.From].Shape.GrassKind),
                    Voxels.GrassKinds.Weight(locations[gorge.Shape.To].Shape.GrassKind), along);
                tintWeight += gw;
                if (d < nearest)
                {
                    nearest = d;
                    owner = (t < 0.5f ? gorge.Shape.From : gorge.Shape.To) + 1;
                    terraceOwner = Ridge;
                }
                if (d < baseDistance)
                {
                    baseDistance = d;
                    terraceBase = gorge.Floor(t);
                }
            }

            // Высокие горы поверх уже вырезанных котловин и ущелий: пологие стенки котловины не срезают пик,
            // но от края ближайшего дна гора поднимается не круче PeakWall (местами положе), поэтому дно остаётся своим.
            var cut = h;
            if (peaks.Length > 0)
            {
                var peak = ridgeHeight;
                foreach (var p in peaks)
                    peak = Math.Max(peak, p.Height(x, z, ridgeHeight, noise));
                if (peak > h)
                {
                    // Стенка над дном то круче, то положе (предгорья), и её верх скруглён, чтобы не было бортика.
                    // Пологие предгорья только внизу массива: чем выше гора над хребтом, тем стенка круче, до PeakWallHigh,
                    // иначе пологий участок срезал бы вершину.
                    var foothill = CampaignNoise.Lerp(PeakWallLow, PeakWall, noise.Fbm(x * 0.03f + 61f, z * 0.03f - 17f, 3));
                    var wall = CampaignNoise.Lerp(foothill, PeakWallHigh, CampaignNoise.SmoothStep(5f, 20f, peak - ridgeHeight));
                    var limit = baseDistance < float.MaxValue ? terraceBase + Math.Max(baseDistance, 0f) * wall : peak;
                    // Скругляем только там, где стенка срезает гору; где стенка выше, гора как есть.
                    cut = Math.Max(h, limit < peak + PeakWallRound ? CampaignNoise.SmoothMin(peak, limit, PeakWallRound) : peak);
                    if (rim > 0f && baseDistance < float.MaxValue)
                        limit = CampaignNoise.Lerp(limit, Math.Max(limit, peak),
                            rim * CampaignNoise.SmoothStep(RimFloor, RimFloor + 16f, baseDistance));
                    h = Math.Max(h, limit < peak + PeakWallRound ? CampaignNoise.SmoothMin(peak, limit, PeakWallRound) : peak);
                }
            }

            // У края карты котловина закрыта горным бортом, а там, где гора встала над тем, что срезала бы стенка,
            // трава и деревья — хребта, не локации.
            var fill = rim > 0f
                ? 1f - CampaignNoise.SmoothStep(0f, RimFill, edge + (noise.Fbm(x * 0.05f + 7f, z * 0.05f - 3f, 2) - 0.5f) * 12f)
                : 0f;
            if (fill > 0f)
            {
                // Над котловиной (где дно вырезано ниже хребта) борт встаёт горой неровной высоты, иначе обрез мира
                // над ней был бы ниже хребта вокруг; вне котловин край остаётся таким, каким его сделали цепь и вершины.
                var carved = CampaignNoise.SmoothStep(2f, 10f, ridgeHeight - h);
                var rimTop = ridge.MaxHeight + 6f + (ridge.PeakHeight - ridge.MaxHeight - 6f) * noise.Ridged(x * 0.03f, z * 0.03f);
                var target = CampaignNoise.Lerp(ridgeHeight, Math.Max(ridgeHeight, rimTop), carved);
                h = Math.Max(h, CampaignNoise.Lerp(h, target, fill));
            }
            if (h > cut)
            {
                var rock = CampaignNoise.SmoothStep(2f, 8f, h - cut);
                tint *= 1f - rock;
                kind *= 1f - rock;
                tintWeight *= 1f - rock;
                if (rock > 0.5f)
                    owner = terraceOwner = Ridge;
            }

            var ridgeTint = ridge.GrassTint ?? Color.white;
            GrassTints[xi, zi] = tintWeight < 1f ? tint + (1f - tintWeight) * ridgeTint : tint / tintWeight;
            var ridgeKind = Voxels.GrassKinds.Weight(ridge.GrassKind);
            GrassKinds[xi, zi] = tintWeight < 1f ? kind + (1f - tintWeight) * ridgeKind : kind / tintWeight;

            Heights[xi, zi] = Math.Max(1f, Math.Min(maxHeight, h));
            Owners[xi, zi] = owner;
            TerraceOwners[xi, zi] = terraceOwner;
            TerraceBases[xi, zi] = terraceBase;
        }

        /// <summary>
        /// Вершины горной цепи вдоль краёв карты: пирамиды через RingSpacing блоков (шаг гуляет), каждая на своём
        /// отступе от края (RingInsetMin…+RingInsetSpread), своей высоты (от MaxHeight + 8 до PeakHeight) и ширины.
        /// Соседние пирамиды сливаются рёбрами в неровные массивы с ветвями, а не в ровный хребет вдоль края.
        /// </summary>
        private static List<CampaignPeakShape> RingPeaks(CampaignRidge ridge, int sizeX, int sizeZ, CampaignNoise noise)
        {
            var result = new List<CampaignPeakShape>();
            var perimeter = 2f * (sizeX + sizeZ);
            var along = 0f;
            for (var i = 0; along < perimeter; i++)
            {
                var inset = RingInsetMin + RingInsetSpread * noise.Hash(i, 201);
                var height = CampaignNoise.Lerp(ridge.MaxHeight + 8f, ridge.PeakHeight, noise.Hash(i, 202));
                var radius = 24f + 16f * noise.Hash(i, 203);
                var step = noise.Hash(i, 204);
                var t = along + (step - 0.5f) * RingSpacing * 0.3f;
                t = (t % perimeter + perimeter) % perimeter;
                Vector2 point;
                if (t < sizeX)
                    point = new Vector2(t, inset);
                else if (t < sizeX + sizeZ)
                    point = new Vector2(sizeX - inset, t - sizeX);
                else if (t < 2f * sizeX + sizeZ)
                    point = new Vector2(2f * sizeX + sizeZ - t, sizeZ - inset);
                else
                    point = new Vector2(inset, perimeter - t);
                result.Add(new CampaignPeakShape
                {
                    Name = $"Вершина цепи {i}",
                    Points = new[] { point },
                    Height = height,
                    Radius = radius,
                });
                along += RingSpacing * (0.8f + 0.4f * step);
            }
            return result;
        }

        /// <summary>
        /// Горная цепь хребта: острые гребни «хребтового» шума с искажением (петляют, сходятся в вершины),
        /// между ними долины не ниже MinHeight. В полосе ChainBandNear–ChainBandFar блоков от края карты и в горных
        /// массивах (крупный шум) гребни встают до PeakHeight, в глубине карты — примерно до MaxHeight.
        /// </summary>
        private static float ChainHeight(CampaignRidge ridge, CampaignNoise noise, float x, float z, float edge)
        {
            var wx = x + (noise.Fbm(x * 0.01f, z * 0.01f + 5f, 3) - 0.5f) * ChainWarp;
            var wz = z + (noise.Fbm(x * 0.01f + 9f, z * 0.01f, 3) - 0.5f) * ChainWarp;
            var ridged = noise.Ridged(wx * ChainFrequency, wz * ChainFrequency);
            var shape = (float)Math.Pow(CampaignNoise.Clamp01((ridged - 0.2f) / 0.7f), 1.3f);
            var liftReach = edge + (noise.Fbm(x * 0.02f + 19f, z * 0.02f + 5f, 2) - 0.5f) * 24f;
            var lift = ChainLift * (1f - CampaignNoise.SmoothStep(0f, ChainLiftWidth, liftReach))
                       * (0.5f + noise.Fbm(x * 0.015f + 3f, z * 0.015f + 77f, 2));
            shape = lift + (1f - lift) * shape;
            // Ширина горной полосы у края гуляет, чтобы её граница не шла по линейке.
            var reach = edge + (noise.Fbm(x * 0.012f + 51f, z * 0.012f - 17f, 3) - 0.5f) * 40f;
            var band = CampaignNoise.SmoothStep(ChainBandNear, ChainBandNear + 25f, reach)
                       * (1f - CampaignNoise.SmoothStep(ChainBandFar, ChainBandFar + 60f, reach));
            var massif = 0.7f * CampaignNoise.SmoothStep(0.35f, 0.75f, noise.Fbm(x * 0.007f + 13f, z * 0.007f - 29f, 3));
            // В глубине гребни до MaxHeight: доля, при которой самый высокий гребень как раз MaxHeight.
            var low = (ridge.MaxHeight - ridge.MinHeight) / (ridge.PeakHeight - ridge.MinHeight);
            var amplitude = CampaignNoise.Lerp(low, 1f, Math.Max(band, massif));
            return ridge.MinHeight + (ridge.PeakHeight - ridge.MinHeight) * shape * amplitude
                   + (noise.Fbm(x * 0.06f, z * 0.06f) - 0.5f) * 3f;
        }

        private struct PreparedLocation
        {
            public readonly CampaignLocationShape Shape;
            private readonly int index;
            // Эллипсы формы: смещение от центра и полуоси. Первый — основной.
            private readonly Vector4[] lobes;
            private readonly float reachX, reachZ, warp, jitter;
            private readonly Vector2 riseAxis;

            public PreparedLocation(CampaignLocationShape shape, int index, CampaignNoise noise)
            {
                Shape = shape;
                this.index = index;
                var r = shape.Radius;
                var rough = Mathf.Clamp(shape.Roughness, 0f, 2f);
                var lobeCount = rough > 0f ? 5 : 1;
                lobes = new Vector4[lobeCount];
                lobes[0] = new Vector4(0f, 0f, r.x, r.y);
                for (var j = 1; j < lobeCount; j++)
                {
                    var angle = noise.Hash(index * 13 + j, 101) * Mathf.PI * 2f;
                    var offset = (0.35f + 0.4f * noise.Hash(j, index * 7 + 202)) * Mathf.Min(rough, 1.5f);
                    lobes[j] = new Vector4(
                        Mathf.Cos(angle) * r.x * offset,
                        Mathf.Sin(angle) * r.y * offset,
                        r.x * (0.35f + 0.35f * noise.Hash(index * 5 + j, 303)),
                        r.y * (0.35f + 0.35f * noise.Hash(j + 3, index * 11 + 404)));
                }
                reachX = r.x * 1.6f + LocationReach;
                reachZ = r.y * 1.6f + LocationReach;
                warp = Mathf.Min(r.x, r.y) * 0.55f * rough;
                jitter = 6f * rough;
                var radians = shape.RiseDirection * Mathf.Deg2Rad;
                riseAxis = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            }

            public bool Near(float x, float z) =>
                Math.Abs(x - Shape.Center.x) <= reachX && Math.Abs(z - Shape.Center.y) <= reachZ;

            /// <summary>Расстояние до края дна в блоках: отрицательное внутри.</summary>
            public float Distance(float x, float z, float ux, float uz, float vx, float vz)
            {
                var wx = x + ux * warp + vx * jitter - Shape.Center.x;
                var wz = z + uz * warp + vz * jitter - Shape.Center.y;
                var d = 1e9f;
                foreach (var lobe in lobes)
                {
                    var ex = (wx - lobe.x) / lobe.z;
                    var ez = (wz - lobe.y) / lobe.w;
                    var e = ((float)Math.Sqrt(ex * ex + ez * ez) - 1f) * Math.Min(lobe.z, lobe.w);
                    d = CampaignNoise.SmoothMin(d, e, 6f);
                }
                return d;
            }

            /// <summary>Высота дна: высота локации, форма профиля (гора, чаша) в рамке локации, шум и подъём.</summary>
            public float Floor(float x, float z, CampaignNoise noise)
            {
                var p = Shape.Profile;
                var dx = (x - Shape.Center.x) / Math.Max(Shape.Radius.x, 1f);
                var dz = (z - Shape.Center.y) / Math.Max(Shape.Radius.y, 1f);
                var shape = TerrainGenerator.ShapeHeight(p, dx * 0.5f + 0.5f, dz * 0.5f + 0.5f) - p.BaseHeight;
                var scale = Math.Max(p.NoiseScale, 1f);
                var bumps = (noise.Fbm(x / scale + index * 57.1f, z / scale - index * 23.7f, 3) - 0.5f) * 2f * p.NoiseAmplitude;
                var along = CampaignNoise.Clamp01((dx * riseAxis.x + dz * riseAxis.y) * 0.5f + 0.5f);
                return Shape.FloorHeight + shape + bumps + Shape.Rise * along;
            }
        }

        private struct PreparedPeak
        {
            private readonly CampaignPeakShape shape;
            private readonly int index;
            private readonly Vector4 bounds;

            public PreparedPeak(CampaignPeakShape shape, int index)
            {
                this.shape = shape;
                this.index = index;
                var points = shape.Points ?? Array.Empty<Vector2>();
                var reach = Math.Max(shape.Radius, 1f) + 8f;
                var min = points.Length > 0 ? points[0] : Vector2.zero;
                var max = min;
                foreach (var p in points)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                bounds = points.Length > 0
                    ? new Vector4(min.x - reach, min.y - reach, max.x + reach, max.y + reach)
                    : new Vector4(1f, 1f, -1f, -1f);
            }

            /// <summary>
            /// Высота горы в точке поверх хребта высотой ridge; за подножием — ridge. Вершина — пирамида: от неё
            /// вниз расходятся 3–4 острых ребра, между ними вогнутые грани с кулуарами, на рёбрах жандармы.
            /// Гребень (ломаная) — сплошная острая кромка: её высота неровно гуляет, на ней острые несимметричные
            /// жандармы со сколами (тем выше, чем больше Teeth), а вниз по склонам через SpurSpacing блоков уходят отроги.
            /// Подножие неровное, у вершины искажение сходит на нет, чтобы вершина стояла в своей точке.
            /// </summary>
            public float Height(float x, float z, float ridge, CampaignNoise noise)
            {
                if (x < bounds.x || z < bounds.y || x > bounds.z || z > bounds.w)
                    return ridge;
                var radius = Math.Max(shape.Radius, 1f);
                var seed = index * 41.3f;
                var near = 1f - CampaignNoise.SmoothStep(0.65f, 0.95f, 1f - Distance(x, z) / radius);
                var wx = x + (noise.Fbm(x * 0.03f + seed, z * 0.03f, 3) - 0.5f) * radius * 0.6f * near;
                var wz = z + (noise.Fbm(x * 0.03f, z * 0.03f - seed, 3) - 0.5f) * radius * 0.6f * near;
                var d = Distance(wx, wz, out var along);
                var top = Math.Max(shape.Height * (0.95f + 0.05f * noise.Fbm(x * 0.04f - seed, z * 0.04f + seed, 2)), ridge);
                float spread;
                var gendarmes = 0f;
                if (shape.Points.Length == 1)
                {
                    // Пирамида: угол до ближайшего ребра (0 на ребре, 1 посередине грани). Рёбра чуть изгибаются.
                    var count = 3 + (noise.Hash(index, 71) > 0.5f ? 1 : 0);
                    var turn = noise.Hash(index, 72) * 2f * (float)Math.PI;
                    var angle = (float)Math.Atan2(wz - shape.Points[0].y, wx - shape.Points[0].x);
                    var sector = (float)Math.PI / count;
                    var face = 1f;
                    for (var k = 0; k < count; k++)
                    {
                        var edge = turn + k * 2f * sector + (noise.Hash(index, 80 + k) - 0.5f) * 0.7f
                                   + (noise.Hash(index, 90 + k) - 0.5f) * 0.9f * d / radius;
                        face = Math.Min(face, Math.Abs(Wrap(angle - edge)) / sector);
                    }
                    var flutes = Triangle(angle * count * 3f / (2f * (float)Math.PI) + noise.Fbm(d * 0.05f + seed, seed, 2) * 1.5f);
                    var grow = CampaignNoise.SmoothStep(0f, PeakSharpTop, d / radius);
                    spread = 1f + (PeakFaceDrop * (float)Math.Pow(face, 1.3f) + PeakFlutes * flutes) * grow;
                    // Жандармы на рёбрах: ступени «хребтового» шума вдоль ребра.
                    var rib = (float)Math.Floor(angle / (2f * sector));
                    gendarmes = (noise.Ridged(d * 0.12f + seed, rib * 7.1f) - 0.5f) * 4f * (1f - face) * (1f - face);
                }
                else
                {
                    // Кромка гребня: крупные подъёмы и седловины — сложенный шум (острые верхушки, без полок).
                    var teeth = CampaignNoise.Clamp01(shape.Teeth);
                    var crest = 1f - Math.Abs(2f * noise.Fbm(along * CrestFrequency + seed, seed * 0.5f, 3) - 1f);
                    crest = (float)Math.Pow(crest, 1.5f);
                    top = Math.Max(ridge + (top - ridge) * (1f - (0.12f + 0.2f * teeth) * (1f - crest)), ridge);
                    // Жандармы через GendarmeSpacing блоков: острая вершина смещена от середины, поэтому один склон
                    // круче другого, высота у каждого своя; на макушке сколы. Между жандармами гребень уже.
                    var cell = along / GendarmeSpacing + 0.45f * (noise.Fbm(along * 0.04f + seed, seed + 3f, 2) - 0.5f);
                    var number = (int)Math.Floor(cell);
                    var f = cell - number;
                    var tip = 0.3f + 0.4f * noise.Hash(number, index * 17 + 1);
                    var u = f < tip ? (tip - f) / tip : (f - tip) / (1f - tip);
                    var tooth = (float)Math.Pow(1f - u, 1.1f) * (0.45f + 0.55f * noise.Hash(number, index * 17 + 2));
                    var chips = Math.Abs(2f * noise.Value(along * 0.45f + seed, seed + 11f) - 1f);
                    top = Math.Max(top - teeth * (GendarmeDepth * (1f - tooth) + GendarmeChips * chips * tooth), ridge);
                    // Отроги: гребень шире там, где от него вниз уходит отрог, и уже между отрогами.
                    var spur = Triangle(along / SpurSpacing + 0.3f * (noise.Fbm(along * 0.05f + seed, seed, 2) - 0.5f));
                    spread = (1f + SpurDrop * (1f - (float)Math.Pow(spur, 2.5f))) * (1f + GendarmePinch * teeth * (1f - tooth));
                }
                var reach = d * spread;
                if (reach >= radius)
                    return ridge;
                var t = 1f - reach / radius;
                var body = (float)Math.Pow(t, PeakProfile);
                // Мелкие скальные выступы на середине склонов.
                var crags = (noise.Ridged(x * 0.09f - seed, z * 0.09f) - 0.45f) * 5f * t * (1f - t);
                return ridge + (top - ridge) * body + crags + gendarmes * t;
            }

            /// <summary>Угол, приведённый к -π..π.</summary>
            private static float Wrap(float angle)
            {
                var full = 2f * (float)Math.PI;
                return angle - full * (float)Math.Floor((angle + (float)Math.PI) / full);
            }

            /// <summary>Треугольная волна 0..1: 0 на целых, 1 посередине между ними.</summary>
            private static float Triangle(float v) => 1f - Math.Abs(2f * (v - (float)Math.Floor(v)) - 1f);

            /// <summary>Расстояние до вершины или до ломаной гребня.</summary>
            private float Distance(float x, float z) => Distance(x, z, out _);

            /// <summary>Расстояние до вершины или до ломаной гребня и путь вдоль гребня до ближайшей точки, в блоках.</summary>
            private float Distance(float x, float z, out float along)
            {
                along = 0f;
                var points = shape.Points;
                if (points.Length == 1)
                    return (float)Math.Sqrt((x - points[0].x) * (x - points[0].x) + (z - points[0].y) * (z - points[0].y));
                var best = float.MaxValue;
                var start = 0f;
                for (var i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1];
                    var dx = points[i].x - a.x;
                    var dz = points[i].y - a.y;
                    var l2 = dx * dx + dz * dz;
                    var s = l2 > 0f ? CampaignNoise.Clamp01(((x - a.x) * dx + (z - a.y) * dz) / l2) : 0f;
                    var ex = x - a.x - s * dx;
                    var ez = z - a.y - s * dz;
                    var length = (float)Math.Sqrt(l2);
                    if (ex * ex + ez * ez < best)
                    {
                        best = ex * ex + ez * ez;
                        along = start + s * length;
                    }
                    start += length;
                }
                return (float)Math.Sqrt(best);
            }
        }

        private struct PreparedGorge
        {
            public readonly CampaignGorgeShape Shape;
            private readonly Vector2[] points;
            // Длина пути от начала до каждой точки.
            private readonly float[] lengths;
            private readonly float total;
            private readonly Vector4 bounds;

            public PreparedGorge(CampaignLayout layout, CampaignGorgeShape shape, int index, CampaignNoise noise)
            {
                Shape = shape;
                var anchors = new List<Vector2> { layout.Locations[shape.From].Center };
                if (shape.Waypoints != null)
                    anchors.AddRange(shape.Waypoints);
                anchors.Add(layout.Locations[shape.To].Center);
                points = Spline(Jitter(anchors, shape.Wiggle, index * 31 + 5, noise), 5);
                lengths = new float[points.Length];
                for (var i = 1; i < points.Length; i++)
                    lengths[i] = lengths[i - 1] + Vector2.Distance(points[i - 1], points[i]);
                total = Math.Max(lengths[points.Length - 1], 1e-3f);

                var margin = GorgeReach + Math.Max(shape.WidthStart, shape.WidthEnd) + 12f;
                var min = points[0];
                var max = points[0];
                foreach (var p in points)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                bounds = new Vector4(min.x - margin, min.y - margin, max.x + margin, max.y + margin);
            }

            public bool Near(float x, float z) => x >= bounds.x && z >= bounds.y && x <= bounds.z && z <= bounds.w;

            /// <summary>Высота дна в доле пути t: от начала к концу по S-кривой, с подъёмом посередине.</summary>
            public float Floor(float t) =>
                CampaignNoise.Lerp(Shape.HeightStart, Shape.HeightEnd, CampaignNoise.SmoothStep(0f, 1f, t))
                + Shape.Bump * (float)Math.Sin(Math.PI * t);

            /// <summary>Расстояние до линии пути и доля пути (0..1) в ближайшей точке.</summary>
            public void Distance(float x, float z, out float distance, out float t)
            {
                distance = float.MaxValue;
                t = 0f;
                for (var i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1];
                    var dx = points[i].x - a.x;
                    var dz = points[i].y - a.y;
                    var l2 = dx * dx + dz * dz;
                    var s = l2 > 0f ? CampaignNoise.Clamp01(((x - a.x) * dx + (z - a.y) * dz) / l2) : 0f;
                    var ex = x - a.x - s * dx;
                    var ez = z - a.y - s * dz;
                    var d2 = ex * ex + ez * ez;
                    if (d2 >= distance)
                        continue;
                    distance = d2;
                    t = (lengths[i - 1] + s * (lengths[i] - lengths[i - 1])) / total;
                }
                distance = (float)Math.Sqrt(distance);
            }

            /// <summary>Между опорными точками — промежуточные со сдвигом вбок, чтобы путь не шёл по прямой.</summary>
            private static List<Vector2> Jitter(List<Vector2> anchors,
                float wiggle, int seed, CampaignNoise noise)
            {
                var result = new List<Vector2> { anchors[0] };
                for (var i = 1; i < anchors.Count; i++)
                {
                    var a = anchors[i - 1];
                    var b = anchors[i];
                    var delta = b - a;
                    var length = Math.Max(delta.magnitude, 1e-3f);
                    var side = new Vector2(-delta.y, delta.x) / length;
                    var count = Math.Max(1, Mathf.RoundToInt(length / 14f));
                    for (var j = 1; j <= count; j++)
                    {
                        var t = j / (count + 1f);
                        var offset = (noise.Hash(seed + i * 7, j) - 0.5f) * Math.Min(wiggle, length * 0.45f);
                        result.Add(a + delta * t + side * offset);
                    }
                    result.Add(b);
                }
                return result;
            }

            /// <summary>Гладкая кривая через точки (Catmull-Rom), steps отрезков на каждый промежуток.</summary>
            private static Vector2[] Spline(List<Vector2> p, int steps)
            {
                var result = new Vector2[(p.Count - 1) * steps + 1];
                var n = 0;
                for (var i = 0; i < p.Count - 1; i++)
                {
                    var p0 = p[Math.Max(i - 1, 0)];
                    var p1 = p[i];
                    var p2 = p[i + 1];
                    var p3 = p[Math.Min(i + 2, p.Count - 1)];
                    for (var k = 0; k < steps; k++)
                    {
                        var t = k / (float)steps;
                        var t2 = t * t;
                        var t3 = t2 * t;
                        result[n++] = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                              + (3f * p1 - p0 - 3f * p2 + p3) * t3);
                    }
                }
                result[n] = p[p.Count - 1];
                return result;
            }
        }
    }
}
