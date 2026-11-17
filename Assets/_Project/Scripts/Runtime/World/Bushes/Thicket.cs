using System;
using System.Collections.Generic;
using Emberhold.World.Items;
using Emberhold.World.Trees;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Bushes
{
    /// <summary>
    /// Куст на карте (#88): стоит в пустой клетке Cell над блоком земли, один на столбец. Удары копят урон; когда урон
    /// дошёл до прочности вида, с куста падает трава, а сам он остаётся голым и отрастает.
    /// </summary>
    public sealed class Bush
    {
        internal Bush(BushSpecies species, Vector3Int cell)
        {
            Species = species;
            Cell = cell;
        }

        public BushSpecies Species { get; }

        /// <summary>Клетка куста: пустая, над блоком земли. Сквозь неё ходят, блок в неё не поставить.</summary>
        public Vector3Int Cell { get; }

        public Vector2Int Column => new(Cell.x, Cell.z);

        /// <summary>Сдвиг куста от центра клетки по X и Z в блоках: кусты не стоят рядами.</summary>
        public Vector2 Offset { get; internal set; }

        /// <summary>Точка куста на карте по X и Z: центр клетки со сдвигом.</summary>
        public Vector2 Position => new(Cell.x + 0.5f + Offset.x, Cell.z + 0.5f + Offset.y);

        /// <summary>Сколько ХП уже снято ударами (с последнего среза).</summary>
        public float Damage { get; internal set; }

        public float Health => Mathf.Max(0f, Species.Durability - Damage);

        /// <summary>Куст ещё стоит: под ним не выкопали землю.</summary>
        public bool IsPresent { get; internal set; } = true;

        /// <summary>Куст зелёный и даёт траву; false — голый, ждёт отрастания.</summary>
        public bool IsGrown { get; internal set; } = true;

        /// <summary>Сколько игровых суток голому кусту ещё отрастать; у зелёного — 0.</summary>
        public float RegrowLeft { get; internal set; }

        /// <summary>Высота куста сейчас: зелёный выше, голый — одни ветки.</summary>
        public float Height => IsGrown ? Species.Height : Species.BareHeight;
    }

    /// <summary>
    /// Кусты на карте (#88). Срезанный куст кладёт траву у основания (в <see cref="DroppedItems"/>) и остаётся голым,
    /// через <see cref="BushSpecies.RegrowDays"/> игровых суток снова зелёный (<see cref="Grow"/>). Пока куст стоит, в его клетку
    /// нельзя поставить блок (<see cref="BlocksPlace"/>), но ходить сквозь него можно. Если под кустом выкопали землю или в его
    /// клетку всё же попал блок, куст пропадает (<see cref="Settle"/>). Чистая логика без Unity-объектов: хранит её
    /// <see cref="Forest.Thicket"/>, показывает ForestView, срезают рабочие. Координаты — мира блоков.
    /// </summary>
    public sealed class Thicket
    {
        private readonly List<Bush> bushes = new();
        private readonly Dictionary<Vector2Int, Bush> byColumn = new();
        private readonly DroppedItems items;

        public Thicket(DroppedItems items)
        {
            this.items = items;
        }

        public IReadOnlyList<Bush> Bushes => bushes;

        /// <summary>Номер изменения: растёт, когда куст посадили, срезали, он отрос или пропал.</summary>
        public int Version { get; private set; }

        /// <summary>С куста срезали траву; она уже лежит на земле.</summary>
        public event Action<Bush> Harvested;

        public Bush At(Vector2Int column) => byColumn.TryGetValue(column, out var bush) ? bush : null;

        /// <summary>Куст в клетке cell или null.</summary>
        public Bush AtCell(Vector3Int cell) => At(new Vector2Int(cell.x, cell.z)) is { } bush && bush.Cell == cell ? bush : null;

        /// <summary>В клетку cell нельзя поставить блок: в ней стоит куст.</summary>
        public bool BlocksPlace(Vector3Int cell) => AtCell(cell) != null;

        /// <summary>Посадить куст в клетку cell. Null, если в столбце уже есть куст.</summary>
        public Bush Plant(Vector3Int cell, BushSpecies species)
        {
            if (species == null)
                throw new ArgumentNullException(nameof(species));
            var column = new Vector2Int(cell.x, cell.z);
            if (byColumn.ContainsKey(column))
                return null;
            var bush = new Bush(species, cell);
            bushes.Add(bush);
            byColumn[column] = bush;
            Version++;
            return bush;
        }

        /// <summary>
        /// Ударить зелёный куст на damage ХП. true — трава срезана: она упала у основания, куст голый и отрастает.
        /// Голый куст ударами не срезать.
        /// </summary>
        public bool Hit(VoxelWorld world, Bush bush, float damage)
        {
            if (bush == null || !bush.IsPresent || !bush.IsGrown || damage <= 0f)
                return false;
            bush.Damage += damage;
            if (bush.Health > 0f)
                return false;
            bush.Damage = 0f;
            bush.IsGrown = false;
            bush.RegrowLeft = bush.Species.RegrowDays;
            Version++;
            if (items != null && world.IsInsideBlocks(bush.Cell))
                items.Drop(world, bush.Cell, new ItemDrop(bush.Species.ItemId, bush.Species.GrassYield));
            Harvested?.Invoke(bush);
            return true;
        }

        /// <summary>Прошло days игровых суток: голые кусты отрастают. Возвращает, сколько кустов снова зелёные.</summary>
        public int Grow(float days)
        {
            if (days <= 0f)
                return 0;
            var regrown = 0;
            foreach (var bush in bushes)
            {
                if (bush.IsGrown)
                    continue;
                bush.RegrowLeft -= days;
                if (bush.RegrowLeft > 0f)
                    continue;
                bush.RegrowLeft = 0f;
                bush.IsGrown = true;
                regrown++;
            }
            if (regrown > 0)
                Version++;
            return regrown;
        }

        /// <summary>Убрать кусты, под которыми выкопали землю или в чью клетку попал блок. Возвращает, сколько пропало.</summary>
        public int Settle(VoxelWorld world)
        {
            var gone = 0;
            for (var i = bushes.Count - 1; i >= 0; i--)
            {
                var bush = bushes[i];
                if (Forest.HasGround(world, bush.Cell))
                    continue;
                bush.IsPresent = false;
                bushes.RemoveAt(i);
                byColumn.Remove(bush.Column);
                gone++;
            }
            if (gone > 0)
                Version++;
            return gone;
        }

        /// <summary>Высота основания куста: земля в его точке, а не в центре клетки (<see cref="Forest.GroundHeight"/>).</summary>
        public static float BaseHeight(VoxelWorld world, Bush bush) => Forest.GroundHeight(world, bush.Cell, bush.Position);

        /// <summary>Коробка куста для выбора и рамки, координаты мира блоков: шириной с куст, от земли до верха (не ниже полублока).</summary>
        public static (Vector3 min, Vector3 max) Bounds(VoxelWorld world, Bush bush)
        {
            var half = Mathf.Max(0.3f, bush.Species.Width * 0.5f);
            var bottom = BaseHeight(world, bush);
            var position = bush.Position;
            return (new Vector3(position.x - half, bottom, position.y - half),
                new Vector3(position.x + half, bottom + Mathf.Max(0.5f, bush.Height), position.y + half));
        }

        /// <summary>Ближайший куст, в который попадает луч из origin по direction ближе maxDistance (координаты мира блоков).</summary>
        public bool Raycast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, out Bush hit, out float distance)
        {
            hit = null;
            distance = maxDistance;
            foreach (var bush in bushes)
            {
                var (min, max) = Bounds(world, bush);
                if (!Forest.RayBox(origin, direction, min, max, out var t) || t >= distance)
                    continue;
                hit = bush;
                distance = t;
            }
            return hit != null;
        }
    }
}
