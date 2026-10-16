using System;
using System.Collections.Generic;
using UnityEngine;
using static Emberhold.Gameplay.Enemies.Bestiary.CreaturePalette;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>Шесть базовых врагов тумана.</summary>
    public enum CreatureKind
    {
        Crawler,
        Brute,
        Shade,
        Moths,
        Mistmother,
        Stilt,
    }

    /// <summary>
    /// Модели базовых врагов, перенос прототипа бестиария (1 единица = 1 блок до масштаба, морда в +Z).
    /// Рост в игре задаёт <see cref="SizeFactor"/>: модель уровня «обычный» вытягивается до роста из таблицы.
    /// </summary>
    public static class CreatureModels
    {
        private static readonly float[] Sides = { -1f, 1f };
        private static readonly Dictionary<CreatureKind, float> SizeCache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SizeCache.Clear();

        /// <summary>Рост модели в блоках (у стайки мотыльков — готовый множитель).</summary>
        public static float TargetHeight(CreatureKind kind) => kind switch
        {
            CreatureKind.Crawler => 1f,
            CreatureKind.Brute => 3.5f,
            CreatureKind.Shade => 2.8f,
            CreatureKind.Mistmother => 3f,
            CreatureKind.Stilt => 5f,
            _ => 1.5f,
        };

        /// <summary>Множитель модели до роста из таблицы (без уровня).</summary>
        public static float SizeFactor(CreatureKind kind)
        {
            if (kind == CreatureKind.Moths)
                return TargetHeight(kind);
            if (!SizeCache.TryGetValue(kind, out var factor))
            {
                var builder = new CreatureBuilder(0, Seed(kind));
                Shape(kind, builder);
                SizeCache[kind] = factor = TargetHeight(kind) / builder.ModelBounds().max.y;
            }
            return factor;
        }

        /// <summary>Модель с обликом уровня (0 — обычный, 1 — матёрый, 2 — древний).</summary>
        public static CreatureBuilder Build(CreatureKind kind, int level)
        {
            var builder = new CreatureBuilder(level, Seed(kind));
            Shape(kind, builder);
            var (frostPer, frostSize) = kind switch
            {
                CreatureKind.Crawler => (3, 0.07f),
                CreatureKind.Brute => (4, 0.16f),
                CreatureKind.Shade => (2, 0.07f),
                CreatureKind.Moths => (1, 0.03f),
                CreatureKind.Mistmother => (2, 0.08f),
                _ => (2, 0.07f),
            };
            builder.Finish(frostPer, frostSize);
            return builder;
        }

        private static int Seed(CreatureKind kind) => kind switch
        {
            CreatureKind.Crawler => 50,
            CreatureKind.Mistmother => 71,
            _ => 36,
        };

        private static void Shape(CreatureKind kind, CreatureBuilder b)
        {
            switch (kind)
            {
                case CreatureKind.Crawler: Crawler(b); break;
                case CreatureKind.Brute: Brute(b); break;
                case CreatureKind.Shade: Shade(b); break;
                case CreatureKind.Moths: Moths(b); break;
                case CreatureKind.Mistmother: Mistmother(b); break;
                default: Stilt(b); break;
            }
        }

        // ---------- 1. Ползун ----------
        private static void Crawler(CreatureBuilder b)
        {
            var l = b.Level;
            const uint shell = 0x323b48, plate = 0x262d38, spike = 0x6c7682;
            // Сегментированное брюшко: спинные пластины, гребень, дыхальца.
            var segments = new[] { V(-0.06f, 0.24f, 0.16f), V(-0.18f, 0.22f, 0.15f), V(-0.3f, 0.18f, 0.13f), V(-0.41f, 0.13f, 0.1f) };
            for (var i = 0; i < segments.Length; i++)
            {
                var (z, rx, ry) = (segments[i].x, segments[i].y, segments[i].z);
                b.E(V(0f, 0.3f + i * 0.005f, z), V(rx, ry, 0.09f), i % 2 == 1 ? shell : Fog);
                b.E(V(0f, 0.3f + ry * 0.85f, z - 0.01f), V(rx * 0.85f, 0.04f, 0.08f), plate, V(0.25f, 0f, 0f), 0);
                b.K(V(0f, 0.3f + ry * 0.95f, z - 0.02f), V(0f, 1f, -0.5f), 0.022f, 0.07f - i * 0.01f, spike, 4);
                foreach (var s in Sides)
                    b.E(V(s * rx * 0.93f, 0.3f, z), 0.014f, Crack, default, 0, layer: CreatureLayer.Glow);
            }
            // Хвост-жало.
            b.Chain(new[] { V(0f, 0.3f, -0.48f), V(0f, 0.36f, -0.58f), V(0f, 0.46f, -0.6f), V(0f, 0.52f, -0.55f) }, 0.045f, 0.015f, shell);
            b.K(V(0f, 0.52f, -0.55f), V(0f, 0.2f, 1f), 0.018f, 0.07f, Bone, 4);
            // Грудь с пластиной.
            b.E(V(0f, 0.32f, 0.1f), V(0.17f, 0.13f, 0.14f), Fog);
            b.E(V(0f, 0.42f, 0.1f), V(0.14f, 0.04f, 0.12f), plate, V(0.1f, 0f, 0f), 0);
            foreach (var s in Sides)
                b.K(V(s * 0.06f, 0.44f, 0.1f), V(s * 0.3f, 1f, -0.3f), 0.016f, 0.05f, spike, 4);
            // Голова: надбровье, дёсны, зубы; снизу холодное брюхо.
            b.E(V(0f, 0.3f, 0.25f), V(0.12f, 0.1f, 0.1f), 0x262d38);
            b.E(V(0f, 0.37f, 0.3f), V(0.12f, 0.03f, 0.05f), FogDark, V(0.3f, 0f, 0f), 0);
            b.E(V(0f, 0.24f, 0.32f), V(0.065f, 0.028f, 0.03f), 0x5a2a36);
            b.Teeth(V(0f, 0.26f, 0.345f), 0.08f, 5, 0.035f, V(0f, -1f, 0.2f), Bone, 0.008f);
            b.Teeth(V(0f, 0.22f, 0.34f), 0.06f, 4, 0.025f, V(0f, 1f, 0.2f), 0xb8ae96, 0.007f);
            b.E(V(0f, 0.225f, 0.02f), V(0.1f, 0.05f, 0.2f), FogBelly, layer: CreatureLayer.Heat);
            Seam(b, 0.008f, V(0.03f, 0.4f, 0.02f), V(0.08f, 0.39f, -0.05f), V(0.06f, 0.43f, -0.14f), V(0.1f, 0.42f, -0.24f));
            Seam(b, 0.007f, V(-0.04f, 0.38f, 0.12f), V(-0.1f, 0.36f, 0.05f));
            foreach (var s in Sides)
            {
                b.K(V(s * 0.07f, 0.38f, 0.28f), V(s * 0.5f, 1f, -0.6f), 0.016f, 0.09f, Bone, 4);                              // рожки
                b.Chain(new[] { V(s * 0.05f, 0.27f, 0.33f), V(s * 0.1f, 0.22f, 0.42f), V(s * 0.05f, 0.14f, 0.46f) }, 0.025f, 0.012f, Bone); // жвалы
                b.K(V(s * 0.095f, 0.22f, 0.42f), V(-s, -0.2f, 0f), 0.008f, 0.035f, 0xe0d8c4, 3);                                // зазубрины
                b.K(V(s * 0.075f, 0.18f, 0.45f), V(-s, -0.2f, 0.2f), 0.007f, 0.03f, 0xe0d8c4, 3);
                b.Chain(new[] { V(s * 0.08f, 0.26f, 0.32f), V(s * 0.14f, 0.2f, 0.37f), V(s * 0.13f, 0.13f, 0.4f) }, 0.012f, 0.006f, FogBelly); // щупики
                b.Chain(new[] { V(s * 0.05f, 0.36f, 0.3f), V(s * 0.12f, 0.5f, 0.36f), V(s * 0.2f, 0.6f, 0.33f) }, 0.01f, 0.005f, FogBelly, noJoints: true); // усики
                b.E(V(s * 0.2f, 0.6f, 0.33f), 0.012f, Crack, default, 0, layer: CreatureLayer.Glow);
                // Лапы модели: риг ползуна заменяет их своими (бедро и голень на IK), остаются полосы и пучки у тела.
                for (var i = 0; i < 3; i++)
                {
                    float z = 0.14f - i * 0.14f, sp = (i - 1) * 0.12f;
                    var col = i == 1 ? FogDark : Fog;
                    var p = new[]
                    {
                        V(s * 0.12f, 0.3f, z), V(s * 0.3f, 0.44f, z + sp * 0.4f), V(s * 0.42f, 0.47f, z + sp * 0.7f),
                        V(s * 0.55f, 0.26f, z + sp * 1.1f), V(s * 0.6f, 0.05f, z + sp * 1.3f),
                    };
                    Limb(b, p, 0.035f, 0.014f, new[] { col, shell, col, plate }, 0x3e4856);
                    b.K(p[2], V(s * 0.3f, 1f, 0f), 0.012f, 0.06f, spike, 4);
                    b.E(V(s * 0.21f, 0.38f, z + sp * 0.2f), V(0.03f, 0.025f, 0.03f), FogBelly, default, 0);
                    b.K(p[4], V(s * 0.4f, -0.6f, 0.6f), 0.01f, 0.045f, Claw, 4);
                    b.K(p[4], V(s * 0.4f, -0.6f, -0.6f), 0.01f, 0.045f, Claw, 4);
                    Tuft(b, V(s * 0.15f, 0.32f, z), V(s, 0.4f, 0f), 3, 0.012f, 0.05f, 0x1a2029, 10 + i * 3 + (s > 0 ? 1 : 0));
                }
            }
            var eyes = new[] { V(0.05f, 0.35f, 0.33f), V(-0.05f, 0.35f, 0.33f), V(0.09f, 0.32f, 0.31f), V(-0.09f, 0.32f, 0.31f), V(0f, 0.38f, 0.32f), V(0.03f, 0.4f, 0.3f), V(-0.03f, 0.4f, 0.3f) };
            for (var i = 0; i < new[] { 2, 4, 7 }[l]; i++)
                b.Eye(eyes[i], 0.02f);
            b.Frost((V(0f, 0.45f, -0.05f), V(0f, 1f, -0.3f)), (V(0.1f, 0.43f, -0.2f), V(0.4f, 1f, -0.2f)), (V(-0.1f, 0.43f, -0.2f), V(-0.4f, 1f, -0.2f)),
                (V(0f, 0.45f, 0.1f), V(0f, 1f, 0.2f)), (V(0f, 0.42f, -0.32f), V(0f, 1f, -0.5f)));
        }

        // ---------- 2. Громила ----------
        private static void Brute(CreatureBuilder b)
        {
            var l = b.Level;
            const uint hide = 0x353e4b, plateA = StoneDark, plateB = 0x6e7378, gum = 0x5c2c34, rope = Bark;
            b.E(V(0f, 1.38f, -0.05f), V(0.78f, 0.6f, 0.58f), Fog, V(0.3f, 0f, 0f));
            b.E(V(0f, 1.0f, 0.05f), V(0.58f, 0.42f, 0.44f), FogBelly);
            for (var i = 0; i < 3; i++)
                b.E(V(0f, 1.1f - i * 0.14f, 0.43f - i * 0.01f), V(0.36f - i * 0.05f, 0.06f, 0.1f), i % 2 == 1 ? FogDark : hide, V(-0.1f, 0f, 0f), 0); // полосы брюха
            foreach (var s in Sides)
                b.E(V(s * 0.26f, 1.3f, 0.4f), V(0.26f, 0.2f, 0.13f), hide, V(-0.3f, 0f, s * 0.2f), 0, 0.2f);                                     // грудные плиты
            // Каменные плиты спины в три ряда.
            for (var i = 0; i < 4; i++)
            {
                float y = 1.82f - i * 0.08f, z = -0.35f - i * 0.12f;
                b.E(V(0f, y, z), V(0.5f - i * 0.06f, 0.14f, 0.22f), i % 2 == 1 ? plateB : FogDark, V(0.6f, 0f, 0f), 0, 0.25f);
                b.K(V(0f, y + 0.1f, z + 0.02f), V(0f, 1f, -0.7f), 0.06f, 0.2f - i * 0.03f, i % 2 == 1 ? Stone : plateB, 4);
                foreach (var s in Sides)
                    b.E(V(s * 0.45f, y - 0.14f, z + 0.06f), V(0.24f, 0.1f, 0.17f), i % 2 == 1 ? plateA : plateB, V(0.6f, 0f, s * 0.55f), 0, 0.3f);
            }
            b.E(V(0.3f, 1.86f, -0.35f), V(0.12f, 0.04f, 0.1f), Moss, V(0.6f, 0f, 0f), 0);
            b.E(V(-0.2f, 1.66f, -0.68f), V(0.1f, 0.04f, 0.08f), Moss, V(0.6f, 0f, 0f), 0);
            Tuft(b, V(0f, 1.84f, 0.1f), V(0f, 1f, 0.3f), 7, 0.04f, 0.16f, FogDark, 21);
            // Голова.
            b.E(V(0f, 1.42f, 0.58f), V(0.27f, 0.22f, 0.24f), Fog);
            b.E(V(0f, 1.57f, 0.72f), V(0.25f, 0.07f, 0.09f), FogDark, V(0.2f, 0f, 0f), 0);                  // надбровье
            foreach (var s in Sides)
                b.E(V(s * 0.1f, 1.5f, 0.76f), 0.055f, 0x0a0d12, default, 0);                                // глазницы
            b.E(V(0f, 1.43f, 0.8f), V(0.08f, 0.07f, 0.07f), hide);                                           // нос
            foreach (var s in Sides)
                b.E(V(s * 0.035f, 1.41f, 0.865f), 0.017f, 0x05070a, default, 0);                           // ноздри
            b.E(V(0f, 1.37f, 0.76f), V(0.2f, 0.06f, 0.1f), Fog);                                             // морда
            b.E(V(0f, 1.28f, 0.7f), V(0.22f, 0.1f, 0.14f), FogDark);                                         // челюсть
            b.E(V(0f, 1.33f, 0.8f), V(0.17f, 0.025f, 0.05f), gum);                                           // дёсны
            b.Teeth(V(0f, 1.36f, 0.82f), 0.18f, 6, 0.05f, V(0f, -1f, 0.1f), 0xd9d2bf, 0.015f);               // верхний ряд
            b.Teeth(V(0f, 1.3f, 0.8f), 0.24f, 5, 0.06f, V(0f, 1f, 0.1f), Bone, 0.02f);                       // нижние клыки
            b.E(V(0f, 1.3f, 0.86f), V(0.06f, 0.02f, 0.07f), 0x7a3a44, V(-0.4f, 0f, 0f));                      // язык
            Tuft(b, V(0f, 1.2f, 0.72f), V(0f, -1f, 0.3f), 4, 0.03f, 0.1f, FogDark, 22);                      // борода
            b.S(V(0.06f, 1.62f, 0.78f), V(0.16f, 1.48f, 0.8f), 0.012f, 0.01f, 0x8a8f99);                      // шрам через бровь
            Seam(b, 0.022f, V(-0.25f, 1.25f, 0.5f), V(-0.05f, 1.14f, 0.5f), V(0.12f, 1.12f, 0.5f), V(0.22f, 0.98f, 0.47f), V(0.18f, 0.84f, 0.46f));
            Seam(b, 0.016f, V(0.12f, 1.12f, 0.5f), V(0.25f, 1.24f, 0.5f), V(0.36f, 1.22f, 0.45f));
            Seam(b, 0.016f, V(-0.05f, 1.14f, 0.5f), V(-0.1f, 1.0f, 0.5f), V(-0.22f, 0.92f, 0.46f));
            Seam(b, 0.02f, V(0.15f, 1.62f, -0.72f), V(0.25f, 1.42f, -0.72f), V(0.2f, 1.2f, -0.66f));
            foreach (var s in Sides)
            {
                // Уши и рога.
                b.K(V(s * 0.23f, 1.5f, 0.55f), V(s, 0.4f, -0.3f), 0.06f, 0.15f, FogDark, 4);
                b.Chain(new[] { V(s * 0.17f, 1.6f, 0.58f), V(s * 0.3f, 1.72f, 0.54f), V(s * 0.39f, 1.79f, 0.62f) }, 0.05f, 0.03f, Bone);
                b.K(V(s * 0.39f, 1.79f, 0.62f), V(-s * 0.1f, 0.5f, 1f), 0.03f, 0.14f, 0xe0d8c4, 4);
                b.K(V(s * 0.26f, 1.29f, 0.8f), V(s * 0.2f, 1f, 0.25f), 0.035f, 0.17f, Bone, 4);                   // большие клыки
                // Плечо с каменным наплечником.
                b.E(V(s * 0.8f, 1.66f, 0.08f), V(0.33f, 0.29f, 0.31f), Fog);
                for (var i = 0; i < 3; i++)
                    b.E(V(s * (0.78f + i * 0.1f), 1.88f - i * 0.13f, 0.08f), V(0.3f - i * 0.04f, 0.08f, 0.3f - i * 0.03f), new[] { plateA, plateB, plateA }[i],
                        V(0f, 0f, s * (-0.35f - i * 0.25f)), 0, 0.25f);
                b.K(V(s * 0.92f, 1.85f, 0.05f), V(s * 0.4f, 1f, 0f), 0.07f, 0.3f, Bone);                         // рог на плече
                b.K(V(s * 0.72f, 1.92f, -0.05f), V(s * 0.2f, 1f, -0.3f), 0.04f, 0.16f, 0xb8ae96, 4);
                Tuft(b, V(s * 0.62f, 1.76f, 0.25f), V(s * 0.3f, 0.6f, 0.6f), 4, 0.035f, 0.12f, FogDark, s < 0 ? 29 : 31);
                // Рука: плечо, бицепс, наручи, кандалы, кулак-валун.
                b.Chain(new[] { V(s * 0.9f, 1.6f, 0.15f), V(s * 1.08f, 0.95f, 0.45f), V(s * 1.02f, 0.36f, 0.58f) }, 0.24f, 0.27f, Fog);
                b.E(V(s * 1.06f, 1.25f, 0.3f), V(0.2f, 0.26f, 0.2f), Fog, V(0.4f, 0f, s * 0.2f));
                b.K(V(s * 1.13f, 0.98f, 0.28f), V(s * 0.3f, -0.2f, -1f), 0.07f, 0.2f, Stone, 4);                  // шип локтя
                b.E(V(s * 1.2f, 0.75f, 0.5f), V(0.13f, 0.17f, 0.19f), plateA, V(0.2f, 0f, 0f), 0, 0.3f);
                b.E(V(s * 1.2f, 0.52f, 0.56f), V(0.12f, 0.14f, 0.18f), plateB, V(0.1f, 0f, 0f), 0, 0.3f);
                b.Ring(V(s * 1.03f, 0.44f, 0.58f), 0.27f, 0.05f, Iron, V(Mathf.PI / 2f + 0.15f, 0f, 0f));
                b.E(V(s * 1.02f, 0.2f, 0.66f), V(0.32f, 0.21f, 0.32f), StoneDark, default, 1, 0.3f);
                for (var k = 0; k < 4; k++)
                {
                    var x = s * (0.83f + k * 0.12f);
                    b.E(V(x, 0.3f, 0.88f), 0.075f, k % 2 == 1 ? 0x6a6f75u : hide, default, 0, 0.2f);          // костяшки
                    b.K(V(x, 0.3f, 0.93f), V(0f, -0.5f, 1f), 0.03f, 0.1f, Claw, 4);
                }
                b.Chain(new[] { V(s * 0.78f, 0.3f, 0.58f), V(s * 0.74f, 0.25f, 0.8f) }, 0.08f, 0.06f, Fog);      // большой палец
                b.K(V(s * 0.74f, 0.25f, 0.84f), V(-s * 0.2f, -0.3f, 1f), 0.025f, 0.08f, Claw, 4);
                // Нога.
                b.Chain(new[] { V(s * 0.35f, 0.85f, -0.2f), V(s * 0.42f, 0.45f, -0.05f), V(s * 0.44f, 0.12f, -0.15f) }, 0.2f, 0.17f, Fog);
                b.E(V(s * 0.43f, 0.47f, 0.12f), V(0.13f, 0.13f, 0.08f), plateA, default, 0, 0.3f);              // наколенник
                b.E(V(s * 0.44f, 0.07f, -0.08f), V(0.17f, 0.08f, 0.22f), FogDark, default, 0);
                for (var k = 0; k < 3; k++)
                {
                    var x = s * (0.36f + k * 0.08f);
                    b.E(V(x, 0.06f, 0.12f), 0.06f, k == 1 ? hide : FogDark, default, 0);
                    b.K(V(x, 0.05f, 0.17f), V(0f, -0.25f, 1f), 0.025f, 0.07f, Claw, 4);
                }
                Tuft(b, V(s * 0.44f, 0.2f, -0.1f), V(0f, -0.3f, 1f), 4, 0.035f, 0.1f, FogDark, s < 0 ? 39 : 41);
            }
            // Рубцы на левой руке, трещины на правой, обрывок цепи на правых кандалах.
            b.S(V(-1.22f, 1.38f, 0.3f), V(-1.28f, 1.12f, 0.4f), 0.015f, 0.012f, 0x8a8f99);
            b.S(V(-1.28f, 1.3f, 0.25f), V(-1.2f, 1.2f, 0.45f), 0.012f, 0.01f, 0x8a8f99);
            Seam(b, 0.016f, V(1.3f, 0.85f, 0.42f), V(1.26f, 0.7f, 0.5f), V(1.3f, 0.55f, 0.45f));
            for (var i = 0; i < 3; i++)
                b.Ring(V(1.3f, 0.4f - i * 0.08f, 0.6f + i * 0.02f), 0.045f, 0.015f, Iron, V(0f, i % 2 == 1 ? Mathf.PI / 2f : 0f, 0f));
            // Пояс с рваной набедренной тканью.
            b.Ring(V(0f, 0.86f, 0.05f), 1f, 0.05f, rope, V(Mathf.PI / 2f, 0f, 0f), V(0.57f, 0.44f, 1f), ringSides: 12);
            var cloth = new uint[] { 0x3b3430, 0x4a3f35, 0x2e2a28 };
            var front = new[] { (-0.2f, 0.47f, 0f, 0.5f), (-0.07f, 0.5f, 0.1f, 0.58f), (0.08f, 0.5f, -0.05f, 0.52f), (0.21f, 0.46f, 0f, 0.45f) };
            for (var i = 0; i < front.Length; i++)
            {
                var (x, z, dx, len) = front[i];
                Strip(b, V(x, 0.86f, z), len, 0.13f, cloth[i % 3], 0f, dx * 0.3f, 0.06f);
            }
            var back = new[] { (-0.25f, -0.38f, 0.5f), (0f, -0.42f, 0.6f), (0.25f, -0.38f, 0.45f) };
            for (var i = 0; i < back.Length; i++)
            {
                var (x, z, len) = back[i];
                Strip(b, V(x, 0.86f, z), len, 0.15f, cloth[(i + 1) % 3], 0f, 0f, -0.05f);
            }
            var eyes = new[] { V(0.1f, 1.5f, 0.78f), V(-0.1f, 1.5f, 0.78f), V(0f, 1.62f, 0.76f) };
            for (var i = 0; i < (l >= 2 ? 3 : 2); i++)
                b.Eye(eyes[i], 0.045f);
            b.Frost((V(0f, 1.92f, -0.2f), V(0f, 1f, -0.3f)), (V(0.45f, 1.75f, -0.3f), V(0.6f, 1f, -0.3f)), (V(-0.45f, 1.75f, -0.3f), V(-0.6f, 1f, -0.3f)),
                (V(0f, 1.75f, -0.6f), V(0f, 0.6f, -1f)), (V(0.85f, 1.95f, 0.1f), V(0.3f, 1f, 0f)), (V(-0.85f, 1.95f, 0.1f), V(-0.3f, 1f, 0f)),
                (V(1.2f, 0.8f, 0.55f), V(0.6f, 1f, 0f)), (V(-1.2f, 0.8f, 0.55f), V(-0.6f, 1f, 0f)));
        }

        // ---------- 3. Тень ----------
        private static void Shade(CreatureBuilder b)
        {
            var l = b.Level;
            const uint robe = 0x151a22, robe2 = 0x1d232c, robe3 = 0x242b36, bone = 0xa49c8a;
            for (var i = 0; i < 3; i++)
                b.K(V(0f, 0.15f + i * 0.08f, 0f), Vector3.up, 0.5f - i * 0.1f, 1.75f - i * 0.1f, new[] { robe, FogDark, robe }[i], 7);
            for (var i = 0; i < 11; i++)                                                                   // рваный подол
            {
                var a = i / 11f * Mathf.PI * 2f;
                b.K(V(Mathf.Cos(a) * 0.42f, 0.3f, Mathf.Sin(a) * 0.42f), V(Mathf.Cos(a) * 0.3f, -1f, Mathf.Sin(a) * 0.3f), 0.07f, 0.45f + i % 3 * 0.12f,
                    i % 2 == 1 ? robe : robe2, 3);
            }
            for (var i = 0; i < 7; i++)                                                                    // второй ярус лохмотьев
            {
                var a = (i + 0.5f) / 7f * Mathf.PI * 2f;
                b.K(V(Mathf.Cos(a) * 0.33f, 0.55f, Mathf.Sin(a) * 0.33f), V(Mathf.Cos(a) * 0.25f, -1f, Mathf.Sin(a) * 0.25f), 0.05f, 0.4f + i % 2 * 0.15f, robe3, 3);
            }
            // Приоткрытая грудь: рёбра и холодное сердце.
            b.B(V(0f, 1.38f, 0.12f), V(0.13f, 0.3f, 0.04f), 0x07090c, V(-0.08f, 0f, 0f));
            for (var i = 0; i < 4; i++)
                b.S(V(-0.06f, 1.28f + i * 0.07f, 0.145f - i * 0.006f), V(0.06f, 1.28f + i * 0.07f, 0.145f - i * 0.006f), 0.009f, 0.009f, bone, 4);
            b.S(V(0f, 1.24f, 0.14f), V(0f, 1.52f, 0.12f), 0.012f, 0.012f, bone, 4);
            b.E(V(0f, 1.4f, 0.155f), 0.028f, Crack, default, 0, layer: CreatureLayer.Glow);
            Seam(b, 0.007f, V(0f, 1.4f, 0.15f), V(0.09f, 1.47f, 0.13f), V(0.13f, 1.6f, 0.1f));
            Seam(b, 0.007f, V(0f, 1.4f, 0.15f), V(-0.08f, 1.3f, 0.15f), V(-0.1f, 1.12f, 0.19f));
            // Пояс с погасшим фонарём и костяным амулетом.
            b.Ring(V(0f, 1.13f, 0f), 0.225f, 0.022f, Bark, V(Mathf.PI / 2f, 0f, 0f), ringSides: 9);
            b.S(V(0.2f, 1.12f, 0.08f), V(0.36f, 0.98f, 0.1f), 0.008f, 0.008f, Bark);
            b.B(V(0.36f, 0.9f, 0.1f), V(0.09f, 0.12f, 0.09f), Iron);
            b.B(V(0.36f, 0.9f, 0.1f), V(0.1f, 0.08f, 0.06f), 0x2a3340);
            b.K(V(0.36f, 0.96f, 0.1f), Vector3.up, 0.07f, 0.06f, Iron, 4);
            b.S(V(-0.18f, 1.12f, 0.12f), V(-0.27f, 0.95f, 0.14f), 0.006f, 0.006f, Bark);
            b.E(V(-0.27f, 0.92f, 0.14f), V(0.03f, 0.045f, 0.02f), bone, default, 0);
            b.K(V(-0.27f, 0.88f, 0.14f), Vector3.down, 0.012f, 0.06f, bone, 3);
            // Капюшон с пустотой лица и челюстью во тьме.
            b.E(V(0f, 1.95f, 0f), V(0.25f, 0.29f, 0.25f), robe);
            b.K(V(0f, 2.1f, -0.12f), V(0f, 0.5f, -1f), 0.18f, 0.45f, robe);
            b.K(V(0.04f, 2.0f, -0.2f), V(0.2f, -0.6f, -1f), 0.1f, 0.4f, robe2, 4);
            b.Ring(V(0f, 1.92f, 0.17f), 0.15f, 0.04f, robe3, V(-0.15f, 0f, 0f), V(1f, 1.3f, 1f), ringSides: 9);
            b.E(V(0f, 1.92f, 0.13f), V(0.16f, 0.19f, 0.1f), 0x05070a);
            b.Teeth(V(0f, 1.79f, 0.2f), 0.08f, 5, 0.03f, V(0f, 1f, 0.15f), bone, 0.008f);
            // Наплечная накидка в два слоя с лентами.
            b.E(V(0f, 1.6f, 0.05f), V(0.3f, 0.12f, 0.22f), FogDark, default, 0);
            b.E(V(0f, 1.68f, 0.02f), V(0.24f, 0.09f, 0.19f), robe3, default, 0);
            for (var i = 0; i < 9; i++)
            {
                var a = -Mathf.PI * 0.1f + i / 8f * Mathf.PI * 1.2f + Mathf.PI;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                Strip(b, V(ca * 0.25f, 1.58f, sa * 0.18f - 0.02f), 0.25f + i % 3 * 0.1f, 0.1f, i % 2 == 1 ? robe2 : robe3, -a - Mathf.PI / 2f, ca * 0.06f, sa * 0.06f);
            }
            foreach (var s in Sides)
            {
                b.Chain(new[] { V(s * 0.25f, 1.62f, 0.05f), V(s * 0.48f, 1.2f, 0.2f), V(s * 0.6f, 0.74f, 0.32f) }, 0.055f, 0.035f, robe);
                b.K(V(s * 0.57f, 0.86f, 0.29f), V(-s * 0.25f, 1f, -0.25f), 0.12f, 0.42f, robe2);                // рукав
                Strip(b, V(s * 0.62f, 0.86f, 0.25f), 0.28f, 0.06f, robe3, 0f, s * 0.03f, -0.04f);
                Strip(b, V(s * 0.52f, 0.86f, 0.3f), 0.36f, 0.05f, robe, 0f, s * 0.01f, -0.03f);
                // Кисть: ладонь, четыре пальца по три фаланги с ногтями, большой палец.
                b.E(V(s * 0.6f, 0.7f, 0.32f), V(0.045f, 0.07f, 0.04f), 0x2a3038, default, 0);
                for (var f = 0; f < 4; f++)
                {
                    float t = f / 3f - 0.5f, x0 = s * (0.6f + t * 0.06f), z0 = 0.32f + t * 0.08f;
                    var p = new[]
                    {
                        V(x0, 0.66f, z0), V(s * (0.62f + t * 0.1f), 0.54f, z0 + t * 0.05f + 0.02f), V(s * (0.63f + t * 0.13f), 0.43f, z0 + t * 0.09f + 0.04f),
                        V(s * (0.62f + t * 0.14f), 0.34f, z0 + t * 0.12f + 0.07f),
                    };
                    b.Chain(p, 0.014f, 0.007f, Claw);
                    b.K(p[3], V(0f, -1f, 0.3f), 0.007f, 0.05f, 0x5b636e, 3);
                }
                b.Chain(new[] { V(s * 0.57f, 0.68f, 0.35f), V(s * 0.52f, 0.6f, 0.4f), V(s * 0.5f, 0.52f, 0.42f) }, 0.012f, 0.007f, Claw);
            }
            var eyes = new[] { V(0.06f, 1.95f, 0.21f), V(-0.06f, 1.95f, 0.21f), V(0f, 2.03f, 0.2f), V(0.11f, 1.88f, 0.19f), V(-0.11f, 1.88f, 0.19f) };
            for (var i = 0; i < new[] { 2, 3, 5 }[l]; i++)
                b.Eye(eyes[i], 0.022f);
            b.Frost((V(0f, 2.2f, -0.1f), V(0f, 1f, -0.4f)), (V(0.15f, 2.15f, -0.05f), V(0.5f, 1f, -0.2f)), (V(-0.15f, 2.15f, -0.05f), V(-0.5f, 1f, -0.2f)),
                (V(0.25f, 1.68f, 0f), V(0.6f, 1f, 0f)), (V(-0.25f, 1.68f, 0f), V(-0.6f, 1f, 0f)));
        }

        // ---------- 4. Мотыльки: каждый мотылёк — своя группа, крылья на шарнирах ----------
        private static void Moths(CreatureBuilder b)
        {
            var l = b.Level;
            var r = new System.Random(4);
            float Next() => (float)r.NextDouble();
            var count = new[] { 4, 5, 6 }[l];
            for (var i = 0; i < count; i++)
            {
                var scale = 0.9f + Next() * 0.3f;
                var at = V((Next() - 0.5f) * 1.4f, 1.1f + Next() * 1.1f, (Next() - 0.5f) * 1.2f);
                b.BeginGroup(Matrix4x4.TRS(at, Quaternion.AngleAxis(Next() * 6f * Mathf.Rad2Deg, Vector3.up), Vector3.one * scale));
                b.E(V(0f, 0f, 0.05f), V(0.075f, 0.065f, 0.085f), Fog);                                                       // грудь
                for (var k = 0; k < 4; k++)                                                                                 // брюшко сегментами
                    b.E(V(0f, -0.005f - k * 0.006f, -0.04f - k * 0.065f), V(0.06f - k * 0.011f, 0.052f - k * 0.009f, 0.045f), k % 2 == 1 ? 0x5a6573u : FogBelly, default, 0);
                b.K(V(0f, -0.03f, -0.27f), V(0f, -0.2f, -1f), 0.015f, 0.05f, FogDark, 4);
                b.E(V(0f, 0.01f, 0.15f), 0.055f, Fog);                                                                       // голова
                Tuft(b, V(0f, 0.04f, 0.1f), V(0f, 1f, 0.4f), 6, 0.015f, 0.06f, 0x9aa3ad, 60 + i);                           // меховой воротник
                b.Chain(new[] { V(0f, -0.02f, 0.19f), V(0f, -0.06f, 0.22f), V(0f, -0.07f, 0.19f) }, 0.006f, 0.004f, Bone);    // хоботок
                foreach (var side in Sides)
                {
                    var sd = (int)side;
                    var wing = b.Wing(V(side * 0.04f, 0.02f, 0.03f), sd, 0.42f, 0.3f, 0x8f98a3, 0x6f7884, 0.9f, 4);
                    b.E(V(side * 0.22f, 0.03f, 0.04f), V(0.05f, 0.012f, 0.045f), 0x3a4250, default, 0, wing: wing);           // кольцо глазка
                    b.E(V(side * 0.22f, 0.04f, 0.04f), V(0.028f, 0.012f, 0.025f), Crack, default, 0, layer: CreatureLayer.Glow, wing: wing);
                    b.S(V(side * 0.05f, 0.03f, 0.03f), V(side * 0.36f, 0.03f, -0.04f), 0.004f, 0.003f, 0x4a525e, 3, wing: wing); // жилка
                    b.Wing(V(side * 0.03f, -0.01f, -0.06f), sd, 0.3f, 0.22f, 0x7a8390, 0x5d6672, 0.7f, 3);                     // задние крылья
                    // Перистые усики.
                    b.S(V(side * 0.02f, 0.04f, 0.18f), V(side * 0.12f, 0.12f, 0.32f), 0.006f, 0.003f, Bone);
                    for (var k = 1; k <= 3; k++)
                    {
                        var t = k / 4f;
                        b.K(V(side * (0.02f + 0.1f * t), 0.04f + 0.08f * t, 0.18f + 0.14f * t), V(side, 0.3f, 0.2f), 0.004f, 0.035f, 0xd9d2bf, 3);
                    }
                    // Три пары ножек.
                    for (var k = 0; k < 3; k++)
                        b.Chain(new[] { V(side * 0.04f, -0.04f, 0.09f - k * 0.05f), V(side * 0.1f, -0.07f, 0.1f - k * 0.06f), V(side * 0.11f, -0.13f, 0.12f - k * 0.08f) },
                            0.006f, 0.003f, FogDark, noJoints: true);
                }
                b.Eye(V(0.03f, 0.03f, 0.19f), 0.015f);
                b.Eye(V(-0.03f, 0.03f, 0.19f), 0.015f);
                b.EndGroup();
            }
            // Пыльца-иней вокруг стайки (риг её не берёт: она задаёт только размер изморози на земле).
            var r2 = new System.Random(44);
            for (var i = 0; i < 6 + l * 3; i++)
                b.E(V(((float)r2.NextDouble() - 0.5f) * 1.2f, 0.3f + (float)r2.NextDouble() * 1.1f, ((float)r2.NextDouble() - 0.5f) * 1.0f), 0.02f, Crack, default, 0,
                    layer: CreatureLayer.Glow);
        }

        // ---------- 5. Туманница ----------
        private static void Mistmother(CreatureBuilder b)
        {
            var l = b.Level;
            const uint hide = 0x353e4b, rib = 0x26303c, shawl = 0x3a4250;
            b.E(V(0f, 1.05f, -0.15f), V(0.45f, 0.5f, 0.4f), Fog);                                                       // тело
            for (var i = 0; i < 3; i++)                                                                                 // пластины груди
                b.E(V(0f, 1.12f - i * 0.15f, 0.2f - i * 0.02f), V(0.3f - i * 0.04f, 0.07f, 0.1f), i % 2 == 1 ? FogDark : hide, V(-0.2f, 0f, 0f), 0);
            for (var i = 0; i < 3; i++)                                                                                 // брюшные кольца
                b.E(V(0f, 0.7f + i * 0.05f, -0.2f - i * 0.12f), V(0.3f - i * 0.04f, 0.08f, 0.14f), i % 2 == 1 ? FogBelly : 0x56616fu, default, 0);
            // Мешок с туманом, рёбра-обручи и светящиеся жилы.
            b.E(V(0f, 1.55f, -0.45f), V(0.62f, 0.55f, 0.55f), 0x8fa6bd, default, 2, 0.08f, CreatureLayer.Glass);
            b.E(V(0f, 1.55f, -0.45f), V(0.3f, 0.28f, 0.3f), 0x6f88a4, default, 1, layer: CreatureLayer.Glass);
            foreach (var xo in new[] { -0.38f, 0f, 0.38f })
            {
                var k = Mathf.Sqrt(1f - (xo / 0.64f) * (xo / 0.64f));
                b.Ring(V(xo, 1.55f, -0.45f), 1f, 0.028f, rib, V(0f, Mathf.PI / 2f, 0f), V(0.56f * k, 0.57f * k, 1f), ringSides: 12);
            }
            b.Ring(V(0f, 1.25f, -0.45f), 1f, 0.03f, rib, V(Mathf.PI / 2f, 0f, 0f), V(0.5f, 0.45f, 1f), ringSides: 12);
            for (var k = 0; k < 5; k++)
            {
                var a = -0.9f + k * 0.45f;
                var points = new Vector3[4];
                for (var t = 0; t <= 3; t++)
                {
                    var th = 0.5f + t * 0.45f;
                    points[t] = V(Mathf.Sin(th) * Mathf.Sin(a) * 0.64f, 1.55f + Mathf.Cos(th) * 0.57f, -0.45f + Mathf.Sin(th) * Mathf.Cos(a) * 0.57f * (k % 2 == 1 ? 1f : -1f));
                }
                Seam(b, 0.01f, points);
            }
            // Трубы-выдохи с раструбами.
            foreach (var s in Sides)
            {
                b.Chain(new[] { V(s * 0.3f, 1.92f, -0.5f), V(s * 0.4f, 2.08f, -0.6f), V(s * 0.46f, 2.2f, -0.62f) }, 0.07f, 0.05f, FogDark, 6);
                b.K(V(s * 0.47f, 2.25f, -0.62f), V(-s * 0.2f, -1f, 0f), 0.1f, 0.14f, hide, 6);
                b.Ring(V(s * 0.37f, 2.04f, -0.57f), 0.07f, 0.015f, rib, V(Mathf.PI / 2f, 0f, s * 0.5f));
            }
            b.Chain(new[] { V(0f, 2.02f, -0.7f), V(0f, 2.12f, -0.8f) }, 0.05f, 0.04f, FogDark, 6);
            b.K(V(0f, 2.16f, -0.82f), V(0f, -1f, 0.2f), 0.07f, 0.1f, hide, 6);
            // Накидка на загривке с лентами.
            b.E(V(0f, 1.42f, 0.02f), V(0.36f, 0.12f, 0.26f), shawl, V(0.2f, 0f, 0f), 0, 0.25f);
            for (var i = 0; i < 6; i++)
            {
                var x = (i / 5f - 0.5f) * 0.7f;
                Strip(b, V(x, 1.36f, 0.12f - Mathf.Abs(x) * 0.2f), 0.25f + i % 3 * 0.1f, 0.07f, i % 2 == 1 ? shawl : 0x2b333fu, 0f, x * 0.2f, 0.03f);
            }
            // Голова, опущенная вниз.
            b.E(V(0f, 1.35f, 0.25f), V(0.2f, 0.22f, 0.2f), FogDark);
            b.E(V(0f, 1.47f, 0.37f), V(0.17f, 0.05f, 0.07f), hide, V(0.3f, 0f, 0f), 0);                                // надбровье
            for (var i = 0; i < 3; i++)                                                                                 // морщины лба
                b.E(V(0f, 1.53f + i * 0.04f, 0.3f - i * 0.05f), V(0.14f - i * 0.02f, 0.025f, 0.06f), i % 2 == 1 ? Fog : hide, default, 0);
            foreach (var s in Sides)
            {
                b.K(V(s * 0.17f, 1.42f, 0.2f), V(s, -0.6f, -0.4f), 0.05f, 0.16f, FogDark, 4);                           // висячие уши
                b.S(V(s * 0.08f, 1.25f, 0.4f), V(s * 0.2f, 1.1f, 0.45f), 0.005f, 0.003f, FogBelly);                    // вибриссы
                b.S(V(s * 0.06f, 1.22f, 0.42f), V(s * 0.16f, 1.04f, 0.5f), 0.005f, 0.003f, FogBelly);
            }
            // Хобот-выдох из колец.
            var trunk = new[] { V(0f, 1.28f, 0.4f), V(0f, 1.12f, 0.5f), V(0f, 0.97f, 0.55f), V(0f, 0.85f, 0.52f) };
            b.Chain(trunk, 0.075f, 0.04f, FogDark);
            for (var i = 0; i < 3; i++)
                b.Ring(trunk[i + 1], 0.065f - i * 0.008f, 0.016f, i % 2 == 1 ? hide : Fog, V(Mathf.PI / 2f - 0.5f + i * 0.3f, 0f, 0f));
            b.K(V(0f, 0.8f, 0.5f), V(0f, 1f, 0.15f), 0.065f, 0.1f, hide, 6);                                            // раструб
            foreach (var s in Sides)
                b.E(V(s * 0.02f, 0.78f, 0.5f), 0.012f, 0x05070a, default, 0);                                           // ноздри
            // Ноги с шипами и когтями.
            foreach (var s in Sides)
            {
                var front = new[] { V(s * 0.3f, 0.9f, 0f), V(s * 0.48f, 0.62f, 0.1f), V(s * 0.52f, 0.35f, 0.17f), V(s * 0.45f, 0.05f, 0.2f) };
                var hind = new[] { V(s * 0.25f, 0.85f, -0.4f), V(s * 0.46f, 0.58f, -0.55f), V(s * 0.52f, 0.3f, -0.66f), V(s * 0.48f, 0.05f, -0.7f) };
                var legs = new[] { front, hind };
                for (var k = 0; k < 2; k++)
                {
                    var p = legs[k];
                    Limb(b, p, 0.065f, 0.03f, new[] { FogDark, Fog, FogDark }, hide);
                    b.K(p[1], V(s * 0.5f, 0.6f, k == 1 ? -0.4f : 0.3f), 0.02f, 0.08f, Bone, 4);
                    b.K(p[3], V(s * 0.2f, -0.5f, 0.8f), 0.015f, 0.06f, Claw, 4);
                    b.K(p[3], V(s * 0.6f, -0.5f, 0.2f), 0.015f, 0.06f, Claw, 4);
                    Tuft(b, V(s * 0.34f, 0.85f, p[0].z), V(s, -0.2f, 0f), 4, 0.025f, 0.09f, 0x1a2029, 70 + k * 2 + (s > 0 ? 1 : 0));
                }
            }
            // Свисающие из мешка нити с каплями тумана.
            var drops = new[] { (0.2f, -0.3f), (-0.25f, -0.55f), (0.05f, -0.75f) };
            for (var i = 0; i < drops.Length; i++)
            {
                var (x, z) = drops[i];
                float y0 = 1.1f, y1 = 0.6f - i * 0.1f;
                b.S(V(x, y0, z), V(x, y1, z), 0.008f, 0.004f, FogBelly);
                b.E(V(x, y1 - 0.03f, z), 0.03f, Crack, default, 0, layer: CreatureLayer.Glow);
            }
            b.Eye(V(0.07f, 1.38f, 0.42f), 0.025f);
            b.Eye(V(-0.07f, 1.38f, 0.42f), 0.025f);
            if (l >= 1)
                b.Eye(V(0f, 1.45f, 0.42f), 0.02f);
            if (l >= 2)
            {
                b.Eye(V(0.13f, 1.43f, 0.38f), 0.015f);
                b.Eye(V(-0.13f, 1.43f, 0.38f), 0.015f);
            }
            b.Frost((V(0f, 2.08f, -0.4f), V(0f, 1f, 0f)), (V(0.38f, 2.0f, -0.45f), V(0.5f, 1f, -0.3f)), (V(-0.38f, 2.0f, -0.45f), V(-0.5f, 1f, -0.3f)),
                (V(0f, 1.5f, 0.1f), V(0f, 1f, 0.3f)));
        }

        // ---------- 6. Ходуля ----------
        private static void Stilt(CreatureBuilder b)
        {
            var l = b.Level;
            const uint plate = 0x3a4452, joint = 0x6c7480, dark = 0x262d38;
            b.E(V(0f, 2.9f, 0f), V(0.32f, 0.2f, 0.36f), Fog);
            b.E(V(0f, 2.8f, 0f), V(0.25f, 0.12f, 0.28f), FogBelly);
            for (var i = 0; i < 3; i++)                                                                                 // брюшные кольца
                b.E(V(0f, 2.73f - i * 0.015f, 0.1f - i * 0.12f), V(0.2f - i * 0.03f, 0.05f, 0.07f), i % 2 == 1 ? FogBelly : 0x56616fu, default, 0);
            // Карапакс из налегающих пластин с гребнем.
            for (var i = 0; i < 4; i++)
            {
                var z = 0.2f - i * 0.13f;
                b.E(V(0f, 3.06f - Mathf.Abs(z) * 0.25f, z), V(0.26f - Mathf.Abs(i - 1.5f) * 0.03f, 0.06f, 0.11f), i % 2 == 1 ? plate : FogDark, V(0.25f, 0f, 0f), 0, 0.2f);
                b.K(V(0f, 3.1f - Mathf.Abs(z) * 0.25f, z), V(0f, 1f, -0.5f), 0.025f, 0.1f, joint, 4);
            }
            foreach (var s in Sides)
                b.E(V(s * 0.22f, 2.97f, -0.02f), V(0.1f, 0.05f, 0.26f), dark, V(0f, 0f, s * 0.5f), 0);
            Seam(b, 0.009f, V(0.08f, 3.08f, 0.12f), V(0.12f, 3.06f, 0f), V(0.08f, 3.04f, -0.15f), V(0.14f, 3.0f, -0.25f));
            Tuft(b, V(0f, 2.95f, -0.32f), V(0f, 0.3f, -1f), 6, 0.025f, 0.12f, 0x1a2029, 80);
            // Голова с жвалами.
            b.E(V(0f, 2.8f, 0.2f), V(0.14f, 0.09f, 0.08f), dark);
            b.E(V(0f, 2.85f, 0.24f), V(0.13f, 0.03f, 0.05f), FogDark, V(0.3f, 0f, 0f), 0);
            b.E(V(0f, 2.7f, 0.26f), V(0.05f, 0.02f, 0.025f), 0x5a2a36);
            b.Teeth(V(0f, 2.71f, 0.275f), 0.06f, 4, 0.03f, V(0f, -1f, 0.2f), Bone, 0.007f);
            foreach (var s in Sides)
            {
                b.Chain(new[] { V(s * 0.05f, 2.72f, 0.25f), V(s * 0.1f, 2.62f, 0.32f), V(s * 0.05f, 2.52f, 0.34f) }, 0.02f, 0.01f, Bone);
                b.Chain(new[] { V(s * 0.09f, 2.74f, 0.22f), V(s * 0.15f, 2.62f, 0.25f), V(s * 0.14f, 2.5f, 0.26f) }, 0.01f, 0.005f, FogBelly);
            }
            // Четыре ноги с лишним суставом: риг ходули заменяет их своими (бедро и голень на IK).
            for (var i = 0; i < 4; i++)
            {
                var a = i / 4f * Mathf.PI * 2f + Mathf.PI / 4f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                Vector3 P(float k, float y) => V(ca * k, y, sa * k);
                var points = new[] { P(0.2f, 2.85f), P(0.55f, 3.2f), P(0.85f, 3.36f), P(1.15f, 2.5f), P(1.36f, 1.5f), P(1.47f, 0.62f), P(1.5f, 0.08f) };
                Limb(b, points, 0.05f, 0.014f, i % 2 == 1 ? new[] { FogDark, Fog } : new[] { Fog, dark }, joint);
                b.K(points[2], V(ca * 0.4f, 1f, sa * 0.4f), 0.025f, 0.14f, Bone, 4);                                    // шип колена
                b.K(points[4], V(ca, 0.2f, sa), 0.015f, 0.08f, joint, 4);
                b.E(P(1.0f, 2.95f), 0.045f, plate, default, 0);                                                          // утолщение бедра
                Tuft(b, P(0.33f, 2.98f), V(ca, 0.5f, sa), 3, 0.015f, 0.07f, 0x1a2029, 90 + i);
                for (var t = -1; t <= 1; t++)                                                                           // три когтя
                {
                    var ta = a + t * 0.7f;
                    b.K(P(1.5f, 0.08f), V(Mathf.Cos(ta), -0.6f, Mathf.Sin(ta)), 0.012f, 0.07f, Claw, 4);
                }
            }
            // Свисающие нити-щупальца с каплями, костяной оберег.
            for (var i = 0; i < 7; i++)
            {
                var a = i / 7f * Mathf.PI * 2f;
                var y1 = 2.0f - i % 3 * 0.25f;
                b.Chain(new[] { V(Mathf.Cos(a) * 0.12f, 2.75f, Mathf.Sin(a) * 0.12f), V(Mathf.Cos(a) * 0.17f, 2.35f, Mathf.Sin(a) * 0.17f), V(Mathf.Cos(a) * 0.2f, y1, Mathf.Sin(a) * 0.2f) },
                    0.015f, 0.004f, i % 2 == 1 ? FogBelly : 0x56616fu, noJoints: true);
                if (i % 2 == 0)
                    b.E(V(Mathf.Cos(a) * 0.2f, y1 - 0.02f, Mathf.Sin(a) * 0.2f), 0.02f, Crack, default, 0, layer: CreatureLayer.Glow);
            }
            b.S(V(0.05f, 2.74f, -0.1f), V(0.08f, 2.35f, -0.12f), 0.005f, 0.005f, Bark);
            b.S(V(0.02f, 2.33f, -0.12f), V(0.14f, 2.37f, -0.12f), 0.015f, 0.012f, Bone);
            b.S(V(0.08f, 2.3f, -0.12f), V(0.08f, 2.18f, -0.12f), 0.012f, 0.008f, Bone);
            var eyes = new[] { V(0.08f, 2.8f, 0.26f), V(-0.08f, 2.8f, 0.26f), V(0f, 2.75f, 0.28f), V(0.13f, 2.84f, 0.21f), V(-0.13f, 2.84f, 0.21f) };
            for (var i = 0; i < new[] { 2, 3, 5 }[l]; i++)
                b.Eye(eyes[i], 0.025f);
            b.Frost((V(0f, 3.12f, 0f), V(0f, 1f, 0f)), (V(0.15f, 3.05f, -0.15f), V(0.4f, 1f, -0.4f)), (V(-0.15f, 3.05f, 0.15f), V(-0.4f, 1f, 0.4f)),
                (V(0.6f, 3.3f, 0.6f), V(0.3f, 1f, 0.3f)), (V(-0.6f, 3.3f, -0.6f), V(-0.3f, 1f, -0.3f)));
        }

        // ---------- общие помощники ----------

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        /// <summary>Светящаяся трещина-ломаная.</summary>
        private static void Seam(CreatureBuilder b, float width, params Vector3[] points)
        {
            for (var i = 0; i < points.Length - 1; i++)
                b.S(points[i], points[i + 1], width, width * 0.7f, CreaturePalette.Crack, 4, CreatureLayer.Glow);
        }

        /// <summary>Рваная тканевая лента: два сужающихся куска и зубец; out — куда отходит низ.</summary>
        private static void Strip(CreatureBuilder b, Vector3 top, float length, float width, uint color, float yaw, float outX, float outZ)
        {
            var middle = V(top.x + outX * 0.5f, top.y - length * 0.55f, top.z + outZ * 0.5f);
            var end = V(top.x + outX, top.y - length, top.z + outZ);
            b.Plank(top, middle, width, 0.015f, color, yaw);
            b.Plank(middle, end, width * 0.7f, 0.015f, color, yaw);
            b.K(V(end.x, end.y + 0.03f, end.z), V(outX * 0.3f, -1f, outZ * 0.3f), width * 0.35f, length * 0.2f, color, 3);
        }

        /// <summary>Пучок шерсти из маленьких конусов.</summary>
        private static void Tuft(CreatureBuilder b, Vector3 center, Vector3 direction, int count, float radius, float height, uint color, int seed)
        {
            var random = new System.Random(seed);
            float Next() => (float)random.NextDouble() - 0.5f;
            for (var i = 0; i < count; i++)
            {
                var dir = direction + V(Next() * 0.9f, Next() * 0.9f, Next() * 0.9f);
                var at = center + V(Next() * radius * 3f, Next() * radius * 2f, Next() * radius * 3f);
                b.K(at, dir, radius, height * (0.7f + (Next() + 0.5f) * 0.6f), color, 3);
            }
        }

        /// <summary>Сустав-цепочка: сегменты чередуют цвета, суставы своего цвета.</summary>
        private static void Limb(CreatureBuilder b, IReadOnlyList<Vector3> points, float r0, float r1, IReadOnlyList<uint> colors, uint jointColor)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var ra = r0 + (r1 - r0) * i / (points.Count - 1);
                var rb = r0 + (r1 - r0) * (i + 1) / (points.Count - 1);
                b.S(points[i], points[i + 1], ra, rb, colors[i % colors.Count]);
                if (i > 0)
                    b.E(points[i], ra * 1.3f, jointColor, default, 0, 0.25f);
            }
        }
    }
}
