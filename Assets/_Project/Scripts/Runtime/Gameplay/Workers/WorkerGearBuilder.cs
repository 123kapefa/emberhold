using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Снаряжение роли поверх тела из <see cref="WorkerMeshBuilder"/>: инструмент или оружие в руке, ноша и щит на спине,
    /// шапка или шлем, фартук, броня. Те же единицы: доли роста, ноги в (0, 0, 0), лицо в +Z, правая рука — +X.
    /// Инструмент висит в опущенной правой руке рукоятью через кулак, головкой вниз и чуть вперёд; лук — в левой.
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
        private static readonly Color Olive = new(0.32f, 0.34f, 0.2f);
        private static readonly Color Fletching = new(0.78f, 0.25f, 0.18f);

        /// <summary>Кулак правой руки: через эту точку идёт рукоять.</summary>
        private static readonly Vector3 RightGrip = new(0.181f, 0.378f, 0.056f);
        private static readonly Vector3 LeftGrip = new(-0.181f, 0.378f, 0.056f);

        /// <summary>Инструмент в опущенной руке: вниз и немного вперёд.</summary>
        private static readonly Vector3 ToolDown = new Vector3(0f, -1f, 0.32f).normalized;

        public static void Add(FacetedMesh mesh, Matrix4x4 scale, WorkerLook look)
        {
            var gear = new Gear(mesh, scale);
            switch (look.Role)
            {
                case WorkerRole.Woodcutter:
                    Axe(gear);
                    RopeCoil(gear);
                    break;
                case WorkerRole.Digger:
                    LeatherApron(gear);
                    Pickaxe(gear);
                    break;
                case WorkerRole.Builder:
                    Planks(gear);
                    ToolBag(gear);
                    Hammer(gear);
                    break;
                case WorkerRole.Stoker:
                    Logs(gear);
                    Poker(gear);
                    StockingCap(gear, look.Hood);
                    break;
                case WorkerRole.Swordsman:
                    MailShirt(gear);
                    Helmet(gear);
                    Shield(gear, look.Hood);
                    Sword(gear);
                    break;
                case WorkerRole.Archer:
                    Quiver(gear);
                    Bracer(gear, -1f);
                    ArcherCap(gear);
                    Bow(gear);
                    break;
                case WorkerRole.Spearman:
                    Gambeson(gear);
                    KettleHat(gear);
                    Spear(gear);
                    break;
            }
            mesh.Transform = scale;
        }

        // ── Инструменты ──────────────────────────────────────────────────────────────────

        // Головки инструментов крупнее настоящих: в игре рабочего видно издалека, сверху.

        private static void Axe(Gear g)
        {
            var end = Handle(g, ToolDown, 0.06f, 0.3f, 0.012f);
            var turn = Quaternion.FromToRotation(Vector3.up, ToolDown);
            // Обух, щека и широкое лезвие вперёд от рукояти.
            g.Box(end + turn * new Vector3(0f, -0.03f, -0.016f), turn, new Vector3(0.026f, 0.044f, 0.034f), MetalDark);
            g.Box(end + turn * new Vector3(0f, -0.03f, 0.034f), turn, new Vector3(0.017f, 0.07f, 0.068f), Metal);
            g.Box(end + turn * new Vector3(0f, -0.03f, 0.078f), turn, new Vector3(0.01f, 0.12f, 0.024f), Metal);
        }

        private static void Pickaxe(Gear g)
        {
            var end = Handle(g, ToolDown, 0.06f, 0.31f, 0.012f);
            var turn = Quaternion.FromToRotation(Vector3.up, ToolDown);
            // Головка поперёк рукояти, изогнутая дугой: длинный клюв вперёд, короткий назад, втулка посередине.
            g.Box(end, turn, new Vector3(0.03f, 0.04f, 0.036f), MetalDark);
            // Локальная +Y — вниз вдоль рукояти: клювы загнуты к рукояти, то есть в −Y, и не уходят в землю.
            g.Tube(new[] { end, end + turn * new Vector3(0f, -0.012f, 0.08f), end + turn * new Vector3(0f, -0.04f, 0.15f) },
                new[] { new Vector2(0.016f, 0.016f), new Vector2(0.012f, 0.012f), new Vector2(0.002f, 0.002f) }, 6, Metal);
            g.Tube(new[] { end, end + turn * new Vector3(0f, -0.01f, -0.06f), end + turn * new Vector3(0f, -0.03f, -0.11f) },
                new[] { new Vector2(0.015f, 0.015f), new Vector2(0.011f, 0.011f), new Vector2(0.002f, 0.002f) }, 6, Metal);
        }

        private static void Hammer(Gear g)
        {
            var end = Handle(g, ToolDown, 0.05f, 0.2f, 0.011f);
            var turn = Quaternion.FromToRotation(Vector3.up, ToolDown);
            // Головка поперёк рукояти: боёк вперёд, раздвоенный гвоздодёр назад.
            g.Box(end + turn * new Vector3(0f, -0.016f, 0.012f), turn, new Vector3(0.044f, 0.048f, 0.07f), MetalDark);
            g.Box(end + turn * new Vector3(0f, -0.016f, 0.054f), turn, new Vector3(0.052f, 0.056f, 0.016f), Metal);
            g.Cone(end + turn * new Vector3(0f, -0.016f, -0.022f), turn * Quaternion.Euler(-100f, 0f, 0f), 0.02f, 0.06f, Metal, 0.5f);
        }

        private static void Poker(Gear g)
        {
            // Кочерга: железный прут через кулак, внизу крюк вперёд; сверху деревянная ручка.
            var down = ToolDown;
            var end = RightGrip + down * 0.32f;
            g.Rod(RightGrip - down * 0.07f, RightGrip + down * 0.02f, 0.012f, Wood);
            g.Rod(RightGrip + down * 0.02f, end, 0.009f, MetalDark);
            var turn = Quaternion.FromToRotation(Vector3.up, down);
            g.Tube(new[] { end, end + turn * new Vector3(0f, 0.012f, 0.03f), end + turn * new Vector3(0f, -0.02f, 0.05f) },
                new[] { new Vector2(0.009f, 0.009f), new Vector2(0.009f, 0.009f), new Vector2(0.005f, 0.005f) }, 5, MetalDark);
        }

        /// <summary>Рукоять через кулак: above — сколько торчит над кулаком, below — длина вниз. Возвращает нижний конец.</summary>
        private static Vector3 Handle(Gear g, Vector3 down, float above, float below, float radius)
        {
            var end = RightGrip + down * below;
            g.Rod(RightGrip - down * above, end, radius, Wood);
            return end;
        }

        // ── Оружие ───────────────────────────────────────────────────────────────────────

        private static void Sword(Gear g)
        {
            var down = new Vector3(0f, -1f, 0.22f).normalized;
            var turn = Quaternion.FromToRotation(Vector3.up, down);
            g.Rod(RightGrip - down * 0.045f, RightGrip + down * 0.03f, 0.011f, Apron);
            g.Blob(RightGrip - down * 0.058f, new Vector3(0.018f, 0.018f, 0.018f), MetalDark);
            g.Box(RightGrip + down * 0.036f, turn, new Vector3(0.11f, 0.016f, 0.022f), MetalDark);
            // Клинок плоский: широкий поперёк руки, тонкий вперёд-назад; к концу сужается в остриё.
            var tip = RightGrip + down * 0.34f;
            g.Tube(new[] { RightGrip + down * 0.04f, RightGrip + down * 0.3f, tip },
                new[] { new Vector2(0.022f, 0.005f), new Vector2(0.019f, 0.005f), new Vector2(0.001f, 0.001f) }, 4, Metal);
        }

        private static void Spear(Gear g)
        {
            var up = new Vector3(0f, 1f, 0.07f).normalized;
            var top = RightGrip + up * 0.95f;
            g.Rod(RightGrip - up * 0.37f, top, 0.012f, Wood);
            g.Rod(top - up * 0.01f, top + up * 0.035f, 0.013f, MetalDark);
            // Листовидный наконечник, плоский.
            g.Tube(new[] { top + up * 0.03f, top + up * 0.075f, top + up * 0.14f },
                new[] { new Vector2(0.014f, 0.006f), new Vector2(0.03f, 0.006f), new Vector2(0.001f, 0.001f) }, 4, Metal);
        }

        private static void Bow(Gear g)
        {
            // Лук в левой руке, вертикально сбоку; плечи изогнуты назад, тетива прямая между концами.
            const int count = 9;
            var path = new Vector3[count];
            var radii = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var t = -1f + 2f * i / (count - 1);
                path[i] = new Vector3(-0.205f, LeftGrip.y + t * 0.32f, LeftGrip.z + 0.02f - 0.09f * t * t);
                var r = Mathf.Lerp(0.014f, 0.007f, Mathf.Abs(t));
                radii[i] = new Vector2(r, r);
            }
            g.Tube(path, radii, 6, Wood);
            g.Rod(path[0], path[count - 1], 0.003f, Linen, 4);
            g.Rod(new Vector3(-0.205f, LeftGrip.y - 0.035f, LeftGrip.z + 0.02f), new Vector3(-0.205f, LeftGrip.y + 0.035f, LeftGrip.z + 0.02f), 0.017f, Apron);
        }

        // ── Ноша и одежда ────────────────────────────────────────────────────────────────

        private static void RopeCoil(Gear g)
        {
            // Моток верёвки через плечо: с правого плеча через грудь и спину к левому бедру, два витка рядом.
            foreach (var shift in new[] { 0f, 0.022f })
            {
                const int count = 16;
                var path = new Vector3[count + 1];
                var radii = new Vector2[count + 1];
                for (var i = 0; i <= count; i++)
                {
                    var a = (float)i / count * Mathf.PI * 2f;
                    // Наклонное кольцо вокруг туловища: высоко на правом плече, низко на левом боку.
                    var x = Mathf.Sin(a) * 0.158f;
                    var y = 0.565f - shift + x * 0.95f;
                    // Глубина кольца — по толщине туловища на этой высоте: на поясе уже, на плече под пелериной шире.
                    var z = Mathf.Cos(a) * (0.09f + Mathf.Clamp01((y - 0.45f) / 0.25f) * 0.022f) + 0.004f;
                    path[i] = new Vector3(x, y, z);
                    radii[i] = new Vector2(0.012f, 0.012f);
                }
                g.Tube(path, radii, 6, Rope * (1f - shift * 6f));
            }
        }

        private static void LeatherApron(Gear g)
        {
            // Тёмный промасленный фартук: юбка до колен поверх рубахи, нагрудник до груди, лямка на шею, завязки на поясе.
            g.Loft(new[]
            {
                new LoftRing(0.3f, 0.158f, 0.112f, 0.006f), new LoftRing(0.44f, 0.137f, 0.097f, 0.002f), new LoftRing(0.52f, 0.122f, 0.088f),
            }, WorkerMeshBuilder.Sides, OiledLeather, capStart: false, capEnd: false, arc: 0.44f, arcMiddle: 0.5f, twoSided: true,
                inner: OiledLeather * 0.7f);
            g.Loft(new[]
            {
                new LoftRing(0.52f, 0.122f, 0.088f), new LoftRing(0.6f, 0.14f, 0.1f, 0.011f), new LoftRing(0.665f, 0.156f, 0.1f, 0.01f),
            }, WorkerMeshBuilder.Sides, OiledLeather, capStart: false, capEnd: false, arc: 0.26f, arcMiddle: 0.5f, twoSided: true,
                inner: OiledLeather * 0.7f);
            foreach (var side in new[] { -1f, 1f })
                g.Tube(new[] { new Vector3(side * 0.05f, 0.66f, 0.1f), new Vector3(side * 0.05f, 0.74f, 0.07f), new Vector3(side * 0.035f, 0.775f, 0.02f) },
                    new[] { new Vector2(0.008f, 0.003f), new Vector2(0.008f, 0.003f), new Vector2(0.008f, 0.003f) }, 4, OiledLeather);
            g.Loft(new[] { new LoftRing(0.505f, 0.124f, 0.09f), new LoftRing(0.522f, 0.124f, 0.09f) }, WorkerMeshBuilder.Sides,
                OiledLeather * 0.8f, capStart: false, capEnd: false);
        }

        private static void Planks(Gear g)
        {
            g.Box(new Vector3(-0.005f, 0.585f, -0.152f), Quaternion.Euler(0f, 0f, 16f), new Vector3(0.058f, 0.46f, 0.012f), WoodLight);
            g.Box(new Vector3(0.012f, 0.575f, -0.165f), Quaternion.Euler(0f, 0f, -9f), new Vector3(0.052f, 0.42f, 0.012f), WoodLight * 0.9f);
            // Верёвка, которой доски притянуты к спине.
            g.Rod(new Vector3(-0.11f, 0.6f, -0.12f), new Vector3(0.11f, 0.6f, -0.12f), 0.006f, Rope);
            g.Rod(new Vector3(-0.07f, 0.6f, -0.174f), new Vector3(0.08f, 0.6f, -0.174f), 0.006f, Rope);
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
            // Вязанка из трёх поленьев поперёк спины, светлые торцы, верёвка вокруг.
            foreach (var center in new[] { new Vector3(0f, 0.53f, -0.135f), new Vector3(0.006f, 0.595f, -0.135f), new Vector3(-0.004f, 0.562f, -0.188f) })
            {
                g.Rod(center + new Vector3(-0.11f, 0f, 0f), center + new Vector3(0.11f, 0f, 0f), 0.03f, Bark, 7);
                g.Rod(center + new Vector3(-0.112f, 0f, 0f), center + new Vector3(-0.108f, 0f, 0f), 0.026f, WoodLight, 7);
                g.Rod(center + new Vector3(0.108f, 0f, 0f), center + new Vector3(0.112f, 0f, 0f), 0.026f, WoodLight, 7);
            }
            foreach (var x in new[] { -0.065f, 0.065f })
            {
                g.Tube(new[]
                {
                    new Vector3(x, 0.66f, -0.11f), new Vector3(x, 0.63f, -0.2f), new Vector3(x, 0.56f, -0.222f),
                    new Vector3(x, 0.49f, -0.18f), new Vector3(x, 0.49f, -0.11f),
                }, new[] { Vector2.one * 0.006f, Vector2.one * 0.006f, Vector2.one * 0.006f, Vector2.one * 0.006f, Vector2.one * 0.006f }, 5, Rope);
            }
        }

        private static void Quiver(Gear g)
        {
            var bottom = new Vector3(-0.075f, 0.47f, -0.13f);
            var top = new Vector3(0.075f, 0.77f, -0.155f);
            var along = (top - bottom).normalized;
            g.Rod(bottom, top, 0.03f, Apron, 8);
            g.Rod(top - along * 0.01f, top + along * 0.004f, 0.033f, Apron * 0.7f, 8);
            // Стрелы с оперением торчат из колчана.
            foreach (var offset in new[] { new Vector3(0.012f, 0f, 0.01f), new Vector3(-0.012f, 0f, 0.008f), new Vector3(0.004f, 0f, -0.013f) })
            {
                var start = top + offset - along * 0.02f;
                var end = top + offset + along * 0.085f;
                g.Rod(start, end, 0.003f, WoodLight, 4);
                g.Box(end - along * 0.02f, Quaternion.FromToRotation(Vector3.up, along), new Vector3(0.018f, 0.035f, 0.003f), Fletching);
            }
            // Ремень колчана через грудь, с правого плеча к левому бедру.
            g.Tube(new[] { new Vector3(0.1f, 0.655f, 0.106f), new Vector3(0f, 0.57f, 0.104f), new Vector3(-0.1f, 0.48f, 0.09f) },
                new[] { new Vector2(0.012f, 0.004f), new Vector2(0.012f, 0.004f), new Vector2(0.012f, 0.004f) }, 4, Apron * 0.8f);
        }

        private static void Bracer(Gear g, float side) =>
            g.Tube(new[] { new Vector3(side * 0.177f, 0.5f, 0.012f), new Vector3(side * 0.178f, 0.448f, 0.032f) },
                new[] { new Vector2(0.036f, 0.035f), new Vector2(0.033f, 0.032f) }, WorkerMeshBuilder.LimbSides, Apron);

        private static void MailShirt(Gear g)
        {
            g.Loft(new[]
            {
                new LoftRing(0.33f, 0.16f, 0.11f, 0.002f), new LoftRing(0.4f, 0.152f, 0.104f, 0.001f), new LoftRing(0.44f, 0.137f, 0.094f),
                new LoftRing(0.51f, 0.118f, 0.082f),
                new LoftRing(0.6f, 0.136f, 0.094f, 0.01f), new LoftRing(0.68f, 0.154f, 0.094f, 0.008f), new LoftRing(0.735f, 0.146f, 0.08f),
                new LoftRing(0.772f, 0.072f, 0.056f),
            }, WorkerMeshBuilder.Sides, Mail, capStart: false, capEnd: false, twoSided: true, inner: Mail * 0.6f);
            Sleeves(g, Mail, 0.005f);
            Belt(g, 0.124f, 0.089f);
        }

        private static void Gambeson(Gear g)
        {
            // Стёганка: толще рубахи, по ней тёмные строчки.
            g.Loft(new[]
            {
                new LoftRing(0.34f, 0.163f, 0.113f, 0.002f), new LoftRing(0.4f, 0.155f, 0.107f, 0.001f), new LoftRing(0.44f, 0.139f, 0.097f),
                new LoftRing(0.51f, 0.122f, 0.086f),
                new LoftRing(0.6f, 0.139f, 0.097f, 0.01f), new LoftRing(0.68f, 0.157f, 0.097f, 0.008f), new LoftRing(0.735f, 0.148f, 0.082f),
                new LoftRing(0.772f, 0.072f, 0.056f),
            }, WorkerMeshBuilder.Sides, Linen, capStart: false, capEnd: false, twoSided: true, inner: Linen * 0.6f);
            foreach (var (y, rx, rz, cz) in new[] { (0.4f, 0.155f, 0.107f, 0.001f), (0.465f, 0.133f, 0.093f, 0f), (0.565f, 0.134f, 0.094f, 0.006f), (0.63f, 0.149f, 0.099f, 0.009f) })
            {
                g.Loft(new[] { new LoftRing(y, rx + 0.002f, rz + 0.002f, cz), new LoftRing(y + 0.007f, rx + 0.002f, rz + 0.002f, cz) },
                    WorkerMeshBuilder.Sides, Linen * 0.62f, capStart: false, capEnd: false);
            }
            Sleeves(g, Linen, 0.007f);
            Belt(g, 0.127f, 0.092f);
        }

        /// <summary>Рукава брони до локтя поверх рубахи, толще её на extra.</summary>
        private static void Sleeves(Gear g, Color color, float extra)
        {
            foreach (var side in new[] { -1f, 1f })
            {
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
        }

        private static void Belt(Gear g, float rx, float rz)
        {
            g.Loft(new[] { new LoftRing(0.5f, rx, rz), new LoftRing(0.528f, rx, rz) }, WorkerMeshBuilder.Sides, WorkerMeshBuilder.Leather,
                capStart: false, capEnd: false);
            g.Box(new Vector3(0f, 0.514f, rz + 0.001f), Quaternion.identity, new Vector3(0.03f, 0.024f, 0.01f), WorkerMeshBuilder.Buckle);
        }

        private static void Shield(Gear g, Color face)
        {
            // Круглый щит на спине: лицевая сторона цвета роли, железный обод и умбон.
            var center = new Vector3(0f, 0.585f, -0.152f);
            var back = Vector3.back;
            g.Loft(new[]
            {
                new LoftRing(center - back * 0.008f, 0.14f, 0.14f, Vector3.right, Vector3.up),
                new LoftRing(center + back * 0.006f, 0.14f, 0.14f, Vector3.right, Vector3.up),
            }, 16, Wood, capStart: true, capEnd: false);
            g.Loft(new[]
            {
                new LoftRing(center + back * 0.006f, 0.14f, 0.14f, Vector3.right, Vector3.up),
                new LoftRing(center + back * 0.008f, 0.001f, 0.001f, Vector3.right, Vector3.up),
            }, 16, face, capStart: false, capEnd: false);
            g.Loft(new[]
            {
                new LoftRing(center - back * 0.009f, 0.146f, 0.146f, Vector3.right, Vector3.up),
                new LoftRing(center + back * 0.01f, 0.146f, 0.146f, Vector3.right, Vector3.up),
            }, 16, MetalDark, capStart: false, capEnd: false, twoSided: true);
            g.Blob(center + back * 0.012f, new Vector3(0.036f, 0.036f, 0.022f), Metal);
        }

        // ── Головные уборы ───────────────────────────────────────────────────────────────

        private static void Helmet(Gear g)
        {
            // Купол с ободом и наносником, низ — над бровями.
            g.Loft(new[]
            {
                new LoftRing(0.902f, 0.088f, 0.096f, 0.002f), new LoftRing(0.94f, 0.085f, 0.093f, 0f), new LoftRing(0.972f, 0.072f, 0.08f, -0.002f),
                new LoftRing(0.995f, 0.046f, 0.054f, -0.004f), new LoftRing(1.01f, 0.012f, 0.014f, -0.004f),
            }, WorkerMeshBuilder.Sides, Metal, capStart: false);
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
