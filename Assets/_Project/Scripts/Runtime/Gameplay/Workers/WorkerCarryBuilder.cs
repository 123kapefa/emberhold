using System;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>Как выглядит ноша.</summary>
    public enum WorkerCarry
    {
        None,
        /// <summary>Бревно на плече.</summary>
        Log,
        /// <summary>Мешок с камнями, перекинутый через плечо на спину.</summary>
        StoneSack,
        /// <summary>Мешок с травой и прочим сыпучим.</summary>
        GrassSack,
    }

    /// <summary>
    /// Сетка ноши и где она бывает: на плече (<see cref="ShoulderAnchor"/>, в системе корпуса), на земле перед рабочим
    /// (<see cref="Ground"/>, в системе рабочего), за что её держит левая рука (<see cref="Grip"/>, в системе ноши).
    /// Бревно лежит на левом плече вдоль хода, рука обхватывает его сбоку сверху. Мешок перекинут через левое плечо:
    /// тело мешка на спине, горловина через плечо спереди, рука держит её у груди. Единицы — блоки при росте height.
    /// </summary>
    public static class WorkerCarryBuilder
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.7f, 0.54f, 0.34f);
        private static readonly Color Burlap = new(0.6f, 0.5f, 0.34f);
        private static readonly Color Stone = new(0.52f, 0.52f, 0.5f);
        private static readonly Color Grass = new(0.42f, 0.56f, 0.24f);
        private static readonly Color Twine = new(0.4f, 0.3f, 0.18f);

        private const float LogRadius = 0.05f;
        private const float LogLength = 0.46f;

        /// <summary>Тело мешка: полуоси (доли роста). Начало координат мешка — середина тела.</summary>
        private static readonly Vector3 SackBody = new(0.12f, 0.15f, 0.078f);

        /// <summary>Горловина: от верха тела через плечо к груди, в системе мешка (доли роста).</summary>
        private static readonly Vector3[] SackNeck =
        {
            new(-0.01f, 0.13f, 0.04f), new(-0.035f, 0.2f, 0.14f), new(-0.03f, 0.18f, 0.26f), new(-0.015f, 0.1f, 0.315f),
        };

        /// <summary>Мешок горловиной вверх: так он висит, когда его держат за горловину, и так стоит на земле.</summary>
        private static readonly Quaternion Upright = Quaternion.Euler(-72f, 0f, 0f);

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

        /// <summary>Ноша на плече: матрица в системе корпуса (начало — его точка вращения).</summary>
        public static Matrix4x4 ShoulderAnchor(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            // Бревно на вершине левого плеча, передний конец чуть выше.
            ? Matrix4x4.TRS(new Vector3(-0.138f, 0.284f, 0.02f) * height, Quaternion.LookRotation(new Vector3(0.05f, 0.1f, 1f)), Vector3.one)
            // Мешок на спине слева, горловина через плечо.
            : Matrix4x4.TRS(new Vector3(-0.07f, 0.085f, -0.215f) * height, Quaternion.identity, Vector3.one);

        /// <summary>Где ноша, положенная на землю перед рабочим: система рабочего, ноги в нуле.</summary>
        public static Matrix4x4 Ground(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            ? Matrix4x4.TRS(new Vector3(-0.1f, LogRadius, 0.3f) * height, Quaternion.Euler(0f, 8f, 0f), Vector3.one)
            // Мешок стоит на земле, горловиной вверх.
            : Matrix4x4.TRS(new Vector3(-0.1f, 0.11f, 0.32f) * height, Upright, Vector3.one);

        /// <summary>Перед грудью на весу, по пути с плеча на землю: система рабочего.</summary>
        public static Matrix4x4 Front(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            ? Matrix4x4.TRS(new Vector3(-0.13f, 0.56f, 0.24f) * height, Quaternion.LookRotation(new Vector3(0.1f, -0.1f, 1f)), Vector3.one)
            // Мешок висит в руке за горловину.
            : Matrix4x4.TRS(new Vector3(-0.12f, 0.27f, 0.28f) * height, Upright, Vector3.one);

        /// <summary>За что держит левая рука, в системе ноши.</summary>
        public static Vector3 Grip(WorkerCarry carry, float height) => carry == WorkerCarry.Log
            // Сбоку снаружи и чуть сверху, ближе к переднему концу.
            ? new Vector3(-LogRadius - 0.022f, 0.022f, 0.1f) * height
            : (SackNeck[^1] + new Vector3(0f, -0.012f, 0.006f)) * height;

        /// <summary>Куда отводить локоть, когда рука держит ношу: в системе рабочего, относительно точки хвата.</summary>
        public static Vector3 ElbowHint(float height) => new Vector3(-0.25f, -0.35f, -0.05f) * height;

        public static Mesh Build(WorkerCarry carry, float height, string name = "Carry")
        {
            var scale = Matrix4x4.Scale(Vector3.one * height);
            var mesh = new FacetedMesh { Transform = scale };
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
                        mesh.AddBlob(Mouth + new Vector3(Mathf.Cos(a) * 0.016f, Mathf.Sin(a) * 0.014f + 0.006f, 0.008f * (i % 2)),
                            Vector3.one * (0.024f - 0.002f * i), 0, 0.3f, new System.Random(i + 3), Paint(Stone * (0.85f + 0.05f * i)));
                    }
                    break;
                case WorkerCarry.GrassSack:
                    Sack(mesh, Burlap * 0.9f);
                    // Из горловины торчат пучки травы.
                    for (var i = 0; i < 6; i++)
                    {
                        var a = i * 1.05f;
                        mesh.Transform = scale * Matrix4x4.TRS(Mouth + new Vector3(Mathf.Cos(a) * 0.014f, Mathf.Sin(a) * 0.012f, 0f),
                            Quaternion.Euler(70f + Mathf.Sin(a) * 25f, Mathf.Cos(a) * 25f, 0f), new Vector3(1f, 1f, 0.35f));
                        mesh.AddCone(4, Vector3.zero, 0.013f, 0.08f + 0.012f * (i % 3), 0f, null, Paint(Grass * (0.85f + 0.06f * (i % 3))));
                    }
                    break;
            }
            mesh.Transform = Matrix4x4.identity;
            return mesh.ToMesh(name);
        }

        private static Vector3 Mouth => SackNeck[^1] + new Vector3(0f, -0.02f, 0.01f);

        private static void Log(FacetedMesh mesh)
        {
            // Бревно вдоль +Z, светлые торцы, сучок сверху.
            var back = Vector3.back * (LogLength * 0.5f);
            var front = Vector3.forward * (LogLength * 0.5f);
            mesh.AddTube(new[] { back, front }, new[] { Vector2.one * LogRadius, Vector2.one * (LogRadius * 0.93f) }, 8, Paint(Bark));
            mesh.AddTube(new[] { back + Vector3.back * 0.003f, back + Vector3.forward * 0.004f }, new[] { Vector2.one * (LogRadius * 0.86f), Vector2.one * (LogRadius * 0.86f) },
                8, Paint(WoodLight));
            mesh.AddTube(new[] { front + Vector3.back * 0.004f, front + Vector3.forward * 0.003f }, new[] { Vector2.one * (LogRadius * 0.8f), Vector2.one * (LogRadius * 0.8f) },
                8, Paint(WoodLight));
            mesh.AddTube(new[] { new Vector3(0.01f, LogRadius * 0.8f, -0.09f), new Vector3(0.03f, LogRadius + 0.03f, -0.11f) },
                new[] { new Vector2(0.013f, 0.013f), new Vector2(0.006f, 0.006f) }, 5, Paint(Bark * 0.85f));
        }

        private static void Sack(FacetedMesh mesh, Color cloth)
        {
            // Тяжёлое тело мешка — шире к дну, горловина сужается и снова расширяется у завязки.
            mesh.AddBlob(Vector3.zero, SackBody, 1, 0.1f, new System.Random(7), Paint(cloth));
            mesh.AddBlob(new Vector3(0f, -0.06f, 0f), new Vector3(SackBody.x * 1.06f, SackBody.y * 0.62f, SackBody.z * 1.08f), 1, 0.08f,
                new System.Random(9), Paint(cloth * 0.95f));
            var radii = new[] { new Vector2(0.06f, 0.045f), new Vector2(0.034f, 0.028f), new Vector2(0.024f, 0.021f), new Vector2(0.03f, 0.026f) };
            mesh.AddTube(SackNeck, radii, 7, Paint(cloth * 0.93f));
            // Перевязь у горловины.
            var neckDirection = (SackNeck[^1] - SackNeck[^2]).normalized;
            mesh.AddTube(new[] { SackNeck[^2] + neckDirection * 0.035f, SackNeck[^2] + neckDirection * 0.048f },
                new[] { new Vector2(0.026f, 0.023f), new Vector2(0.026f, 0.023f) }, 7, Paint(Twine));
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);
    }
}
