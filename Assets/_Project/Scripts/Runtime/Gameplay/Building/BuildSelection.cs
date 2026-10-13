using System;
using Emberhold.World.Voxels;

namespace Emberhold.Gameplay.Building
{
    /// <summary>Формы, которые игрок ставит в прототипе. Детали на несколько клеток, деревянные формы и дверь — #44–#46.</summary>
    public enum BuildForm
    {
        Full,
        Slab,
        Ramp,
        OuterCorner,
        InnerCorner,
        Stairs,
        SquarePillar,
        RoundPillar,
        Beam,
        Bracket,
        HalfArch,
    }

    /// <summary>
    /// Что игрок сейчас ставит: форма, материал, поворот и высота полублока. Без поворота скат,
    /// углы ската и лестница поднимаются к +Z (от игрока, если камера смотрит вдоль +Z), балка лежит вдоль Z,
    /// кронштейн прилегает к стене на +Z, нога полуарки стоит на +Z.
    /// </summary>
    public sealed class BuildSelection
    {
        public static readonly byte[] Materials = { BlockMaterials.Stone, BlockMaterials.Wood, BlockMaterials.Dirt };
        private static readonly BuildForm[] Forms = (BuildForm[])Enum.GetValues(typeof(BuildForm));

        private int materialIndex;
        private int slabHeight = BlockShape.MaxHeight / 2;

        public BuildForm Form { get; private set; } = BuildForm.Full;
        /// <summary>Поворот: четверти оборота против часовой стрелки (вид сверху: +X вправо, +Z вверх), 0..3.</summary>
        public int Rotation { get; private set; }
        public byte Material => Materials[materialIndex];

        /// <summary>Высота полублока в шестнадцатых, 1..16.</summary>
        public int SlabHeight
        {
            get => slabHeight;
            set => slabHeight = Math.Clamp(value, 1, BlockShape.MaxHeight);
        }

        public void NextForm(int direction = 1) => Form = Forms[Wrap((int)Form + direction, Forms.Length)];
        public void NextMaterial(int direction = 1) => materialIndex = Wrap(materialIndex + direction, Materials.Length);
        public void Rotate() => Rotation = (Rotation + 1) & 3;

        /// <summary>Блок, который встанет при клике.</summary>
        public Block Block => new(Material, Shape(Form, Rotation, SlabHeight));

        /// <summary>Форма блока для выбранной формы, поворота и высоты полублока.</summary>
        public static BlockShape Shape(BuildForm form, int rotation, int slabHeight)
        {
            const int h = BlockShape.MaxHeight;
            var shape = form switch
            {
                BuildForm.Full => BlockShape.Full,
                BuildForm.Slab => BlockShape.Flat(Math.Clamp(slabHeight, 1, h)),
                // Углы: C00, C10 — ближняя сторона (z = 0), C01, C11 — дальняя (z = 1).
                BuildForm.Ramp => new BlockShape(0, 0, h, h),
                BuildForm.OuterCorner => new BlockShape(0, 0, 0, h),
                BuildForm.InnerCorner => new BlockShape(0, h, h, h),
                BuildForm.Stairs => BlockShape.Catalog(BlockForm.Stairs, 0),
                BuildForm.SquarePillar => BlockShape.Catalog(BlockForm.SquarePillar, 0),
                BuildForm.RoundPillar => BlockShape.Catalog(BlockForm.RoundPillar, 0),
                BuildForm.Beam => BlockShape.Catalog(BlockForm.Beam, 0),
                BuildForm.Bracket => BlockShape.Catalog(BlockForm.Bracket, 0),
                BuildForm.HalfArch => BlockShape.Catalog(BlockForm.HalfArch, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(form)),
            };
            for (var i = 0; i < (rotation & 3); i++)
                shape = shape.RotatedQuarter();
            return shape;
        }

        public static string FormName(BuildForm form) => form switch
        {
            BuildForm.Full => "Полный блок",
            BuildForm.Slab => "Полублок",
            BuildForm.Ramp => "Скат",
            BuildForm.OuterCorner => "Внешний угол ската",
            BuildForm.InnerCorner => "Внутренний угол ската",
            BuildForm.Stairs => "Лестница",
            BuildForm.SquarePillar => "Столб квадратный",
            BuildForm.RoundPillar => "Столб круглый",
            BuildForm.Beam => "Балка",
            BuildForm.Bracket => "Кронштейн",
            BuildForm.HalfArch => "Полуарка",
            _ => form.ToString(),
        };

        public static string MaterialName(byte material) => material switch
        {
            BlockMaterials.Stone => "Камень",
            BlockMaterials.Wood => "Дерево",
            BlockMaterials.Dirt => "Земля",
            BlockMaterials.Grass => "Трава",
            BlockMaterials.Cliff => "Скала",
            BlockMaterials.Bedrock => "Коренная порода",
            _ => material.ToString(),
        };

        private static int Wrap(int value, int count) => (value % count + count) % count;
    }
}
