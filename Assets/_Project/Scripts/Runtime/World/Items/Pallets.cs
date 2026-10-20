using System;
using System.Collections.Generic;
using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Items
{
    /// <summary>
    /// Поддон: склад одного вида ресурса на площадке <see cref="Size"/>×<see cref="Size"/> клеток высотой
    /// <see cref="Height"/>. Origin — угловая клетка (меньшие x и z) над землёй, на которой стоит поддон.
    /// Вид ресурса задаёт первая бронь или первое положенное; опустевший поддон без брони снова принимает любой.
    /// </summary>
    public sealed class Pallet
    {
        public const int Size = 2;
        public const int Height = 2;

        internal Pallet(Vector3Int origin)
        {
            Origin = origin;
        }

        public Vector3Int Origin { get; }

        /// <summary>Какой ресурс лежит или едет сюда; null — поддон пуст и свободен.</summary>
        public string ItemId { get; internal set; }

        /// <summary>Сколько единиц лежит.</summary>
        public int Count { get; internal set; }

        /// <summary>Сколько единиц уже несут сюда: место под них занято.</summary>
        public int Reserved { get; internal set; }

        /// <summary>Середина площадки на уровне земли, в координатах мира блоков.</summary>
        public Vector3 Center => Origin + new Vector3(Size * 0.5f, 0f, Size * 0.5f);

        /// <summary>Клетки над землёй, на которых стоит поддон (нижний слой).</summary>
        public IEnumerable<Vector3Int> FloorCells
        {
            get
            {
                for (var dz = 0; dz < Size; dz++)
                for (var dx = 0; dx < Size; dx++)
                    yield return Origin + new Vector3Int(dx, 0, dz);
            }
        }

        /// <summary>Клетка внутри поддона (любой из двух слоёв).</summary>
        public bool Covers(Vector3Int cell) =>
            cell.x >= Origin.x && cell.x < Origin.x + Size
            && cell.z >= Origin.z && cell.z < Origin.z + Size
            && cell.y >= Origin.y && cell.y < Origin.y + Height;
    }

    /// <summary>
    /// Все поддоны мира. Только данные: ставит игрок, носят и забирают рабочие, показывает отряд рабочих.
    /// Сколько ресурса помещается на поддон, задаёт capacityOf по id ресурса.
    /// </summary>
    public sealed class Pallets
    {
        private readonly List<Pallet> pallets = new();
        private readonly Func<string, int> capacityOf;

        public Pallets(Func<string, int> capacityOf)
        {
            this.capacityOf = capacityOf ?? (_ => 0);
        }

        public IReadOnlyList<Pallet> All => pallets;

        /// <summary>Номер изменения: растёт, когда поддон поставили, убрали или его содержимое изменилось.</summary>
        public int Version { get; private set; }

        /// <summary>Сколько ресурса itemId помещается на один поддон.</summary>
        public int Capacity(string itemId) => string.IsNullOrEmpty(itemId) ? 0 : Mathf.Max(0, capacityOf(itemId));

        public bool Contains(Pallet pallet) => pallet != null && pallets.Contains(pallet);

        /// <summary>Поддон, внутри которого клетка cell, или null.</summary>
        public Pallet At(Vector3Int cell)
        {
            foreach (var pallet in pallets)
            {
                if (pallet.Covers(cell))
                    return pallet;
            }
            return null;
        }

        /// <summary>
        /// Можно ли поставить поддон с угловой клеткой origin: все клетки площадки в мире и пусты на оба слоя,
        /// под каждой твёрдый пол, и площадка не задевает другой поддон.
        /// </summary>
        public bool CanPlace(VoxelWorld world, Vector3Int origin) => IsGround(world, origin) && !Overlaps(origin);

        /// <summary>Поставить поддон; null, если на этом месте нельзя (<see cref="CanPlace"/>).</summary>
        public Pallet Add(VoxelWorld world, Vector3Int origin)
        {
            if (!CanPlace(world, origin))
                return null;
            var pallet = new Pallet(origin);
            pallets.Add(pallet);
            Version++;
            return pallet;
        }

        /// <summary>Убрать поддон. Что на нём лежало, убирающий сам высыпает на землю.</summary>
        public bool Remove(Pallet pallet)
        {
            if (!pallets.Remove(pallet))
                return false;
            Version++;
            return true;
        }

        /// <summary>Поддон всё ещё стоит как надо: пол под ним не выкопали, в его клетки не поставили блок.</summary>
        public bool IsValid(VoxelWorld world, Pallet pallet) => IsGround(world, pallet.Origin);

        /// <summary>Сколько ещё единиц itemId можно сюда принести: 0, если тут лежит или едет другой ресурс.</summary>
        public int Space(Pallet pallet, string itemId)
        {
            if (!Contains(pallet) || string.IsNullOrEmpty(itemId) || pallet.ItemId != null && pallet.ItemId != itemId)
                return 0;
            return Mathf.Max(0, Capacity(itemId) - pallet.Count - pallet.Reserved);
        }

        /// <summary>Занять место под одну единицу itemId, которую сейчас понесут. false — места нет.</summary>
        public bool Reserve(Pallet pallet, string itemId)
        {
            if (Space(pallet, itemId) < 1)
                return false;
            pallet.ItemId = itemId;
            pallet.Reserved++;
            Version++;
            return true;
        }

        /// <summary>Отпустить бронь: ношу так и не принесли.</summary>
        public void Release(Pallet pallet)
        {
            if (pallet == null || pallet.Reserved <= 0)
                return;
            pallet.Reserved--;
            ForgetTypeIfEmpty(pallet);
            Version++;
        }

        /// <summary>
        /// Положить ношу; сначала она занимает свою бронь, а сверх брони — свободное место.
        /// false — не того вида или не помещается: тогда ничего не меняется.
        /// </summary>
        public bool Store(Pallet pallet, ItemDrop drop)
        {
            if (!Contains(pallet) || pallet.ItemId != null && pallet.ItemId != drop.ItemId)
                return false;
            var fromReserve = Mathf.Min(pallet.Reserved, drop.Count);
            if (pallet.Count + pallet.Reserved - fromReserve + drop.Count > Capacity(drop.ItemId))
                return false;
            pallet.ItemId = drop.ItemId;
            pallet.Reserved -= fromReserve;
            pallet.Count += drop.Count;
            Version++;
            return true;
        }

        /// <summary>Взять с поддона до count единиц. false — поддона нет или на нём пусто.</summary>
        public bool Take(Pallet pallet, int count, out ItemDrop taken)
        {
            taken = default;
            if (count <= 0 || !Contains(pallet) || pallet.Count <= 0)
                return false;
            var amount = Mathf.Min(count, pallet.Count);
            taken = new ItemDrop(pallet.ItemId, amount);
            pallet.Count -= amount;
            ForgetTypeIfEmpty(pallet);
            Version++;
            return true;
        }

        private static void ForgetTypeIfEmpty(Pallet pallet)
        {
            if (pallet.Count == 0 && pallet.Reserved == 0)
                pallet.ItemId = null;
        }

        private bool Overlaps(Vector3Int origin)
        {
            foreach (var other in pallets)
            {
                var o = other.Origin;
                if (origin.x < o.x + Pallet.Size && o.x < origin.x + Pallet.Size
                    && origin.z < o.z + Pallet.Size && o.z < origin.z + Pallet.Size
                    && origin.y < o.y + Pallet.Height && o.y < origin.y + Pallet.Height)
                    return true;
            }
            return false;
        }

        /// <summary>Под каждой клеткой площадки твёрдый пол, а сами клетки на высоту поддона пусты.</summary>
        private static bool IsGround(VoxelWorld world, Vector3Int origin)
        {
            for (var dz = 0; dz < Pallet.Size; dz++)
            for (var dx = 0; dx < Pallet.Size; dx++)
            {
                var floor = origin + new Vector3Int(dx, -1, dz);
                if (!world.IsInsideBlocks(floor))
                    return false;
                var below = world.GetBlock(floor);
                if (below.IsAir || below.Shape.IsCatalog && !below.Shape.CoversFloor)
                    return false;
                for (var dy = 0; dy < Pallet.Height; dy++)
                {
                    var cell = origin + new Vector3Int(dx, dy, dz);
                    if (!world.IsInsideBlocks(cell) || !world.GetBlock(cell).IsAir)
                        return false;
                }
            }
            return true;
        }
    }
}
