using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Ограниченный мир из чанков. Координаты блоков — целые, от (0,0,0) до SizeInBlocks - 1.
    /// Запоминает, какие чанки изменились, чтобы перестраивать только их меши.
    /// Ничего не знает о генерации и о сцене: карту можно будет так же загрузить из файла.
    /// </summary>
    public sealed class VoxelWorld
    {
        private readonly ChunkData[] chunks;
        private readonly HashSet<Vector3Int> dirtyChunks = new();

        public Vector3Int SizeInChunks { get; }
        public Vector3Int SizeInBlocks => SizeInChunks * ChunkData.Size;

        /// <summary>Растёт при каждой смене блока через SetBlock: по нему кэши (например, пути) узнают, что мир изменился.</summary>
        public int Version { get; private set; }

        public VoxelWorld(Vector3Int sizeInChunks)
        {
            if (sizeInChunks.x <= 0 || sizeInChunks.y <= 0 || sizeInChunks.z <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeInChunks), "Размер мира в чанках должен быть положительным");
            SizeInChunks = sizeInChunks;
            chunks = new ChunkData[sizeInChunks.x * sizeInChunks.y * sizeInChunks.z];
            for (var i = 0; i < chunks.Length; i++)
                chunks[i] = new ChunkData();
        }

        public bool IsInsideBlocks(Vector3Int block)
        {
            var size = SizeInBlocks;
            return (uint)block.x < size.x && (uint)block.y < size.y && (uint)block.z < size.z;
        }

        public bool IsInsideChunks(Vector3Int chunk) =>
            (uint)chunk.x < SizeInChunks.x && (uint)chunk.y < SizeInChunks.y && (uint)chunk.z < SizeInChunks.z;

        public ChunkData GetChunk(Vector3Int chunk) => chunks[ChunkIndex(chunk)];

        /// <summary>Блок по мировой координате; за пределами мира — воздух.</summary>
        public Block GetBlock(Vector3Int block)
        {
            if (!IsInsideBlocks(block))
                return Block.Air;
            var (chunk, local) = Split(block);
            return chunks[ChunkIndex(chunk)].Get(local.x, local.y, local.z);
        }

        /// <summary>
        /// Поставить блок. Помечает его чанк, а если блок на границе — и соседний чанк:
        /// у соседа могла открыться или закрыться грань.
        /// </summary>
        public void SetBlock(Vector3Int block, Block value)
        {
            if (!IsInsideBlocks(block))
                throw new ArgumentOutOfRangeException(nameof(block), $"Блок {block} вне мира {SizeInBlocks}");

            var (chunk, local) = Split(block);
            var data = chunks[ChunkIndex(chunk)];
            if (data.Get(local.x, local.y, local.z) == value)
                return;
            data.Set(local.x, local.y, local.z, value);
            Version++;

            MarkDirty(chunk);
            const int last = ChunkData.Size - 1;
            if (local.x == 0) MarkDirty(chunk + Vector3Int.left);
            if (local.x == last) MarkDirty(chunk + Vector3Int.right);
            if (local.y == 0) MarkDirty(chunk + Vector3Int.down);
            if (local.y == last) MarkDirty(chunk + Vector3Int.up);
            if (local.z == 0) MarkDirty(chunk + Vector3Int.back);
            if (local.z == last) MarkDirty(chunk + Vector3Int.forward);
        }

        public void MarkDirty(Vector3Int chunk)
        {
            if (IsInsideChunks(chunk))
                dirtyChunks.Add(chunk);
        }

        public void MarkAllDirty()
        {
            for (var y = 0; y < SizeInChunks.y; y++)
            for (var z = 0; z < SizeInChunks.z; z++)
            for (var x = 0; x < SizeInChunks.x; x++)
                dirtyChunks.Add(new Vector3Int(x, y, z));
        }

        public bool IsDirty(Vector3Int chunk) => dirtyChunks.Contains(chunk);

        public int DirtyCount => dirtyChunks.Count;

        /// <summary>Переложить изменённые чанки в список и забыть о них.</summary>
        public void TakeDirtyChunks(List<Vector3Int> result)
        {
            result.AddRange(dirtyChunks);
            dirtyChunks.Clear();
        }

        private int ChunkIndex(Vector3Int chunk) =>
            chunk.x + chunk.z * SizeInChunks.x + chunk.y * SizeInChunks.x * SizeInChunks.z;

        private static (Vector3Int chunk, Vector3Int local) Split(Vector3Int block)
        {
            // Внутри мира координаты неотрицательны, поэтому хватает деления.
            var chunk = new Vector3Int(block.x / ChunkData.Size, block.y / ChunkData.Size, block.z / ChunkData.Size);
            return (chunk, block - chunk * ChunkData.Size);
        }
    }
}
