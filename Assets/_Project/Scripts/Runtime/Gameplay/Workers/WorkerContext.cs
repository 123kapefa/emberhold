using System;
using System.Collections.Generic;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Всё, с чем работают рабочие одного отряда: мир, задачи, ресурсы на земле, урон блоков, устойчивость
    /// и где светло. Без Unity-объектов, чтобы EditMode-тесты гоняли рабочих на маленьком мире.
    /// </summary>
    public sealed class WorkerContext
    {
        private readonly List<BlockFall> falls = new();
        private readonly Action<Vector3Int> worldChanged;

        public WorkerContext(VoxelWorld world, WorkOrders orders, DroppedItems items, BlockDamage damage,
            BlockStability stability, WorkerSettings settings, Func<Vector2Int, bool> isLit, Func<bool> hasAnyLight = null,
            Action<Vector3Int> worldChanged = null)
        {
            this.worldChanged = worldChanged;
            World = world;
            Orders = orders;
            Items = items;
            Damage = damage;
            Stability = stability;
            Settings = settings;
            IsLit = isLit ?? (_ => true);
            HasAnyLight = hasAnyLight ?? (() => true);
            Surface = new WalkSurface(world, settings.MaxStep);
            Flood = new ColumnFlood(settings.SearchLimit);
        }

        public VoxelWorld World { get; }
        public WorkOrders Orders { get; }
        public DroppedItems Items { get; }
        public BlockDamage Damage { get; }
        public BlockStability Stability { get; }
        public WorkerSettings Settings { get; }

        /// <summary>Столбец освещён: рабочие ходят и работают только там.</summary>
        public Func<Vector2Int, bool> IsLit { get; }

        /// <summary>Горит хотя бы один огонь: если нет, из темноты идти некуда.</summary>
        public Func<bool> HasAnyLight { get; }

        public WalkSurface Surface { get; }

        /// <summary>Один разлив на всех: рабочие обновляются по очереди и сразу забирают результат.</summary>
        public ColumnFlood Flood { get; }

        /// <summary>
        /// Мир изменился в клетке: блоки без опоры падают, их урон едет вместе с ними. В сцене это делает
        /// редактор (worldChanged), чтобы падение было видно так же, как после правки игрока.
        /// </summary>
        public void Settle(Vector3Int changed)
        {
            if (worldChanged != null)
            {
                worldChanged(changed);
                return;
            }
            falls.Clear();
            if (Stability.Settle(World, changed, falls) > 0)
                Damage.Move(falls);
        }

        /// <summary>
        /// Можно ли из столбца stand дотянуться до клетки cell: столбец соседний (или тот же, если sameColumn),
        /// а клетка не дальше <see cref="WorkerSettings.ReachDown"/> блоков ниже ног и
        /// <see cref="WorkerSettings.ReachUp"/> выше.
        /// </summary>
        public bool CanReach(Vector2Int stand, Vector3Int cell, bool sameColumn)
        {
            var dx = Mathf.Abs(stand.x - cell.x);
            var dz = Mathf.Abs(stand.y - cell.z);
            if (dx > 1 || dz > 1 || dx == 0 && dz == 0 && !sameColumn)
                return false;
            var feet = Surface.Height(stand);
            return cell.y + 1 > feet - Settings.ReachDown + 1e-3f && cell.y < feet + Settings.ReachUp;
        }

        /// <summary>
        /// Ближайший по последнему разливу освещённый столбец, откуда достать до клетки. false — не достать.
        /// </summary>
        public bool TryStandSpot(Vector3Int cell, bool sameColumn, out Vector2Int stand, out float distance)
        {
            stand = default;
            distance = float.PositiveInfinity;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var column = new Vector2Int(cell.x + dx, cell.z + dz);
                var d = Flood.Distance(column);
                if (d >= distance || !CanReach(column, cell, sameColumn))
                    continue;
                distance = d;
                stand = column;
            }
            return !float.IsPositiveInfinity(distance);
        }

        /// <summary>Ближайшая по последнему разливу освещённая кучка ресурса itemId, до которой можно дотянуться.</summary>
        public DroppedItem NearestPile(string itemId, out Vector2Int stand, out float distance)
        {
            DroppedItem best = null;
            stand = default;
            distance = float.PositiveInfinity;
            foreach (var pile in Items.Items)
            {
                if (pile.Drop.ItemId != itemId || !IsLit(new Vector2Int(pile.Cell.x, pile.Cell.z)))
                    continue;
                if (!TryStandSpot(pile.Cell, true, out var spot, out var d) || d >= distance)
                    continue;
                best = pile;
                stand = spot;
                distance = d;
            }
            return best;
        }
    }
}
