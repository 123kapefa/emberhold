using System;
using System.Collections.Generic;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>Материал детали: у каждого слоя свой материал рига (тело, свечение, стекло, лёд, изморозь на земле, жар).</summary>
    public enum CreatureLayer
    {
        Body,
        Glow,
        Glass,
        Ice,
        Ground,
        Heat,
    }

    /// <summary>Что за деталь: часть тела, глаз или добавка облика уровня (иней, трещины, лёд, ядро, корона, осколки, изморозь).</summary>
    public enum ElementKind
    {
        Shape,
        Eye,
        Frost,
        Crack,
        Ice,
        Core,
        Crown,
        Orbit,
        Ground,
    }

    /// <summary>Одна деталь модели: тело FacetedMesh в своём месте, её слой, вид и середина (по ней риг решает, к какой части она относится).</summary>
    public sealed class CreatureElement
    {
        internal Matrix4x4 Matrix;
        internal Action<FacetedMesh, Func<Vector3, Color>> Shape;
        internal Func<Vector3, Color> Paint;
        internal Func<System.Random, (Vector3 point, Vector3 normal)> Sample;

        /// <summary>Середина детали в координатах модели.</summary>
        public Vector3 Center { get; internal set; }

        /// <summary>Границы детали в координатах модели.</summary>
        public Bounds Bounds { get; internal set; }

        /// <summary>Радиус или ширина детали с учётом её масштаба: крупные несут трещины и ледяные пластины.</summary>
        public float Extent { get; internal set; }

        public ElementKind Kind { get; internal set; }
        public CreatureLayer Layer { get; internal set; }

        /// <summary>Номер группы (отдельный мотылёк стайки), −1 — сама модель.</summary>
        public int Group { get; internal set; } = -1;

        /// <summary>Номер крыла, к которому прикреплена деталь, −1 — не на крыле.</summary>
        public int Wing { get; internal set; } = -1;

        /// <summary>Мелкий шарик (дыхальце, кончик усика): его не путают со старыми лапами.</summary>
        public bool Tiny { get; internal set; }

        /// <summary>Добавлена обликом уровня, а не частью тела.</summary>
        public bool LevelAdd => Kind != ElementKind.Shape && Kind != ElementKind.Eye;

        /// <summary>Сдвиг детали в координатах модели (ледяное ядро ходули переносится внутрь тела).</summary>
        public Vector3 Offset { get; set; }
    }

    /// <summary>Крыло: шарнир в root (координаты группы), сторона и размах взмаха.</summary>
    public readonly struct CreatureWing
    {
        public CreatureWing(Vector3 root, int side, float amplitude, int group)
        {
            Root = root;
            Side = side;
            Amplitude = amplitude;
            Group = group;
        }

        public Vector3 Root { get; }
        public int Side { get; }
        public float Amplitude { get; }
        public int Group { get; }
    }

    /// <summary>
    /// Сборщик гранёных существ, перенос прототипа бестиария: эллипсоиды, отрезки, конусы, коробки, кольца, глаза и иней.
    /// Единица — блок, морда смотрит в +Z. Детали копятся списком; <see cref="Finish"/> добавляет облик уровня, а
    /// <see cref="Bake"/> раскладывает их по частям рига и слоям материалов. Цвета вершин — с отливом уровня, a = 0.
    /// </summary>
    public sealed class CreatureBuilder
    {
        private static readonly Bounds UnitBlob = new(Vector3.zero, Vector3.one * 2f);

        private readonly List<CreatureElement> elements = new();
        private readonly List<(Vector3 point, Vector3 direction)> frostPoints = new();
        private readonly List<Matrix4x4> groups = new();
        private readonly List<CreatureWing> wings = new();
        private readonly int seed;
        private int counter;
        private int group = -1;
        private bool levelStage;

        public CreatureBuilder(int level, int seed)
        {
            Level = level;
            this.seed = seed;
        }

        public int Level { get; }
        public IReadOnlyList<CreatureElement> Elements => elements;
        public IReadOnlyList<CreatureWing> Wings => wings;
        public IReadOnlyList<Matrix4x4> Groups => groups;

        /// <summary>Холодный свет матёрого и древнего: точка в координатах модели (null у обычного).</summary>
        public Vector3? LightAt { get; private set; }

        /// <summary>Следующие детали — отдельная группа (мотылёк стайки) со своим местом, поворотом и размером.</summary>
        public int BeginGroup(Matrix4x4 matrix)
        {
            groups.Add(matrix);
            group = groups.Count - 1;
            return group;
        }

        public void EndGroup() => group = -1;

        // ---------- примитивы ----------

        /// <summary>Эллипсоид: гранёный икосаэдр (detail делений), неровный, повёрнутый rot (радианы, порядок XYZ).</summary>
        public CreatureElement E(Vector3 center, Vector3 radii, uint color, Vector3 rot = default, int detail = 1, float jitter = 0.16f,
            CreatureLayer layer = CreatureLayer.Body, int wing = -1)
        {
            var random = new System.Random(seed * 97 + counter++);
            var element = Add(Matrix4x4.TRS(center, EulerXYZ(rot), radii), UnitBlob, 1f,
                (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, detail, jitter, random, paint), color, layer, ElementKind.Shape, wing,
                r =>
                {
                    var direction = RandomDirection(r);
                    return (direction, direction);
                });
            element.Tiny = radii.x < 0.02f;
            return element;
        }

        public CreatureElement E(Vector3 center, float radius, uint color, Vector3 rot = default, int detail = 1, float jitter = 0.16f,
            CreatureLayer layer = CreatureLayer.Body, int wing = -1) =>
            E(center, Vector3.one * radius, color, rot, detail, jitter, layer, wing);

        /// <summary>Отрезок между точками (конечность): усечённый конус от r0 у a до r1 у b (по умолчанию r0 · 0.75).</summary>
        public CreatureElement S(Vector3 a, Vector3 b, float r0, float? r1, uint color, int sides = 5,
            CreatureLayer layer = CreatureLayer.Body, int wing = -1)
        {
            var top = r1 ?? r0 * 0.75f;
            var length = Vector3.Distance(a, b);
            var radius = Mathf.Max(r0, top);
            return Add(Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, b - a), Vector3.one),
                new Bounds(new Vector3(0f, length * 0.5f, 0f), new Vector3(radius * 2f, length, radius * 2f)), r0,
                (mesh, paint) => mesh.AddPrism(sides, Vector3.zero, r0, top, length, 0f, null, paint), color, layer, ElementKind.Shape, wing,
                r =>
                {
                    var t = (float)r.NextDouble();
                    var angle = (float)r.NextDouble() * Mathf.PI * 2f;
                    var at = Mathf.Lerp(r0, top, t);
                    return (new Vector3(Mathf.Cos(angle) * at, t * length, Mathf.Sin(angle) * at),
                        new Vector3(Mathf.Cos(angle), (r0 - top) / Mathf.Max(length, 1e-4f), Mathf.Sin(angle)));
                });
        }

        /// <summary>Цепочка отрезков, в суставах — шары (кроме noJoints).</summary>
        public void Chain(IReadOnlyList<Vector3> points, float r0, float r1, uint color, int sides = 5, bool noJoints = false,
            CreatureLayer layer = CreatureLayer.Body, int wing = -1)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var t0 = (float)i / (points.Count - 1);
                var t1 = (float)(i + 1) / (points.Count - 1);
                S(points[i], points[i + 1], r0 + (r1 - r0) * t0, r0 + (r1 - r0) * t1, color, sides, layer, wing);
                if (i > 0 && !noJoints)
                    E(points[i], (r0 + (r1 - r0) * t0) * 1.15f, color, default, 0, 0.16f, layer, wing);
            }
        }

        /// <summary>Конус от точки basePoint в направлении direction.</summary>
        public CreatureElement K(Vector3 basePoint, Vector3 direction, float radius, float height, uint color, int sides = 5,
            CreatureLayer layer = CreatureLayer.Body, int wing = -1) =>
            Cone(basePoint, direction, radius, height, color, sides, layer, ElementKind.Shape, wing);

        /// <summary>Коробка с центром center и размерами size, повёрнутая rot.</summary>
        public CreatureElement B(Vector3 center, Vector3 size, uint color, Vector3 rot = default, CreatureLayer layer = CreatureLayer.Body) =>
            Add(Matrix4x4.TRS(center, EulerXYZ(rot), Vector3.one), new Bounds(Vector3.zero, size), size.x,
                (mesh, paint) => mesh.AddBox(Vector3.zero, size, paint), color, layer, ElementKind.Shape, -1);

        /// <summary>Плоская полоса (ткань, ремень) между точками; yaw поворачивает ширину вокруг оси полосы.</summary>
        public CreatureElement Plank(Vector3 a, Vector3 b, float width, float thickness, uint color, float yaw = 0f)
        {
            var length = Vector3.Distance(a, b);
            var size = new Vector3(width, length, thickness);
            var rotation = Quaternion.FromToRotation(Vector3.up, b - a) * Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up);
            return Add(Matrix4x4.TRS((a + b) * 0.5f, rotation, Vector3.one), new Bounds(Vector3.zero, size), width,
                (mesh, paint) => mesh.AddBox(Vector3.zero, size, paint), color, CreatureLayer.Body, ElementKind.Shape, -1);
        }

        /// <summary>Кольцо (тор) вокруг оси Z, повёрнутое rot и растянутое scale.</summary>
        public CreatureElement Ring(Vector3 center, float radius, float tube, uint color, Vector3 rot = default, Vector3? scale = null,
            int tubeSides = 4, int ringSides = 10)
        {
            var outer = radius + tube;
            return Add(Matrix4x4.TRS(center, EulerXYZ(rot), scale ?? Vector3.one),
                new Bounds(Vector3.zero, new Vector3(outer * 2f, outer * 2f, tube * 2f)), radius,
                (mesh, paint) => mesh.AddTorus(radius, tube, tubeSides, ringSides, paint), color, CreatureLayer.Body, ElementKind.Shape, -1);
        }

        /// <summary>Светящийся глаз цвета уровня.</summary>
        public CreatureElement Eye(Vector3 center, float radius, int wing = -1)
        {
            var element = Add(Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius), UnitBlob, 1f,
                (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, 0, 0f, null, paint), CreaturePalette.Eyes[Level],
                CreatureLayer.Glow, ElementKind.Eye, wing);
            element.Paint = Paint(CreaturePalette.Hex(CreaturePalette.Eyes[Level]));
            return element;
        }

        /// <summary>Зубы вдоль дуги шириной width, через один короче.</summary>
        public void Teeth(Vector3 center, float width, int count, float height, Vector3 direction, uint color = CreaturePalette.Bone,
            float radius = 0.025f)
        {
            for (var i = 0; i < count; i++)
            {
                var x = center.x + (count == 1 ? 0f : ((float)i / (count - 1) - 0.5f) * width);
                K(new Vector3(x, center.y, center.z), direction, radius, height * (i % 2 == 1 ? 0.7f : 1f), color, 4);
            }
        }

        /// <summary>Крыло на шарнире в root (для взмахов): перья-пластины; детали с wing = номер крыла крепятся к нему же.</summary>
        public int Wing(Vector3 root, int side, float length, float width, uint color, uint color2, float amplitude, int feathers)
        {
            wings.Add(new CreatureWing(root, side, amplitude, group));
            var index = wings.Count - 1;
            for (var i = 0; i < feathers; i++)
            {
                var t = (float)i / Mathf.Max(1, feathers - 1);
                var random = new System.Random(counter++);
                var scale = new Vector3(length * (0.55f - t * 0.25f), 0.025f, width * (0.55f - t * 0.1f));
                var at = new Vector3(side * length * (0.45f + t * 0.2f), 0f, -t * width * 0.5f + width * 0.15f);
                Add(Matrix4x4.TRS(at, Quaternion.AngleAxis(side * t * 0.35f * Mathf.Rad2Deg, Vector3.up), scale), UnitBlob, 1f,
                    (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, 0, 0.1f, random, paint), i % 2 == 1 ? color : color2,
                    CreatureLayer.Body, ElementKind.Shape, index);
            }
            return index;
        }

        /// <summary>Точки роста ледяных кристаллов (матёрый и древний): место и направление.</summary>
        public void Frost(params (Vector3 point, Vector3 direction)[] points) => frostPoints.AddRange(points);

        /// <summary>Достраивает облик уровня: иней по точкам Frost, холодный свет, трещины, лёд, изморозь, у древнего ядро и корона.</summary>
        public void Finish(int frostPer, float frostSize)
        {
            var random = new System.Random(seed * 7 + Level);
            var want = Level * frostPer;
            var count = Mathf.Min(want, frostPoints.Count * 3);
            for (var i = 0; i < count; i++)
            {
                var (point, direction) = frostPoints[i % frostPoints.Count];
                var at = point + new Vector3(Range(random, 0.12f), 0f, Range(random, 0.12f));
                var dir = direction + new Vector3(Range(random, 0.5f), Range(random, 0.3f), Range(random, 0.5f));
                var size = frostSize * (1f + Level * 0.25f) * (0.7f + (float)random.NextDouble() * 0.6f);
                Cone(at, dir, size * 0.45f, size * 2.6f, CreaturePalette.Frost, 4, CreatureLayer.Ice, ElementKind.Frost, -1);
            }
            if (Level >= 1)
                LightAt = new Vector3(0f, ModelBounds().max.y * 0.85f, 0f);
            LevelLook();
        }

        /// <summary>Границы всех деталей в координатах модели.</summary>
        public Bounds ModelBounds()
        {
            var bounds = elements.Count > 0 ? elements[0].Bounds : new Bounds();
            foreach (var element in elements)
                bounds.Encapsulate(element.Bounds);
            return bounds;
        }

        /// <summary>
        /// Сетки по частям рига и слоям. partOf даёт номер части детали (−1 — убрать); вершины части сдвинуты на
        /// −pivots[часть] (в координатах её группы). Пустые части — пустые сетки.
        /// </summary>
        public Dictionary<CreatureLayer, Mesh[]> Bake(string name, Func<CreatureElement, int> partOf, IReadOnlyList<Vector3> pivots)
        {
            var layers = new Dictionary<CreatureLayer, FacetedMesh>();
            foreach (var element in elements)
            {
                var part = partOf(element);
                if (part < 0)
                    continue;
                if (!layers.TryGetValue(element.Layer, out var mesh))
                    layers[element.Layer] = mesh = new FacetedMesh();
                mesh.Part = part;
                mesh.Transform = Matrix4x4.Translate(element.Offset) * element.Matrix;
                element.Shape(mesh, element.Paint);
            }
            var result = new Dictionary<CreatureLayer, Mesh[]>();
            foreach (var pair in layers)
                result[pair.Key] = pair.Value.ToMeshes($"{name} {pair.Key}", pivots);
            return result;
        }

        // ---------- облик уровней: туман забирает существо ----------
        // Матёрый: светящиеся трещины, ледяная броня сверху, изморозь под ногами.
        // Древний: ещё ледяное ядро, корона кристаллов, кружащие осколки, кольцо инея и тёмный круг на земле.
        private void LevelLook()
        {
            if (Level == 0)
                return;
            levelStage = true;
            var random = new System.Random(seed * 31 + Level * 7);
            var box = ModelBounds();
            var size = box.size;
            var center = box.center;
            var sz = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var reach = Mathf.Max(size.x, size.z) * 0.55f;
            // Крупные части несут трещины и лёд; у стайки мелких мотыльков их нет — остаются изморозь и древние добавки.
            var big = elements.FindAll(e => e.Kind == ElementKind.Shape && e.Sample != null && e.Extent > sz * 0.05f);

            (Vector3, Vector3)? SurfacePoint(bool wantUp)
            {
                for (var tries = 0; tries < 40 && big.Count > 0; tries++)
                {
                    var element = big[random.Next(big.Count)];
                    var (local, localNormal) = element.Sample(random);
                    var world = WorldMatrix(element);
                    var point = world.MultiplyPoint3x4(local);
                    var normal = world.inverse.transpose.MultiplyVector(localNormal).normalized;
                    if (point.y < size.y * 0.08f || (wantUp && normal.y < 0.45f))
                        continue;
                    return (point, normal);
                }
                return null;
            }

            // Светящиеся трещины: ломаные из трёх тонких палочек по поверхности.
            for (var i = 0; i < (Level == 1 ? 10 : 16); i++)
            {
                var hit = SurfacePoint(false);
                if (hit == null)
                    continue;
                var (point, normal) = hit.Value;
                var direction = Vector3.Cross(new Vector3(Range(random, 1f), Range(random, 1f), Range(random, 1f)), normal).normalized;
                for (var k = 0; k < 3; k++)
                {
                    var length = sz * (0.035f + (float)random.NextDouble() * 0.03f);
                    var stick = new Vector3(sz * 0.008f, length, sz * 0.008f);
                    Add(Matrix4x4.TRS(point + normal * (sz * 0.004f) + direction * (length * 0.5f), Quaternion.FromToRotation(Vector3.up, direction), Vector3.one),
                        new Bounds(Vector3.zero, stick), stick.x, (mesh, paint) => mesh.AddBox(Vector3.zero, stick, paint), CreaturePalette.Crack,
                        CreatureLayer.Glow, ElementKind.Crack, -1);
                    point += direction * length;
                    direction = Quaternion.AngleAxis(Range(random, 1.6f) * Mathf.Rad2Deg, normal) * direction;
                }
            }

            // Ледяная броня сверху: плоские пластины, на половине — сосулька.
            for (var i = 0; i < (Level == 1 ? 7 : 12); i++)
            {
                var hit = SurfacePoint(true);
                if (hit == null)
                    continue;
                var (point, normal) = hit.Value;
                var plateRandom = new System.Random(seed * 53 + counter++);
                var w = sz * (0.05f + (float)random.NextDouble() * 0.04f);
                var turn = Quaternion.FromToRotation(Vector3.up, normal);
                Add(Matrix4x4.TRS(point + normal * (w * 0.12f), turn, new Vector3(w, w * 0.3f, w * 0.8f)), UnitBlob, 1f,
                    (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, 0, 0.3f, plateRandom, paint), CreaturePalette.Ice,
                    CreatureLayer.Ice, ElementKind.Ice, -1);
                if (random.NextDouble() < 0.5)
                    Cone(point, normal, w * 0.25f, w * 1.2f, CreaturePalette.Ice, 4, CreatureLayer.Ice, ElementKind.Ice, -1);
            }

            // Изморозь на земле: светлый круг и ледяные иголки.
            var frostRadius = reach * (Level == 1 ? 1f : 1.15f);
            Disc(new Vector3(center.x, 0.015f, center.z), frostRadius, 18, CreaturePalette.FrostGround);
            for (var i = 0; i < (Level == 1 ? 8 : 14); i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var distance = reach * (0.4f + (float)random.NextDouble() * 0.7f);
                var height = sz * (0.03f + (float)random.NextDouble() * 0.04f);
                var tilt = EulerXYZ(new Vector3(Range(random, 0.6f), 0f, Range(random, 0.6f)));
                var middle = new Vector3(center.x + Mathf.Cos(angle) * distance, sz * 0.015f, center.z + Mathf.Sin(angle) * distance);
                var up = tilt * Vector3.up;
                Cone(middle - up * (height * 0.5f), up, sz * 0.012f, height, CreaturePalette.Ice, 4, CreatureLayer.Ice, ElementKind.Ground, -1);
            }
            if (Level < 2)
                return;

            // Древний: ледяное ядро внутри тела.
            var coreAt = new Vector3(center.x, box.min.y + size.y * 0.55f, center.z);
            Add(Matrix4x4.TRS(coreAt, Quaternion.identity, Vector3.one * (sz * 0.07f)), UnitBlob, 1f,
                (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, 0, 0f, null, paint), CreaturePalette.Core, CreatureLayer.Glow, ElementKind.Core, -1);
            // Корона кристаллов на макушке.
            var top = new Vector3(center.x, box.max.y, center.z);
            for (var i = 0; i < 7; i++)
            {
                var angle = i / 7f * Mathf.PI * 2f;
                var height = sz * (0.09f + i % 2 * 0.05f);
                var up = EulerXYZ(new Vector3(Mathf.Sin(angle) * 0.35f, 0f, -Mathf.Cos(angle) * 0.35f)) * Vector3.up;
                var middle = new Vector3(top.x + Mathf.Cos(angle) * sz * 0.06f, top.y - sz * 0.01f + height * 0.5f, top.z + Mathf.Sin(angle) * sz * 0.06f);
                Cone(middle - up * (height * 0.5f), up, sz * 0.02f, height, CreaturePalette.Ice, 4, CreatureLayer.Ice, ElementKind.Crown, -1);
            }
            // Осколки льда по кругу: риг крутит их вокруг тела.
            for (var i = 0; i < 9; i++)
            {
                var r = sz * (0.018f + (float)random.NextDouble() * 0.015f);
                var angle = i / 9f * Mathf.PI * 2f;
                var distance = reach * (0.95f + (float)random.NextDouble() * 0.2f);
                var at = new Vector3(center.x + Mathf.Cos(angle) * distance, box.min.y + size.y * (0.25f + (float)random.NextDouble() * 0.55f),
                    center.z + Mathf.Sin(angle) * distance);
                Add(Matrix4x4.TRS(at, Quaternion.identity, new Vector3(r, r * 1.4f, r)), UnitBlob, 1f,
                    (mesh, paint) => mesh.AddBlob(Vector3.zero, Vector3.one, 0, 0.2f, new System.Random(i + 5), paint), CreaturePalette.Ice,
                    CreatureLayer.Ice, ElementKind.Orbit, -1);
            }
            // Кольцо инея и тёмный круг: свет вокруг древнего слабеет.
            var ringRadius = reach * 1.18f;
            Add(Matrix4x4.TRS(new Vector3(center.x, 0.025f, center.z), Quaternion.AngleAxis(90f, Vector3.right), new Vector3(1f, 1f, 0.05f)),
                new Bounds(Vector3.zero, new Vector3(ringRadius * 2.2f, ringRadius * 2.2f, 0.01f)), ringRadius,
                (mesh, paint) => mesh.AddTorus(ringRadius, reach * 0.06f, 4, 32, paint), CreaturePalette.FrostRing, CreatureLayer.Ground, ElementKind.Ground, -1);
            Disc(new Vector3(center.x, 0.008f, center.z), reach * 1.5f, 32, CreaturePalette.DarkCircle);
            for (var i = 0; i < 12; i++)
            {
                var angle = i / 12f * Mathf.PI * 2f;
                Cone(new Vector3(center.x + Mathf.Cos(angle) * ringRadius, 0f, center.z + Mathf.Sin(angle) * ringRadius), Vector3.up,
                    sz * 0.015f, sz * 0.06f, CreaturePalette.Ice, 4, CreatureLayer.Ice, ElementKind.Ground, -1);
            }
        }

        // ---------- внутреннее ----------

        private CreatureElement Cone(Vector3 basePoint, Vector3 direction, float radius, float height, uint color, int sides,
            CreatureLayer layer, ElementKind kind, int wing) =>
            Add(Matrix4x4.TRS(basePoint, Quaternion.FromToRotation(Vector3.up, direction.normalized), Vector3.one),
                new Bounds(new Vector3(0f, height * 0.5f, 0f), new Vector3(radius * 2f, height, radius * 2f)), radius,
                (mesh, paint) => mesh.AddCone(sides, Vector3.zero, radius, height, 0f, null, paint), color, layer, kind, wing,
                r =>
                {
                    var t = (float)r.NextDouble() * 0.8f;
                    var angle = (float)r.NextDouble() * Mathf.PI * 2f;
                    var at = radius * (1f - t);
                    return (new Vector3(Mathf.Cos(angle) * at, t * height, Mathf.Sin(angle) * at),
                        new Vector3(Mathf.Cos(angle), radius / Mathf.Max(height, 1e-4f), Mathf.Sin(angle)));
                });

        // Плоский круг на земле (изморозь, тёмный круг).
        private void Disc(Vector3 center, float radius, int sides, uint color) =>
            Add(Matrix4x4.Translate(center), new Bounds(new Vector3(0f, 0.002f, 0f), new Vector3(radius * 2f, 0.004f, radius * 2f)), radius,
                (mesh, paint) => mesh.AddPrism(sides, Vector3.zero, radius, radius, 0.004f, 0f, null, paint), color, CreatureLayer.Ground,
                ElementKind.Ground, -1);

        private CreatureElement Add(Matrix4x4 matrix, Bounds local, float extent, Action<FacetedMesh, Func<Vector3, Color>> shape, uint color,
            CreatureLayer layer, ElementKind kind, int wing, Func<System.Random, (Vector3, Vector3)> sample = null)
        {
            if (wing >= 0)
                matrix = Matrix4x4.Translate(wings[wing].Root) * matrix;
            var element = new CreatureElement
            {
                Matrix = matrix,
                Shape = shape,
                Paint = Paint(levelStage ? CreaturePalette.Hex(color) : CreaturePalette.Tint(color, Level)),
                Sample = sample,
                Extent = extent * MaxScale(matrix),
                Kind = kind,
                Layer = layer,
                Group = group,
                Wing = wing,
            };
            var world = WorldMatrix(element);
            element.Center = world.MultiplyPoint3x4(local.center);
            element.Bounds = Transform(world, local);
            elements.Add(element);
            return element;
        }

        private Matrix4x4 WorldMatrix(CreatureElement element) => element.Group >= 0 ? groups[element.Group] * element.Matrix : element.Matrix;

        private static Bounds Transform(Matrix4x4 matrix, Bounds local)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(local.min), Vector3.zero);
            for (var i = 1; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }
            return result;
        }

        private static float MaxScale(Matrix4x4 matrix) =>
            Mathf.Max(matrix.GetColumn(0).magnitude, Mathf.Max(matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude));

        private static Func<Vector3, Color> Paint(Color color)
        {
            var linear = color.linear;
            linear.a = 0f;
            return _ => linear;
        }

        private static float Range(System.Random random, float span) => ((float)random.NextDouble() - 0.5f) * span;

        private static Vector3 RandomDirection(System.Random random)
        {
            for (;;)
            {
                var v = new Vector3(Range(random, 2f), Range(random, 2f), Range(random, 2f));
                var length = v.magnitude;
                if (length > 0.05f && length <= 1f)
                    return v / length;
            }
        }

        /// <summary>Поворот как у Euler XYZ прототипа (сначала Z, потом Y, потом X — в осях модели), углы в радианах.</summary>
        public static Quaternion EulerXYZ(Vector3 radians) =>
            Quaternion.AngleAxis(radians.x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(radians.y * Mathf.Rad2Deg, Vector3.up)
            * Quaternion.AngleAxis(radians.z * Mathf.Rad2Deg, Vector3.forward);
    }
}
