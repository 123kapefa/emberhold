using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Как выглядит ноша на плече.</summary>
    public enum WorkerCarry
    {
        None,
        /// <summary>Бревно на плече.</summary>
        Log,
        /// <summary>Мешок с камнями.</summary>
        StoneSack,
        /// <summary>Мешок с травой и прочим сыпучим.</summary>
        GrassSack,
    }

    /// <summary>
    /// Сетка ноши на левом плече. Начало координат — точка вращения корпуса (<see cref="WorkerSkeleton.Pivot"/>
    /// для <see cref="WorkerPart.Torso"/>), поэтому ношу двигают матрицей корпуса. Единицы — блоки при росте height.
    /// </summary>
    public static class WorkerCarryBuilder
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.7f, 0.54f, 0.34f);
        private static readonly Color Burlap = new(0.6f, 0.5f, 0.34f);
        private static readonly Color Stone = new(0.52f, 0.52f, 0.5f);
        private static readonly Color Grass = new(0.42f, 0.56f, 0.24f);
        private static readonly Color Twine = new(0.4f, 0.3f, 0.18f);

        /// <summary>Вершина левого плеча над пелериной, в долях роста от точки вращения корпуса.</summary>
        private static readonly Vector3 Shoulder = new(-0.142f, 0.298f, -0.01f);

        /// <summary>Открытая горловина мешка, спереди сверху.</summary>
        private static readonly Vector3 Mouth = Shoulder + new Vector3(0f, 0.105f, 0.15f);

        /// <summary>
        /// Ноша по ресурсу: поленья — бревно, камень — мешок с камнями, остальное — мешок с травой.
        /// Истопник носит поленья вязанкой на спине, которая и так на нём, поэтому на плече у него ничего.
        /// </summary>
        public static WorkerCarry CarryFor(string itemId, WorkerRole role) => itemId switch
        {
            null or "" => WorkerCarry.None,
            "wood" when role == WorkerRole.Stoker => WorkerCarry.None,
            "wood" => WorkerCarry.Log,
            "stone" => WorkerCarry.StoneSack,
            _ => WorkerCarry.GrassSack,
        };

        public static Mesh Build(WorkerCarry carry, float height, string name = "Carry")
        {
            var mesh = new FacetedMesh { Transform = Matrix4x4.Scale(Vector3.one * height) };
            switch (carry)
            {
                case WorkerCarry.Log:
                    Log(mesh);
                    break;
                case WorkerCarry.StoneSack:
                    Sack(mesh, Burlap);
                    // Из открытой горловины выглядывают камни.
                    for (var i = 0; i < 4; i++)
                    {
                        var a = i * 1.7f;
                        mesh.AddBlob(Mouth + new Vector3(Mathf.Cos(a) * 0.014f, Mathf.Sin(a) * 0.012f + 0.004f, 0.006f * (i % 2)),
                            Vector3.one * (0.022f - 0.002f * i), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.85f + 0.05f * i)));
                    }
                    break;
                case WorkerCarry.GrassSack:
                    Sack(mesh, Burlap * 0.9f);
                    // Из горловины торчат пучки травы вперёд-вверх.
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i * 1.05f;
                        mesh.Transform = Matrix4x4.Scale(Vector3.one * height)
                                         * Matrix4x4.TRS(Mouth + new Vector3(Mathf.Cos(a) * 0.012f, Mathf.Sin(a) * 0.01f, 0f),
                                             Quaternion.Euler(60f + Mathf.Sin(a) * 20f, Mathf.Cos(a) * 20f, 0f), new Vector3(1f, 1f, 0.35f));
                        mesh.AddCone(4, Vector3.zero, 0.012f, 0.07f + 0.01f * (i % 3), 0f, null, Paint(Grass * (0.85f + 0.06f * (i % 3))));
                    }
                    break;
            }
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static void Log(FacetedMesh mesh)
        {
            // Бревно вдоль плеча, передний конец чуть выше: рука держит его спереди.
            var along = new Vector3(0.05f, 0.12f, 1f).normalized;
            var center = Shoulder + new Vector3(0f, 0.048f, 0.03f);
            var back = center - along * 0.24f;
            var front = center + along * 0.22f;
            mesh.AddTube(new[] { back, front }, new[] { new Vector2(0.05f, 0.05f), new Vector2(0.046f, 0.046f) }, 8, Paint(Bark));
            mesh.AddTube(new[] { back - along * 0.004f, back + along * 0.004f }, new[] { new Vector2(0.044f, 0.044f), new Vector2(0.044f, 0.044f) }, 8, Paint(WoodLight));
            mesh.AddTube(new[] { front - along * 0.004f, front + along * 0.004f }, new[] { new Vector2(0.04f, 0.04f), new Vector2(0.04f, 0.04f) }, 8, Paint(WoodLight));
            // Сучок.
            mesh.AddTube(new[] { center - along * 0.08f + Vector3.up * 0.04f, center - along * 0.1f + new Vector3(0.02f, 0.075f, 0f) },
                new[] { new Vector2(0.013f, 0.013f), new Vector2(0.006f, 0.006f) }, 5, Paint(Bark * 0.85f));
        }

        private static void Sack(FacetedMesh mesh, Color cloth)
        {
            // Мешок лежит на плече: тяжёлое тело на плече, открытая горловина спереди сверху.
            mesh.AddBlob(Shoulder + new Vector3(0f, 0.06f, 0.0f), new Vector3(0.07f, 0.065f, 0.11f), 1, 0.12f, new System.Random(7), Paint(cloth));
            mesh.AddLoft(new[]
            {
                new LoftRing(Shoulder + new Vector3(0f, 0.08f, 0.09f), 0.034f, 0.03f, Vector3.right, Vector3.up),
                new LoftRing(Shoulder + new Vector3(0f, 0.095f, 0.125f), 0.02f, 0.018f, Vector3.right, Vector3.up),
                new LoftRing(Mouth, 0.026f, 0.022f, Vector3.right, Vector3.up),
            }, 7, Paint(cloth * 0.92f), capEnd: false);
            // Перевязь под горловиной.
            mesh.AddLoft(new[]
            {
                new LoftRing(Shoulder + new Vector3(0f, 0.093f, 0.12f), 0.023f, 0.021f, Vector3.right, Vector3.up),
                new LoftRing(Shoulder + new Vector3(0f, 0.097f, 0.13f), 0.023f, 0.021f, Vector3.right, Vector3.up),
            }, 7, Paint(Twine));
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);
    }
}
