using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.World.Bushes
{
    /// <summary>
    /// Гранёная low-poly сетка куста в стиле деревьев (<see cref="Trees.TreeMeshBuilder"/>). Зелёный куст — пучок веток, почти
    /// скрытый низкой широкой шапкой листвы из нескольких комков. Голый (срезанный) — только ветки: короткие срезанные стебли
    /// и несколько тонких прутьев, ниже и темнее, без листвы. Основание в (0, 0, 0), ось Y вверх, единица — блок.
    /// Цвет вершин как у деревьев: a = 1 у листвы (шейдер Emberhold/World/Tree красит её оттенком материала), 0 у веток.
    /// Вид, номер формы и состояние всегда дают одну и ту же сетку.
    /// </summary>
    public static class BushMeshBuilder
    {
        private const float LeafShadeLow = 0.6f;
        private const float LeafShadeHigh = 1.1f;

        /// <summary>Сетка формы variant (0..Variants−1) вида species: зелёная или голая.</summary>
        public static Mesh Build(BushSpecies species, int variant, bool bare)
        {
            var random = new System.Random(Seed(species, variant));
            var mesh = new FacetedMesh();
            var height = species.Height;
            var radius = species.Width * 0.5f;
            var twig = species.TwigColor.linear;
            var stems = 5 + random.Next(2);
            var start = Range(random, 0f, Mathf.PI * 2f);
            for (var i = 0; i < stems; i++)
            {
                // Ветки расходятся от середины наружу и вверх; у голого куста они короче — срезаны.
                var angle = start + (float)i / stems * Mathf.PI * 2f + Range(random, -0.3f, 0.3f);
                var tilt = Range(random, 14f, 32f);
                var length = (bare ? Range(random, 0.42f, 0.6f) : Range(random, 0.55f, 0.75f)) * height;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Чуть над землёй: наклонённое основание ветки не уходит под ноль.
                mesh.Transform = Matrix4x4.TRS(direction * 0.03f + Vector3.up * 0.02f, Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, direction)), Vector3.one);
                mesh.AddPrism(4, Vector3.zero, 0.028f, 0.012f, length, 0f, random, p => Shade(twig, p.y / height, 0.7f, 1f));
                if (bare)
                {
                    // Прутик-отросток сбоку и светлый срез на конце.
                    mesh.AddPrism(4, Vector3.up * length, 0.014f, 0.016f, 0.015f, 0f, random, _ => Untinted(Color.Lerp(twig, Color.white, 0.4f)));
                    mesh.Transform = mesh.Transform * Matrix4x4.TRS(Vector3.up * (length * 0.55f),
                        Quaternion.AngleAxis(Range(random, 25f, 40f), Vector3.forward), Vector3.one);
                    mesh.AddPrism(3, Vector3.zero, 0.01f, 0.004f, length * 0.45f, 0f, random, p => Shade(twig, 0.6f, 0.8f, 1f));
                }
            }
            mesh.Transform = Matrix4x4.identity;
            if (!bare)
            {
                // Шапка листвы: средний комок и кольцо пониже, к краю шире, книзу темнее.
                var leaf = species.LeafColor.linear;
                var bottom = height * 0.18f;
                mesh.AddBlob(new Vector3(0f, height * 0.62f, 0f), new Vector3(radius * 0.62f, height * 0.38f, radius * 0.62f), 0, 0.2f, random,
                    p => Leaf(leaf, p.y, bottom, height));
                var count = 4 + random.Next(2);
                for (var i = 0; i < count; i++)
                {
                    var angle = start + (i + 0.5f) / count * Mathf.PI * 2f + Range(random, -0.25f, 0.25f);
                    var distance = Range(random, 0.42f, 0.5f) * radius;
                    var r = Range(random, 0.42f, 0.5f) * radius;
                    var y = height * Range(random, 0.36f, 0.46f);
                    mesh.AddBlob(new Vector3(Mathf.Cos(angle) * distance, y, Mathf.Sin(angle) * distance), new Vector3(r, Mathf.Min(r, y - bottom * 0.5f), r),
                        0, 0.22f, random, p => Leaf(leaf, p.y, bottom, height));
                }
            }
            return mesh.ToMesh($"Bush {species.name} {variant}{(bare ? " bare" : "")}");
        }

        private static Color Leaf(Color leaf, float y, float bottom, float top)
        {
            var t = Mathf.Clamp01((y - bottom) / Mathf.Max(1e-3f, top - bottom));
            var c = leaf * Mathf.Lerp(LeafShadeLow, LeafShadeHigh, t);
            return new Color(c.r, c.g, c.b, 1f);
        }

        private static Color Shade(Color color, float t, float low, float high)
        {
            var c = color * Mathf.Lerp(low, high, Mathf.Clamp01(t));
            return new Color(c.r, c.g, c.b, 0f);
        }

        private static Color Untinted(Color color) => new(color.r, color.g, color.b, 0f);

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // Seed по имени вида (FNV-1a), а не string.GetHashCode: тот может меняться между запусками.
        private static int Seed(BushSpecies species, int variant)
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
