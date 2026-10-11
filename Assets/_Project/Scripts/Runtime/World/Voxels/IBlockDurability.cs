using System;

namespace Emberhold.World.Voxels
{
    /// <summary>Прочность блока в единицах здоровья (ХП) по его материалу.</summary>
    public interface IBlockDurability
    {
        float HitPoints(byte material);
    }

    /// <summary>Одинаковая прочность у всех материалов — для тестов и как запасной вариант.</summary>
    public sealed class UniformBlockDurability : IBlockDurability
    {
        private readonly float hitPoints;

        public UniformBlockDurability(float hitPoints)
        {
            if (hitPoints <= 0f)
                throw new ArgumentOutOfRangeException(nameof(hitPoints), "Прочность должна быть положительной");
            this.hitPoints = hitPoints;
        }

        public float HitPoints(byte material) => hitPoints;
    }
}
