using System;
using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Trees
{
    /// <summary>
    /// Дерево на карте: стоит в пустой клетке Cell над блоком земли, одно на столбец. Удары копят урон,
    /// когда урон дошёл до прочности вида, дерево срублено.
    /// </summary>
    public sealed class TreeStand
    {
        internal TreeStand(TreeSpecies species, Vector3Int cell)
        {
            Species = species;
            Cell = cell;
        }

        public TreeSpecies Species { get; }

        /// <summary>Клетка, в которой стоит основание дерева: пустая, над блоком земли.</summary>
        public Vector3Int Cell { get; }

        public Vector2Int Column => new(Cell.x, Cell.z);

        /// <summary>Сколько ХП уже снято ударами.</summary>
        public float Damage { get; internal set; }

        public float Health => Mathf.Max(0f, Species.Durability - Damage);

        /// <summary>
        /// Сдвиг ствола от центра клетки по X и Z в блоках (до ±<see cref="ForestScatter.MaxOffset"/>): деревья в роще
        /// не стоят рядами. Дерево по-прежнему занимает свою клетку.
        /// </summary>
        public Vector2 Offset { get; internal set; }

        /// <summary>Точка ствола на карте по X и Z: центр клетки со сдвигом.</summary>
        public Vector2 Position => new(Cell.x + 0.5f + Offset.x, Cell.z + 0.5f + Offset.y);

        /// <summary>Роща, в которой растёт дерево; null — одиночное дерево.</summary>
        public Grove Grove { get; internal set; }

        /// <summary>Дерево ещё стоит (не срублено и не упало).</summary>
        public bool IsStanding { get; internal set; } = true;

        /// <summary>
        /// Куда дерево упадёт (по X и Z, длина 1): от того, кто ударил последним. Нулевой — никто не бил,
        /// направление выбирает вид.
        /// </summary>
        public Vector2 FallDirection { get; internal set; }

        /// <summary>Дерево срублено (а не упало, потеряв опору): после него остался пень.</summary>
        public bool Chopped { get; internal set; }
    }

    /// <summary>
    /// Пень срубленного дерева: стоит в клетке дерева, пока под ним есть земля и клетка пуста. Только вид:
    /// на работу и строительство не влияет — поставленный в клетку блок или выкопанная земля его убирают.
    /// </summary>
    public sealed class TreeStump
    {
        internal TreeStump(TreeStand tree)
        {
            Species = tree.Species;
            Cell = tree.Cell;
            Offset = tree.Offset;
        }

        public TreeSpecies Species { get; }
        public Vector3Int Cell { get; }
        public Vector2 Offset { get; }

        /// <summary>Точка пня на карте по X и Z: там же, где стоял ствол.</summary>
        public Vector2 Position => new(Cell.x + 0.5f + Offset.x, Cell.z + 0.5f + Offset.y);
    }

    /// <summary>
    /// Деревья на карте. Срубленное дерево целиком падает кучкой поленьев у основания (в <see cref="DroppedItems"/>)
    /// и оставляет пень (<see cref="Stumps"/>). Если под деревом выкопали землю или в его клетку поставили блок,
    /// дерево падает так же, но без пня (<see cref="Settle"/>).
    /// Деревья не отрастают. Чистая логика без Unity-объектов: показывает её ForestView, рубят рабочие.
    /// Координаты — мира блоков.
    /// </summary>
    public sealed class Forest
    {
        private readonly List<TreeStand> trees = new();
        private readonly List<TreeStump> stumps = new();
        private readonly List<Grove> groves = new();
        private readonly Dictionary<Vector2Int, TreeStand> byColumn = new();
        private readonly DroppedItems items;

        public Forest(DroppedItems items)
        {
            this.items = items;
        }

        public IReadOnlyList<TreeStand> Trees => trees;

        /// <summary>Пни срубленных деревьев.</summary>
        public IReadOnlyList<TreeStump> Stumps => stumps;

        /// <summary>Рощи леса, в том числе уже вырубленные.</summary>
        public IReadOnlyList<Grove> Groves => groves;

        /// <summary>Номер изменения: растёт, когда дерево посадили или оно упало, когда пень появился или исчез.</summary>
        public int Version { get; private set; }

        /// <summary>Дерево упало (срублено или потеряло опору); поленья уже лежат на земле.</summary>
        public event Action<TreeStand> Felled;

        /// <summary>Дерево ударили, но оно устояло (для вида: крона дрожит, летят щепки).</summary>
        public event Action<TreeStand> Struck;

        public TreeStand At(Vector2Int column) => byColumn.TryGetValue(column, out var tree) ? tree : null;

        /// <summary>Роща, в область которой попадает столбец; null — вне рощ.</summary>
        public Grove GroveAt(Vector2Int column)
        {
            foreach (var grove in groves)
            {
                if (grove.Contains(column))
                    return grove;
            }
            return null;
        }

        /// <summary>Добавить рощу (её деревья сажаются потом через Plant с этой рощей).</summary>
        internal void AddGrove(Grove grove) => groves.Add(grove);

        /// <summary>Убрать рощу: её деревья остаются одиночными.</summary>
        internal void RemoveGrove(Grove grove)
        {
            if (!groves.Remove(grove))
                return;
            foreach (var tree in grove.trees)
                tree.Grove = null;
            grove.trees.Clear();
        }

        /// <summary>Посадить дерево в клетку cell (в рощу grove, если она задана). Null, если в столбце уже есть дерево.</summary>
        public TreeStand Plant(Vector3Int cell, TreeSpecies species, Grove grove = null)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            var column = new Vector2Int(cell.x, cell.z);
            if (byColumn.ContainsKey(column))
                return null;
            var tree = new TreeStand(species, cell) { Grove = grove };
            grove?.trees.Add(tree);
            trees.Add(tree);
            byColumn[column] = tree;
            Version++;
            return tree;
        }

        /// <summary>
        /// Ударить дерево на damage ХП. from — откуда бьют (X и Z): срубленное дерево падает от него.
        /// true — дерево срублено, поленья упали, остался пень.
        /// </summary>
        public bool Hit(VoxelWorld world, TreeStand tree, float damage, Vector2? from = null)
        {
            if (tree == null || !tree.IsStanding || damage <= 0f)
                return false;
            if (from is { } source)
            {
                var away = tree.Position - source;
                if (away.sqrMagnitude > 1e-6f)
                    tree.FallDirection = away.normalized;
            }
            tree.Damage += damage;
            if (tree.Health > 0f)
            {
                Struck?.Invoke(tree);
                return false;
            }
            tree.Chopped = true;
            Fell(world, tree);
            return true;
        }

        /// <summary>Уронить дерево: оно исчезает, у основания ложится кучка поленьев.</summary>
        public void Fell(VoxelWorld world, TreeStand tree)
        {
            if (tree == null || !tree.IsStanding)
                return;
            tree.IsStanding = false;
            trees.Remove(tree);
            byColumn.Remove(tree.Column);
            tree.Grove?.trees.Remove(tree);
            Version++;
            if (tree.Chopped && HasGround(world, tree.Cell))
                stumps.Add(new TreeStump(tree));
            if (items != null && world.IsInsideBlocks(tree.Cell))
                items.Drop(world, tree.Cell, new ItemDrop(tree.Species.ItemId, tree.Species.WoodYield));
            Felled?.Invoke(tree);
        }

        /// <summary>
        /// Уронить деревья, под которыми выкопали землю или в чью клетку поставили блок, и убрать такие же пни.
        /// Возвращает, сколько деревьев упало.
        /// </summary>
        public int Settle(VoxelWorld world)
        {
            if (stumps.RemoveAll(stump => !HasGround(world, stump.Cell)) > 0)
                Version++;
            var fallen = 0;
            for (var i = trees.Count - 1; i >= 0; i--)
            {
                var tree = trees[i];
                if (HasGround(world, tree.Cell))
                    continue;
                Fell(world, tree);
                fallen++;
            }
            return fallen;
        }

        /// <summary>Высота основания дерева: земля в точке ствола (<see cref="GroundHeight"/>), а не в центре клетки.</summary>
        public static float BaseHeight(VoxelWorld world, TreeStand tree) => GroundHeight(world, tree.Cell, tree.Position);

        /// <summary>Высота основания пня, как у дерева.</summary>
        public static float BaseHeight(VoxelWorld world, TreeStump stump) => GroundHeight(world, stump.Cell, stump.Position);

        /// <summary>
        /// Высота земли под точкой point (X, Z) в клетке cell над землёй: на нетронутой земле — гладкая поверхность
        /// в этой точке (по углам столбца), иначе верх блока под клеткой. Так стоят вещи, сдвинутые от центра клетки.
        /// </summary>
        public static float GroundHeight(VoxelWorld world, Vector3Int cell, Vector2 point)
        {
            var surface = world.Surface;
            if (surface != null && surface.IsNatural(cell.x, cell.z) && cell.y - 1 <= surface.TopY(cell.x, cell.z))
            {
                var u = Mathf.Clamp01(point.x - cell.x);
                var v = Mathf.Clamp01(point.y - cell.z);
                var h00 = surface.CornerIn(cell.x, cell.z, cell.x, cell.z);
                var h10 = surface.CornerIn(cell.x, cell.z, cell.x + 1, cell.z);
                var h01 = surface.CornerIn(cell.x, cell.z, cell.x, cell.z + 1);
                var h11 = surface.CornerIn(cell.x, cell.z, cell.x + 1, cell.z + 1);
                return Mathf.Lerp(Mathf.Lerp(h00, h10, u), Mathf.Lerp(h01, h11, u), v) / BlockShape.MaxHeight;
            }
            return DroppedItems.RestHeight(world, cell);
        }

        /// <summary>
        /// Ближайшее дерево, в которое попадает луч (координаты мира блоков), в пределах maxDistance.
        /// Дерево для луча — коробка шириной с крону от основания до макушки.
        /// </summary>
        public bool Raycast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, out TreeStand hit, out float distance)
        {
            hit = null;
            distance = maxDistance;
            foreach (var tree in trees)
            {
                var half = Mathf.Max(tree.Species.CrownWidth, tree.Species.TrunkWidth) * 0.5f;
                var bottom = BaseHeight(world, tree);
                var position = tree.Position;
                var min = new Vector3(position.x - half, bottom, position.y - half);
                var max = new Vector3(position.x + half, bottom + tree.Species.Height, position.y + half);
                if (!RayBox(origin, direction, min, max, out var t) || t >= distance)
                    continue;
                hit = tree;
                distance = t;
            }
            return hit != null;
        }

        /// <summary>Клетка дерева пуста, а под ней блок.</summary>
        public static bool HasGround(VoxelWorld world, Vector3Int cell)
        {
            var below = cell + Vector3Int.down;
            return world.IsInsideBlocks(cell) && world.IsInsideBlocks(below)
                && world.GetBlock(cell).IsAir && !world.GetBlock(below).IsAir;
        }

        private static bool RayBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float t)
        {
            var near = 0f;
            var far = float.PositiveInfinity;
            t = 0f;
            for (var axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < 1e-6f)
                {
                    if (origin[axis] < min[axis] || origin[axis] > max[axis])
                        return false;
                    continue;
                }
                var t1 = (min[axis] - origin[axis]) / direction[axis];
                var t2 = (max[axis] - origin[axis]) / direction[axis];
                if (t1 > t2)
                    (t1, t2) = (t2, t1);
                near = Mathf.Max(near, t1);
                far = Mathf.Min(far, t2);
                if (near > far)
                    return false;
            }
            t = near;
            return true;
        }
    }
}
