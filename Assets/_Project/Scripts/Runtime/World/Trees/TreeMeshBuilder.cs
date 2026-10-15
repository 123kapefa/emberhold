using System.Collections.Generic;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Гранёная low-poly сетка дерева по силуэту вида (<see cref="TreeSpecies.Shape"/>) и его размерам: высоте,
    /// стволу и ширине кроны. Основание в (0, 0, 0), ось Y вверх, единица — блок; крона не выходит за CrownWidth
    /// заметно больше, чем на неровность граней. У каждого треугольника свои вершины и нормаль грани, поэтому грани
    /// читаются при любом свете. Цвет вершин: rgb — цвет части (ствол, полоски, крона темнее книзу), a — 1 у кроны
    /// и 0 у ствола: шейдер Emberhold/World/Tree красит крону оттенком материала. Вид и номер формы всегда дают
    /// одну и ту же сетку.
    /// </summary>
    public static class TreeMeshBuilder
    {
        // Крона темнее книзу, ствол — чуть темнее у земли: притенение без запечённого света.
        private const float CrownShadeLow = 0.62f;
        private const float CrownShadeHigh = 1.08f;
        private const float TrunkShadeLow = 0.8f;
        private const float TrunkShadeHigh = 1.05f;

        /// <summary>Сетка формы variant (0..Variants−1) вида species.</summary>
        public static Mesh Build(TreeSpecies species, int variant)
        {
            var random = new System.Random(Seed(species, variant));
            var mesh = new FacetedMesh();
            switch (species.Shape)
            {
                case TreeShape.Conifer:
                    BuildConifer(mesh, species, random);
                    break;
                case TreeShape.Tiered:
                    BuildTiered(mesh, species, random);
                    break;
                default:
                    BuildLeafy(mesh, species, random);
                    break;
            }
            return mesh.ToMesh($"Tree {species.name} {variant}");
        }

        /// <summary>
        /// Оттенок кроны одного дерева для _BaseColor материала: небольшой разброс тона и яркости (по seed клетки)
        /// и оттенок травы локации (rgb — множитель, a — насыщенность, как у травы в Ground.shader). Белый — крона как у вида.
        /// </summary>
        public static Color CrownTint(Color crownColor, Color grassTint, int seed)
        {
            var random = new System.Random(seed);
            Color.RGBToHSV(crownColor, out var h, out var s, out var v);
            var hue = Mathf.Repeat(h + Range(random, -0.025f, 0.025f), 1f);
            var tinted = Color.HSVToRGB(hue, s, Mathf.Clamp01(v * Range(random, 0.92f, 1.06f)));
            var luma = tinted.r * 0.3f + tinted.g * 0.59f + tinted.b * 0.11f;
            var gray = new Color(luma, luma, luma);
            tinted = Color.LerpUnclamped(gray, tinted, grassTint.a) * grassTint;
            // Цвет кроны в вершинах = цвет вида × тень, поэтому оттенок — отношение нового цвета к цвету вида.
            return new Color(Ratio(tinted.r, crownColor.r), Ratio(tinted.g, crownColor.g), Ratio(tinted.b, crownColor.b), 1f);
        }

        private static float Ratio(float value, float of) => of > 1e-3f ? value / of : 1f;

        // Берёза: светлый ствол с тёмными полосками, крона из гранёных комков — верхний и 5–6 вокруг ствола.
        private static void BuildLeafy(FacetedMesh mesh, TreeSpecies species, System.Random random)
        {
            var height = species.Height;
            var crownHeight = Mathf.Max(0.2f, height - species.TrunkHeight);
            var radius = species.CrownWidth * 0.5f;
            var trunk = new Trunk(6, species.TrunkWidth * 0.5f, species.TrunkWidth * 0.28f, height * 0.82f);
            AddTrunk(mesh, species, random, trunk, 0.015f);
            if (species.StripeColor.a > 0f)
            {
                for (var i = 0; i < 6; i++)
                {
                    var y = Range(random, 0.07f, 0.53f) * height;
                    var r = trunk.RadiusAt(y) * 1.15f;
                    mesh.AddPrism(6, Vector3.up * y, r, r, 0.05f, 0f, random, _ => species.StripeColor.linear.WithAlpha(0f));
                }
            }

            var blobs = new List<(Vector3 center, Vector3 radii)>();
            var top = 0.7f * radius;
            blobs.Add((new Vector3(0f, height - top * 1.1f, 0f), new Vector3(top, top * 1.1f, top)));
            var count = 6 + random.Next(2);
            for (var i = 1; i < count; i++)
            {
                var angle = (float)i / (count - 1) * Mathf.PI * 2f + Range(random, 0f, 1f);
                var distance = Range(random, 0.3f, 0.42f) * radius;
                var r = Range(random, 0.5f, 0.6f) * radius;
                var y = species.TrunkHeight + crownHeight * Range(random, 0.18f, 0.6f);
                blobs.Add((new Vector3(Mathf.Cos(angle) * distance, y, Mathf.Sin(angle) * distance), new Vector3(r, r * 1.1f, r)));
            }
            AddCrownBlobs(mesh, species, random, blobs, 0, 0.22f);
        }

        // Сосна: тёмный ствол и четыре яруса гранёных конусов, к макушке уже.
        private static void BuildConifer(FacetedMesh mesh, TreeSpecies species, System.Random random)
        {
            var height = species.Height;
            var radius = species.CrownWidth * 0.5f;
            var crownHeight = Mathf.Max(0.2f, height - species.TrunkHeight);
            AddTrunk(mesh, species, random, new Trunk(6, species.TrunkWidth * 0.5f, species.TrunkWidth * 0.33f, height * 0.6f), 0.03f);

            const int tiers = 4;
            var start = species.TrunkHeight * 0.875f;
            var cones = new List<(Vector3 basePoint, float radius, float height)>();
            for (var i = 0; i < tiers; i++)
            {
                var t = (float)i / (tiers - 1);
                var h = (0.44f - 0.1f * t) * crownHeight;
                var y = start + t * (height - start - h);
                var r = (0.95f - 0.52f * t) * radius * Range(random, 0.92f, 1.08f);
                cones.Add((new Vector3(Range(random, -0.04f, 0.04f), y, Range(random, -0.04f, 0.04f)), r, h));
            }
            var bottom = cones[0].basePoint.y;
            foreach (var cone in cones)
                mesh.AddCone(7, cone.basePoint, cone.radius, cone.height, 0.15f * radius, random,
                    p => CrownColor(species, p.y, bottom, height));
        }

        // Вековой кедр: толстый ствол и шесть широких плоских ярусов; нижние ярусы из нескольких лап.
        private static void BuildTiered(FacetedMesh mesh, TreeSpecies species, System.Random random)
        {
            var height = species.Height;
            var radius = species.CrownWidth * 0.5f;
            AddTrunk(mesh, species, random, new Trunk(7, species.TrunkWidth * 0.58f, species.TrunkWidth * 0.29f, height * 0.72f), 0.04f);

            const int tiers = 6;
            var padHeight = 0.045f * height;
            var first = species.TrunkHeight + 0.1f;
            var last = height - padHeight * 1.25f;
            var blobs = new List<(Vector3 center, Vector3 radii)>();
            for (var i = 0; i < tiers; i++)
            {
                var t = (float)i / (tiers - 1);
                var y = first + t * (last - first);
                // Лапы яруса вместе не выходят за крону: смещение до 0,55 и размер до 0,77 радиуса яруса.
                var tier = (0.76f - 0.5f * t) * radius;
                var pads = i < 3 ? 3 : i < 5 ? 2 : 1;
                var start = Range(random, 0f, Mathf.PI * 2f);
                for (var j = 0; j < pads; j++)
                {
                    var angle = start + (float)j / pads * Mathf.PI * 2f + Range(random, -0.4f, 0.4f);
                    var distance = pads == 1 ? Range(random, 0f, 0.1f) * radius : tier * Range(random, 0.4f, 0.55f);
                    var r = tier * (pads == 1 ? 1f : 0.7f) * Range(random, 0.9f, 1.1f);
                    var center = new Vector3(Mathf.Cos(angle) * distance, y + Range(random, -0.15f, 0.15f), Mathf.Sin(angle) * distance);
                    blobs.Add((center, new Vector3(r, padHeight, r * Range(random, 0.8f, 1f))));
                }
            }
            AddCrownBlobs(mesh, species, random, blobs, 1, 0.22f);
        }

        private readonly struct Trunk
        {
            public readonly int Sides;
            public readonly float BottomRadius;
            public readonly float TopRadius;
            public readonly float Height;

            public Trunk(int sides, float bottomRadius, float topRadius, float height)
            {
                Sides = sides;
                BottomRadius = bottomRadius;
                TopRadius = topRadius;
                Height = height;
            }

            public float RadiusAt(float y) => Mathf.Lerp(BottomRadius, TopRadius, y / Height);
        }

        private static void AddTrunk(FacetedMesh mesh, TreeSpecies species, System.Random random, Trunk trunk, float jitter)
        {
            var color = species.TrunkColor.linear;
            mesh.AddPrism(trunk.Sides, Vector3.zero, trunk.BottomRadius, trunk.TopRadius, trunk.Height, jitter, random,
                p => (color * Mathf.Lerp(TrunkShadeLow, TrunkShadeHigh, Mathf.Clamp01(p.y / trunk.Height))).WithAlpha(0f));
        }

        private static void AddCrownBlobs(FacetedMesh mesh, TreeSpecies species, System.Random random,
            List<(Vector3 center, Vector3 radii)> blobs, int subdivisions, float jitter)
        {
            var bottom = float.MaxValue;
            foreach (var blob in blobs)
                bottom = Mathf.Min(bottom, blob.center.y - blob.radii.y);
            var top = species.Height;
            foreach (var blob in blobs)
                mesh.AddBlob(blob.center, blob.radii, subdivisions, jitter, random, p => CrownColor(species, p.y, bottom, top));
        }

        private static Color CrownColor(TreeSpecies species, float y, float bottom, float top)
        {
            var t = Mathf.Clamp01((y - bottom) / Mathf.Max(1e-3f, top - bottom));
            return (species.CrownColor.linear * Mathf.Lerp(CrownShadeLow, CrownShadeHigh, t)).WithAlpha(1f);
        }

        private static Color WithAlpha(this Color color, float alpha) => new(color.r, color.g, color.b, alpha);

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // Seed по имени вида (FNV-1a), а не string.GetHashCode: тот может меняться между запусками.
        private static int Seed(TreeSpecies species, int variant)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in species.name)
                    hash = (hash ^ c) * 16777619u;
                hash = (hash ^ (uint)variant) * 16777619u;
                return (int)hash;
            }
        }
    }
}
