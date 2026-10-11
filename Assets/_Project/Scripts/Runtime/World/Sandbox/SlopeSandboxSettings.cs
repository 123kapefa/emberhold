using System;
using UnityEngine;

namespace Emberhold.World.Sandbox
{
    /// <summary>
    /// Ручки песочницы склонов. Изменение в инспекторе во время Play сразу пересобирает площадки.
    /// </summary>
    [CreateAssetMenu(fileName = "SlopeSandboxSettings", menuName = "Emberhold/Slope Sandbox Settings")]
    public sealed class SlopeSandboxSettings : ScriptableObject
    {
        [Tooltip("Шаг высоты углов в шестнадцатых блока: 16 — целые блоки, 4 — четверти, 1 — самый мелкий.")]
        [Range(1, 16)]
        [SerializeField] private int heightStep = 4;
        [Tooltip("Сдвигать углы на границу слоя, чтобы склон столбца умещался в один блок (как в долине).")]
        [SerializeField] private bool snapToLayers = true;
        [Tooltip("Множитель высоты всех макетов: больше — круче склоны.")]
        [Range(0.25f, 3f)]
        [SerializeField] private float steepness = 1f;
        [Tooltip("Высота ровной земли площадок в блоках.")]
        [Range(1f, 12f)]
        [SerializeField] private float baseHeight = 4f;
        [Tooltip("Толщина земли под травой в блоках, глубже — камень.")]
        [Min(0)]
        [SerializeField] private int dirtDepth = 3;
        [Tooltip("Seed шумного рельефа.")]
        [SerializeField] private int seed = 1;

        public int HeightStep => heightStep;
        public bool SnapToLayers => snapToLayers;
        public float Steepness => steepness;
        public float BaseHeight => baseHeight;
        public int DirtDepth => dirtDepth;
        public int Seed => seed;

        /// <summary>Вызывается при изменении в инспекторе.</summary>
        public event Action Changed;

        private void OnValidate() => Changed?.Invoke();
    }
}
