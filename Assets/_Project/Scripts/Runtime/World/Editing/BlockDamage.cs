using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Накопленное разрушение блоков: доля 0..1 на блок. Когда доля доходит до 1, блок исчезает целиком,
    /// сколько бы он ни был заполнен. Игрок добавляет долю кликами, рабочие потом будут добавлять её
    /// по времени (dt / секунд на блок). Хранятся только начатые блоки.
    /// </summary>
    public sealed class BlockDamage
    {
        private readonly Dictionary<Vector3Int, float> progress = new();

        /// <summary>Сколько блоков сейчас повреждено.</summary>
        public int Count => progress.Count;

        /// <summary>Номер изменения: растёт при каждом изменении урона, чтобы вид перестраивался только тогда.</summary>
        public int Version { get; private set; }

        public IEnumerable<KeyValuePair<Vector3Int, float>> Entries => progress;

        public float Get(Vector3Int block) => progress.TryGetValue(block, out var value) ? value : 0f;

        /// <summary>
        /// Добавить урон блоку. Возвращает true, если блок разрушен и убран из мира.
        /// Урон воздуху и блокам вне мира ничего не делает.
        /// </summary>
        public bool Add(VoxelWorld world, Vector3Int block, float amount)
        {
            if (amount <= 0f || !world.IsInsideBlocks(block) || world.GetBlock(block).IsAir)
            {
                Forget(block);
                return false;
            }

            var value = Get(block) + amount;
            Version++;
            // Небольшой допуск: четыре удара по 0.25 во float должны давать ровно разрушение.
            if (value >= 1f - 1e-4f)
            {
                progress.Remove(block);
                world.SetBlock(block, Block.Air);
                return true;
            }

            progress[block] = value;
            return false;
        }

        /// <summary>Забыть урон блока, например когда его убрали другим способом.</summary>
        public void Forget(Vector3Int block)
        {
            if (progress.Remove(block))
                Version++;
        }

        /// <summary>Убрать записи о блоках, которых в мире уже нет.</summary>
        public void ForgetMissing(VoxelWorld world)
        {
            List<Vector3Int> missing = null;
            foreach (var pair in progress)
            {
                if (world.GetBlock(pair.Key).IsAir)
                    (missing ??= new List<Vector3Int>()).Add(pair.Key);
            }
            if (missing == null)
                return;
            foreach (var block in missing)
                progress.Remove(block);
            Version++;
        }

        /// <summary>Этап разрушения для показа: 0 — целый, stages — почти разрушен.</summary>
        public static int Stage(float value, int stages) =>
            Mathf.Clamp(Mathf.FloorToInt(value * (stages + 1) + 1e-4f), 0, stages);
    }
}
