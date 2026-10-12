using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Куда может встать ползун: высота земли в столбце блоков и проверка, не слишком ли высокий уступ.
    /// Считается по данным мира, а не лучами, поэтому стена, поставленная в этом кадре, сразу мешает пройти.
    /// Координаты — локальные координаты мира: блок (x, y, z) занимает куб от (x, y, z), 1 блок = 1 метр.
    /// Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public static class CrawlerFooting
    {
        private const float Epsilon = 0.001f;

        /// <summary>
        /// Высота земли в точке (x, z), куда можно шагнуть с высоты from, поднявшись не больше maxStep.
        /// Вниз спускаться можно на любую высоту. false — уступ выше шага, край мира или под точкой пусто.
        /// </summary>
        public static bool TryStep(VoxelWorld world, float x, float z, float from, float maxStep, out float surface)
        {
            surface = 0f;
            var size = world.SizeInBlocks;
            var cx = Mathf.FloorToInt(x);
            var cz = Mathf.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= size.x || cz >= size.z)
                return false;

            var top = from + maxStep;
            var startY = Mathf.Min(Mathf.FloorToInt(top), size.y - 1);
            for (var y = startY; y >= 0; y--)
            {
                var block = world.GetBlock(new Vector3Int(cx, y, cz));
                if (block.IsAir)
                    continue;
                surface = y + Height(block.Shape);
                return surface <= top + Epsilon;
            }
            return false;
        }

        /// <summary>Самая верхняя земля в точке (x, z) — куда встаёт появившийся ползун.</summary>
        public static bool TryTopSurface(VoxelWorld world, float x, float z, out float surface) =>
            TryStep(world, x, z, world.SizeInBlocks.y, 0f, out surface);

        /// <summary>
        /// Высота верха формы внутри блока, 0..1. У скатов — средняя высота углов, чтобы ползун не висел
        /// над склоном; у фигурных форм (лестница, столб) — самый верх.
        /// </summary>
        public static float Height(BlockShape shape) =>
            shape.IsCatalog ? shape.Top / (float)BlockShape.MaxHeight : shape.Fill;
    }
}
