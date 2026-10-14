using System;
using System.Collections.Generic;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Снаряжение роли поверх тела из <see cref="WorkerMeshBuilder"/>: инструмент или оружие, ноша и щит на спине,
    /// шапка или шлем, фартук, броня. Те же единицы: доли роста, ноги в (0, 0, 0), лицо в +Z, правая рука — +X.
    /// Инструмент лежит в кулаке опущенной руки рукоятью вперёд-вверх: головка перед кулаком, черенок уходит назад-вниз,
    /// как носят топор или молоток «за шейку». Всё, что на спине, прижато к телу и держится ремнями, которые идут
    /// по поверхности туловища (<see cref="TorsoPoint"/>), а не по воздуху.
    /// </summary>
    internal static class WorkerGearBuilder
    {
        private static readonly Color Wood = new(0.45f, 0.31f, 0.19f);
        private static readonly Color WoodLight = new(0.66f, 0.5f, 0.31f);
        private static readonly Color Bark = new(0.33f, 0.22f, 0.14f);
        private static readonly Color Metal = new(0.6f, 0.62f, 0.66f);
        private static readonly Color MetalDark = new(0.34f, 0.35f, 0.38f);
        private static readonly Color Mail = new(0.47f, 0.49f, 0.52f);
        private static readonly Color Rope = new(0.66f, 0.55f, 0.35f);
        private static readonly Color Linen = new(0.66f, 0.6f, 0.47f);
        private static readonly Color Apron = new(0.46f, 0.33f, 0.21f);
        private static readonly Color OiledLeather = new(0.27f, 0.22f, 0.18f);
        private static readonly Color Strap = new(0.3f, 0.21f, 0.14f);
        private static readonly Color Olive = new(0.32f, 0.34f, 0.2f);
        private static readonly Color Fletching = new(0.78f, 0.25f, 0.18f);

        /// <summary>Ось хвата инструмента: вперёд и вверх, в плоскости движения.</summary>
        private static readonly Vector3 ToolAxis = new Vector3(0f, 0.48f, 0.88f).normalized;
        private static readonly Vector3 SpearAxis = new Vector3(0f, 1f, 0.22f).normalized;
        private static readonly Vector3 BowAxis = new Vector3(0f, 0.82f, 0.57f).normalized;

        /// <summary>Сечения туловища: высота, полуширина, полуглубина, сдвиг вперёд (рубаха с поясом).</summary>
        private static readonly (float y, float rx, float rz, float cz)[] Torso =
        {
            (0.37f, 0.148f, 0.098f, 0.002f), (0.44f, 0.128f, 0.086f, 0f), (0.5f, 0.117f, 0.081f, 0f), (0.528f, 0.117f, 0.081f, 0f),
            (0.6f, 0.13f, 0.088f, 0.01f), (0.68f, 0.148f, 0.088f, 0.008f), (0.735f, 0.14f, 0.074f, 0f),
        };

        /// <summary>
        /// Сечения того, что надето на корпус под пелериной (y, полуширина, полуглубина, сдвиг вперёд; доли роста):
        /// кольчуга, стёганка или рубаха из <see cref="WorkerMeshBuilder"/>.
        /// </summary>
        public static Vector4[] TorsoUnderCape(WorkerRole role) => role switch
        {
            WorkerRole.Swordsman => new[]
            {
                new Vector4(0.505f, 0.118f, 0.082f, 0f), new Vector4(0.6f, 0.136f, 0.094f, 0.01f), new Vector4(0.68f, 0.154f, 0.094f, 0.008f),
                new Vector4(0.735f, 0.146f, 0.08f, 0f), new Vector4(0.772f, 0.072f, 0.056f, 0f),
            },
            WorkerRole.Spearman => new[]
            {
                new Vector4(0.505f, 0.122f, 0.086f, 0f), new Vector4(0.6f, 0.139f, 0.097f, 0.01f), new Vector4(0.68f, 0.157f, 0.097f, 0.008f),
                new Vector4(0.735f, 0.148f, 0.082f, 0f), new Vector4(0.772f, 0.072f, 0.056f, 0f),
            },
            _ => new[]
            {
                new Vector4(0.505f, 0.112f, 0.076f, 0f), new Vector4(0.6f, 0.13f, 0.088f, 0.01f), new Vector4(0.68f, 0.148f, 0.088f, 0.008f),
                new Vector4(0.735f, 0.14f, 0.074f, 0f), new Vector4(0.765f, 0.07f, 0.052f, 0f), new Vector4(0.775f, 0.047f, 0.046f, 0.004f),
            },
        };

        /// <summary>
        /// Что лежит на корпусе под пелериной поверх рубахи (лямки, ремни, верёвка) — насколько оно толще рубахи, доли роста.
        /// </summary>
        public static float UnderCapeExtra(WorkerRole role) => role switch
        {
            WorkerRole.Woodcutter => 0.036f,
            WorkerRole.Swordsman => 0.018f,
            WorkerRole.Stoker or WorkerRole.Archer => 0.016f,
            _ => 0f,
        };

        /// <summary>
        /// Куда сдвинуть мешок на спине, чтобы он лёг поверх снаряжения (вязанка, щит, доски, колчан): назад от спины (x)
        /// и вверх (y), доли роста.
        /// </summary>
        public static Vector2 BackLoadShift(WorkerRole role) => role switch
        {
            // Вязанка поперёк спины ниже мешка: мешок над ней и чуть дальше от спины.
            WorkerRole.Stoker => new Vector2(0.08f, 0.09f),
            WorkerRole.Swordsman => new Vector2(0.05f, 0f),
            WorkerRole.Builder => new Vector2(0.06f, 0f),
            WorkerRole.Archer => new Vector2(0.09f, 0f),
            _ => Vector2.zero,
        };

        /// <summary>Насколько рукава брони толще рукава рубахи (доли роста).</summary>
        public static float SleeveExtra(WorkerRole role) => role switch
        {
            WorkerRole.Swordsman => 0.005f,
            WorkerRole.Spearman => 0.007f,
            _ => 0f,
        };

        /// <summary>Центр кулака: через него проходит рукоять.</summary>
        public static Vector3 FistCenter(float side) => new(side * 0.179f, 0.383f, 0.05f);

        /// <summary>Ось хвата в руке side (+1 — правая), если рука что-то держит; иначе null — рука раскрыта.</summary>
        public static Vector3? GripAxis(WorkerRole role, float side) => role switch
        {
            WorkerRole.Woodcutter or WorkerRole.Digger or WorkerRole.Builder or WorkerRole.Stoker when side > 0f => ToolAxis,
            WorkerRole.Spearman when side > 0f => SpearAxis,
            WorkerRole.Archer when side < 0f => BowAxis,
            _ => null,
        };

        public static void Add(FacetedMesh mesh, Matrix4x4 scale, WorkerLook look)
        {
            var gear = new Gear(mesh, scale);
            // Каждая вещь — в своей части: в руке — в предплечье, шапка — в голове, всё ниже пояса — в подоле, остальное — в корпусе.
            void On(WorkerPart part) => mesh.Part = (int)part;
            switch (look.Role)
            {
                case WorkerRole.Woodcutter:
                    On(WorkerPart.ForearmRight);
                    Axe(gear);
                    On(WorkerPart.Torso);
                    RopeCoil(gear);
                    break;
                case WorkerRole.Digger:
                    LeatherApron(gear);
                    On(WorkerPart.Hips);
                    MinerPouch(gear);
                    On(WorkerPart.ForearmRight);
                    Pickaxe(gear);
                    break;
                case WorkerRole.Builder:
                    On(WorkerPart.Torso);
                    Planks(gear);
                    On(WorkerPart.Hips);
                    ToolBag(gear);
                    On(WorkerPart.ForearmRight);
                    Hammer(gear);
                    break;
                case WorkerRole.Stoker:
                    On(WorkerPart.Torso);
                    Logs(gear);
                    On(WorkerPart.Head);
                    StockingCap(gear, look.Hood);
                    On(WorkerPart.ForearmRight);
                    Poker(gear);
                    break;
                case WorkerRole.Swordsman:
                    On(WorkerPart.Torso);
                    MailShirt(gear);
                    Shield(gear, look.Hood);
                    On(WorkerPart.Hips);
                    SheathedSword(gear);
                    On(WorkerPart.Head);
                    Helmet(gear);
                    break;
                case WorkerRole.Archer:
                    On(WorkerPart.Torso);
                    Quiver(gear);
                    On(WorkerPart.ForearmLeft);
                    Bracer(gear, -1f);
                    Bow(gear);
                    On(WorkerPart.Head);
                    ArcherCap(gear);
                    break;
                case WorkerRole.Spearman:
                    On(WorkerPart.Torso);
                    Gambeson(gear);
                    On(WorkerPart.Head);
                    KettleHat(gear);
                    On(WorkerPart.ForearmRight);
                    Spear(gear);
                    break;
            }
            mesh.Part = (int)WorkerPart.Torso;
            mesh.Transform = scale;
        }

        // ── Туловище и ремни ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Точка на поверхности туловища: угол от середины груди (0°) к правому боку (90°) и спине (180°), высота y.
        /// margin — насколько над рубахой (броня толще), backExtra — сколько добавить посередине спины (ремень поверх ноши).
        /// </summary>
        private static Vector3 TorsoPoint(float degrees, float y, float margin, float backExtra = 0f)
        {
            var (rx, rz, cz) = TorsoAt(y);
            var a = degrees * Mathf.Deg2Rad;
            var behind = Mathf.Max(0f, -Mathf.Cos(a));
            return new Vector3(Mathf.Sin(a) * (rx + margin), y, Mathf.Cos(a) * (rz + margin) + cz - backExtra * behind * behind * behind);
        }

        /// <summary>Задняя поверхность туловища посередине на высоте y.</summary>
        private static float BackZ(float y)
        {
            var (_, rz, cz) = TorsoAt(y);
            return cz - rz;
        }

        private static (float rx, float rz, float cz) TorsoAt(float y)
        {
            if (y <= Torso[0].y)
                return (Torso[0].rx, Torso[0].rz, Torso[0].cz);
            for (var i = 1; i < Torso.Length; i++)
            {
                if (y > Torso[i].y)
                    continue;
                var t = (y - Torso[i - 1].y) / (Torso[i].y - Torso[i - 1].y);
                return (Mathf.Lerp(Torso[i - 1].rx, Torso[i].rx, t), Mathf.Lerp(Torso[i - 1].rz, Torso[i].rz, t), Mathf.Lerp(Torso[i - 1].cz, Torso[i].cz, t));
            }
            var last = Torso[^1];
            return (last.rx, last.rz, last.cz);
        }

        /// <summary>
        /// Кольцо ремня или верёвки вокруг туловища, наклонное: y = y0 + slope · sin(угла), то есть выше на правом боку.
        /// backExtra — ремень обходит ношу на спине.
        /// </summary>
        private static void TorsoLoop(Gear g, float y0, float slope, float margin, float backExtra, float width, float thickness, Color color)
        {
            const int count = 28;
            var points = new List<Vector3>();
            for (var i = 0; i < count; i++)
            {
                var degrees = 360f * i / count;
                points.Add(TorsoPoint(degrees, y0 + slope * Mathf.Sin(degrees * Mathf.Deg2Rad), margin, backExtra));
            }
            g.Band(points, width, thickness, color, closed: true);
        }

        // ── Инструменты: головка перед кулаком, черенок назад-вниз ───────────────────────

        private static Vector3 Handle(Gear g, Vector3 axis, float ahead, float behind, float radius, Color color)
        {
            var fist = FistCenter(1f);
            g.Rod(fist - axis * behind, fist + axis * ahead, radius, color);
            return fist + axis * ahead;
        }

        /// <summary>Поперёк рукояти в плоскости движения, вниз-вперёд.</summary>
        private static Vector3 Across(Vector3 axis) => -Vector3.Cross(Vector3.right, axis).normalized;

        private static void Axe(Gear g)
        {
            var top = Handle(g, ToolAxis, 0.075f, 0.24f, 0.012f, Wood);
            var down = Across(ToolAxis);
            var head = top - ToolAxis * 0.022f;
            // Лезвие клином вниз-вперёд: от щеки к острой кромке шире вдоль рукояти и тоньше; обух назад-вверх.
            g.Tube(new[] { head, head + down * 0.045f, head + down * 0.085f },
                new[] { new Vector2(0.015f, 0.022f), new Vector2(0.008f, 0.04f), new Vector2(0.0008f, 0.056f) }, 4, Metal);
            g.Tube(new[] { head - down * 0.035f, head + down * 0.004f }, new[] { new Vector2(0.017f, 0.02f), new Vector2(0.017f, 0.02f) }, 4, MetalDark);
        }

        private static void Pickaxe(Gear g)
        {
            var top = Handle(g, ToolAxis, 0.075f, 0.26f, 0.012f, Wood);
            var down = Across(ToolAxis);
            var head = top - ToolAxis * 0.02f;
            g.Tube(new[] { head - ToolAxis * 0.022f, head + ToolAxis * 0.022f }, new[] { new Vector2(0.021f, 0.021f), new Vector2(0.021f, 0.021f) }, 6, MetalDark);
            // Клювы в обе стороны поперёк рукояти, загнуты назад к черенку и сходятся в остриё.
            foreach (var (dir, length) in new[] { (down, 0.15f), (-down, 0.11f) })
            {
                g.Tube(new[] { head, head + dir * (length * 0.5f) - ToolAxis * 0.008f, head + dir * length - ToolAxis * 0.035f },
                    new[] { new Vector2(0.016f, 0.016f), new Vector2(0.012f, 0.012f), new Vector2(0.0015f, 0.0015f) }, 6, Metal);
            }
        }

        private static void Hammer(Gear g)
        {
            var top = Handle(g, ToolAxis, 0.06f, 0.2f, 0.011f, Wood);
            var down = Across(ToolAxis);
            var head = top - ToolAxis * 0.02f;
            var turn = Quaternion.LookRotation(ToolAxis, down);
            // Боёк вниз-вперёд, раздвоенный гвоздодёр назад-вверх.
            g.Box(head + down * 0.015f, turn, new Vector3(0.04f, 0.07f, 0.042f), MetalDark);
            g.Box(head + down * 0.055f, turn, new Vector3(0.048f, 0.014f, 0.05f), Metal);
            foreach (var x in new[] { -0.011f, 0.011f })
            {
                var root = head - down * 0.02f + Vector3.right * x;
                g.Tube(new[] { root, root - down * 0.035f - ToolAxis * 0.01f, root - down * 0.06f - ToolAxis * 0.03f },
                    new[] { new Vector2(0.009f, 0.016f), new Vector2(0.007f, 0.012f), new Vector2(0.002f, 0.004f) }, 4, Metal);
            }
        }

        private static void Poker(Gear g)
        {
            // Кочерга одним прутом: деревянная ручка в кулаке, железный прут назад-вниз, на конце крюк вниз.
            var fist = FistCenter(1f);
            g.Rod(fist - ToolAxis * 0.035f, fist + ToolAxis * 0.06f, 0.013f, Wood);
            var down = Across(ToolAxis);
            var end = fist - ToolAxis * 0.3f;
            g.Tube(new[] { fist - ToolAxis * 0.03f, fist - ToolAxis * 0.15f, end, end - ToolAxis * 0.028f + down * 0.014f, end - ToolAxis * 0.032f + down * 0.042f },
                new[] { new Vector2(0.009f, 0.009f), new Vector2(0.009f, 0.009f), new Vector2(0.009f, 0.009f), new Vector2(0.009f, 0.009f), new Vector2(0.005f, 0.005f) },
                6, MetalDark);
        }

        // ── Оружие ───────────────────────────────────────────────────────────────────────

        private static void SheathedSword(Gear g)
        {
            // Меч в ножнах на левом бедре: ножны от пояса назад-вниз вдоль ноги, рукоять торчит вперёд-вверх.
            var mouth = new Vector3(-0.124f, 0.5f, 0.012f);
            var along = new Vector3(-0.04f, -0.32f, -0.17f).normalized;
            var tip = mouth + along * 0.36f;
            g.Tube(new[] { mouth, mouth + along * 0.3f, tip },
                new[] { new Vector2(0.01f, 0.024f), new Vector2(0.009f, 0.021f), new Vector2(0.006f, 0.008f) }, 6, OiledLeather);
            g.Tube(new[] { mouth - along * 0.004f, mouth + along * 0.03f }, new[] { new Vector2(0.012f, 0.026f), new Vector2(0.012f, 0.026f) }, 6, MetalDark);
            g.Tube(new[] { tip - along * 0.04f, tip + along * 0.004f }, new[] { new Vector2(0.009f, 0.017f), new Vector2(0.006f, 0.008f) }, 6, MetalDark);
            // Перекрестье поперёк ножен, рукоять и навершие.
            var guard = mouth - along * 0.008f;
            var cross = Vector3.Cross(along, Vector3.right).normalized;
            g.Rod(guard - cross * 0.05f, guard + cross * 0.05f, 0.008f, MetalDark);
            var pommel = guard - along * 0.08f;
            g.Rod(guard, pommel, 0.01f, Apron);
            g.Blob(pommel - along * 0.008f, new Vector3(0.016f, 0.016f, 0.016f), MetalDark);
            // Петля ножен на поясе.
            g.Band(new List<Vector3> { TorsoPoint(-112f, 0.516f, 0.004f), TorsoPoint(-97f, 0.506f, 0.012f), mouth + along * 0.02f }, 0.014f, 0.004f,
                Strap, closed: false);
        }

        private static void Spear(Gear g)
        {
            var fist = FistCenter(1f);
            var top = fist + SpearAxis * 0.95f;
            g.Rod(fist - SpearAxis * 0.36f, top, 0.012f, Wood);
            g.Rod(top - SpearAxis * 0.01f, top + SpearAxis * 0.035f, 0.014f, MetalDark);
            // Листовидный наконечник, плоский поперёк.
            g.Tube(new[] { top + SpearAxis * 0.03f, top + SpearAxis * 0.075f, top + SpearAxis * 0.14f },
                new[] { new Vector2(0.03f, 0.006f), new Vector2(0.028f, 0.006f), new Vector2(0.001f, 0.001f) }, 4, Metal);
        }

        private static void Bow(Gear g)
        {
            // Лук в кулаке левой руки, наклонён вперёд: плечи выгнуты назад, тетива прямая между концами.
            var fist = FistCenter(-1f);
            var back = Vector3.Cross(BowAxis, Vector3.right).normalized;
            const int count = 11;
            var path = new Vector3[count];
            var radii = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var t = -1f + 2f * i / (count - 1);
                path[i] = fist + BowAxis * (t * 0.32f) + back * (0.07f * t * t);
                var r = Mathf.Lerp(0.013f, 0.0065f, Mathf.Abs(t));
                radii[i] = new Vector2(r, r);
            }
            g.Tube(path, radii, 6, Wood);
            g.Rod(path[0], path[count - 1], 0.003f, Linen, 4);
            g.Rod(fist - BowAxis * 0.04f, fist + BowAxis * 0.04f, 0.016f, Apron);
        }

        // ── Ноша и одежда ────────────────────────────────────────────────────────────────

        private static void RopeCoil(Gear g)
        {
            // Два витка верёвки через плечо: с правого плеча через грудь к левому боку над поясом, по телу.
            // Ниже пояса не спускаются: там подол, он гнётся за бёдрами и прошёл бы сквозь верёвку.
            TorsoLoop(g, 0.623f, 0.092f, 0.014f, 0f, 0.024f, 0.022f, Rope);
            TorsoLoop(g, 0.61f, 0.092f, 0.014f, 0f, 0.024f, 0.022f, Rope * 0.88f);
        }

        private static void LeatherApron(Gear g)
        {
            // Тёмный промасленный фартук: юбка до колен поверх подола, нагрудник до груди, завязки на поясе.
            g.OnPart(WorkerPart.Hips);
            g.Loft(new[]
            {
                new LoftRing(0.3f, 0.158f, 0.112f, 0.006f), new LoftRing(0.44f, 0.137f, 0.097f, 0.002f), new LoftRing(0.52f, 0.122f, 0.088f),
            }, WorkerMeshBuilder.Sides, OiledLeather, capStart: false, capEnd: false, arc: 0.44f, arcMiddle: 0.5f, twoSided: true,
                inner: OiledLeather * 0.7f);
            g.OnPart(WorkerPart.Torso);
            g.Loft(new[]
            {
                new LoftRing(0.52f, 0.122f, 0.088f), new LoftRing(0.6f, 0.14f, 0.1f, 0.011f), new LoftRing(0.665f, 0.156f, 0.1f, 0.01f),
            }, WorkerMeshBuilder.Sides, OiledLeather, capStart: false, capEnd: false, arc: 0.26f, arcMiddle: 0.5f, twoSided: true,
                inner: OiledLeather * 0.7f);
            g.Loft(new[] { new LoftRing(0.505f, 0.124f, 0.09f), new LoftRing(0.522f, 0.124f, 0.09f) }, WorkerMeshBuilder.Sides,
                OiledLeather * 0.8f, capStart: false, capEnd: false);
        }

        private static void MinerPouch(Gear g)
        {
            // Шахтёрская поясная сумка на левом боку ближе к спине (там, где не ходит рука): короб с клапаном и пряжкой,
            // висит на петле с пояса.
            var turn = Quaternion.Euler(0f, 120f, 0f);
            var side = TorsoPoint(-120f, 0.465f, 0.03f);
            g.Box(side, turn, new Vector3(0.085f, 0.085f, 0.05f), Apron);
            g.Box(side + new Vector3(-0.003f, 0.03f, 0f), turn, new Vector3(0.09f, 0.032f, 0.056f), Apron * 0.72f);
            g.Box(side + new Vector3(-0.027f, 0.012f, 0f), turn, new Vector3(0.012f, 0.014f, 0.004f), MetalDark);
            g.Band(new List<Vector3> { TorsoPoint(-125f, 0.53f, 0.004f), TorsoPoint(-121f, 0.51f, 0.016f), side + new Vector3(0f, 0.04f, 0f) },
                0.016f, 0.004f, OiledLeather, closed: false);
        }

        private static void Planks(Gear g)
        {
            // Две доски крестом прижаты к спине: низ лежит на подоле, середина — на пелерине; две верёвки вокруг тела.
            var bottom = new Vector3(0f, 0.37f, BackZ(0.37f) - 0.004f);
            var middle = new Vector3(0f, 0.64f, -0.124f);
            var up = (middle - bottom).normalized;
            var back = -Vector3.Cross(Vector3.right, up).normalized;
            foreach (var (tilt, layer, shade) in new[] { (14f, 0f, 1f), (-12f, 1f, 0.88f) })
            {
                var plankUp = Quaternion.AngleAxis(tilt, back) * up;
                var center = bottom + up * 0.2f + back * (0.006f + 0.012f * layer);
                g.Box(center, Quaternion.LookRotation(back, plankUp), new Vector3(0.056f, 0.4f, 0.012f), WoodLight * shade);
            }
            TorsoLoop(g, 0.6f, 0f, 0.006f, 0.062f, 0.012f, 0.012f, Rope);
            // Нижняя верёвка ниже пояса — на подоле: гнётся вместе с ним, когда бёдра уходят вперёд.
            g.OnPart(WorkerPart.Hips);
            TorsoLoop(g, 0.455f, 0f, 0.006f, 0.04f, 0.012f, 0.012f, Rope);
            g.OnPart(WorkerPart.Torso);
        }

        private static void ToolBag(Gear g)
        {
            var turn = Quaternion.Euler(0f, -30f, 0f);
            g.Box(new Vector3(-0.115f, 0.465f, 0.045f), turn, new Vector3(0.055f, 0.075f, 0.065f), Apron);
            g.Box(new Vector3(-0.115f, 0.5f, 0.045f), turn, new Vector3(0.06f, 0.016f, 0.07f), Apron * 0.75f);
            // Рукоятки инструментов из сумки.
            g.Rod(new Vector3(-0.11f, 0.49f, 0.035f), new Vector3(-0.12f, 0.55f, 0.03f), 0.006f, Wood);
            g.Rod(new Vector3(-0.125f, 0.49f, 0.06f), new Vector3(-0.13f, 0.535f, 0.07f), 0.005f, WoodLight);
        }

        private static void Logs(Gear g)
        {
            // Вязанка поперёк спины: два полена лежат на спине, третье — в ложбинке за ними; лямки через плечи.
            var low = new Vector3(0f, 0.498f, BackZ(0.498f) - 0.03f);
            var high = new Vector3(0f, 0.562f, BackZ(0.562f) - 0.03f);
            var outer = (low + high) * 0.5f + new Vector3(0f, 0f, -0.053f);
            foreach (var center in new[] { low, high, outer })
            {
                g.Rod(center + new Vector3(-0.11f, 0f, 0f), center + new Vector3(0.11f, 0f, 0f), 0.03f, Bark, 7);
                g.Rod(center + new Vector3(-0.113f, 0f, 0f), center + new Vector3(-0.108f, 0f, 0f), 0.026f, WoodLight, 7);
                g.Rod(center + new Vector3(0.108f, 0f, 0f), center + new Vector3(0.113f, 0f, 0f), 0.026f, WoodLight, 7);
            }
            // Лямки: от верха вязанки под пелериной через плечо, спереди по груди вниз, под мышкой назад к низу вязанки.
            foreach (var side in new[] { -1f, 1f })
            {
                // Лямка выходит с верха вязанки, у самой спины, и сразу ложится на тело.
                var points = new List<Vector3> { high + new Vector3(side * 0.07f, 0.026f, -0.004f) };
                // Под пелериной лямка идёт по плечу точками на уровне её края, чтобы не проходить сквозь тело.
                foreach (var (degrees, y) in new[] { (162f, 0.615f), (125f, 0.66f), (95f, 0.668f), (65f, 0.668f), (40f, 0.66f), (30f, 0.58f), (55f, 0.53f), (100f, 0.52f), (140f, 0.52f) })
                    points.Add(TorsoPoint(side * degrees, y, 0.006f));
                points.Add(low + new Vector3(side * 0.07f, -0.026f, -0.012f));
                g.Band(points, 0.016f, 0.005f, Strap, closed: false);
            }
        }

        private static void Quiver(Gear g)
        {
            // Колчан наискось по спине, от левого бедра к правому плечу, прижат к спине и пелерине.
            var bottom = new Vector3(-0.068f, 0.465f, BackZ(0.465f) - 0.032f);
            var top = new Vector3(0.078f, 0.76f, -0.122f);
            var along = (top - bottom).normalized;
            g.Rod(bottom, top, 0.03f, Apron, 8);
            g.Rod(top - along * 0.01f, top + along * 0.004f, 0.033f, Apron * 0.7f, 8);
            foreach (var offset in new[] { new Vector3(0.012f, 0f, 0.01f), new Vector3(-0.012f, 0f, 0.008f), new Vector3(0.004f, 0f, -0.013f) })
            {
                var start = top + offset - along * 0.02f;
                var end = top + offset + along * 0.085f;
                g.Rod(start, end, 0.003f, WoodLight, 4);
                g.Box(end - along * 0.02f, Quaternion.FromToRotation(Vector3.up, along), new Vector3(0.018f, 0.035f, 0.003f), Fletching);
            }
            // Ремень колчана по телу: из-под правой руки наискось через грудь к левому боку над поясом и назад поверх колчана.
            // Не заходит ни под пелерину (её поднимает рука), ни ниже пояса (там подол, он гнётся за бёдрами).
            TorsoLoop(g, 0.56f, 0.04f, 0.006f, 0.068f, 0.016f, 0.005f, Strap);
        }

        private static void Bracer(Gear g, float side) =>
            g.Tube(new[] { new Vector3(side * 0.177f, 0.5f, 0.012f), new Vector3(side * 0.178f, 0.448f, 0.032f) },
                new[] { new Vector2(0.036f, 0.035f), new Vector2(0.033f, 0.032f) }, WorkerMeshBuilder.LimbSides, Apron);

        private static void MailShirt(Gear g)
        {
            // Подол кольчуги ниже пояса ходит с подолом рубахи, верх — с корпусом; шов закрывает пояс.
            g.OnPart(WorkerPart.Hips);
            g.Loft(new[]
            {
                new LoftRing(0.33f, 0.16f, 0.11f, 0.002f), new LoftRing(0.4f, 0.152f, 0.104f, 0.001f), new LoftRing(0.44f, 0.137f, 0.094f),
                new LoftRing(0.515f, 0.118f, 0.082f),
            }, WorkerMeshBuilder.Sides, Mail, capStart: false, capEnd: false, twoSided: true, inner: Mail * 0.6f);
            g.OnPart(WorkerPart.Torso);
            g.Loft(new[]
            {
                new LoftRing(0.505f, 0.118f, 0.082f),
                new LoftRing(0.6f, 0.136f, 0.094f, 0.01f), new LoftRing(0.68f, 0.154f, 0.094f, 0.008f), new LoftRing(0.735f, 0.146f, 0.08f),
                new LoftRing(0.772f, 0.072f, 0.056f),
            }, WorkerMeshBuilder.Sides, Mail, capStart: false, capEnd: false, twoSided: true, inner: Mail * 0.6f);
            Sleeves(g, Mail, SleeveExtra(WorkerRole.Swordsman));
            Belt(g, 0.124f, 0.089f);
        }

        private static void Gambeson(Gear g)
        {
            // Стёганка: толще рубахи, по ней тёмные строчки. Ниже пояса — в подоле.
            g.OnPart(WorkerPart.Hips);
            g.Loft(new[]
            {
                new LoftRing(0.34f, 0.163f, 0.113f, 0.002f), new LoftRing(0.4f, 0.155f, 0.107f, 0.001f), new LoftRing(0.44f, 0.139f, 0.097f),
                new LoftRing(0.515f, 0.122f, 0.086f),
            }, WorkerMeshBuilder.Sides, Linen, capStart: false, capEnd: false, twoSided: true, inner: Linen * 0.6f);
            g.OnPart(WorkerPart.Torso);
            g.Loft(new[]
            {
                new LoftRing(0.505f, 0.122f, 0.086f),
                new LoftRing(0.6f, 0.139f, 0.097f, 0.01f), new LoftRing(0.68f, 0.157f, 0.097f, 0.008f), new LoftRing(0.735f, 0.148f, 0.082f),
                new LoftRing(0.772f, 0.072f, 0.056f),
            }, WorkerMeshBuilder.Sides, Linen, capStart: false, capEnd: false, twoSided: true, inner: Linen * 0.6f);
            foreach (var (y, rx, rz, cz) in new[] { (0.4f, 0.155f, 0.107f, 0.001f), (0.465f, 0.133f, 0.093f, 0f), (0.565f, 0.134f, 0.094f, 0.006f), (0.63f, 0.149f, 0.099f, 0.009f) })
            {
                g.OnPart(y < 0.5f ? WorkerPart.Hips : WorkerPart.Torso);
                g.Loft(new[] { new LoftRing(y, rx + 0.002f, rz + 0.002f, cz), new LoftRing(y + 0.007f, rx + 0.002f, rz + 0.002f, cz) },
                    WorkerMeshBuilder.Sides, Linen * 0.62f, capStart: false, capEnd: false);
            }
            g.OnPart(WorkerPart.Torso);
            Sleeves(g, Linen, SleeveExtra(WorkerRole.Spearman));
            Belt(g, 0.127f, 0.092f);
        }

        /// <summary>Рукава брони до локтя поверх рубахи, толще её на extra.</summary>
        private static void Sleeves(Gear g, Color color, float extra)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                g.OnPart(WorkerMeshBuilder.Side(side, WorkerPart.UpperArmLeft, WorkerPart.UpperArmRight));
                g.Tube(new[]
                {
                    new Vector3(side * 0.11f, 0.695f, 0f), new Vector3(side * 0.152f, 0.672f, -0.004f), new Vector3(side * 0.17f, 0.62f, -0.01f),
                    new Vector3(side * 0.175f, 0.545f, -0.008f),
                }, new[]
                {
                    new Vector2(0.034f + extra, 0.034f + extra), new Vector2(0.043f + extra, 0.041f + extra),
                    new Vector2(0.036f + extra, 0.036f + extra), new Vector2(0.032f + extra, 0.033f + extra),
                }, WorkerMeshBuilder.LimbSides, color);
            }
            g.OnPart(WorkerPart.Torso);
        }

        private static void Belt(Gear g, float rx, float rz)
        {
            g.Loft(new[] { new LoftRing(0.5f, rx, rz), new LoftRing(0.528f, rx, rz) }, WorkerMeshBuilder.Sides, WorkerMeshBuilder.Leather,
                capStart: false, capEnd: false);
            g.Box(new Vector3(0f, 0.514f, rz + 0.001f), Quaternion.identity, new Vector3(0.03f, 0.024f, 0.01f), WorkerMeshBuilder.Buckle);
        }

        private static void Shield(Gear g, Color face)
        {
            // Круглый щит на спине: внутренней стороной лежит на пелерине и на бёдрах, верх чуть наклонён назад;
            // лицевая сторона цвета роли, железный обод и умбон; ремень через грудь.
            var lowTouch = new Vector3(0f, 0.445f, BackZ(0.445f) - 0.003f);
            var highTouch = new Vector3(0f, 0.725f, -0.13f);
            var up = (highTouch - lowTouch).normalized;
            var outward = -Vector3.Cross(Vector3.right, up).normalized;
            var inner = (lowTouch + highTouch) * 0.5f;
            LoftRing Ring(float offset, float radius) => new(inner + outward * offset, radius, radius, Vector3.right, up);
            g.Loft(new[] { Ring(0f, 0.14f), Ring(0.014f, 0.14f) }, 16, Wood, capStart: true, capEnd: false);
            g.Loft(new[] { Ring(0.014f, 0.14f), Ring(0.016f, 0.001f) }, 16, face, capStart: false, capEnd: false);
            g.Loft(new[] { Ring(-0.001f, 0.146f), Ring(0.018f, 0.146f) }, 16, MetalDark, capStart: false, capEnd: false, twoSided: true);
            g.Blob(inner + outward * 0.02f, new Vector3(0.036f, 0.036f, 0.036f), Metal);
            // Ремень щита — из-под правой руки к левому боку над поясом: не под пелериной и не ниже пояса.
            TorsoLoop(g, 0.585f, 0.065f, 0.008f, 0.06f, 0.018f, 0.005f, Strap);
        }

        // ── Головные уборы ───────────────────────────────────────────────────────────────

        private static void Helmet(Gear g)
        {
            // Купол с ободом, наносником и шпилем на макушке.
            g.Loft(new[]
            {
                new LoftRing(0.902f, 0.088f, 0.096f, 0.002f), new LoftRing(0.94f, 0.085f, 0.093f, 0f), new LoftRing(0.972f, 0.072f, 0.08f, -0.002f),
                new LoftRing(0.995f, 0.046f, 0.054f, -0.004f), new LoftRing(1.012f, 0.016f, 0.018f, -0.004f),
            }, WorkerMeshBuilder.Sides, Metal, capStart: false);
            g.Loft(new[] { new LoftRing(1.004f, 0.024f, 0.026f, -0.004f), new LoftRing(1.02f, 0.013f, 0.014f, -0.004f), new LoftRing(1.068f, 0.001f, 0.001f, -0.004f) },
                8, Metal);
            g.Loft(new[] { new LoftRing(0.898f, 0.091f, 0.099f, 0.002f), new LoftRing(0.918f, 0.091f, 0.099f, 0.002f) },
                WorkerMeshBuilder.Sides, MetalDark, capStart: false, capEnd: false, twoSided: true);
            g.Box(new Vector3(0f, 0.884f, 0.1f), Quaternion.Euler(-8f, 0f, 0f), new Vector3(0.013f, 0.05f, 0.008f), MetalDark);
        }

        private static void KettleHat(Gear g)
        {
            // Шапель: невысокий купол и широкие поля, чуть опущенные вниз.
            g.Loft(new[]
            {
                new LoftRing(0.912f, 0.086f, 0.094f, 0.001f), new LoftRing(0.95f, 0.081f, 0.089f, -0.001f), new LoftRing(0.982f, 0.062f, 0.07f, -0.003f),
                new LoftRing(0.998f, 0.025f, 0.03f, -0.004f),
            }, WorkerMeshBuilder.Sides, Metal, capStart: false);
            g.Loft(new[] { new LoftRing(0.914f, 0.086f, 0.094f, 0.001f), new LoftRing(0.896f, 0.138f, 0.146f, 0.001f) },
                WorkerMeshBuilder.Sides, MetalDark, capStart: false, capEnd: false, twoSided: true);
        }

        private static void StockingCap(Gear g, Color color)
        {
            // Колпак поверх волос, кончик загнут назад; отворот темнее.
            g.Loft(new[]
            {
                new LoftRing(0.91f, 0.085f, 0.093f, 0.002f), new LoftRing(0.95f, 0.081f, 0.09f, -0.001f), new LoftRing(0.985f, 0.068f, 0.077f, -0.008f),
                new LoftRing(1.01f, 0.048f, 0.054f, -0.025f), new LoftRing(1.03f, 0.026f, 0.028f, -0.047f), new LoftRing(1.045f, 0.004f, 0.004f, -0.068f),
            }, WorkerMeshBuilder.Sides, color, capStart: false);
            g.Loft(new[] { new LoftRing(0.905f, 0.089f, 0.097f, 0.002f), new LoftRing(0.93f, 0.088f, 0.096f, 0.001f) },
                WorkerMeshBuilder.Sides, color * 0.72f, capStart: false, capEnd: false, twoSided: true);
        }

        private static void ArcherCap(Gear g)
        {
            g.Loft(new[]
            {
                new LoftRing(0.905f, 0.085f, 0.093f, 0.004f), new LoftRing(0.945f, 0.081f, 0.089f, 0f), new LoftRing(0.978f, 0.063f, 0.071f, -0.006f),
                new LoftRing(0.997f, 0.03f, 0.036f, -0.01f), new LoftRing(1.002f, 0.004f, 0.005f, -0.01f),
            }, WorkerMeshBuilder.Sides, Olive, capStart: false);
            g.Loft(new[] { new LoftRing(0.9f, 0.088f, 0.096f, 0.004f), new LoftRing(0.918f, 0.088f, 0.096f, 0.004f) },
                WorkerMeshBuilder.Sides, Olive * 0.75f, capStart: false, capEnd: false, twoSided: true);
            // Перо сбоку, назад и вверх.
            g.Cone(new Vector3(0.072f, 0.94f, -0.02f), Quaternion.Euler(-40f, 0f, -18f), 0.016f, 0.11f, Fletching, 0.3f);
        }

        /// <summary>Обёртка над FacetedMesh: все размеры в долях роста, перенос — через scale.</summary>
        private readonly struct Gear
        {
            private readonly FacetedMesh mesh;
            private readonly Matrix4x4 scale;

            public Gear(FacetedMesh mesh, Matrix4x4 scale)
            {
                this.mesh = mesh;
                this.scale = scale;
            }

            public void OnPart(WorkerPart part) => mesh.Part = (int)part;

            public void Rod(Vector3 from, Vector3 to, float radius, Color color, int sides = 6)
            {
                mesh.Transform = scale;
                mesh.AddTube(new[] { from, to }, new[] { new Vector2(radius, radius), new Vector2(radius, radius) }, sides, Paint(color));
            }

            public void Tube(Vector3[] path, Vector2[] radii, int sides, Color color)
            {
                mesh.Transform = scale;
                mesh.AddTube(path, radii, sides, Paint(color));
            }

            /// <summary>
            /// Плоская лента (ремень, верёвка) по ломаной: ширина лежит вдоль поверхности тела, толщина — наружу от оси Y,
            /// поэтому лента не перекручивается на боках. closed — кольцо.
            /// </summary>
            public void Band(List<Vector3> points, float width, float thickness, Color color, bool closed)
            {
                var count = points.Count;
                var rings = new List<LoftRing>();
                for (var i = 0; i < count; i++)
                {
                    var previous = closed ? points[(i - 1 + count) % count] : points[Mathf.Max(0, i - 1)];
                    var next = closed ? points[(i + 1) % count] : points[Mathf.Min(count - 1, i + 1)];
                    var along = (next - previous).normalized;
                    var outward = new Vector3(points[i].x, 0f, points[i].z);
                    outward = outward.sqrMagnitude < 1e-6f ? Vector3.back : outward.normalized;
                    var across = Vector3.Cross(along, outward);
                    across = across.sqrMagnitude < 1e-6f ? Vector3.up : across.normalized;
                    outward = Vector3.Cross(across, along).normalized;
                    rings.Add(new LoftRing(points[i], width * 0.5f, thickness * 0.5f, across, outward));
                }
                if (closed && count > 0)
                    rings.Add(rings[0]);
                mesh.Transform = scale;
                mesh.AddLoft(rings, 6, Paint(color), capStart: !closed, capEnd: !closed);
            }

            public void Loft(LoftRing[] rings, int sides, Color color, bool capStart = true, bool capEnd = true, float arc = 1f,
                float arcMiddle = 0f, bool twoSided = false, Color? inner = null)
            {
                mesh.Transform = scale;
                mesh.AddLoft(rings, sides, Paint(color), capStart, capEnd, arc, arcMiddle, twoSided,
                    inner.HasValue ? Paint(inner.Value) : null);
            }

            public void Box(Vector3 center, Quaternion rotation, Vector3 size, Color color)
            {
                mesh.Transform = scale * Matrix4x4.TRS(center, rotation, Vector3.one);
                mesh.AddBox(Vector3.zero, size, Paint(color));
                mesh.Transform = scale;
            }

            public void Blob(Vector3 center, Vector3 radii, Color color)
            {
                mesh.Transform = scale;
                mesh.AddBlob(center, radii, 1, 0f, null, Paint(color));
            }

            /// <summary>Конус от основания position вдоль rotation · up; flatten сплющивает его по локальной Z.</summary>
            public void Cone(Vector3 position, Quaternion rotation, float radius, float height, Color color, float flatten = 1f)
            {
                mesh.Transform = scale * Matrix4x4.TRS(position, rotation, new Vector3(1f, 1f, flatten));
                mesh.AddCone(5, Vector3.zero, radius, height, 0f, null, Paint(color));
                mesh.Transform = scale;
            }

            private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);
        }
    }
}
