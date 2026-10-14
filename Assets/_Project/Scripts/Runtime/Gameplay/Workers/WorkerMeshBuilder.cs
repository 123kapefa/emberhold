using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Гранёная low-poly сетка рабочего в стиле деревьев, слепленная лофтами — кольцами разного сечения: сапоги с носком,
    /// ноги с икрой и коленом, рубаха с талией, грудью и покатыми плечами, руки с локтем и кистью с большим пальцем,
    /// голова с челюстью, скулами, носом, ушами и волосами, у части борода. Капюшон цвета роли откинут на спину:
    /// пелерина на плечах, валик вокруг шеи и мешок капюшона по спине. Снаряжение роли (инструмент, оружие, шапка,
    /// фартук, броня) добавляет <see cref="WorkerGearBuilder"/>.
    /// Ноги в (0, 0, 0), лицо смотрит в +Z, единица — блок, рост height. Все размеры — доли роста.
    /// Цвет вершин — цвет части, a = 0: шейдер Emberhold/World/Tree оставляет его как есть.
    /// </summary>
    public static class WorkerMeshBuilder
    {
        internal const int Sides = 14;
        internal const int LimbSides = 10;

        internal static readonly Color Pants = new(0.29f, 0.243f, 0.196f);
        internal static readonly Color Boots = new(0.18f, 0.149f, 0.125f);
        internal static readonly Color Leather = new(0.369f, 0.251f, 0.157f);
        internal static readonly Color Buckle = new(0.784f, 0.635f, 0.29f);
        internal static readonly Color Eyes = new(0.133f, 0.102f, 0.078f);

        /// <summary>Рабочий одной сеткой, в позе стоя.</summary>
        public static Mesh Build(WorkerLook look, float height, string name = "Worker") => BuildFaceted(look, height).ToMesh(name);

        /// <summary>
        /// Рабочий по частям <see cref="WorkerPart"/> для анимации: у каждой части начало координат — её точка вращения
        /// (<see cref="WorkerSkeleton.Pivot"/>). Собранные позой покоя, части дают ту же сетку, что <see cref="Build"/>.
        /// </summary>
        public static Mesh[] BuildParts(WorkerLook look, float height, string name = "Worker") =>
            BuildFaceted(look, height).ToMeshes(name, WorkerSkeleton.PivotsFor(height));

        internal static int Part(WorkerPart part) => (int)part;

        internal static WorkerPart Side(float side, WorkerPart left, WorkerPart right) => side < 0f ? left : right;

        private static FacetedMesh BuildFaceted(WorkerLook look, float height)
        {
            var mesh = new FacetedMesh();
            var scale = Matrix4x4.Scale(Vector3.one * height);
            mesh.Transform = scale;
            var tunic = Paint(look.Tunic);
            var skin = Paint(look.Skin);
            var hair = Paint(look.Hair);
            var hood = Paint(look.Hood);
            // Лесоруб работает с закатанными рукавами: предплечья открыты, манжет нет.
            var rolledSleeves = look.Role == WorkerRole.Woodcutter;

            // Ноги: сапог с носком, голень с икрой, колено, бедро толще к тазу.
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * 0.058f;
                mesh.Part = Part(Side(side, WorkerPart.ShinLeft, WorkerPart.ShinRight));
                mesh.AddLoft(new[]
                {
                    new LoftRing(0f, 0.036f, 0.062f, 0.022f, x), new LoftRing(0.03f, 0.038f, 0.066f, 0.024f, x),
                    new LoftRing(0.05f, 0.034f, 0.044f, 0.008f, x), new LoftRing(0.13f, 0.034f, 0.036f, 0f, x),
                }, LimbSides, Paint(Boots));
                // Отворот голенища.
                mesh.AddLoft(new[] { new LoftRing(0.112f, 0.039f, 0.041f, 0f, x), new LoftRing(0.14f, 0.04f, 0.043f, 0f, x) }, LimbSides, Paint(Leather));
                // Голень до колена, колено — шар, чтобы сгиб не разрывал штанину; выше — бедро.
                var knee = new LoftRing(0.265f, 0.031f, 0.033f, 0.002f, x);
                mesh.AddLoft(new[] { new LoftRing(0.12f, 0.031f, 0.032f, 0f, x), new LoftRing(0.2f, 0.035f, 0.04f, -0.006f, x), knee },
                    LimbSides, Paint(Pants));
                mesh.AddBlob(new Vector3(x, 0.265f, 0.002f), new Vector3(0.032f, 0.026f, 0.034f), 1, 0f, null, Paint(Pants));
                mesh.Part = Part(Side(side, WorkerPart.ThighLeft, WorkerPart.ThighRight));
                mesh.AddLoft(new[] { knee, new LoftRing(0.36f, 0.046f, 0.05f, 0.004f, x * 0.95f), new LoftRing(0.44f, 0.054f, 0.055f, 0f, x * 0.85f) },
                    LimbSides, Paint(Pants));
            }
            mesh.Part = Part(WorkerPart.Torso);

            // Рубаха: подол расходится, бёдра, талия уже, грудь, покатые плечи, ворот у шеи.
            mesh.AddLoft(new[]
            {
                new LoftRing(0.37f, 0.148f, 0.098f, 0.002f), new LoftRing(0.44f, 0.128f, 0.086f), new LoftRing(0.51f, 0.112f, 0.076f),
                new LoftRing(0.6f, 0.13f, 0.088f, 0.01f), new LoftRing(0.68f, 0.148f, 0.088f, 0.008f), new LoftRing(0.735f, 0.14f, 0.074f),
                new LoftRing(0.765f, 0.07f, 0.052f), new LoftRing(0.775f, 0.047f, 0.046f, 0.004f),
            }, Sides, tunic);
            // Тёмная кайма по подолу.
            mesh.AddLoft(new[] { new LoftRing(0.369f, 0.15f, 0.1f, 0.002f), new LoftRing(0.39f, 0.145f, 0.097f, 0.0015f) }, Sides,
                Paint(look.Tunic * 0.72f), capStart: false, capEnd: false);
            // Пояс с пряжкой и сумка на боку.
            mesh.AddLoft(new[] { new LoftRing(0.5f, 0.116f, 0.08f), new LoftRing(0.528f, 0.116f, 0.08f) }, Sides, Paint(Leather));
            mesh.AddBox(new Vector3(0f, 0.514f, 0.081f), new Vector3(0.03f, 0.024f, 0.01f), Paint(Buckle));
            mesh.Transform = scale * Matrix4x4.TRS(new Vector3(0.095f, 0.49f, 0.045f), Quaternion.Euler(0f, 35f, 0f), Vector3.one);
            mesh.AddBox(Vector3.zero, new Vector3(0.045f, 0.055f, 0.03f), Paint(Leather));
            mesh.Transform = scale;

            // Руки начинаются внутри плеч, под пелериной: локоть чуть назад, предплечье вперёд; плоская кисть с большим пальцем.
            foreach (var side in new[] { -1f, 1f })
            {
                // Плечо до локтя и шар локтя — часть плеча, ниже — предплечье.
                mesh.Part = Part(Side(side, WorkerPart.UpperArmLeft, WorkerPart.UpperArmRight));
                var elbow = new Vector3(side * 0.175f, 0.55f, -0.008f);
                mesh.AddTube(new[]
                {
                    new Vector3(side * 0.11f, 0.695f, 0f), new Vector3(side * 0.152f, 0.672f, -0.004f), new Vector3(side * 0.17f, 0.62f, -0.01f), elbow,
                }, new[] { new Vector2(0.034f, 0.034f), new Vector2(0.043f, 0.041f), new Vector2(0.036f, 0.036f), new Vector2(0.031f, 0.032f) },
                    LimbSides, tunic);
                mesh.AddBlob(elbow, new Vector3(0.031f, 0.03f, 0.032f), 1, 0f, null, rolledSleeves ? skin : tunic);
                if (rolledSleeves)
                {
                    // Рукав закатан валиком над локтем, ниже — голое предплечье.
                    mesh.AddTube(new[] { new Vector3(side * 0.171f, 0.6f, -0.01f), new Vector3(side * 0.174f, 0.565f, -0.009f) },
                        new[] { new Vector2(0.04f, 0.04f), new Vector2(0.039f, 0.039f) }, LimbSides, Paint(look.Tunic * 0.8f));
                }
                mesh.Part = Part(Side(side, WorkerPart.ForearmLeft, WorkerPart.ForearmRight));
                mesh.AddTube(new[] { elbow, new Vector3(side * 0.178f, 0.48f, 0.018f), new Vector3(side * 0.178f, 0.42f, 0.04f) },
                    new[] { new Vector2(0.031f, 0.032f), new Vector2(0.033f, 0.031f), new Vector2(0.028f, 0.027f) }, LimbSides, tunic);
                if (rolledSleeves)
                {
                    mesh.AddTube(new[]
                    {
                        new Vector3(side * 0.174f, 0.566f, -0.009f), new Vector3(side * 0.175f, 0.55f, -0.008f),
                        new Vector3(side * 0.178f, 0.48f, 0.018f), new Vector3(side * 0.178f, 0.425f, 0.04f),
                    }, new[] { new Vector2(0.034f, 0.035f), new Vector2(0.033f, 0.034f), new Vector2(0.034f, 0.033f), new Vector2(0.029f, 0.028f) },
                        LimbSides, skin);
                }
                else
                {
                    // Манжета рукава темнее рубахи.
                    mesh.AddTube(new[] { new Vector3(side * 0.178f, 0.445f, 0.031f), new Vector3(side * 0.178f, 0.418f, 0.042f) },
                        new[] { new Vector2(0.031f, 0.03f), new Vector2(0.031f, 0.03f) }, LimbSides, Paint(look.Tunic * 0.72f));
                }
                // Кисть: пустая — ладонь с большим пальцем впереди, с инструментом — кулак вокруг рукояти.
                if (WorkerGearBuilder.GripAxis(look.Role, side) is { } grip)
                    AddFist(mesh, side, grip, skin);
                else
                    AddOpenHand(mesh, side, skin);
            }

            // Голова: шея, плавно переходящая в челюсть, скулы, лоб, затылок, макушка.
            mesh.Part = Part(WorkerPart.Head);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.77f, 0.042f, 0.042f), new LoftRing(0.8f, 0.043f, 0.045f, 0.006f),
                new LoftRing(0.815f, 0.05f, 0.056f, 0.02f), new LoftRing(0.83f, 0.057f, 0.065f, 0.016f),
                new LoftRing(0.845f, 0.062f, 0.07f, 0.012f), new LoftRing(0.875f, 0.07f, 0.078f, 0.008f),
                new LoftRing(0.91f, 0.072f, 0.08f, 0.004f), new LoftRing(0.945f, 0.066f, 0.074f, -0.002f),
                new LoftRing(0.97f, 0.048f, 0.056f, -0.004f), new LoftRing(0.983f, 0.02f, 0.024f, -0.004f),
            }, Sides, skin, capStart: false);
            // Нос — четырёхгранный клин посередине лица, чуть темнее кожи.
            mesh.AddLoft(new[] { new LoftRing(0.866f, 0.012f, 0.012f, 0.088f), new LoftRing(0.876f, 0.01f, 0.01f, 0.09f), new LoftRing(0.9f, 0.006f, 0.006f, 0.084f) },
                4, Paint(look.Skin * 0.9f));
            foreach (var side in new[] { -1f, 1f })
            {
                // Глаза и брови повёрнуты по скруглению лица, чтобы не торчали сбоку.
                mesh.Transform = scale * Matrix4x4.TRS(new Vector3(side * 0.029f, 0.889f, 0.079f), Quaternion.Euler(0f, side * 20f, 0f), Vector3.one);
                mesh.AddBox(Vector3.zero, new Vector3(0.013f, 0.012f, 0.01f), Paint(Eyes));
                mesh.Transform = scale * Matrix4x4.TRS(new Vector3(side * 0.03f, 0.905f, 0.077f), Quaternion.Euler(0f, side * 22f, side * -8f), Vector3.one);
                mesh.AddBox(Vector3.zero, new Vector3(0.025f, 0.007f, 0.012f), hair);
                mesh.Transform = scale;
                // Уши — сплюснутые комки.
                mesh.AddBlob(new Vector3(side * 0.071f, 0.878f, 0.0f), new Vector3(0.009f, 0.02f, 0.015f), 1, 0f, null, Paint(look.Skin * 0.92f));
            }
            // Борода по челюсти — передняя половина кольца; рот — тёмная полоска (в бороде — темнее её).
            if (look.Beard)
            {
                mesh.AddLoft(new[]
                {
                    new LoftRing(0.8f, 0.028f, 0.02f, 0.062f), new LoftRing(0.818f, 0.05f, 0.03f, 0.052f),
                    new LoftRing(0.845f, 0.062f, 0.03f, 0.05f), new LoftRing(0.86f, 0.06f, 0.024f, 0.05f),
                }, Sides, hair, arc: 0.5f, arcMiddle: 0.5f, twoSided: true);
            }
            mesh.AddBox(new Vector3(0f, look.Beard ? 0.852f : 0.848f, 0.08f), new Vector3(0.026f, 0.005f, 0.008f),
                Paint(look.Beard ? look.Hair * 0.55f : Eyes));
            // Волосы: сзади и с боков от затылка до висков, сверху шапка до линии лба. Дуга нижней части — ровно
            // HairSides граней полного кольца, поэтому её верхний край совпадает с краем шапки без шва.
            const int hairSides = 10;
            var hairEdge = new LoftRing(0.905f, 0.08f, 0.088f, 0.002f);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.83f, 0.066f, 0.073f, -0.016f), new LoftRing(0.87f, 0.078f, 0.086f, -0.004f), hairEdge,
            }, hairSides, hair, capStart: false, capEnd: false, arc: (float)hairSides / Sides, twoSided: true);
            mesh.AddLoft(new[]
            {
                hairEdge, new LoftRing(0.95f, 0.074f, 0.083f, -0.002f),
                new LoftRing(0.977f, 0.054f, 0.064f, -0.004f), new LoftRing(0.992f, 0.02f, 0.026f, -0.004f),
            }, Sides, hair, capStart: false, twoSided: true);
            // Пряди на затылке и висках: плоские гранёные клинья остриём вниз от края волос, лежат по шее —
            // неровный край вместо ровной «шапки».
            for (var i = 0; i <= 8; i++)
            {
                var angle = -112f + 28f * i;
                var radians = angle * Mathf.Deg2Rad;
                var length = i % 2 == 0 ? 0.06f : 0.044f;
                var root = new Vector3(Mathf.Sin(radians) * 0.056f, 0.855f, -0.012f - Mathf.Cos(radians) * 0.062f);
                // Локальная +Z смотрит от головы наружу; клин наклонён вниз и к шее.
                mesh.Transform = scale * Matrix4x4.TRS(root, Quaternion.Euler(0f, 180f - angle, 0f) * Quaternion.Euler(200f, 0f, 0f),
                    new Vector3(1f, 1f, 0.5f));
                mesh.AddCone(5, Vector3.zero, 0.022f, length, 0f, null, hair);
            }
            mesh.Transform = scale;

            mesh.Part = Part(WorkerPart.Torso);
            // Капюшон откинут: валик вокруг шеи сзади и пустой мешок капюшона по спине, изнанка темнее;
            // пелерина на плечах закрывает начало рук.
            var lining = Paint(look.Hood * 0.45f);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.745f, 0.1f, 0.075f, -0.008f), new LoftRing(0.77f, 0.078f, 0.064f, -0.008f), new LoftRing(0.79f, 0.052f, 0.05f, -0.004f),
            }, Sides, hood, capStart: false, capEnd: false, arc: 0.7f, twoSided: true, innerColor: lining);
            // Мешок капюшона продолжает валик и лежит по спинке пелерины, кончиком чуть ниже её края.
            mesh.AddLoft(new[]
            {
                new LoftRing(0.61f, 0.003f, 0.003f, -0.12f), new LoftRing(0.628f, 0.014f, 0.007f, -0.123f), new LoftRing(0.66f, 0.04f, 0.012f, -0.124f),
                new LoftRing(0.695f, 0.058f, 0.015f, -0.117f), new LoftRing(0.735f, 0.065f, 0.016f, -0.097f), new LoftRing(0.767f, 0.06f, 0.017f, -0.074f),
                new LoftRing(0.792f, 0.05f, 0.02f, -0.058f),
            }, Sides, hood, capStart: true, capEnd: false, twoSided: true, innerColor: lining);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.655f, 0.196f, 0.112f, -0.002f), new LoftRing(0.7f, 0.2f, 0.108f), new LoftRing(0.745f, 0.135f, 0.082f),
                new LoftRing(0.776f, 0.06f, 0.052f),
            }, Sides, hood, capStart: false, capEnd: false, twoSided: true, innerColor: lining);

            WorkerGearBuilder.Add(mesh, scale, look);
            mesh.Transform = Matrix4x4.identity;
            mesh.Part = Part(WorkerPart.Torso);
            return mesh;
        }

        private static void AddOpenHand(FacetedMesh mesh, float side, Func<Vector3, Color> skin)
        {
            mesh.AddTube(new[]
            {
                new Vector3(side * 0.178f, 0.425f, 0.04f), new Vector3(side * 0.179f, 0.4f, 0.046f), new Vector3(side * 0.18f, 0.368f, 0.05f),
                new Vector3(side * 0.178f, 0.342f, 0.052f),
            }, new[] { new Vector2(0.023f, 0.021f), new Vector2(0.029f, 0.021f), new Vector2(0.029f, 0.021f), new Vector2(0.019f, 0.014f) },
                LimbSides, skin);
            mesh.AddTube(new[] { new Vector3(side * 0.168f, 0.402f, 0.064f), new Vector3(side * 0.162f, 0.376f, 0.08f) },
                new[] { new Vector2(0.011f, 0.011f), new Vector2(0.008f, 0.008f) }, 6, skin);
        }

        /// <summary>
        /// Кулак вокруг рукояти: ладонь от запястья, валик согнутых пальцев вдоль оси хвата axis через центр кулака
        /// и большой палец, обхвативший рукоять сверху спереди.
        /// </summary>
        private static void AddFist(FacetedMesh mesh, float side, Vector3 axis, Func<Vector3, Color> skin)
        {
            var center = WorkerGearBuilder.FistCenter(side);
            mesh.AddTube(new[] { new Vector3(side * 0.178f, 0.425f, 0.04f), new Vector3(side * 0.179f, 0.402f, 0.045f), center + new Vector3(0f, 0.004f, -0.004f) },
                new[] { new Vector2(0.023f, 0.021f), new Vector2(0.027f, 0.022f), new Vector2(0.026f, 0.022f) }, LimbSides, skin);
            mesh.AddTube(new[] { center - axis * 0.026f, center - axis * 0.012f, center + axis * 0.012f, center + axis * 0.024f },
                new[] { new Vector2(0.019f, 0.019f), new Vector2(0.026f, 0.025f), new Vector2(0.026f, 0.025f), new Vector2(0.02f, 0.019f) },
                LimbSides, skin);
            // Большой палец: от ладони внутрь, поверх рукояти перед указательным.
            var inward = new Vector3(-side, 0f, 0f);
            mesh.AddTube(new[] { center + axis * 0.012f + inward * 0.012f + Vector3.up * 0.012f, center + axis * 0.03f + inward * 0.016f },
                new[] { new Vector2(0.01f, 0.01f), new Vector2(0.008f, 0.008f) }, 6, skin);
        }

        internal static Func<Vector3, Color> Paint(Color color)
        {
            var linear = color.linear;
            linear.a = 0f;
            return _ => linear;
        }
    }
}
