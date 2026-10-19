using System;
using System.Collections.Generic;
using Emberhold.World.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>Шаблоны материалов существ (шейдер Emberhold/World/Tree): непрозрачный и прозрачный, оба со свечением.</summary>
    [Serializable]
    public sealed class CreatureMaterials
    {
        public Material Opaque;
        public Material Transparent;
    }

    /// <summary>
    /// Поза существа, которую задаёт поведение: act — атака по роли (0..1), recoil — отдача от удара, flash — вспышка,
    /// fold — падение при смерти, fade — растворение, glow — жар внутри, vis — видимость тени, over — тело над стеной,
    /// latch — ползун присосался к огню.
    /// </summary>
    public sealed class CreaturePose
    {
        public float Act;
        public float Recoil;
        public float Flash;
        public float Fold;
        public float Fade;
        public float Glow;
        public float Vis = 1f;
        public float Over;
        public float Latch;
    }

    /// <summary>
    /// Риг существа: модель из <see cref="CreatureModels"/> разложена по частям с шарнирами (координаты модели), каждая
    /// часть — свой объект, её сетки по слоям материалов. Материалы свои у каждого экземпляра: их подсвечивают при ударе,
    /// растворяют при смерти и делают прозрачными. Наследники двигают части в <see cref="Animate"/> по позе
    /// <see cref="Pose"/>. Перенос прототипа: числа те же, сцена зеркальна ему (Unity — левая система координат).
    /// </summary>
    public abstract class CreatureRig
    {
        private static readonly Color FlashColor = CreaturePalette.Hex(CreaturePalette.Flash);

        private readonly List<Transform> parts = new();
        private readonly List<Vector3> rests = new();
        private readonly Dictionary<Transform, int> partIndex = new();
        private readonly Dictionary<CreatureLayer, RigMaterial> materials = new();
        private readonly HashSet<Mesh> meshes = new();
        private readonly Transform orbit;

        protected CreatureRig(CreatureKind kind, int level, CreatureMaterials templates)
        {
            Kind = kind;
            Level = level;
            Root = new GameObject(kind.ToString()).transform;
            Model = CreatureModels.Build(kind, level);
            Scale = CreaturePalette.LevelScale[level] * CreatureModels.SizeFactor(kind);
            Ground = Pivot(Root, Vector3.zero);
            orbit = Pivot(Root, Vector3.zero);
            CreateMaterials(templates);
        }

        public CreatureKind Kind { get; }
        public int Level { get; }
        public Transform Root { get; }
        public CreaturePose Pose { get; } = new();

        /// <summary>Масштаб корня: рост из таблицы × уровень.</summary>
        public float Scale { get; }

        /// <summary>Высота середины тела в координатах модели (для частиц удара).</summary>
        protected float CenterHeight { get; set; } = 1f;

        protected CreatureBuilder Model { get; }
        protected Transform Ground { get; }
        protected float Time { get; private set; }
        protected float Speed { get; set; }
        protected float Gait { get; set; }

        public static CreatureRig Create(CreatureKind kind, int level, CreatureMaterials templates) => kind switch
        {
            CreatureKind.Crawler => new CrawlerRig(level, templates),
            CreatureKind.Brute => new BruteRig(level, templates),
            CreatureKind.Shade => new ShadeRig(level, templates),
            CreatureKind.Moths => new MothsRig(level, templates),
            CreatureKind.Mistmother => new MistmotherRig(level, templates),
            _ => new StiltRig(level, templates),
        };

        /// <summary>Кадр: speed — скорость бега в единицах модели в секунду (0 — стоит).</summary>
        public void Tick(float dt, float speed)
        {
            Time += dt;
            Animate(dt, speed);
            orbit.localRotation = Quaternion.AngleAxis(Time * 0.6f * Mathf.Rad2Deg, Vector3.up);
            ApplyFx();
        }

        /// <summary>Меняет собственное свечение слоя (жар в брюхе ползуна): вспышка удара ложится поверх.</summary>
        protected void SetLayerEmission(CreatureLayer layer, Color emission, float intensity)
        {
            var material = materials[layer];
            material.Emission = emission;
            material.Intensity = intensity;
        }

        /// <summary>Середина тела в мире: сюда летят частицы удара.</summary>
        public virtual Vector3 Center => Root.TransformPoint(new Vector3(0f, CenterHeight, 0f));

        public void Destroy()
        {
            foreach (var mesh in meshes)
                Release(mesh);
            foreach (var material in materials.Values)
                Release(material.Material);
            Release(Root.gameObject);
        }

        // В игре — Destroy, в редакторе вне игры (тесты) — DestroyImmediate.
        private static void Release(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        protected abstract void Animate(float dt, float speed);

        // ---------- части ----------

        /// <summary>Шарнир в точке at (координаты модели); его положение хранится относительно шарнира-родителя.</summary>
        protected Transform Pivot(Transform parent, Vector3 at)
        {
            var pivot = new GameObject("Part").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at - Rest(parent);
            Register(pivot, at);
            return pivot;
        }

        /// <summary>Часть со своей системой координат (мотылёк стайки): rest — шарнир в координатах её группы.</summary>
        protected void Register(Transform part, Vector3 rest)
        {
            partIndex[part] = parts.Count;
            parts.Add(part);
            rests.Add(rest);
        }

        protected Vector3 Rest(Transform part) => partIndex.TryGetValue(part, out var index) ? rests[index] : Vector3.zero;

        /// <summary>
        /// Раскладывает детали модели по шарнирам: classify(деталь) → шарнир или null (убрать). Изморозь на земле — в Ground,
        /// кружащие осколки древнего — в своё кольцо. Сетки частей встают в свои шарниры.
        /// </summary>
        protected void BuildModel(Func<CreatureElement, Transform> classify)
        {
            var baked = Model.Bake(Kind.ToString(), element =>
            {
                if (element.Kind == ElementKind.Ground)
                    return partIndex[Ground];
                if (element.Kind == ElementKind.Orbit)
                    return partIndex[orbit];
                var part = classify(element);
                return part == null ? -1 : partIndex[part];
            }, rests);
            foreach (var pair in baked)
            {
                var (layer, partMeshes) = (pair.Key, pair.Value);
                for (var i = 0; i < partMeshes.Length; i++)
                {
                    if (partMeshes[i].vertexCount == 0)
                    {
                        Release(partMeshes[i]);
                        continue;
                    }
                    AddRenderer(parts[i], partMeshes[i], layer);
                }
            }
            if (Model.LightAt is { } lightAt)
                AddLight(Root, lightAt, CreaturePalette.Hex(CreaturePalette.Crack), 0.4f + 0.4f * Level, (2f + 1.5f * Level) * Scale);
            Root.localScale = Vector3.one * Scale;
        }

        /// <summary>Отдельная деталь (сегмент ноги, коготь) прямо под корнем: риг ставит её сам каждый кадр.</summary>
        protected Transform Piece(Mesh mesh, CreatureLayer layer = CreatureLayer.Body)
        {
            var piece = new GameObject("Piece").transform;
            piece.SetParent(Root, false);
            AddRenderer(piece, mesh, layer);
            return piece;
        }

        protected Light AddLight(Transform parent, Vector3 at, Color color, float intensity, float range)
        {
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = at - Rest(parent);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>Точка модели at, закреплённая на шарнире part, в мире.</summary>
        public Vector3 World(Transform part, Vector3 at) => part.TransformPoint(at - Rest(part));

        // ---------- сетки деталей рига ----------

        /// <summary>Сегмент конечности: усечённый конус высотой 1 от начала координат вверх (масштаб по Y — длина).</summary>
        protected Mesh SegmentMesh(float bottom, float top, int sides, uint color) =>
            Faceted(mesh => mesh.AddPrism(sides, Vector3.zero, bottom, top, 1f, 0f, null, Paint(color)));

        /// <summary>Конус с серединой в начале координат (как у прототипа), вершина в +Y.</summary>
        protected Mesh ConeMesh(float radius, float height, int sides, uint color) =>
            Faceted(mesh => mesh.AddCone(sides, new Vector3(0f, -height * 0.5f, 0f), radius, height, 0f, null, Paint(color)));

        /// <summary>Гранёный шарик (сустав).</summary>
        protected Mesh BallMesh(float radius, uint color) =>
            Faceted(mesh => mesh.AddBlob(Vector3.zero, Vector3.one * radius, 0, 0f, null, Paint(color)));

        /// <summary>Ставит сегмент от a до b (координаты корня).</summary>
        protected static void Place(Transform segment, Vector3 a, Vector3 b)
        {
            var d = b - a;
            segment.localPosition = a;
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, d);
            segment.localScale = new Vector3(1f, Mathf.Max(d.magnitude, 1e-4f), 1f);
        }

        /// <summary>Поворот как rotation.set(x, y, z) прототипа (радианы, порядок XYZ).</summary>
        protected static Quaternion Rot(float x, float y, float z) => CreatureBuilder.EulerXYZ(new Vector3(x, y, z));

        protected static float Ease(float k) => k <= 0f ? 0f : k >= 1f ? 1f : k * k * (3f - 2f * k);

        protected static float Segment(float k, float a, float b) => Ease((k - a) / (b - a));

        private Mesh Faceted(Action<FacetedMesh> build)
        {
            var faceted = new FacetedMesh();
            build(faceted);
            var mesh = faceted.ToMesh($"{Kind} piece");
            meshes.Add(mesh);
            return mesh;
        }

        private Func<Vector3, Color> Paint(uint color)
        {
            var linear = CreaturePalette.Tint(color, Level).linear;
            linear.a = 0f;
            return _ => linear;
        }

        private void AddRenderer(Transform parent, Mesh mesh, CreatureLayer layer)
        {
            meshes.Add(mesh);
            var holder = new GameObject(layer.ToString());
            holder.transform.SetParent(parent, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials[layer].Material;
            renderer.shadowCastingMode = layer == CreatureLayer.Ground ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        // ---------- материалы и эффекты ----------

        private sealed class RigMaterial
        {
            public Material Material;
            public Color Emission;
            public float Intensity;
            public float Opacity;
            public bool Transparent;
            public bool? AppliedTransparent;
            public bool AppliedZWrite;
        }

        private void CreateMaterials(CreatureMaterials templates)
        {
            var ancient = Level == 2;
            var levelEmission = CreaturePalette.LevelEmission(Level);
            Add(CreatureLayer.Body, levelEmission, 1f, ancient ? 0.6f : 1f);
            Add(CreatureLayer.Heat, CreaturePalette.Hex(0x5a86a8), 0.4f + 0.2f * Level, ancient ? 0.6f : 1f);
            Add(CreatureLayer.Glow, CreaturePalette.Hex(CreaturePalette.CrackGlow), 0.7f + 0.4f * Level, 1f);
            Add(CreatureLayer.Glass, CreaturePalette.Hex(0x4a6a8a), 0.35f + 0.2f * Level, 0.55f);
            Add(CreatureLayer.Ice, CreaturePalette.Hex(CreaturePalette.IceGlow), Level == 2 ? 0.7f : 0.35f, 0.85f);
            Add(CreatureLayer.Ground, Color.black, 1f, 0.45f);

            void Add(CreatureLayer layer, Color emission, float intensity, float opacity)
            {
                var transparent = opacity < 0.999f;
                var material = new Material(transparent ? templates.Transparent : templates.Opaque) { name = $"{Kind} {layer}" };
                materials[layer] = new RigMaterial
                {
                    Material = material, Emission = emission, Intensity = intensity, Opacity = opacity, Transparent = transparent,
                };
            }
        }

        // Вспышка от удара, растворение при смерти, видимость тени.
        private void ApplyFx()
        {
            var pose = Pose;
            var opacity = (1f - pose.Fade) * (0.1f + 0.9f * pose.Vis);
            foreach (var m in materials.Values)
            {
                m.Material.SetColor(EmissionColor, Color.Lerp(m.Emission, FlashColor, pose.Flash) * (m.Intensity + pose.Flash * 1.5f));
                var transparent = m.Transparent || opacity < 0.999f;
                var zWrite = opacity > 0.7f && (!m.Transparent || m.Opacity > 0.7f);
                if (m.AppliedTransparent != transparent || m.AppliedZWrite != zWrite)
                {
                    SetSurface(m.Material, transparent, zWrite);
                    m.AppliedTransparent = transparent;
                    m.AppliedZWrite = zWrite;
                }
                m.Material.SetColor(BaseColor, new Color(1f, 1f, 1f, m.Opacity * opacity));
            }
        }

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Переключает материал Lit между непрозрачным и прозрачным (смешивание по альфе).</summary>
        private static void SetSurface(Material material, bool transparent, bool zWrite)
        {
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", zWrite ? 1f : 0f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            material.renderQueue = (int)(transparent ? RenderQueue.Transparent : RenderQueue.Geometry);
            if (transparent)
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
        }
    }
}
