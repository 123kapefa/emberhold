using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>Блок упал: откуда, куда и какой.</summary>
    public readonly struct BlockFall
    {
        public readonly Vector3Int From;
        public readonly Vector3Int To;
        public readonly Block Block;

        public BlockFall(Vector3Int from, Vector3Int to, Block block)
        {
            From = from;
            To = to;
            Block = block;
        }

        public override string ToString() => $"{Block}: {From} → {To}";
    }

    /// <summary>
    /// Простая устойчивость: блок держится, если через цепочку соседей (<see cref="BlockSupport.Connects"/>)
    /// доходит до земли (<see cref="BlockSupport.IsGround"/>). Вызывается после каждого изменения мира
    /// и проверяет только сам изменённый блок и его соседей: оторваться могли только они и то, что на них держится.
    /// Блоки без опоры падают вниз по своему столбцу до первого непустого блока и остаются целыми.
    /// Поиск идёт сначала вниз, поэтому для рельефа он почти сразу упирается в дно мира;
    /// если связная группа больше <see cref="SearchLimit"/> блоков, она считается стоящей.
    /// Только данные: меш перестроит <see cref="VoxelWorldView"/>, лежащие предметы переложит их Settle.
    /// </summary>
    public sealed class BlockStability
    {
        public const int DefaultSearchLimit = 32768;

        // Вниз — последним: со стека он снимается первым.
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.up, Vector3Int.left, Vector3Int.right, Vector3Int.back, Vector3Int.forward, Vector3Int.down,
        };

        private readonly HashSet<Vector3Int> supported = new();
        private readonly HashSet<Vector3Int> unsupported = new();
        private readonly HashSet<Vector3Int> visited = new();
        private readonly Stack<Vector3Int> stack = new();
        private readonly List<Vector3Int> falling = new();
        private readonly List<Block> fallingBlocks = new();

        public BlockStability(int searchLimit = DefaultSearchLimit)
        {
            SearchLimit = Mathf.Max(1, searchLimit);
        }

        /// <summary>Больше скольких блоков связная группа считается стоящей без дальнейшего поиска.</summary>
        public int SearchLimit { get; }

        /// <summary>
        /// Блок в клетке changed поставили, убрали или изменили: уронить всё, что от этого потеряло опору.
        /// Упавшие блоки дописываются в falls (если не null). Возвращает, сколько блоков упало.
        /// </summary>
        public int Settle(VoxelWorld world, Vector3Int changed, List<BlockFall> falls = null)
        {
            supported.Clear();
            unsupported.Clear();
            Check(world, changed);
            foreach (var direction in Directions)
                Check(world, changed + direction);
            return unsupported.Count == 0 ? 0 : Drop(world, falls);
        }

        /// <summary>Есть ли у блока опора. У воздуха и клеток вне мира — нет.</summary>
        public bool IsSupported(VoxelWorld world, Vector3Int cell)
        {
            supported.Clear();
            return !world.GetBlock(cell).IsAir && Search(world, cell);
        }

        private void Check(VoxelWorld world, Vector3Int cell)
        {
            if (world.GetBlock(cell).IsAir || supported.Contains(cell) || unsupported.Contains(cell))
                return;
            // Всё, что поиск обошёл, связано со стартовым блоком, а значит, стоит или висит вместе с ним.
            var result = Search(world, cell) ? supported : unsupported;
            result.UnionWith(visited);
        }

        /// <summary>Обход связной группы от cell в глубину, сначала вниз. true — группа стоит на земле.</summary>
        private bool Search(VoxelWorld world, Vector3Int start)
        {
            visited.Clear();
            stack.Clear();
            visited.Add(start);
            stack.Push(start);
            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                if (BlockSupport.IsGround(cell) || supported.Contains(cell) || visited.Count > SearchLimit)
                    return true;
                var block = world.GetBlock(cell);
                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if (visited.Contains(next) || !world.IsInsideBlocks(next))
                        continue;
                    if (!BlockSupport.Connects(block, direction, world.GetBlock(next)))
                        continue;
                    visited.Add(next);
                    stack.Push(next);
                }
            }
            return false;
        }

        /// <summary>
        /// Уронить висящие блоки: сначала убрать их все, затем снизу вверх опустить каждый по столбцу
        /// до первого непустого блока. Нижние падают первыми, поэтому верхние ложатся на них в том же порядке.
        /// </summary>
        private int Drop(VoxelWorld world, List<BlockFall> falls)
        {
            falling.Clear();
            falling.AddRange(unsupported);
            falling.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));

            fallingBlocks.Clear();
            foreach (var cell in falling)
            {
                fallingBlocks.Add(world.GetBlock(cell));
                world.SetBlock(cell, Block.Air);
            }

            var moved = 0;
            for (var i = 0; i < falling.Count; i++)
            {
                var from = falling[i];
                var to = from;
                while (to.y > 0 && world.GetBlock(to + Vector3Int.down).IsAir)
                    to += Vector3Int.down;
                world.SetBlock(to, fallingBlocks[i]);
                if (to == from)
                    continue;
                moved++;
                falls?.Add(new BlockFall(from, to, fallingBlocks[i]));
            }
            return moved;
        }
    }
}
