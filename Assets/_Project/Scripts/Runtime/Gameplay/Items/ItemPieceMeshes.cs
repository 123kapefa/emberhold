using System;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Items
{
    /// <summary>Форма обломка: осколок блока, крошка, лист или хвоинка.</summary>
    public enum DebrisShape
    {
        Shard,
        Chip,
        Leaf,
    }

    /// <summary>
    /// Гранёные сетки предметов и обломков для материала деревьев (Emberhold/World/Tree): цвет — в вершинах.
    /// У поленьев и камней цвет свой (альфа 0), у обломков вершины белые с альфой 1, поэтому цвет им задаёт
    /// _BaseColor материала (MaterialPropertyBlock): одна сетка на любой материал блока. Сетки строятся один раз.
    /// Единицы — блоки, середина сетки в начале координат.
    /// </summary>
    public static class ItemPieceMeshes
    {
        private static readonly Color Bark = new(0.36f, 0.24f, 0.15f);
        private static readonly Color WoodLight = new(0.72f, 0.56f, 0.36f);
        private static readonly Color StoneGrey = new(0.55f, 0.55f, 0.53f);

        /// <summary>Сколько разных камней и осколков.</summary>
        public const int Variants = 3;

        private static Mesh log;
        private static readonly Mesh[] Stones = new Mesh[Variants];
        private static readonly Mesh[][] Debris = new Mesh[3][];

        /// <summary>Полено вдоль Z, радиус и длина — как в кучке (<see cref="ItemPileLayout"/>).</summary>
        public static Mesh Log()
        {
            if (log != null)
                return log;
            var mesh = new FacetedMesh();
            var r = ItemPileLayout.LogRadius;
            var back = Vector3.back * (ItemPileLayout.LogLength * 0.5f);
            var front = Vector3.forward * (ItemPileLayout.LogLength * 0.5f);
            mesh.AddTube(new[] { back, front }, new[] { Vector2.one * r, Vector2.one * (r * 0.93f) }, 7, Paint(Bark));
            // Светлые торцы чуть выступают из коры.
            mesh.AddTube(new[] { back + Vector3.back * 0.004f, back + Vector3.forward * 0.004f },
                new[] { Vector2.one * (r * 0.84f), Vector2.one * (r * 0.84f) }, 7, Paint(WoodLight));
            mesh.AddTube(new[] { front + Vector3.back * 0.004f, front + Vector3.forward * 0.004f },
                new[] { Vector2.one * (r * 0.78f), Vector2.one * (r * 0.78f) }, 7, Paint(WoodLight));
            // Сучок.
            mesh.AddTube(new[] { new Vector3(0.02f, r * 0.8f, -0.12f), new Vector3(0.04f, r + 0.035f, -0.14f) },
                new[] { new Vector2(0.018f, 0.018f), new Vector2(0.008f, 0.008f) }, 5, Paint(Bark * 0.85f));
            return log = Keep(mesh.ToMesh("Log"));
        }

        /// <summary>Камень variant: неровный гранёный булыжник радиуса <see cref="StoneHeap.Radius"/>.</summary>
        public static Mesh Stone(int variant)
        {
            variant = Mod(variant, Variants);
            if (Stones[variant] != null)
                return Stones[variant];
            var mesh = new FacetedMesh();
            var random = new System.Random(17 + variant * 31);
            var radius = StoneHeap.Radius;
            var radii = new Vector3(radius * (1.05f + 0.1f * variant), radius * 0.78f, radius * (0.95f - 0.05f * variant));
            mesh.AddBlob(Vector3.zero, radii, 1, 0.22f, random, FacetShade(StoneGrey, 0.86f, 1.04f, 0f, variant));
            return Stones[variant] = Keep(mesh.ToMesh($"Stone {variant}"));
        }

        /// <summary>Обломок формы shape, вариант variant: размер 1 блок, белый, цвет задаёт _BaseColor.</summary>
        public static Mesh Piece(DebrisShape shape, int variant)
        {
            variant = Mod(variant, Variants);
            var set = Debris[(int)shape] ??= new Mesh[Variants];
            if (set[variant] != null)
                return set[variant];
            var mesh = new FacetedMesh();
            var random = new System.Random(101 + (int)shape * 13 + variant * 7);
            var radii = shape switch
            {
                // Осколок — неровный клин, крошка — комочек, лист — тонкая пластинка.
                DebrisShape.Shard => new Vector3(0.5f, 0.36f + 0.06f * variant, 0.3f),
                DebrisShape.Chip => new Vector3(0.5f, 0.42f, 0.45f),
                _ => new Vector3(0.5f, 0.07f, 0.32f),
            };
            var jitter = shape == DebrisShape.Shard ? 0.35f : 0.2f;
            mesh.AddBlob(Vector3.zero, radii, 0, jitter, random, FacetShade(Color.white, 0.8f, 1f, 1f, variant));
            return set[variant] = Keep(mesh.ToMesh($"{shape} {variant}"));
        }

        /// <summary>Цвет грани: color с небольшим разбросом яркости по месту грани, чтобы грани читались. alpha — в вершины.</summary>
        private static Func<Vector3, Color> FacetShade(Color color, float min, float max, float alpha, int seed)
        {
            var linear = color.linear;
            return point =>
            {
                var t = Mathf.Repeat(Mathf.Sin(Vector3.Dot(point, new Vector3(12.9898f, 78.233f, 37.719f)) + seed) * 43758.5453f, 1f);
                var shade = linear * Mathf.Lerp(min, max, t);
                shade.a = alpha;
                return shade;
            };
        }

        private static Func<Vector3, Color> Paint(Color color) => WorkerMeshBuilder.Paint(color);

        // Общие сетки живут всё время игры: при выгрузке сцены их не уничтожать.
        private static Mesh Keep(Mesh mesh)
        {
            mesh.hideFlags = HideFlags.DontSave;
            return mesh;
        }

        private static int Mod(int value, int count) => (value % count + count) % count;
    }
}
