using System;
using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Vegetation
{
    /// <summary>Один пучок мелочи: стоит в клетке над землёй, сдвинут от её центра, повёрнут и чуть разного размера.</summary>
    public sealed class GroundCoverPatch
    {
        public GroundCoverPatch(Vector3Int cell, GroundCoverKind kind, int variant, Vector2 offset, float rotation, float scale)
        {
            Cell = cell;
            Kind = kind;
            Variant = variant;
            Offset = offset;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>Клетка над землёй, в которой стоит пучок.</summary>
        public Vector3Int Cell { get; }
        public GroundCoverKind Kind { get; }
        /// <summary>Номер формы вида, 0..Kind.Variants−1.</summary>
        public int Variant { get; }
        /// <summary>Сдвиг от центра клетки по X и Z, в блоках.</summary>
        public Vector2 Offset { get; }
        /// <summary>Поворот вокруг оси Y, в градусах.</summary>
        public float Rotation { get; }
        public float Scale { get; }

        /// <summary>Точка пучка на карте (X, Z): центр клетки плюс сдвиг.</summary>
        public Vector2 Position => new Vector2(Cell.x + 0.5f, Cell.z + 0.5f) + Offset;
    }

    /// <summary>
    /// Мелочь на земле мира: трава, цветы, папоротник, грибы и прочее — только для вида. По пучку на столбец.
    /// Пучок пропадает, если под ним выкопали землю или в его клетку поставили блок (<see cref="Watch"/>).
    /// Чистая логика без Unity-объектов: показывает её GroundCoverView. Координаты — мира блоков.
    /// </summary>
    public sealed class GroundCover
    {
        private readonly Dictionary<Vector2Int, GroundCoverPatch> byColumn = new();
        private VoxelWorld watched;

        public int Count => byColumn.Count;
        public IEnumerable<GroundCoverPatch> Patches => byColumn.Values;

        /// <summary>Пучок пропал (под ним копали или строили).</summary>
        public event Action<GroundCoverPatch> Removed;

        /// <summary>
        /// В столбце пучка сменился блок, а пучок остался: его основание могло сдвинуться (например, столбец
        /// перестал быть нетронутой гладкой землёй, когда копали сбоку под верхом).
        /// </summary>
        public event Action<GroundCoverPatch> Changed;

        public GroundCoverPatch At(Vector2Int column) => byColumn.TryGetValue(column, out var patch) ? patch : null;

        /// <summary>Добавить пучок; false — столбец уже занят.</summary>
        public bool Add(GroundCoverPatch patch)
        {
            var column = new Vector2Int(patch.Cell.x, patch.Cell.z);
            if (byColumn.ContainsKey(column))
                return false;
            byColumn[column] = patch;
            return true;
        }

        /// <summary>Следить за миром: после каждой смены блока проверять пучок в этом столбце. null — перестать.</summary>
        public void Watch(VoxelWorld world)
        {
            if (watched != null)
                watched.BlockChanged -= OnBlockChanged;
            watched = world;
            if (watched != null)
                watched.BlockChanged += OnBlockChanged;
        }

        /// <summary>Убрать пучок столбца, если клетка над землёй больше не пуста или под ней нет земли.</summary>
        public bool Settle(VoxelWorld world, Vector2Int column)
        {
            if (!byColumn.TryGetValue(column, out var patch) || Trees.Forest.HasGround(world, patch.Cell))
                return false;
            byColumn.Remove(column);
            Removed?.Invoke(patch);
            return true;
        }

        private void OnBlockChanged(Vector3Int block)
        {
            var column = new Vector2Int(block.x, block.z);
            if (!Settle(watched, column) && byColumn.TryGetValue(column, out var patch))
                Changed?.Invoke(patch);
        }

        /// <summary>Высота основания пучка: земля в его точке, а не в центре клетки (<see cref="Trees.Forest.GroundHeight"/>).</summary>
        public static float BaseHeight(VoxelWorld world, GroundCoverPatch patch) =>
            Trees.Forest.GroundHeight(world, patch.Cell, patch.Position);
    }
}
