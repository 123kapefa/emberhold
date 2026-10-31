using System;
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

        /// <summary>Какой-то круг света появился, пропал или поменял радиус: границам пора перерисовать, что у них накрыто.</summary>
        public event Action LightChanged;

        public void Register(LightSource source)
        {
            if (sources.Contains(source))
                return;
            sources.Add(source);
            NotifyLightChanged();
        }

        public void Unregister(LightSource source)
        {
            if (sources.Remove(source))
                NotifyLightChanged();
        }

        /// <summary>Сообщить, что круг одного из источников изменился.</summary>
        public void NotifyLightChanged() => LightChanged?.Invoke();

        /// <summary>Точка мира (в координатах сцены) освещена хотя бы одним источником.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            // Источник может двигаться (позже — экспедиции), поэтому круги собираем при каждом запросе.
            CollectCircles(zone);
            return zone.Contains(worldPoint);
        }

        /// <summary>Свет доходит до точки мира: под огнём только в пределах шара радиуса (копать и строить можно только тут).</summary>
        public bool Reaches(Vector3 worldPoint)
        {
            CollectCircles(zone);
            return zone.Reaches(worldPoint);
        }

        /// <summary>Заполнить target кругами горящих источников на текущий момент (туман берёт их отсюда же).</summary>
        public void CollectCircles(LightZone target)
        {
            target.Clear();
            foreach (var source in sources)
            {
                if (source.IsLit)
                    target.Add(source.Circle);
            }
        }
    }
}
