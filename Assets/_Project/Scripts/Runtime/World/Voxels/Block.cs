using System;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Блок мира: материал и форма. Пустой блок — воздух: материал 0 и нулевая форма.
    /// В чанке хранится упакованным в 2 байта (см. <see cref="ChunkData"/>).
    /// </summary>
    public readonly struct Block : IEquatable<Block>
    {
        public readonly byte Material;
        public readonly BlockShape Shape;

        public static Block Air => default;

        /// <summary>Блок без материала или с нулевой высотой всех углов считается воздухом.</summary>
        public Block(byte material, BlockShape shape)
        {
            if (material == BlockMaterials.Air || shape.IsEmpty)
            {
                Material = BlockMaterials.Air;
                Shape = default;
                return;
            }
            Material = material;
            Shape = shape;
        }

        public static Block Full(byte material) => new(material, BlockShape.Full);

        public static Block Flat(byte material, int height) => new(material, BlockShape.Flat(height));

        public bool IsAir => Material == BlockMaterials.Air;

        public bool Equals(Block other) => Material == other.Material && Shape == other.Shape;
        public override bool Equals(object obj) => obj is Block other && Equals(other);
        public override int GetHashCode() => Material << 20 ^ Shape.GetHashCode();
        public static bool operator ==(Block a, Block b) => a.Equals(b);
        public static bool operator !=(Block a, Block b) => !a.Equals(b);

        public override string ToString() => IsAir ? "Air" : $"{Material}:{Shape}";
    }
}
