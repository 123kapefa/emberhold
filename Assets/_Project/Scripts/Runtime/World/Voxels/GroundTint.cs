using System;
using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Оттенок травы по миру: цвет в каждом углу сетки блоков, между углами — плавно. rgb умножается на траву,
    /// a — насыщенность (1 — как есть, меньше — блёклее). Генератор смешивает оттенки локаций, и мешер кладёт
    /// оттенок своего места в цвет вершин: его берут гладкая земля, блоки травы и бахрома травы на соседях.
    /// Там же по углам — смесь видов травы (<see cref="GrassKind"/>): какую текстуру травы брать.
    /// </summary>
    public sealed class GroundTint
    {
        private readonly Color[,] corners;
        private readonly Color[,] kinds;

        public int SizeX { get; }
        public int SizeZ { get; }

        /// <param name="corners">Оттенки углов (ширина + 1) × (длина + 1); массив не копируется.</param>
        /// <param name="kinds">
        /// Смесь видов травы в углах того же размера (<see cref="GrassKinds.Weight"/>); null — везде луговая.
        /// Массив не копируется.
        /// </param>
        public GroundTint(Color[,] corners, Color[,] kinds = null)
        {
            this.corners = corners ?? throw new ArgumentNullException(nameof(corners));
            SizeX = corners.GetLength(0) - 1;
            SizeZ = corners.GetLength(1) - 1;
            if (kinds != null && (kinds.GetLength(0) != SizeX + 1 || kinds.GetLength(1) != SizeZ + 1))
                throw new ArgumentException($"Нужна сетка углов {SizeX + 1}×{SizeZ + 1}", nameof(kinds));
            this.kinds = kinds;
        }

        public Color Corner(int x, int z) => corners[x, z];

        /// <summary>Оттенок в точке (x, z) в блоках мира: между четырьмя углами клетки, за краем — как у края.</summary>
        public Color Sample(float x, float z) => Sample(corners, x, z);

        /// <summary>Смесь видов травы в точке (x, z): r — топь, g — мёртвый город, b — горы, остаток — луговая.</summary>
        public Color SampleKinds(float x, float z) => kinds == null ? default : Sample(kinds, x, z);

        private Color Sample(Color[,] grid, float x, float z)
        {
            x = Mathf.Clamp(x, 0f, SizeX);
            z = Mathf.Clamp(z, 0f, SizeZ);
            var x0 = Mathf.Min((int)x, SizeX - 1);
            var z0 = Mathf.Min((int)z, SizeZ - 1);
            var u = x - x0;
            var v = z - z0;
            var bottom = Color.LerpUnclamped(grid[x0, z0], grid[x0 + 1, z0], u);
            var top = Color.LerpUnclamped(grid[x0, z0 + 1], grid[x0 + 1, z0 + 1], u);
            return Color.LerpUnclamped(bottom, top, v);
        }
    }
}
