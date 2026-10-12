using System;
using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Снятая прочность блоков. Прочность блока — прочность материала (<see cref="IBlockDurability"/>),
    /// умноженная на заполнение: блок на половину разбивается вдвое быстрее полного. Удар снимает ХП;
    /// когда ХП кончились, блок исчезает целиком.
    /// Начатый блок помнит урон: если рабочий ударил дважды и ушёл, следующий продолжит с того же места.
    /// Хранятся только начатые блоки.
    /// </summary>
    public sealed class BlockDamage
    {
        private readonly Dictionary<Vector3Int, float> taken = new();
        private readonly IBlockDurability durability;

        public BlockDamage(IBlockDurability durability)
        {
            this.durability = durability;
        }

        /// <summary>Блок разрушен ударом: координата и каким он был до разрушения (для выпадения ресурсов).</summary>
        public event Action<Vector3Int, Block> Destroyed;

        /// <summary>Сколько блоков сейчас повреждено.</summary>
        public int Count => taken.Count;

        /// <summary>Номер изменения: растёт при каждом изменении урона, чтобы вид перестраивался только тогда.</summary>
        public int Version { get; private set; }

        /// <summary>Повреждённые блоки и снятые с них ХП.</summary>
        public IEnumerable<KeyValuePair<Vector3Int, float>> Entries => taken;

        /// <summary>Сколько ХП уже снято с блока.</summary>
        public float Taken(Vector3Int block) => taken.TryGetValue(block, out var value) ? value : 0f;

        /// <summary>Полная прочность блока: прочность материала × заполнение. У воздуха — 0.</summary>
        public float MaxHitPoints(Block block) =>
            block.IsAir ? 0f : durability.HitPoints(block.Material) * block.Shape.Fill;

        /// <summary>Сколько ХП у блока осталось; у воздуха — 0.</summary>
        public float Remaining(VoxelWorld world, Vector3Int block) =>
            Mathf.Max(0f, MaxHitPoints(world.GetBlock(block)) - Taken(block));

        /// <summary>Доля разрушения 0..1 — для показа трещин.</summary>
        public float Fraction(VoxelWorld world, Vector3Int block)
        {
            var max = MaxHitPoints(world.GetBlock(block));
            return max <= 0f ? 0f : Mathf.Clamp01(Taken(block) / max);
        }

        /// <summary>
        /// Ударить блок на damage ХП. Возвращает true, если блок разрушен и убран из мира.
        /// Удар по воздуху и блокам вне мира ничего не делает.
        /// </summary>
        public bool Hit(VoxelWorld world, Vector3Int block, float damage)
        {
            if (damage <= 0f || !world.IsInsideBlocks(block))
                return false;
            var current = world.GetBlock(block);
            if (current.IsAir)
            {
                Forget(block);
                return false;
            }

            var value = Taken(block) + damage;
            Version++;
            // Небольшой допуск, чтобы 4 удара по 25 при прочности 100 ломали блок и при ошибках округления.
            if (value >= MaxHitPoints(current) - 1e-3f)
            {
                taken.Remove(block);
                world.SetBlock(block, Block.Air);
                Destroyed?.Invoke(block, current);
                return true;
            }

            taken[block] = value;
            return false;
        }

        /// <summary>Забыть урон блока, например когда его убрали другим способом.</summary>
        public void Forget(Vector3Int block)
        {
            if (taken.Remove(block))
                Version++;
        }

        /// <summary>
        /// Блоки упали (<see cref="BlockStability"/>): урон едет вместе с блоком. Сначала снимаем урон со всех
        /// старых мест, потом ставим на новые — блок может упасть в клетку, которую только что освободил другой.
        /// </summary>
        public void Move(IReadOnlyList<BlockFall> falls)
        {
            List<(Vector3Int to, float value)> moved = null;
            foreach (var fall in falls)
            {
                if (taken.Remove(fall.From, out var value))
                    (moved ??= new List<(Vector3Int, float)>()).Add((fall.To, value));
            }
            if (moved == null)
                return;
            foreach (var (to, value) in moved)
                taken[to] = value;
            Version++;
        }

        /// <summary>Убрать записи о блоках, которых в мире уже нет.</summary>
        public void ForgetMissing(VoxelWorld world)
        {
            List<Vector3Int> missing = null;
            foreach (var pair in taken)
            {
                if (world.GetBlock(pair.Key).IsAir)
                    (missing ??= new List<Vector3Int>()).Add(pair.Key);
            }
            if (missing == null)
                return;
            foreach (var block in missing)
                taken.Remove(block);
            Version++;
        }

        /// <summary>Этап трещин для доли разрушения: 0 — целый, stages — почти разрушен.</summary>
        public static int Stage(float fraction, int stages) =>
            Mathf.Clamp(Mathf.FloorToInt(fraction * (stages + 1) + 1e-4f), 0, stages);
    }
}
