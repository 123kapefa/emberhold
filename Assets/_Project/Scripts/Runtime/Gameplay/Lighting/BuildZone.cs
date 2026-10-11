using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Lighting
{
    /// <summary>
    /// Зона застройки: где можно строить и копать. Считается из списка активных LightSource,
    /// одна система вместо проверок в каждом месте. Источники регистрируются сами в OnEnable.
    /// </summary>
    public sealed class BuildZone : MonoBehaviour
    {
        private readonly List<LightSource> sources = new();
        private readonly LightZone zone = new();

        public IReadOnlyList<LightSource> Sources => sources;

        public void Register(LightSource source)
        {
            if (!sources.Contains(source))
                sources.Add(source);
        }

        public void Unregister(LightSource source) => sources.Remove(source);

        /// <summary>Точка мира (в координатах сцены) освещена хотя бы одним источником.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            // Источник может двигаться (позже — экспедиции), поэтому круги собираем при каждом запросе.
            zone.Clear();
            foreach (var source in sources)
                zone.Add(source.Circle);
            return zone.Contains(worldPoint);
        }
    }
}
