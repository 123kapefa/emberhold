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
        // Частота рёбер массива: соседние рёбра примерно в 1 / RibFrequency блоков.
        private const float RibFrequency = 0.035f;
        // На сколько блоков скруглён верх стенки массива над котловиной.
        private const float PeakWallRound = 8f;

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
            var peaks = new PreparedPeak[layout.Peaks.Count];
            for (var i = 0; i < peaks.Length; i++)
                peaks[i] = new PreparedPeak(layout.Peaks[i], i);

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
            var crest = CampaignNoise.Clamp01((noise.Ridged(x * 0.016f, z * 0.016f) - 0.12f) / 0.72f);
            var h = CampaignNoise.Lerp(ridge.MinHeight, ridge.MaxHeight, crest)
                    + (noise.Fbm(x * 0.06f, z * 0.06f) - 0.5f) * 3f
                    + (1f - CampaignNoise.SmoothStep(0f, 24f, edge)) * 4f;
            h = Math.Max(ridge.MinHeight, Math.Min(ridge.MaxHeight, h));
            var ridgeHeight = h;

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
            if (peaks.Length > 0)
            {
                var peak = ridgeHeight;
                foreach (var p in peaks)
                    peak = Math.Max(peak, p.Height(x, z, ridgeHeight, noise));
                if (peak > h)
                {
                    // Стенка над дном то круче, то положе (предгорья), и её верх скруглён, чтобы не было бортика.
                    // Пологие предгорья только внизу массива: чем выше гора над хребтом, тем стенка ближе к PeakWall,
                    // иначе пологий участок срезал бы вершину.
                    var foothill = CampaignNoise.Lerp(PeakWallLow, PeakWall, noise.Fbm(x * 0.03f + 61f, z * 0.03f - 17f, 3));
                    var wall = CampaignNoise.Lerp(foothill, PeakWall, CampaignNoise.SmoothStep(5f, 20f, peak - ridgeHeight));
                    var limit = baseDistance < float.MaxValue ? terraceBase + Math.Max(baseDistance, 0f) * wall : peak;
                    // Скругляем только там, где стенка срезает гору; где стенка выше, гора как есть.
                    h = Math.Max(h, limit < peak + PeakWallRound ? CampaignNoise.SmoothMin(peak, limit, PeakWallRound) : peak);
                }
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
            /// Высота горы в точке поверх хребта высотой ridge; за подножием — ridge. Это массив, а не конус:
            /// склоны вогнутые, по ним от вершины расходятся острые рёбра (гребни «хребтового» шума с искажением),
            /// между рёбрами — кулуары, на рёбрах — вершины пониже главной. Подножие неровное, у самой вершины
            /// рельеф сходится в одну острую точку на полной высоте.
            /// </summary>
            public float Height(float x, float z, float ridge, CampaignNoise noise)
            {
                if (x < bounds.x || z < bounds.y || x > bounds.z || z > bounds.w)
                    return ridge;
                var radius = Math.Max(shape.Radius, 1f);
                var seed = index * 41.3f;
                // Неровное подножие: расстояние искажается шумом. К вершине искажение сходит на нет,
                // чтобы вершина стояла в своей точке на полной высоте.
                var near = 1f - CampaignNoise.SmoothStep(0.65f, 0.95f, 1f - Distance(x, z) / radius);
                var wx = x + (noise.Fbm(x * 0.03f + seed, z * 0.03f, 3) - 0.5f) * radius * 0.6f * near;
                var wz = z + (noise.Fbm(x * 0.03f, z * 0.03f - seed, 3) - 0.5f) * radius * 0.6f * near;
                var d = Distance(wx, wz);
                if (d >= radius)
                    return ridge;
                var t = 1f - d / radius;
                // Вдоль гребня вершины то выше, то ниже, до десятой части высоты.
                var top = Math.Max(shape.Height * (0.92f + 0.08f * noise.Fbm(x * 0.04f - seed, z * 0.04f + seed, 2)), ridge);
                var body = (float)Math.Pow(t, 1.4f);
                // Рёбра и кулуары: гребни шума с искажением, чтобы рёбра петляли, а не шли сеткой.
                var qx = x + (noise.Fbm(x * 0.02f + seed, z * 0.02f + 7f, 3) - 0.5f) * 24f;
                var qz = z + (noise.Fbm(x * 0.02f - 9f, z * 0.02f - seed, 3) - 0.5f) * 24f;
                var ribs = noise.Ridged(qx * RibFrequency + seed, qz * RibFrequency - seed);
                var summit = CampaignNoise.SmoothStep(0.65f, 0.95f, t);
                var relief = CampaignNoise.Lerp(0.55f + 0.45f * ribs, 1f, summit);
                // Мелкие скальные выступы на середине склонов.
                var crags = (noise.Ridged(x * 0.09f - seed, z * 0.09f) - 0.45f) * 6f * t * (1f - t);
                return ridge + (top - ridge) * body * relief + crags;
            }

            /// <summary>Расстояние до вершины или до ломаной гребня.</summary>
            private float Distance(float x, float z)
            {
                var points = shape.Points;
                if (points.Length == 1)
                    return (float)Math.Sqrt((x - points[0].x) * (x - points[0].x) + (z - points[0].y) * (z - points[0].y));
                var best = float.MaxValue;
                for (var i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1];
                    var dx = points[i].x - a.x;
                    var dz = points[i].y - a.y;
                    var l2 = dx * dx + dz * dz;
                    var s = l2 > 0f ? CampaignNoise.Clamp01(((x - a.x) * dx + (z - a.y) * dz) / l2) : 0f;
                    var ex = x - a.x - s * dx;
                    var ez = z - a.y - s * dz;
                    best = Math.Min(best, ex * ex + ez * ez);
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
