using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Роща: плотная группа деревьев неровной формы вокруг центра. Её сажает <see cref="ForestScatter"/>, хранит
    /// <see cref="Forest"/>. Область рощи готова для приказа «рубить рощу»: деревья в ней знают свою рощу
    /// (<see cref="TreeStand.Grove"/>), срубленные из рощи уходят. Координаты — мира блоков, по X и Z.
    /// </summary>
    public sealed class Grove
    {
        // Край рощи волнистый: радиус по направлению меняется на ±(Wave2 + Wave3) от среднего.
        private const float Wave2 = 0.22f;
        private const float Wave3 = 0.12f;

        internal readonly List<TreeStand> trees = new();
        private readonly float phase2;
        private readonly float phase3;

        internal Grove(Vector2 center, float radius, TreeSpecies mainSpecies, float phase2, float phase3)
        {
            Center = center;
            Radius = radius;
            MainSpecies = mainSpecies;
            this.phase2 = phase2;
            this.phase3 = phase3;
        }

        /// <summary>Центр рощи (точка на карте, не клетка).</summary>
        public Vector2 Center { get; }

        /// <summary>Средний радиус в блоках; край волнистый, от 0,66 до 1,34 этого.</summary>
        public float Radius { get; }

        /// <summary>Главный вид: его в роще больше всего. Null — роща без главного вида.</summary>
        public TreeSpecies MainSpecies { get; }

        /// <summary>Стоящие деревья рощи.</summary>
        public IReadOnlyList<TreeStand> Trees => trees;

        /// <summary>Роща вырублена: деревьев не осталось.</summary>
        public bool IsCleared => trees.Count == 0;

        /// <summary>Наибольший радиус края: роща целиком внутри круга такого радиуса вокруг центра.</summary>
        public float MaxRadius => Radius * (1f + Wave2 + Wave3);

        /// <summary>Радиус края в направлении angle (радианы, от оси X к оси Z).</summary>
        public float RadiusAt(float angle) =>
            Radius * (1f + Wave2 * Mathf.Sin(2f * angle + phase2) + Wave3 * Mathf.Sin(3f * angle + phase3));

        /// <summary>Насколько точка глубоко в роще: 0 — центр, 1 — край, больше 1 — снаружи.</summary>
        public float Depth(Vector2 point)
        {
            var offset = point - Center;
            var distance = offset.magnitude;
            if (distance < 1e-4f)
                return 0f;
            return distance / RadiusAt(Mathf.Atan2(offset.y, offset.x));
        }

        /// <summary>Столбец (по центру клетки) внутри рощи.</summary>
        public bool Contains(Vector2Int column) => Depth(new Vector2(column.x + 0.5f, column.y + 0.5f)) <= 1f;
    }
}
