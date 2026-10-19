using System.Collections.Generic;
using Emberhold.World;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Items
{
    /// <summary>Цвета обломков блоков по материалу: близко к средним цветам их текстур.</summary>
    public static class DebrisColors
    {
        private static readonly Color Grass = new(0.36f, 0.5f, 0.22f);
        private static readonly Color Dirt = new(0.42f, 0.31f, 0.21f);
        private static readonly Color Stone = new(0.56f, 0.56f, 0.54f);
        private static readonly Color Wood = new(0.58f, 0.42f, 0.26f);
        private static readonly Color Bark = new(0.36f, 0.25f, 0.16f);

        /// <summary>Свежий срез дерева (щепки).</summary>
        public static readonly Color WoodChip = new(0.78f, 0.64f, 0.42f);

        /// <summary>
        /// Цвет обломка блока material; index разводит обломки одного блока: у травы обломок 0 — дёрн, остальные — земля,
        /// у дерева 1 — кора.
        /// </summary>
        public static Color Of(byte material, int index) => material switch
        {
            BlockMaterials.Grass => index == 0 ? Grass : Dirt,
            BlockMaterials.Dirt => Dirt,
            BlockMaterials.Wood => index == 1 ? Bark : Wood,
            _ => Stone,
        };
    }

    /// <summary>
    /// Обломки: осколки разбитого блока, крошки от удара, щепки и листья с дерева. Каждый обломок летит сам
    /// (скорость, вращение, тяжесть), отскакивает от земли, катится и к концу жизни уменьшается до нуля.
    /// Листья и хвоя падают медленно и кружатся. Объекты обломков переиспользуются. Координаты — мира блоков,
    /// на сцену их переводит transform мира. Обломки не мешают ни лучу мыши, ни рабочим.
    /// </summary>
    public sealed class Debris
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float Gravity = 22f;
        private const float LeafGravity = 3.5f;
        private const float Bounce = 0.32f;
        private const float GroundFriction = 0.55f;
        private const float ShrinkSeconds = 0.35f;

        /// <summary>Больше этого обломков одновременно не бывает: новые вытесняют самые старые.</summary>
        public const int MaxPieces = 220;

        private readonly Transform parent;
        private readonly Material material;
        private readonly List<Piece> pieces = new();
        private readonly Stack<GameObject> pool = new();
        private readonly MaterialPropertyBlock properties = new();
        private readonly System.Random random = new(7);

        public Debris(Transform parent, Material material)
        {
            this.parent = parent;
            this.material = material;
        }

        /// <summary>Сколько обломков сейчас летит или лежит.</summary>
        public int Count => pieces.Count;

        /// <summary>
        /// Выпустить count обломков формы shape из коробки center ± half. Скорость — наружу от центра (speed)
        /// плюс вверх (lift) и в сторону push; size — размер обломка в блоках, life — сколько он живёт, секунды.
        /// </summary>
        public void Burst(Vector3 center, Vector3 half, int count, DebrisShape shape, Color color, float size, float speed,
            float lift, float life, Vector3 push = default)
        {
            for (var i = 0; i < count; i++)
            {
                var offset = new Vector3(Signed() * half.x, Signed() * half.y, Signed() * half.z);
                var outward = offset.sqrMagnitude > 1e-6f ? offset.normalized : Random.onUnitSphere;
                var velocity = outward * (speed * Range(0.6f, 1.2f)) + Vector3.up * (lift * Range(0.7f, 1.3f)) + push;
                Spawn(new Piece
                {
                    Shape = shape,
                    Color = Vary(color),
                    Position = center + offset,
                    Velocity = velocity,
                    Rotation = Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f)),
                    Spin = Random.onUnitSphere * Range(240f, 720f),
                    Size = size * Range(0.7f, 1.3f),
                    Life = life * Range(0.8f, 1.2f),
                });
            }
        }

        /// <summary>Сдвинуть обломки на deltaTime и поставить их объекты; world — для земли, space — мир в сцене.</summary>
        public void Update(VoxelWorld world, Transform space, float deltaTime)
        {
            for (var i = pieces.Count - 1; i >= 0; i--)
            {
                var piece = pieces[i];
                piece.Age += deltaTime;
                if (piece.Age >= piece.Life || world == null)
                {
                    Release(i);
                    continue;
                }
                Move(piece, world, deltaTime);
                var scale = piece.Size * Mathf.Clamp01((piece.Life - piece.Age) / ShrinkSeconds);
                var t = piece.Object.transform;
                t.SetPositionAndRotation(space.TransformPoint(piece.Position), space.rotation * piece.Rotation);
                t.localScale = Vector3.one * scale;
            }
        }

        /// <summary>Убрать все обломки (например, мир сменился).</summary>
        public void Clear()
        {
            for (var i = pieces.Count - 1; i >= 0; i--)
                Release(i);
        }

        /// <summary>Уничтожить объекты обломков (при уничтожении вида).</summary>
        public void Dispose()
        {
            Clear();
            while (pool.Count > 0)
            {
                var go = pool.Pop();
                if (go != null)
                    Object.Destroy(go);
            }
        }

        private void Move(Piece piece, VoxelWorld world, float deltaTime)
        {
            var leaf = piece.Shape == DebrisShape.Leaf;
            if (piece.Resting)
                return;
            piece.Velocity += Vector3.down * ((leaf ? LeafGravity : Gravity) * deltaTime);
            if (leaf)
            {
                // Лист планирует: воздух гасит скорость и раскачивает его из стороны в сторону.
                piece.Velocity *= Mathf.Exp(-2.5f * deltaTime);
                piece.Velocity += new Vector3(Mathf.Sin(piece.Age * 5f + piece.Size * 40f), 0f, Mathf.Cos(piece.Age * 4f)) * (1.2f * deltaTime);
            }
            piece.Position += piece.Velocity * deltaTime;
            piece.Rotation = Quaternion.Euler(piece.Spin * deltaTime) * piece.Rotation;

            var cell = new Vector3Int(Mathf.FloorToInt(piece.Position.x), Mathf.FloorToInt(piece.Position.y), Mathf.FloorToInt(piece.Position.z));
            if (!world.IsInsideBlocks(new Vector3Int(cell.x, 0, cell.z)))
                return;
            var ground = DroppedItems.RestHeight(world, cell) + piece.Size * 0.3f;
            if (piece.Position.y >= ground)
                return;
            piece.Position = new Vector3(piece.Position.x, ground, piece.Position.z);
            if (leaf || piece.Velocity.y > -1.2f)
            {
                // Лёг: лист сразу, остальное — когда отскок стал слабым.
                piece.Resting = true;
                piece.Velocity = Vector3.zero;
                return;
            }
            piece.Velocity = new Vector3(piece.Velocity.x * GroundFriction, -piece.Velocity.y * Bounce, piece.Velocity.z * GroundFriction);
            piece.Spin *= 0.6f;
        }

        private void Spawn(Piece piece)
        {
            if (pieces.Count >= MaxPieces)
                Release(0);
            var go = pool.Count > 0 ? pool.Pop() : Create();
            go.GetComponent<MeshFilter>().sharedMesh = ItemPieceMeshes.Piece(piece.Shape, random.Next(ItemPieceMeshes.Variants));
            var renderer = go.GetComponent<MeshRenderer>();
            properties.SetColor(BaseColorId, piece.Color);
            renderer.SetPropertyBlock(properties);
            go.transform.localScale = Vector3.zero;
            go.SetActive(true);
            piece.Object = go;
            pieces.Add(piece);
        }

        private GameObject Create()
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Мелочь: тени от неё не видны, а стоят дорого.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private void Release(int index)
        {
            var go = pieces[index].Object;
            pieces.RemoveAt(index);
            if (go == null)
                return;
            go.SetActive(false);
            pool.Push(go);
        }

        private Color Vary(Color color)
        {
            var k = Range(0.88f, 1.08f);
            return new Color(color.r * k, color.g * k, color.b * k, 1f);
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        private float Signed() => (float)random.NextDouble() * 2f - 1f;

        private sealed class Piece
        {
            public DebrisShape Shape;
            public Color Color;
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 Spin;
            public float Size;
            public float Life;
            public float Age;
            public bool Resting;
            public GameObject Object;
        }
    }
}
