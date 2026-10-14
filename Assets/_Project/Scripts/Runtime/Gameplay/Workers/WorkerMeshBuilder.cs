using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Гранёная low-poly сетка рабочего в стиле деревьев: сапоги, штаны, рубаха с поясом и сумкой, руки с кистями,
    /// голова с носом, глазами и бровями, у части борода, капюшон с хвостом и накидка на плечи и спину.
    /// Ноги в (0, 0, 0), лицо смотрит в +Z, единица — блок, рост height. Все размеры — доли роста.
    /// Цвет вершин — цвет части, a = 0: шейдер Emberhold/World/Tree оставляет его как есть.
    /// </summary>
    public static class WorkerMeshBuilder
    {
        private static readonly Color Pants = new(0.29f, 0.243f, 0.196f);
        private static readonly Color Boots = new(0.18f, 0.149f, 0.125f);
        private static readonly Color Leather = new(0.369f, 0.251f, 0.157f);
        private static readonly Color Buckle = new(0.784f, 0.635f, 0.29f);
        private static readonly Color Eyes = new(0.133f, 0.102f, 0.078f);

        public static Mesh Build(WorkerLook look, float height, string name = "Worker")
        {
            var mesh = new FacetedMesh();
            var u = height;
            var tunic = Paint(look.Tunic);
            var skin = Paint(look.Skin);
            var hood = Paint(look.Hood);

            // Ноги: ступня, голенище сапога, голень, бедро.
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * 0.055f * u;
                mesh.AddBox(new Vector3(x, 0.025f * u, 0.015f * u), new Vector3(0.075f, 0.05f, 0.12f) * u, Paint(Boots));
                mesh.AddPrism(6, new Vector3(x, 0.04f * u, 0f), 0.034f * u, 0.036f * u, 0.1f * u, 0f, null, Paint(Boots));
                mesh.AddPrism(6, new Vector3(x, 0.13f * u, 0f), 0.034f * u, 0.038f * u, 0.16f * u, 0f, null, Paint(Pants));
                mesh.AddPrism(6, new Vector3(x, 0.275f * u, 0f), 0.04f * u, 0.048f * u, 0.17f * u, 0f, null, Paint(Pants));
            }

            // Подол, грудь, пояс с пряжкой и сумкой.
            mesh.AddPrism(8, new Vector3(0f, 0.405f * u, 0f), 0.15f * u, 0.12f * u, 0.13f * u, 0f, null, tunic);
            mesh.AddPrism(8, new Vector3(0f, 0.52f * u, 0f), 0.12f * u, 0.14f * u, 0.24f * u, 0f, null, tunic);
            mesh.AddPrism(8, new Vector3(0f, 0.5175f * u, 0f), 0.125f * u, 0.125f * u, 0.025f * u, 0f, null, Paint(Leather));
            mesh.AddBox(new Vector3(0f, 0.53f * u, 0.125f * u), new Vector3(0.03f, 0.025f, 0.012f) * u, Paint(Buckle));
            Place(mesh, new Vector3(0.1f * u, 0.5f * u, 0.06f * u), Quaternion.Euler(0f, 30f, 0f));
            mesh.AddBox(Vector3.zero, new Vector3(0.05f, 0.055f, 0.03f) * u, Paint(Leather));

            // Накидка цвета роли: на плечах и короткая на спине.
            Place(mesh, Vector3.zero, Quaternion.identity);
            mesh.AddPrism(8, new Vector3(0f, 0.715f * u, 0f), 0.17f * u, 0.1f * u, 0.07f * u, 0f, null, hood);
            Place(mesh, new Vector3(0f, 0.62f * u, -0.13f * u), Quaternion.Euler(7f, 0f, 0f));
            mesh.AddBox(Vector3.zero, new Vector3(0.22f, 0.26f, 0.02f) * u, hood);

            // Руки чуть вперёд и в стороны: плечо, предплечье, кисть.
            foreach (var side in new[] { -1f, 1f })
            {
                var shoulder = Matrix4x4.TRS(new Vector3(side * 0.155f * u, 0.75f * u, 0f),
                    Quaternion.Euler(-14f, 0f, side * 7f), Vector3.one);
                mesh.Transform = shoulder;
                mesh.AddPrism(6, new Vector3(0f, -0.15f * u, 0f), 0.032f * u, 0.036f * u, 0.15f * u, 0f, null, tunic);
                mesh.Transform = shoulder * Matrix4x4.TRS(new Vector3(0f, -0.15f * u, 0f), Quaternion.Euler(-12f, 0f, 0f), Vector3.one);
                mesh.AddPrism(6, new Vector3(0f, -0.13f * u, 0f), 0.028f * u, 0.032f * u, 0.13f * u, 0f, null, tunic);
                mesh.AddBlob(new Vector3(0f, -0.15f * u, 0f), Vector3.one * (0.03f * u), 0, 0f, null, skin);
            }

            // Шея и голова.
            Place(mesh, Vector3.zero, Quaternion.identity);
            mesh.AddPrism(6, new Vector3(0f, 0.765f * u, 0f), 0.045f * u, 0.04f * u, 0.05f * u, 0f, null, skin);
            var head = new Vector3(0f, 0.875f * u, 0.005f * u);
            mesh.AddBlob(head, new Vector3(0.076f, 0.086f, 0.08f) * u, 1, 0f, null, skin);
            foreach (var side in new[] { -1f, 1f })
            {
                mesh.AddBox(head + new Vector3(side * 0.029f, 0.014f, 0.074f) * u, new Vector3(0.014f, 0.012f, 0.01f) * u, Paint(Eyes));
                mesh.AddBox(head + new Vector3(side * 0.03f, 0.033f, 0.068f) * u, new Vector3(0.03f, 0.007f, 0.012f) * u, Paint(look.Hair));
            }
            if (look.Beard)
                mesh.AddBlob(head + new Vector3(0f, -0.045f, 0.045f) * u, new Vector3(0.055f, 0.045f, 0.03f) * u, 0, 0f, null, Paint(look.Hair));
            Place(mesh, head + new Vector3(0f, -0.005f, 0.065f) * u, Quaternion.Euler(90f, 0f, 0f));
            mesh.AddCone(4, Vector3.zero, 0.014f * u, 0.035f * u, 0f, null, skin);

            // Капюшон: облегает голову сзади и сверху, лицо открыто; хвост свисает по спине.
            Place(mesh, Vector3.zero, Quaternion.identity);
            mesh.AddBlob(head + new Vector3(0f, 0.012f, -0.028f) * u, new Vector3(0.095f, 0.1f, 0.092f) * u, 1, 0f, null, hood);
            Place(mesh, head + new Vector3(0f, 0.02f, -0.085f) * u, Quaternion.Euler(-160f, 0f, 0f));
            mesh.AddCone(6, Vector3.zero, 0.04f * u, 0.13f * u, 0f, null, hood);

            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static void Place(FacetedMesh mesh, Vector3 position, Quaternion rotation) =>
            mesh.Transform = Matrix4x4.TRS(position, rotation, Vector3.one);

        private static Func<Vector3, Color> Paint(Color color)
        {
            var linear = color.linear;
            linear.a = 0f;
            return _ => linear;
        }
    }
}
