using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies
{
    /// <summary>
    /// Пути ползунов к огню. Для каждого огня — один разлив по столбцам мира от огня (<see cref="ColumnFlood"/>
    /// «к старту»): из любого достигнутого столбца видно, в какой соседний ползти. Поле общее для всех ползунов
    /// и пересчитывается, только когда мир изменился (<see cref="VoxelWorld.Version"/>).
    /// Подъём — не выше <see cref="CrawlerSettings.MaxStep"/>, спуск — любой, как у <see cref="CrawlerFooting"/>.
    /// Если от огня до ползуна пути нет (сплошная стена в два блока), столбец ползуна не достигнут.
    /// Координаты — мира блоков. Чистая логика без Unity-объектов, её проверяют EditMode-тесты.
    /// </summary>
    public sealed class CrawlerPaths
    {
        // Огонь виден с 20–25 м (радиус света и туман за ним), круг такого радиуса — около 2000 столбцов;
        // с запасом на обходы.
        public const int DefaultSearchLimit = 8192;

        private readonly VoxelWorld world;
        private readonly WalkSurface surface;
        private readonly int searchLimit;
        private readonly Dictionary<Vector2Int, ColumnFlood> fields = new();
        private int version;

        public CrawlerPaths(VoxelWorld world, float maxStep, int searchLimit = DefaultSearchLimit)
        {
            this.world = world;
            this.searchLimit = searchLimit;
            // Высота — как у CrawlerFooting, иначе путь и шаг ползуна расходились бы на лестницах и скатах.
            surface = new WalkSurface(world, maxStep, float.PositiveInfinity, ColumnHeight);
            version = world.Version;
        }

        public VoxelWorld World => world;

        /// <summary>
        /// Следующий столбец на пути из from к огню в столбце fire (сам fire, если ползун уже в нём).
        /// false — пути нет или from слишком далеко.
        /// </summary>
        public bool TryNext(Vector2Int fire, Vector2Int from, out Vector2Int next)
        {
            next = from;
            if (from == fire)
                return true;
            return Field(fire).TryPrevious(from, out next);
        }

        /// <summary>Есть ли путь из from к огню в столбце fire.</summary>
        public bool CanReach(Vector2Int fire, Vector2Int from) => from == fire || Field(fire).Reached(from);

        /// <summary>Где стоит ползун в столбце; в пустой столбец не зайти.</summary>
        private float ColumnHeight(Vector2Int column) =>
            CrawlerFooting.TryTopSurface(world, column.x + 0.5f, column.y + 0.5f, out var height) ? height : float.PositiveInfinity;

        private ColumnFlood Field(Vector2Int fire)
        {
            if (version != world.Version)
            {
                version = world.Version;
                fields.Clear();
            }
            if (!fields.TryGetValue(fire, out var flood))
            {
                flood = new ColumnFlood(searchLimit);
                flood.Run(surface, fire, null, toStart: true);
                fields[fire] = flood;
            }
            return flood;
        }
    }
}
