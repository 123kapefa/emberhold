using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Стартовый огонь для рассадки леса (<see cref="ForestScatter"/>): роща стоит центром на краю его круга света,
    /// чтобы первые дрова росли рядом. Координаты — мира блоков, по X и Z.
    /// </summary>
    public readonly struct ForestStart
    {
        public ForestStart(Vector2 center, float lightRadius)
        {
            Center = center;
            LightRadius = lightRadius;
        }

        /// <summary>Где огонь, по X и Z.</summary>
        public Vector2 Center { get; }

        /// <summary>Радиус полного круга света в блоках.</summary>
        public float LightRadius { get; }
    }
}
