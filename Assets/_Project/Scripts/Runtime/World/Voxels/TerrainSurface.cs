using System;
using System.Collections;
using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Гладкая поверхность нетронутой земли: высоты в углах сетки блоков (в шестнадцатых от дна мира)
    /// с любой крутизной, а не только до блока на блок, как у скатов. Блоки мира под ней остаются
    /// обычными и повторяют её как могут; рисуется же нетронутая земля по этой поверхности, одной
    /// сплошной сеткой без ступенек и швов. Столбец перестаёт быть нетронутым, как только меняется
    /// любой блок под поверхностью (<see cref="Covers"/>): дальше он рисуется блоками.
    /// Блоки, поставленные целиком над поверхностью, нетронутость не снимают.
    /// </summary>
    public sealed class TerrainSurface
    {
        private readonly int[,] corners;
        private readonly int[,] topY;
        private readonly BitArray edited;
        private byte[,] rock;

        public int SizeX { get; }
        public int SizeZ { get; }

        /// <param name="corners">Высоты углов (ширина + 1) × (длина + 1) в шестнадцатых; массив не копируется.</param>
        /// <param name="topY">Слой верхнего блока земли в каждом столбце (ширина × длина).</param>
        public TerrainSurface(int[,] corners, int[,] topY)
        {
            SizeX = topY.GetLength(0);
            SizeZ = topY.GetLength(1);
            if (corners.GetLength(0) != SizeX + 1 || corners.GetLength(1) != SizeZ + 1)
                throw new ArgumentException($"Нужна сетка углов {SizeX + 1}×{SizeZ + 1}", nameof(corners));
            this.corners = corners;
            this.topY = topY;
            edited = new BitArray(SizeX * SizeZ);
        }

        public bool Contains(int x, int z) => (uint)x < SizeX && (uint)z < SizeZ;

        /// <summary>Нетронут ли столбец: рисуется гладкой поверхностью.</summary>
        public bool IsNatural(int x, int z) => Contains(x, z) && !edited[x + z * SizeX];

        /// <summary>Слой верхнего блока земли в столбце, каким его построил генератор.</summary>
        public int TopY(int x, int z) => topY[x, z];

        /// <summary>Высота угла сетки в шестнадцатых.</summary>
        public int Corner(int x, int z) => corners[x, z];

        /// <summary>
        /// Лежит ли блок (x, y, z) под поверхностью, хотя бы частично: ниже её самого высокого угла в столбце.
        /// Такой блок — часть земли, его смена делает столбец тронутым.
        /// </summary>
        public bool Covers(int x, int y, int z)
        {
            var top = Math.Max(Math.Max(corners[x, z], corners[x + 1, z]), Math.Max(corners[x, z + 1], corners[x + 1, z + 1]));
            return y * BlockShape.MaxHeight < top;
        }

        /// <summary>Высота поверхности в середине столбца, в блоках.</summary>
        public float CenterHeight(int x, int z) =>
            (corners[x, z] + corners[x + 1, z] + corners[x, z + 1] + corners[x + 1, z + 1]) * 0.25f / BlockShape.MaxHeight;

        /// <summary>
        /// Высота земли для блока (x, y, z), если это верхний блок земли нетронутого столбца:
        /// на неё встают рабочие, ползуны и предметы, чтобы не висеть над гладкой поверхностью и не тонуть в ней.
        /// </summary>
        public bool TryGroundHeight(int x, int y, int z, out float height)
        {
            height = 0f;
            if (!IsNatural(x, z) || y != topY[x, z])
                return false;
            height = CenterHeight(x, z);
            return true;
        }

        /// <summary>Отметить столбец тронутым. true — он был нетронут (изменилась картинка у него и у соседей).</summary>
        public bool MarkEdited(int x, int z)
        {
            if (!IsNatural(x, z))
                return false;
            edited[x + z * SizeX] = true;
            return true;
        }

        /// <summary>
        /// Насколько угол каменный, 0..255: поверхность плавно переходит из травы в камень между углами.
        /// Размер как у сетки углов; null — везде трава.
        /// </summary>
        public void SetRock(byte[,] weights)
        {
            if (weights != null && (weights.GetLength(0) != SizeX + 1 || weights.GetLength(1) != SizeZ + 1))
                throw new ArgumentException($"Нужна сетка углов {SizeX + 1}×{SizeZ + 1}", nameof(weights));
            rock = weights;
        }

        /// <summary>Каменность угла, 0..1.</summary>
        public float Rock(int x, int z) => rock == null ? 0f : rock[x, z] / 255f;

        /// <summary>Нормаль поверхности в углу сетки по соседним углам.</summary>
        public Vector3 Normal(int x, int z)
        {
            var gx = (corners[Math.Min(x + 1, SizeX), z] - corners[Math.Max(x - 1, 0), z]) * 0.5f / BlockShape.MaxHeight;
            var gz = (corners[x, Math.Min(z + 1, SizeZ)] - corners[x, Math.Max(z - 1, 0)]) * 0.5f / BlockShape.MaxHeight;
            return new Vector3(-gx, 1f, -gz).normalized;
        }
    }
}
