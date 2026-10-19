using System.Collections.Generic;
using UnityEngine;

namespace Emberhold.Gameplay.Items
{
    /// <summary>
    /// Сыпучая горка камней одной кучки: камни вылетают из разбитого блока, падают под тяжестью, отскакивают от земли,
    /// сталкиваются, скатываются друг с друга и ложатся там, где остановились. Координаты — от середины кучки на земле
    /// (земля — y = 0), в блоках; горка не выходит за свою клетку. Чистая математика: показывает <see cref="DroppedItemsView"/>.
    /// </summary>
    public sealed class StoneHeap
    {
        /// <summary>Сколько камней видно на одну единицу ресурса.</summary>
        public const int PerUnit = 3;

        /// <summary>Радиус камня обычного размера в блоках.</summary>
        public const float Radius = 0.14f;

        /// <summary>Дальше этого от середины по X и Z камни не откатываются: кучка остаётся в своей клетке.</summary>
        public const float Reach = 0.5f - Radius;

        private const float Gravity = 22f;
        private const float Bounce = 0.3f;
        private const float GroundFriction = 0.5f;
        private const float RestSpeed = 0.35f;
        private const float RestSeconds = 0.15f;
        // Дольше этого камень не катается: ложится, где был (страховка от вечного дрожания в тесной куче).
        private const float MaxRollSeconds = 4f;

        private readonly List<Stone> stones = new();
        private readonly System.Random random;

        public StoneHeap(int seed) => random = new System.Random(seed);

        public IReadOnlyList<Stone> Stones => stones;

        /// <summary>Все камни лежат.</summary>
        public bool Settled
        {
            get
            {
                foreach (var stone in stones)
                {
                    if (!stone.Resting)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Выпустить count камней из точки from (от середины кучки) не раньше чем через delay секунд:
        /// разлетаются в стороны и вверх.
        /// </summary>
        public void Burst(Vector3 from, int count, float delay = 0f)
        {
            for (var i = 0; i < count; i++)
            {
                var angle = Range(0f, Mathf.PI * 2f);
                var side = Range(0.6f, 1.5f);
                stones.Add(new Stone
                {
                    Position = from + new Vector3(Range(-0.15f, 0.15f), Range(-0.1f, 0.1f), Range(-0.15f, 0.15f)),
                    Velocity = new Vector3(Mathf.Cos(angle) * side, Range(2.2f, 3.6f), Mathf.Sin(angle) * side),
                    Rotation = Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f)),
                    Spin = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f)).normalized * Range(300f, 700f),
                    Scale = Range(0.8f, 1.2f),
                    Variant = random.Next(1000),
                    Wait = delay + i * 0.03f,
                });
            }
        }

        /// <summary>Убрать последние count камней (ресурс забрали).</summary>
        public void RemoveLast(int count)
        {
            count = Mathf.Clamp(count, 0, stones.Count);
            stones.RemoveRange(stones.Count - count, count);
            // Опору могли забрать: верхние камни снова падают.
            foreach (var stone in stones)
            {
                stone.Resting = false;
                stone.Still = 0f;
                stone.Age = 0f;
            }
        }

        /// <summary>Сдвинуть камни на deltaTime: тяжесть, отскок от земли и друг от друга, трение.</summary>
        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || Settled)
                return;
            // Крупный шаг дробим: быстрые камни не должны проскакивать друг сквозь друга.
            var steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / 0.01f), 1, 8);
            var dt = deltaTime / steps;
            for (var s = 0; s < steps; s++)
            {
                foreach (var stone in stones)
                    Move(stone, dt);
                Collide(dt);
            }
        }

        private void Move(Stone stone, float dt)
        {
            if (stone.Wait > 0f)
            {
                stone.Wait -= dt;
                return;
            }
            if (stone.Resting)
                return;
            stone.Age += dt;
            if (stone.Age > MaxRollSeconds)
            {
                Rest(stone);
                return;
            }
            stone.Velocity += Vector3.down * (Gravity * dt);
            stone.Position += stone.Velocity * dt;
            stone.Rotation = Quaternion.Euler(stone.Spin * dt) * stone.Rotation;

            var radius = stone.Radius;
            // Камень не уходит за свою клетку: отскакивает от её края.
            for (var axis = 0; axis < 3; axis += 2)
            {
                var limit = 0.5f - radius;
                if (Mathf.Abs(stone.Position[axis]) <= limit)
                    continue;
                var position = stone.Position;
                position[axis] = Mathf.Sign(position[axis]) * limit;
                stone.Position = position;
                var velocity = stone.Velocity;
                velocity[axis] *= -Bounce;
                stone.Velocity = velocity;
            }

            if (stone.Position.y >= radius)
                return;
            stone.Position = new Vector3(stone.Position.x, radius, stone.Position.z);
            if (stone.Velocity.y < -1.2f)
            {
                stone.Velocity = new Vector3(stone.Velocity.x * GroundFriction, -stone.Velocity.y * Bounce, stone.Velocity.z * GroundFriction);
                stone.Spin *= 0.6f;
                return;
            }
            stone.Velocity = new Vector3(stone.Velocity.x * (1f - 8f * dt), 0f, stone.Velocity.z * (1f - 8f * dt));
            Calm(stone, dt);
        }

        /// <summary>
        /// Камни, налетевшие друг на друга, расталкиваются; летящий скатывается с лежащего, а лежащий не сдвигается.
        /// </summary>
        private void Collide(float dt)
        {
            for (var i = 0; i < stones.Count; i++)
            {
                var a = stones[i];
                if (a.Wait > 0f)
                    continue;
                for (var j = i + 1; j < stones.Count; j++)
                {
                    var b = stones[j];
                    if (b.Wait > 0f)
                        continue;
                    var offset = a.Position - b.Position;
                    var touch = a.Radius + b.Radius;
                    var distance = offset.magnitude;
                    if (distance >= touch)
                        continue;
                    var normal = distance > 1e-5f ? offset / distance : Vector3.up;
                    var overlap = touch - distance;
                    if (a.Resting && b.Resting)
                    {
                        // Оба легли, зайдя друг в друга: верхний приподнимаем, оба остаются лежать.
                        if (overlap < touch * 0.1f)
                            continue;
                        if (a.Position.y >= b.Position.y)
                            a.Position += normal * overlap;
                        else
                            b.Position -= normal * overlap;
                        continue;
                    }
                    // Лежащий камень стоит на месте, летящий отталкивается от него целиком.
                    var shareA = a.Resting == b.Resting ? 0.5f : a.Resting ? 0f : 1f;
                    if (shareA > 0f)
                        Push(a, normal * (overlap * shareA), normal, dt);
                    if (shareA < 1f)
                        Push(b, -normal * (overlap * (1f - shareA)), -normal, dt);
                }
            }
        }

        private void Push(Stone stone, Vector3 shift, Vector3 normal, float dt)
        {
            stone.Position += shift;
            if (stone.Position.y < stone.Radius)
                stone.Position = new Vector3(stone.Position.x, stone.Radius, stone.Position.z);
            // Скорость к соседу гасится, вдоль — остаётся: камень скатывается по боку другого, но с трением.
            var into = Vector3.Dot(stone.Velocity, normal);
            if (into < 0f)
                stone.Velocity -= normal * (into * (1f + Bounce));
            if (normal.y > 0.3f)
            {
                var along = Vector3.Dot(stone.Velocity, normal);
                stone.Velocity = normal * along + (stone.Velocity - normal * along) * (1f - 10f * dt);
            }
            // Сосед хоть немного снизу — опора: медленный камень на ней ложится.
            if (normal.y >= 0.2f)
                Calm(stone, dt);
            else if (shift.sqrMagnitude > 1e-8f)
                stone.Resting = false;
        }

        /// <summary>Камень на опоре и почти не движется: через миг он ложится и больше не катится.</summary>
        private static void Calm(Stone stone, float dt)
        {
            if (stone.Velocity.sqrMagnitude > RestSpeed * RestSpeed)
            {
                stone.Still = 0f;
                return;
            }
            stone.Still += dt;
            stone.Spin *= 0.8f;
            if (stone.Still >= RestSeconds)
                Rest(stone);
        }

        private static void Rest(Stone stone)
        {
            stone.Resting = true;
            stone.Velocity = Vector3.zero;
            stone.Spin = Vector3.zero;
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        /// <summary>Один камень горки.</summary>
        public sealed class Stone
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 Spin;
            public float Scale;
            public int Variant;
            /// <summary>Сколько ещё ждать до вылета, секунды.</summary>
            public float Wait;
            public float Still;
            /// <summary>Сколько секунд камень в движении с последнего толчка.</summary>
            public float Age;
            public bool Resting;

            public float Radius => StoneHeap.Radius * Scale * 0.85f;
        }
    }
}
