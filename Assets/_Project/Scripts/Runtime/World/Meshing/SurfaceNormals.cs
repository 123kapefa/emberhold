using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Meshing
{
    /// <summary>
    /// Сглаженные нормали верха блоков: нормаль в углу считается по высотам поверхности в соседних углах
    /// сетки (центральная разность), поэтому соседние треугольники одного склона освещаются плавно,
    /// без граней. Высота поверхности в углу — верхний непустой блок столбца. Если вершина не лежит
    /// на этой поверхности (под нависанием или у края обрыва), остаётся нормаль своего треугольника.
    /// </summary>
    public sealed class SurfaceNormals
    {
        // Углы чанка от −1 до Size + 1 включительно: для центральной разности нужны соседи за краем.
        private const int GridSize = ChunkData.Size + 3;
        private const int NoSurface = int.MinValue;

        private readonly int[,] heights = new int[GridSize, GridSize];

        /// <summary>Посчитать высоты поверхности вокруг чанка.</summary>
        public void Prepare(VoxelWorld world, Vector3Int chunk)
        {
            var origin = chunk * ChunkData.Size;
            for (var k = 0; k < GridSize; k++)
            for (var i = 0; i < GridSize; i++)
                heights[i, k] = SurfaceAt(world, origin.x + i - 1, origin.z + k - 1);
        }

        /// <summary>
        /// Нормаль в углу (x, z) в координатах чанка (0..Size) для вершины на высоте height (шестнадцатые от дна мира).
        /// </summary>
        public Vector3 At(int x, int z, int height, Vector3 fallback)
        {
            var i = x + 1;
            var k = z + 1;
            if (heights[i, k] != height)
                return fallback;

            var left = Neighbour(i - 1, k, height);
            var right = Neighbour(i + 1, k, height);
            var back = Neighbour(i, k - 1, height);
            var forward = Neighbour(i, k + 1, height);
            // Высоты в шестнадцатых, шаг сетки — блок (16 шестнадцатых).
            var dx = (right - left) / (2f * BlockShape.MaxHeight);
            var dz = (forward - back) / (2f * BlockShape.MaxHeight);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        /// <summary>Высота соседнего угла; за краем мира или у обрыва выше блока — как у самой вершины.</summary>
        private int Neighbour(int i, int k, int height)
        {
            var h = heights[i, k];
            if (h == NoSurface || Mathf.Abs(h - height) > BlockShape.MaxHeight)
                return height;
            return h;
        }

        /// <summary>Абсолютная высота поверхности в углу сетки мира (cx, cz).</summary>
        private static int SurfaceAt(VoxelWorld world, int cx, int cz)
        {
            var size = world.SizeInBlocks;
            if (cx < 0 || cz < 0 || cx > size.x || cz > size.z)
                return NoSurface;

            // Угол принадлежит четырём столбцам; берём тот, у которого он ближний левый, а на дальнем краю мира — соседа.
            var x = Mathf.Min(cx, size.x - 1);
            var z = Mathf.Min(cz, size.z - 1);
            var cornerX = cx - x;
            var cornerZ = cz - z;
            for (var y = size.y - 1; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(x, y, z));
                if (block.IsAir)
                    continue;
                // У фигурной формы (лестница) углы — не поверхность, сглаживать по ней нечего.
                if (block.Shape.IsCatalog)
                    return NoSurface;
                var s = block.Shape;
                var corner = cornerX == 0 ? (cornerZ == 0 ? s.C00 : s.C01) : (cornerZ == 0 ? s.C10 : s.C11);
                return y * BlockShape.MaxHeight + corner;
            }
            return NoSurface;
        }
    }
}
