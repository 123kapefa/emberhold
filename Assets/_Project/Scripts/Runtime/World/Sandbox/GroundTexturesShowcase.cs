using Emberhold.World.Generation;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Макет для просмотра текстур земли: мир 64 × 64 с гладкой поверхностью, где рядом видно всё.
    /// Спереди (малые Z) — ровные площадки травы, земли (блоки) и камня; посередине — пологий склон
    /// и крутой склон с переходом трава → камень; сзади — обрыв, яма со стенками блоков и поставленные блоки.
    /// Каменность углов — по крутизне, как у карты кампании, площадка камня каменная целиком.
    /// </summary>
    public static class GroundTexturesShowcase
    {
        public static readonly Vector3Int SizeInChunks = new(4, 3, 4);

        /// <summary>Высота ровной земли в блоках.</summary>
        public const int BaseHeight = 8;

        /// <summary>С какой крутизны (блоков подъёма на блок) земля становится каменной — как у карты кампании.</summary>
        public const float SteepRock = 1.25f;

        public readonly struct Area
        {
            public readonly string Name;
            /// <summary>Где площадка лежит в мире, в блоках по X и Z.</summary>
            public readonly RectInt Rect;

            public Area(string name, RectInt rect)
            {
                Name = name;
                Rect = rect;
            }
        }

        public static readonly Area GrassPad = new("Трава", new RectInt(0, 0, 21, 20));
        public static readonly Area DirtPad = new("Земля (блоки)", new RectInt(23, 2, 17, 16));
        public static readonly Area RockPad = new("Камень", new RectInt(43, 0, 21, 20));
        public static readonly Area GentleSlope = new("Пологий склон 1:4", new RectInt(4, 24, 24, 16));
        public static readonly Area SteepSlope = new("Крутой склон: трава → камень", new RectInt(32, 24, 14, 16));
        public static readonly Area Cliff = new("Обрыв", new RectInt(0, 46, 26, 18));
        public static readonly Area Pit = new("Яма", new RectInt(36, 50, 12, 9));
        /// <summary>Вскопанный участок посреди травы: по краю скосы вниз на полблока, внутри ровная земля.</summary>
        public static readonly Area DugPatch = new("Вскопанный участок 6×6", new RectInt(6, 13, 6, 6));
        /// <summary>Как на примере: 3 × 3 блока земли вровень с травой, середина чуть ниже (скосы к ней без ступенек).</summary>
        public static readonly Area SmallDugPatch = new("Вскопанный участок 3×3", new RectInt(15, 14, 3, 3));

        public static readonly Area[] Areas =
            { GrassPad, DirtPad, RockPad, GentleSlope, SteepSlope, Cliff, Pit, DugPatch, SmallDugPatch };

        /// <summary>Дно ямы: верхний блок пола.</summary>
        public const int PitFloor = 3;

        /// <summary>Где поставить костёр, в блоках мира: между ямой, обрывом и крутым склоном.</summary>
        public static readonly Vector2Int Campfire = new(31, 46);

        /// <summary>Поставленные на траву блоки: столбик каждого материала и стопки.</summary>
        public static readonly (Vector3Int Block, byte Material)[] PlacedBlocks =
        {
            (new Vector3Int(4, BaseHeight, 10), BlockMaterials.Grass),
            (new Vector3Int(6, BaseHeight, 10), BlockMaterials.Dirt),
            (new Vector3Int(8, BaseHeight, 10), BlockMaterials.Stone),
            (new Vector3Int(11, BaseHeight, 10), BlockMaterials.Stone),
            (new Vector3Int(11, BaseHeight + 1, 10), BlockMaterials.Stone),
            (new Vector3Int(12, BaseHeight, 10), BlockMaterials.Dirt),
            (new Vector3Int(13, BaseHeight, 10), BlockMaterials.Dirt),
            (new Vector3Int(13, BaseHeight + 1, 10), BlockMaterials.Grass),
            (new Vector3Int(15, BaseHeight, 9), BlockMaterials.Grass),
            (new Vector3Int(16, BaseHeight, 9), BlockMaterials.Grass),
            (new Vector3Int(15, BaseHeight, 10), BlockMaterials.Grass),
            (new Vector3Int(16, BaseHeight, 10), BlockMaterials.Grass),
        };

        public static VoxelWorld Build()
        {
            var world = new VoxelWorld(SizeInChunks);
            Fill(world);
            return world;
        }

        /// <summary>Заполнить мир макетом: гладкая поверхность, затем площадка земли, яма и поставленные блоки.</summary>
        public static void Fill(VoxelWorld world)
        {
            var size = world.SizeInBlocks;
            var corners = new int[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
                corners[x, z] = CornerTerrain.Quantize(Height(x, z), 1, size.y);

            var rockWeights = new byte[size.x + 1, size.z + 1];
            for (var z = 0; z <= size.z; z++)
            for (var x = 0; x <= size.x; x++)
            {
                var gx = (corners[Mathf.Min(x + 1, size.x), z] - corners[Mathf.Max(x - 1, 0), z]) * 0.5f / BlockShape.MaxHeight;
                var gz = (corners[x, Mathf.Min(z + 1, size.z)] - corners[x, Mathf.Max(z - 1, 0)]) * 0.5f / BlockShape.MaxHeight;
                var slope = Mathf.Sqrt(gx * gx + gz * gz) + (Mathf.PerlinNoise(x * 0.1f + 7.3f, z * 0.1f + 2.1f) - 0.5f) * 0.6f;
                var weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SteepRock - 0.3f, SteepRock + 0.3f, slope));
                if (Contains(RockPad.Rect, x, z))
                    weight = 1f;
                rockWeights[x, z] = (byte)Mathf.RoundToInt(weight * 255f);
            }

            var grass = TerrainMaterials.Default;
            var rock = new TerrainMaterials
            {
                Surface = BlockMaterials.Stone,
                Subsurface = BlockMaterials.Stone,
                SubsurfaceDepth = 1,
                Deep = BlockMaterials.Stone,
                Bed = BlockMaterials.Stone,
            };
            var materials = new TerrainMaterials[size.x, size.z];
            var bedLevels = new int[size.x, size.z];
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var weight = rockWeights[x, z] + rockWeights[x + 1, z] + rockWeights[x, z + 1] + rockWeights[x + 1, z + 1];
                materials[x, z] = weight > 2 * 255 ? rock : grass;
            }
            CornerTerrain.FillSmooth(world, corners, materials, bedLevels).SetRock(rockWeights);

            // Площадка земли — блоками: верхний блок сменён на землю, столбец рисуется гранями блоков.
            for (var z = DirtPad.Rect.yMin; z < DirtPad.Rect.yMax; z++)
            for (var x = DirtPad.Rect.xMin; x < DirtPad.Rect.xMax; x++)
                world.SetBlock(new Vector3Int(x, BaseHeight - 1, z), Block.Full(BlockMaterials.Dirt));

            // Яма: выкопаны блоки до пола, в стенках видны трава, земля и камень по слоям.
            for (var z = Pit.Rect.yMin; z < Pit.Rect.yMax; z++)
            for (var x = Pit.Rect.xMin; x < Pit.Rect.xMax; x++)
            for (var y = PitFloor + 1; y < BaseHeight; y++)
                world.SetBlock(new Vector3Int(x, y, z), Block.Air);

            Dig(world, DugPatch.Rect, BlockShape.MaxHeight / 2);
            Dig(world, SmallDugPatch.Rect, BlockShape.MaxHeight * 3 / 4);

            foreach (var (block, material) in PlacedBlocks)
                world.SetBlock(block, Block.Full(material));
        }

        /// <summary>
        /// Вскопанный участок: верхний блок столбцов — земля, углы на краю участка на уровне травы,
        /// внутренние углы — на высоте inner (шестнадцатые): у края скосы, внутри ровно.
        /// </summary>
        private static void Dig(VoxelWorld world, RectInt rect, int inner)
        {
            int Corner(int cx, int cz) =>
                cx == rect.xMin || cx == rect.xMax || cz == rect.yMin || cz == rect.yMax ? BlockShape.MaxHeight : inner;
            for (var z = rect.yMin; z < rect.yMax; z++)
            for (var x = rect.xMin; x < rect.xMax; x++)
                world.SetBlock(new Vector3Int(x, BaseHeight - 1, z), new Block(BlockMaterials.Dirt,
                    new BlockShape(Corner(x, z), Corner(x + 1, z), Corner(x, z + 1), Corner(x + 1, z + 1))));
        }

        /// <summary>Высота угла сетки (x, z) в блоках.</summary>
        public static float Height(int x, int z)
        {
            float h = BaseHeight;
            // Середина: вдоль X пологий подъём 1:4 на 6 блоков, полка, затем склон всё круче и плато.
            // По краям полосы рельеф плавно сходит к ровной земле.
            if (z > 20 && z < 44)
            {
                var rise = x < 4 ? 0f
                    : x < 28 ? 0.25f * (x - 4)
                    : x < 32 ? 6f
                    : x < 40 ? 6f + 0.75f * (x - 32)
                    : x < 46 ? 12f + 2f * (x - 40)
                    : 24f;
                h += rise * Mathf.SmoothStep(0f, 1f, Mathf.Min(Mathf.InverseLerp(20f, 24f, z), Mathf.InverseLerp(44f, 40f, z)));
            }
            // Сзади слева — уступ на 8 блоков выше с отвесным краем.
            if (x <= Cliff.Rect.xMax && z >= Cliff.Rect.yMin + 4)
                h += 8f;
            return h;
        }

        private static bool Contains(RectInt rect, int x, int z) =>
            x >= rect.xMin && x <= rect.xMax && z >= rect.yMin && z <= rect.yMax;
    }
}
