using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Гранёная low-poly сетка рабочего в стиле деревьев, слепленная лофтами — кольцами разного сечения: сапоги с носком,
    /// ноги с икрой и коленом, рубаха с талией, грудью и покатыми плечами, руки с локтем и кистью с большим пальцем,
    /// голова с челюстью, скулами, носом, ушами и волосами, у части борода. Капюшон цвета роли откинут на спину:
    /// пелерина на плечах, валик вокруг шеи и пустой мешок капюшона на лопатках с тёмной изнанкой.
    /// Ноги в (0, 0, 0), лицо смотрит в +Z, единица — блок, рост height. Все размеры — доли роста.
    /// Цвет вершин — цвет части, a = 0: шейдер Emberhold/World/Tree оставляет его как есть.
    /// </summary>
    public static class WorkerMeshBuilder
    {
        private const int Sides = 14;
        private const int LimbSides = 10;

        private static readonly Color Pants = new(0.29f, 0.243f, 0.196f);
        private static readonly Color Boots = new(0.18f, 0.149f, 0.125f);
        private static readonly Color Leather = new(0.369f, 0.251f, 0.157f);
        private static readonly Color Buckle = new(0.784f, 0.635f, 0.29f);
        private static readonly Color Eyes = new(0.133f, 0.102f, 0.078f);

        public static Mesh Build(WorkerLook look, float height, string name = "Worker")
        {
            var mesh = new FacetedMesh();
            var scale = Matrix4x4.Scale(Vector3.one * height);
            mesh.Transform = scale;
            var tunic = Paint(look.Tunic);
            var skin = Paint(look.Skin);
            var hair = Paint(look.Hair);
            var hood = Paint(look.Hood);

            // Ноги: сапог с носком, голень с икрой, колено, бедро толще к тазу.
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * 0.058f;
                mesh.AddLoft(new[]
                {
                    new LoftRing(0f, 0.036f, 0.062f, 0.022f, x), new LoftRing(0.03f, 0.038f, 0.066f, 0.024f, x),
                    new LoftRing(0.05f, 0.034f, 0.044f, 0.008f, x), new LoftRing(0.13f, 0.034f, 0.036f, 0f, x),
                }, LimbSides, Paint(Boots));
                mesh.AddLoft(new[]
                {
                    new LoftRing(0.12f, 0.031f, 0.032f, 0f, x), new LoftRing(0.2f, 0.035f, 0.04f, -0.006f, x),
                    new LoftRing(0.265f, 0.031f, 0.033f, 0.002f, x), new LoftRing(0.36f, 0.044f, 0.048f, 0.004f, x * 0.95f),
                    new LoftRing(0.44f, 0.054f, 0.055f, 0f, x * 0.85f),
                }, LimbSides, Paint(Pants));
            }

            // Рубаха: подол расходится, бёдра, талия уже, грудь, покатые плечи, ворот у шеи.
            mesh.AddLoft(new[]
            {
                new LoftRing(0.37f, 0.148f, 0.098f, 0.002f), new LoftRing(0.44f, 0.128f, 0.086f), new LoftRing(0.51f, 0.112f, 0.076f),
                new LoftRing(0.6f, 0.13f, 0.088f, 0.01f), new LoftRing(0.68f, 0.148f, 0.088f, 0.008f), new LoftRing(0.735f, 0.14f, 0.074f),
                new LoftRing(0.765f, 0.07f, 0.05f), new LoftRing(0.775f, 0.042f, 0.04f, 0.004f),
            }, Sides, tunic);
            // Пояс с пряжкой и сумка на боку.
            mesh.AddLoft(new[] { new LoftRing(0.5f, 0.116f, 0.08f), new LoftRing(0.528f, 0.116f, 0.08f) }, Sides, Paint(Leather));
            mesh.AddBox(new Vector3(0f, 0.514f, 0.081f), new Vector3(0.03f, 0.024f, 0.01f), Paint(Buckle));
            mesh.Transform = scale * Matrix4x4.TRS(new Vector3(0.095f, 0.49f, 0.045f), Quaternion.Euler(0f, 35f, 0f), Vector3.one);
            mesh.AddBox(Vector3.zero, new Vector3(0.045f, 0.055f, 0.03f), Paint(Leather));
            mesh.Transform = scale;

            // Руки начинаются внутри плеч, под пелериной: локоть чуть назад, предплечье вперёд; плоская кисть с большим пальцем.
            foreach (var side in new[] { -1f, 1f })
            {
                mesh.AddTube(new[]
                {
                    new Vector3(side * 0.11f, 0.695f, 0f), new Vector3(side * 0.152f, 0.672f, -0.004f), new Vector3(side * 0.17f, 0.62f, -0.01f),
                    new Vector3(side * 0.175f, 0.55f, -0.008f), new Vector3(side * 0.178f, 0.48f, 0.018f), new Vector3(side * 0.178f, 0.42f, 0.04f),
                }, new[]
                {
                    new Vector2(0.034f, 0.034f), new Vector2(0.043f, 0.041f), new Vector2(0.036f, 0.036f),
                    new Vector2(0.031f, 0.032f), new Vector2(0.033f, 0.031f), new Vector2(0.028f, 0.027f),
                }, LimbSides, tunic);
                mesh.AddTube(new[]
                {
                    new Vector3(side * 0.178f, 0.425f, 0.04f), new Vector3(side * 0.179f, 0.4f, 0.046f), new Vector3(side * 0.18f, 0.37f, 0.05f),
                    new Vector3(side * 0.178f, 0.345f, 0.052f),
                }, new[] { new Vector2(0.022f, 0.02f), new Vector2(0.026f, 0.016f), new Vector2(0.026f, 0.016f), new Vector2(0.016f, 0.011f) },
                    LimbSides, skin);
                mesh.AddTube(new[] { new Vector3(side * 0.17f, 0.4f, 0.062f), new Vector3(side * 0.166f, 0.375f, 0.074f) },
                    new[] { new Vector2(0.009f, 0.009f), new Vector2(0.007f, 0.007f) }, 6, skin);
            }

            // Голова: шея, подбородок, челюсть, скулы, лоб, затылок, макушка.
            mesh.AddLoft(new[]
            {
                new LoftRing(0.77f, 0.036f, 0.036f), new LoftRing(0.8f, 0.036f, 0.038f, 0.004f),
                new LoftRing(0.808f, 0.03f, 0.03f, 0.05f), new LoftRing(0.82f, 0.05f, 0.058f, 0.024f),
                new LoftRing(0.845f, 0.062f, 0.07f, 0.012f), new LoftRing(0.875f, 0.07f, 0.078f, 0.008f),
                new LoftRing(0.91f, 0.072f, 0.08f, 0.004f), new LoftRing(0.945f, 0.066f, 0.074f, -0.002f),
                new LoftRing(0.97f, 0.048f, 0.056f, -0.004f), new LoftRing(0.983f, 0.02f, 0.024f, -0.004f),
            }, Sides, skin, capStart: false);
            // Нос — четырёхгранный клин посередине лица, чуть темнее кожи.
            mesh.AddLoft(new[] { new LoftRing(0.866f, 0.012f, 0.012f, 0.088f), new LoftRing(0.876f, 0.01f, 0.01f, 0.09f), new LoftRing(0.9f, 0.006f, 0.006f, 0.084f) },
                4, Paint(look.Skin * 0.9f));
            foreach (var side in new[] { -1f, 1f })
            {
                mesh.AddBox(new Vector3(side * 0.03f, 0.89f, 0.08f), new Vector3(0.014f, 0.013f, 0.01f), Paint(Eyes));
                mesh.AddBox(new Vector3(side * 0.07f, 0.88f, 0.002f), new Vector3(0.012f, 0.028f, 0.022f), Paint(look.Skin * 0.92f));
                // Брови чуть домиком.
                mesh.Transform = scale * Matrix4x4.TRS(new Vector3(side * 0.031f, 0.906f, 0.08f), Quaternion.Euler(0f, 0f, side * -8f), Vector3.one);
                mesh.AddBox(Vector3.zero, new Vector3(0.03f, 0.007f, 0.012f), hair);
                mesh.Transform = scale;
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
            mesh.AddBox(new Vector3(0f, look.Beard ? 0.852f : 0.848f, 0.077f), new Vector3(0.026f, 0.005f, 0.01f),
                Paint(look.Beard ? look.Hair * 0.55f : Eyes));
            // Волосы: сзади и с боков от затылка до висков, сверху шапка до линии лба. Верх боковой части заходит
            // под шапку, чтобы между ними не было щели.
            mesh.AddLoft(new[]
            {
                new LoftRing(0.835f, 0.064f, 0.07f, -0.014f), new LoftRing(0.875f, 0.075f, 0.083f, -0.004f),
                new LoftRing(0.915f, 0.076f, 0.084f, 0.001f), new LoftRing(0.94f, 0.07f, 0.078f, -0.002f),
            }, Sides, hair, capStart: false, capEnd: false, arc: 0.68f, twoSided: true);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.905f, 0.078f, 0.086f, 0.002f), new LoftRing(0.95f, 0.072f, 0.081f, -0.002f),
                new LoftRing(0.975f, 0.052f, 0.062f, -0.004f), new LoftRing(0.99f, 0.02f, 0.026f, -0.004f),
            }, Sides, hair, capStart: false, twoSided: true);

            // Капюшон откинут: валик вокруг шеи сзади и пустой мешок капюшона на лопатках, изнанка темнее;
            // пелерина на плечах закрывает начало рук.
            var lining = Paint(look.Hood * 0.45f);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.745f, 0.1f, 0.075f, -0.008f), new LoftRing(0.775f, 0.075f, 0.062f, -0.01f), new LoftRing(0.8f, 0.058f, 0.056f, -0.012f),
            }, Sides, hood, capStart: false, capEnd: false, arc: 0.7f, twoSided: true, innerColor: lining);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.62f, 0.005f, 0.005f, -0.105f), new LoftRing(0.65f, 0.03f, 0.018f, -0.108f), new LoftRing(0.7f, 0.055f, 0.03f, -0.112f),
                new LoftRing(0.755f, 0.065f, 0.036f, -0.11f), new LoftRing(0.79f, 0.062f, 0.036f, -0.102f),
            }, Sides, hood, capStart: true, capEnd: false, twoSided: true, innerColor: lining);
            mesh.AddLoft(new[]
            {
                new LoftRing(0.655f, 0.196f, 0.112f, -0.002f), new LoftRing(0.7f, 0.2f, 0.108f), new LoftRing(0.745f, 0.135f, 0.082f),
                new LoftRing(0.776f, 0.06f, 0.052f),
            }, Sides, hood, capStart: false, capEnd: false, twoSided: true, innerColor: lining);

            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static Func<Vector3, Color> Paint(Color color)
        {
            var linear = color.linear;
            linear.a = 0f;
            return _ => linear;
        }
    }
}
