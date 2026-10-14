using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Облик одного рабочего: цвета кожи, волос, рубахи и капюшона с накидкой, есть ли борода. Капюшон — цвет роли
    /// (одинаковый у всех с этой ролью), остальное у каждого своё. Сетку по облику строит <see cref="WorkerMeshBuilder"/>.
    /// </summary>
    public readonly struct WorkerLook
    {
        public WorkerLook(Color skin, Color hair, Color tunic, Color hood, bool beard)
        {
            Skin = skin;
            Hair = hair;
            Tunic = tunic;
            Hood = hood;
            Beard = beard;
        }

        public Color Skin { get; }
        public Color Hair { get; }
        public Color Tunic { get; }
        public Color Hood { get; }
        public bool Beard { get; }

        /// <summary>
        /// Облик по номеру рабочего: кожа, волосы и рубаха из палитр, борода с вероятностью beardChance.
        /// Один номер всегда даёт один облик. Пустая палитра даёт белый цвет.
        /// </summary>
        public static WorkerLook Pick(int seed, IReadOnlyList<Color> skins, IReadOnlyList<Color> hair, IReadOnlyList<Color> tunics,
            Color hood, float beardChance)
        {
            var random = new System.Random(unchecked(seed * 7919 + 13));
            return new WorkerLook(From(skins, random), From(hair, random), From(tunics, random), hood, random.NextDouble() < beardChance);
        }

        private static Color From(IReadOnlyList<Color> palette, System.Random random)
        {
            var index = random.Next(int.MaxValue);
            return palette == null || palette.Count == 0 ? Color.white : palette[index % palette.Count];
        }
    }
}
