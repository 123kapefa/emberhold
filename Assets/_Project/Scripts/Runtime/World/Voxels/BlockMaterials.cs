namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Номера материалов блоков. В хранилище на материал отведено 6 бит, поэтому номер не больше <see cref="Max"/>.
    /// Пока это константы прототипа; позже станут таблицей в Data/Blocks.
    /// </summary>
    public static class BlockMaterials
    {
        public const byte Air = 0;
        public const byte Grass = 1;
        public const byte Dirt = 2;
        public const byte Stone = 3;

        /// <summary>Сколько материалов прототипа заведено, включая воздух.</summary>
        public const int Count = 4;

        public const byte Max = 63;
    }
}
