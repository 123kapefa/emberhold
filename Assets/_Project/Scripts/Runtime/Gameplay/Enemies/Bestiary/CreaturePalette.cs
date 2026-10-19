using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>Цвета существ тумана (sRGB, как в прототипе бестиария) и оттенки уровней.</summary>
    public static class CreaturePalette
    {
        public const uint FogBody = 0x2b333f;
        public const uint FogDark = 0x1d232c;
        public const uint FogBelly = 0x46515f;
        public const uint Frost = 0xb3cde3;
        public const uint Bone = 0xcfc6b0;
        public const uint Claw = 0x8f99a6;
        public const uint Bark = 0x5a3e2c;
        public const uint Moss = 0x5d7034;
        public const uint Stone = 0x8a8e92;
        public const uint StoneDark = 0x5f6368;
        public const uint Iron = 0x5b5f63;

        /// <summary>Светящиеся трещины, дыхальца, капли: цвет и свечение.</summary>
        public const uint Crack = 0x9fd8ff;
        public const uint CrackGlow = 0x7fc3ff;

        public const uint Ice = 0xd4ecfa;
        public const uint IceGlow = 0x4a88c0;
        public const uint FrostGround = 0xdcecf6;
        public const uint FrostRing = 0xbfe6ff;
        public const uint DarkCircle = 0x02040a;
        public const uint Core = 0xe6f8ff;
        public const uint Flash = 0xdff4ff;

        /// <summary>Глаза по уровням: обычный, матёрый, древний.</summary>
        public static readonly uint[] Eyes = { 0xcff4ff, 0xa8ecff, 0xe6fbff };

        /// <summary>Рост по уровням: матёрый и древний крупнее.</summary>
        public static readonly float[] LevelScale = { 1f, 1.18f, 1.36f };

        public static Color Hex(uint hex) => new(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f);

        /// <summary>Цвет с синеватым отливом уровня: туман забирает существо.</summary>
        public static Color Tint(uint hex, int level)
        {
            var color = Hex(hex);
            return level switch
            {
                1 => Color.Lerp(color, Hex(0x8fb4d4), 0.3f),
                2 => Color.Lerp(color, Hex(0xb8d8f0), 0.45f),
                _ => color,
            };
        }

        /// <summary>Свечение тела у матёрого и древнего; у обычного его нет.</summary>
        public static Color LevelEmission(int level) => level switch
        {
            1 => Hex(0x0e2034),
            2 => Hex(0x24527c),
            _ => Color.black,
        };
    }
}
