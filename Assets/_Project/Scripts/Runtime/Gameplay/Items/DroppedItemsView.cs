using System.Collections.Generic;
using Emberhold.Gameplay.Building;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Items
{
    /// <summary>
    /// Ресурсы на земле и обломки. Слушает удары по блокам (<see cref="BlockDamage"/>): от удара летят крошки,
    /// разбитый блок раскалывается на осколки (<see cref="Debris"/>), а выпавший по таблице BlockDrops ресурс
    /// ложится на место блока. Кучку видно предметами: поленья — по одному на единицу, поленницей
    /// (<see cref="ItemPileLayout"/>), вылетают из места выпадения (из блока или, по <see cref="ExpectArrivals"/>,
    /// из упавшего дерева), летят дугой и с подскоком ложатся на место. Камни — по <see cref="StoneHeap.PerUnit"/>
    /// на единицу: вылетают из разбитого блока, падают, отскакивают, сталкиваются и ссыпаются горкой (<see cref="StoneHeap"/>).
    /// Каждый кадр кладёт кучки на опору заново, чтобы они не висели в воздухе, когда блок под ними выкопали.
    /// Если задан <see cref="BlockFallView"/>, опустившаяся кучка падает с тем же ускорением, что и блоки,
    /// поэтому ресурс на упавшем блоке летит вместе с ним; данные (<see cref="DroppedItems"/>) меняются сразу.
    /// </summary>
    public sealed class DroppedItemsView : MonoBehaviour
    {
        private const float LandingSeconds = 0.16f;
        private const float LandingHop = 0.06f;
        private const float SlotEase = 10f;
        private const float ArrivalStagger = 0.05f;

        [SerializeField] private BlockEditController editor;
        [SerializeField] private VoxelWorldView worldView;
        [SerializeField] private BlockDropTable drops;
        [Tooltip("Материал предметов и обломков на шейдере Emberhold/World/Tree: цвет из вершин сетки, обломкам — _BaseColor.")]
        [SerializeField] private Material pieceMaterial;
        [Tooltip("Сколько предметов в кучке показывать, не больше.")]
        [Min(1)]
        [SerializeField] private int maxStack = 12;
        [Tooltip("Падение блоков: с его ускорением падают и кучки, потерявшие опору. Пусто — кучки переставляются сразу.")]
        [SerializeField] private BlockFallView blockFalls;

        private readonly Dictionary<DroppedItem, Pile> piles = new();
        private readonly List<DroppedItem> gone = new();
        private readonly Dictionary<DroppedItem, float> heightsBefore = new();
        // Падающие кучки: с какой высоты и когда начали падать.
        private readonly Dictionary<DroppedItem, (float from, float start)> falling = new();
        private readonly List<DroppedItem> landed = new();
        private readonly List<Arrival> arrivals = new();
        private readonly Stack<GameObject> pool = new();
        private readonly System.Random random = new(11);
        private BlockDamage damage;

        /// <summary>Лежащие на земле ресурсы; рабочие забирают их отсюда.</summary>
        public DroppedItems Items { get; private set; }

        /// <summary>Обломки: осколки блоков, крошки, щепки и листья с деревьев.</summary>
        public Debris Debris { get; private set; }

        /// <summary>Сколько предметов кучек сейчас видно (для тестов).</summary>
        public int ShownPieces { get; private set; }

        private void Awake()
        {
            if (editor == null || worldView == null || drops == null)
            {
                Debug.LogError("[DroppedItems] Не назначены BlockEditController, VoxelWorldView или BlockDrops.", this);
                enabled = false;
                return;
            }
            Items = new DroppedItems(drops);
            Debris = new Debris(transform, pieceMaterial);
        }

        private void Start()
        {
            // Урон создаётся в Awake редактора, поэтому подписываемся в Start.
            damage = editor.Damage;
            if (damage == null)
            {
                Debug.LogError("[DroppedItems] У BlockEditController нет BlockDamage.", this);
                enabled = false;
                return;
            }
            damage.Destroyed += OnBlockDestroyed;
            damage.Damaged += OnBlockDamaged;
        }

        private void OnDestroy()
        {
            if (damage != null)
            {
                damage.Destroyed -= OnBlockDestroyed;
                damage.Damaged -= OnBlockDamaged;
            }
            Debris?.Dispose();
            foreach (var pile in piles.Values)
            {
                foreach (var unit in pile.Units)
                    Destroy(unit.Object);
                foreach (var stone in pile.StoneObjects)
                    Destroy(stone);
            }
            while (pool.Count > 0)
                Destroy(pool.Pop());
        }

        /// <summary>
        /// Следующие предметы itemId, которые появятся в кучке клетки cell, вылетят из точек origins (по одному
        /// на точку, координаты мира блоков) не раньше чем через delay секунд. Так поленья вылетают из упавшего ствола.
        /// </summary>
        public void ExpectArrivals(Vector3Int cell, string itemId, IReadOnlyList<Vector3> origins, float delay)
        {
            if (origins == null || origins.Count == 0)
                return;
            arrivals.Add(new Arrival(cell, itemId, new Queue<Vector3>(origins), Time.time + Mathf.Max(0f, delay)));
        }

        private void OnBlockDestroyed(Vector3Int block, Block destroyed)
        {
            if (worldView.World == null)
                return;
            Items.OnBlockDestroyed(worldView.World, block, destroyed);
            // Блок раскалывается на осколки, между ними — мелкие крошки.
            var center = block + Vector3.one * 0.5f;
            var fill = Mathf.Clamp(destroyed.Shape.Fill, 0.25f, 1f);
            var half = new Vector3(0.32f, 0.32f * fill, 0.32f);
            var bottom = center + Vector3.down * (0.5f * (1f - fill));
            Debris.Burst(bottom, half, 4 + random.Next(4), DebrisShape.Shard, ColorOf(destroyed, block, 0), 0.24f, 1.6f, 2.6f, 2.4f);
            Debris.Burst(bottom, half, 3, DebrisShape.Shard, ColorOf(destroyed, block, 1), 0.2f, 1.8f, 3f, 2.2f);
            Debris.Burst(bottom, half, 6, DebrisShape.Chip, ColorOf(destroyed, block, 2), 0.08f, 2.4f, 3.2f, 1.3f);
        }

        private void OnBlockDamaged(Vector3Int block, Block hit, float fraction)
        {
            // От удара с верха блока летят крошки; чем ближе блок к разрушению, тем больше.
            var top = block + new Vector3(0.5f, Mathf.Clamp(hit.Shape.Fill, 0.25f, 1f), 0.5f);
            Debris.Burst(top, new Vector3(0.35f, 0.02f, 0.35f), 3 + Mathf.RoundToInt(fraction * 3f), DebrisShape.Chip,
                ColorOf(hit, block, random.Next(3)), 0.07f, 1.3f, 3.4f, 1f);
        }

        /// <summary>Цвет обломка блока: у травы часть обломков зелёные (дёрн), остальные — земля.</summary>
        private Color ColorOf(Block block, Vector3Int cell, int index)
        {
            var color = DebrisColors.Of(block.Material, index);
            if (block.Material == BlockMaterials.Grass && index == 0 && worldView.World?.GrassTint is { } tint)
                color *= tint.Sample(cell.x + 0.5f, cell.z + 0.5f);
            color.a = 1f;
            return color;
        }

        private void LateUpdate()
        {
            var world = worldView.World;
            if (world == null)
                return;
            heightsBefore.Clear();
            foreach (var item in Items.Items)
                heightsBefore[item] = item.Position.y;
            Items.Settle(world);
            if (blockFalls != null)
                StartFalls();

            ShowPiles();
            Debris.Update(world, worldView.transform, Time.deltaTime);
            arrivals.RemoveAll(arrival => arrival.Origins.Count == 0 || Time.time > arrival.Start + 5f);
        }

        /// <summary>
        /// Предметы кучек: новые вылетают из места выпадения, взятые исчезают, поленья стоят по местам поленницы,
        /// камни лежат там, где их остановила горка.
        /// </summary>
        private void ShowPiles()
        {
            var space = worldView.transform;
            var now = Time.time;
            var shown = 0;
            foreach (var item in Items.Items)
            {
                if (!piles.TryGetValue(item, out var pile))
                    piles[item] = pile = new Pile(Yaw(item.Cell), item.Cell.GetHashCode());
                var count = Mathf.Min(item.Drop.Count, maxStack);
                var ground = new Vector3(item.Position.x, ShownHeight(item), item.Position.z);
                shown += ItemPileLayout.IsLog(item.Drop.ItemId)
                    ? ShowLogs(item, pile, count, ground, space, now)
                    : ShowStones(item, pile, count, ground, space);
            }
            ShownPieces = shown;

            // Кучки, которых больше нет (забрали целиком, слились с другой), убираем.
            gone.Clear();
            foreach (var item in piles.Keys)
            {
                if (!Contains(item))
                    gone.Add(item);
            }
            foreach (var item in gone)
            {
                var pile = piles[item];
                foreach (var unit in pile.Units)
                    Release(unit.Object);
                foreach (var stone in pile.StoneObjects)
                    Release(stone);
                piles.Remove(item);
            }
        }

        /// <summary>Поленья кучки: по одному на единицу, поленницей. Возвращает, сколько видно.</summary>
        private int ShowLogs(DroppedItem item, Pile pile, int count, Vector3 ground, Transform space, float now)
        {
            var ease = 1f - Mathf.Exp(-SlotEase * Time.deltaTime);
            var shown = 0;
            while (pile.Units.Count > count)
            {
                var last = pile.Units.Count - 1;
                Release(pile.Units[last].Object);
                pile.Units.RemoveAt(last);
            }
            var turn = Quaternion.Euler(0f, pile.Yaw, 0f);
            for (var i = pile.Units.Count; i < count; i++)
                pile.Units.Add(NewUnit(item, i, ground, now));

            for (var i = 0; i < pile.Units.Count; i++)
            {
                var unit = pile.Units[i];
                var slot = ItemPileLayout.Slot(i, count);
                var spot = turn * slot.Position;
                // Кучка стала больше или меньше: поленья плавно сдвигаются на новые места.
                if (unit.HasSlot && unit.Slot != spot)
                    unit.Offset += unit.Slot - spot;
                unit.Slot = spot;
                unit.HasSlot = true;
                unit.Offset *= 1f - ease;
                var target = ground + spot + unit.Offset;
                var rotation = turn * slot.Rotation;
                var t = (now - unit.Start) / unit.Duration;
                if (t < 0f)
                {
                    unit.Object.SetActive(false);
                    continue;
                }
                Vector3 position;
                if (t < 1f)
                {
                    // Полёт дугой из места выпадения, полено кувыркается и выравнивается к концу.
                    position = Vector3.Lerp(unit.From, target, t) + Vector3.up * (unit.Arc * 4f * t * (1f - t));
                    rotation = Quaternion.Slerp(unit.Tumble, rotation, t * t);
                }
                else
                {
                    var after = (now - unit.Start - unit.Duration) / LandingSeconds;
                    position = target + (after < 1f ? Vector3.up * (LandingHop * Mathf.Sin(after * Mathf.PI)) : Vector3.zero);
                }
                unit.Object.transform.SetPositionAndRotation(space.TransformPoint(position), space.rotation * rotation);
                unit.Object.transform.localScale = space.lossyScale * slot.Scale;
                unit.Object.SetActive(true);
                shown++;
            }
            return shown;
        }

        /// <summary>
        /// Камни кучки: по <see cref="StoneHeap.PerUnit"/> на единицу. Новые вылетают из блока, на месте которого лежит
        /// кучка (или из точки <see cref="ExpectArrivals"/>), и ссыпаются горкой. Возвращает, сколько видно.
        /// </summary>
        private int ShowStones(DroppedItem item, Pile pile, int count, Vector3 ground, Transform space)
        {
            var heap = pile.Heap;
            var wanted = count * StoneHeap.PerUnit;
            if (heap.Stones.Count > wanted)
                heap.RemoveLast(heap.Stones.Count - wanted);
            var stagger = 0;
            while (heap.Stones.Count < wanted)
            {
                // Из блока: середина клетки над кучкой.
                var from = new Vector3(0f, 0.5f, 0f);
                if (TakeArrival(item, out var origin, out var delay))
                    from = origin - ground;
                heap.Burst(from, StoneHeap.PerUnit, delay + stagger++ * ArrivalStagger);
            }
            heap.Step(Time.deltaTime);

            while (pile.StoneObjects.Count > heap.Stones.Count)
            {
                var last = pile.StoneObjects.Count - 1;
                Release(pile.StoneObjects[last]);
                pile.StoneObjects.RemoveAt(last);
            }
            while (pile.StoneObjects.Count < heap.Stones.Count)
            {
                var stoneData = heap.Stones[pile.StoneObjects.Count];
                var go = pool.Count > 0 ? pool.Pop() : CreateObject();
                go.GetComponent<MeshFilter>().sharedMesh = ItemPieceMeshes.Stone(stoneData.Variant);
                go.name = "Dropped Stone";
                pile.StoneObjects.Add(go);
            }

            var shown = 0;
            for (var i = 0; i < heap.Stones.Count; i++)
            {
                var stone = heap.Stones[i];
                var go = pile.StoneObjects[i];
                if (stone.Wait > 0f)
                {
                    go.SetActive(false);
                    continue;
                }
                go.transform.SetPositionAndRotation(space.TransformPoint(ground + stone.Position), space.rotation * stone.Rotation);
                go.transform.localScale = space.lossyScale * stone.Scale;
                go.SetActive(true);
                shown++;
            }
            return shown;
        }

        /// <summary>Ждёт ли кучку предмет из точки <see cref="ExpectArrivals"/>: откуда он вылетит и через сколько секунд.</summary>
        private bool TakeArrival(DroppedItem item, out Vector3 origin, out float delay)
        {
            foreach (var arrival in arrivals)
            {
                if (arrival.Cell != item.Cell || arrival.ItemId != item.Drop.ItemId || arrival.Origins.Count == 0)
                    continue;
                origin = arrival.Origins.Dequeue();
                delay = Mathf.Max(0f, arrival.Start - Time.time) + arrival.Taken++ * ArrivalStagger;
                return true;
            }
            origin = default;
            delay = 0f;
            return false;
        }

        private Unit NewUnit(DroppedItem item, int index, Vector3 ground, float now)
        {
            var from = ground + new Vector3(Signed() * 0.2f, 0.5f, Signed() * 0.2f);
            var start = now + index % 4 * ArrivalStagger;
            if (TakeArrival(item, out var origin, out var delay))
            {
                from = origin;
                start = now + delay;
            }
            var distance = Vector3.Distance(from, ground);
            var go = pool.Count > 0 ? pool.Pop() : CreateObject();
            var log = ItemPileLayout.IsLog(item.Drop.ItemId);
            go.GetComponent<MeshFilter>().sharedMesh = log ? ItemPieceMeshes.Log() : ItemPieceMeshes.Stone(random.Next(ItemPieceMeshes.Variants));
            go.name = log ? "Dropped Log" : "Dropped Item";
            go.SetActive(false);
            return new Unit
            {
                Object = go,
                From = from,
                Start = start,
                Duration = Mathf.Clamp(0.32f + 0.07f * distance, 0.32f, 0.8f),
                Arc = 0.35f + 0.12f * distance,
                Tumble = Quaternion.Euler(Signed() * 180f, Signed() * 180f, Signed() * 180f),
            };
        }

        private GameObject CreateObject()
        {
            var go = new GameObject("Dropped Item");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = pieceMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            // Без коллайдера: предмет не должен мешать лучу выбора блока.
            return go;
        }

        private void Release(GameObject go)
        {
            if (go == null)
                return;
            go.SetActive(false);
            pool.Push(go);
        }

        /// <summary>Кучки, которые Settle опустил в этом кадре, начинают падать с той высоты, где их видно сейчас.</summary>
        private void StartFalls()
        {
            foreach (var item in Items.Items)
            {
                if (!heightsBefore.TryGetValue(item, out var before))
                    continue;
                var height = item.Position.y;
                if (height < before - 0.001f)
                    falling[item] = (falling.ContainsKey(item) ? ShownHeight(item) : before, Time.time);
                else if (height > before + 0.001f)
                    falling.Remove(item);
            }

            // Долетевшие и исчезнувшие (слились с другой кучкой, подобраны) больше не падают.
            landed.Clear();
            foreach (var item in falling.Keys)
            {
                if (!Contains(item) || ShownHeight(item) <= item.Position.y)
                    landed.Add(item);
            }
            foreach (var item in landed)
                falling.Remove(item);
        }

        private bool Contains(DroppedItem item)
        {
            foreach (var other in Items.Items)
            {
                if (other == item)
                    return true;
            }
            return false;
        }

        /// <summary>Высота, на которой кучку видно сейчас: в полёте — выше точки опоры.</summary>
        private float ShownHeight(DroppedItem item)
        {
            if (blockFalls == null || !falling.TryGetValue(item, out var fall))
                return item.Position.y;
            var drop = FallMotion.Drop(fall.from - item.Position.y, Time.time - fall.start, blockFalls.Gravity);
            return Mathf.Max(item.Position.y, fall.from - drop);
        }

        /// <summary>Поворот кучки по её клетке: кучки рядом лежат не одинаково.</summary>
        private static float Yaw(Vector3Int cell)
        {
            unchecked
            {
                var h = (uint)(cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xffff) / 65536f * 360f;
            }
        }

        private float Signed() => (float)random.NextDouble() * 2f - 1f;

        private sealed class Pile
        {
            public readonly float Yaw;
            /// <summary>Поленья: по одному на единицу.</summary>
            public readonly List<Unit> Units = new();
            /// <summary>Камни горкой и их объекты (по порядку камней горки).</summary>
            public readonly StoneHeap Heap;
            public readonly List<GameObject> StoneObjects = new();

            public Pile(float yaw, int seed)
            {
                Yaw = yaw;
                Heap = new StoneHeap(seed);
            }
        }

        private sealed class Unit
        {
            public GameObject Object;
            public Vector3 From;
            public float Start;
            public float Duration;
            public float Arc;
            public Quaternion Tumble;
            public Vector3 Slot;
            public bool HasSlot;
            public Vector3 Offset;
        }

        private sealed class Arrival
        {
            public readonly Vector3Int Cell;
            public readonly string ItemId;
            public readonly Queue<Vector3> Origins;
            public readonly float Start;
            public int Taken;

            public Arrival(Vector3Int cell, string itemId, Queue<Vector3> origins, float start)
            {
                Cell = cell;
                ItemId = itemId;
                Origins = origins;
                Start = start;
            }
        }
    }
}
