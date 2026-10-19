using System.Collections.Generic;
using Emberhold.World.Meshing;
using UnityEngine;

namespace Emberhold.Gameplay.Enemies.Bestiary
{
    /// <summary>
    /// Реквизит тестовой сцены «Бестиарий»: земля, костёр с топливом, стена из блоков, фонарь на столбе и частицы
    /// (искры жара, клубы тумана и дыма, обломки стены). Всё строится в коде гранёными сетками с цветом вершин.
    /// </summary>
    public sealed class BestiaryProps
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Transform root;
        private readonly Material solid;
        private readonly Material ember;
        private readonly Material flame;
        private readonly Material glass;
        private readonly Material puff;
        private readonly Material spark;
        private readonly Mesh quad;
        private readonly Mesh sparkMesh;
        private readonly Transform[] flames = new Transform[3];
        private readonly List<PuffState> puffs = new();
        private readonly List<SparkState> sparks = new();
        private readonly List<DebrisState> debris = new();
        private readonly MaterialPropertyBlock block = new();
        private readonly Transform staged;

        public BestiaryProps(Transform root, CreatureMaterials templates)
        {
            this.root = root;
            solid = Instance(templates.Opaque, "Prop", Color.black, 1f);
            ember = Instance(templates.Opaque, "Ember", CreaturePalette.Hex(0xff5a1a), 1f);
            flame = Instance(templates.Transparent, "Flame", CreaturePalette.Hex(0xffb347) * 1.5f, 0.9f);
            glass = Instance(templates.Opaque, "Lamp glass", CreaturePalette.Hex(0xffb24a), 1f);
            spark = Instance(templates.Opaque, "Spark", CreaturePalette.Hex(0xffa040) * 2f, 1f);
            puff = Instance(templates.Transparent, "Puff", Color.black, 1f);
            puff.SetTexture("_BaseMap", RadialTexture());
            quad = Quad();
            sparkMesh = Faceted(m => m.AddBlob(Vector3.zero, Vector3.one * 0.025f, 0, 0f, null, Paint(0xffa040)));
            BuildGround();
            BuildFire();
            staged = new GameObject("Staged").transform;
            staged.SetParent(root, false);
            Fire = new GameObject("Fire light").AddComponent<Light>();
            Fire.transform.SetParent(root, false);
            Fire.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Fire.type = LightType.Point;
            Fire.color = CreaturePalette.Hex(0xff9a3c);
            Fire.range = 12f;
            Fire.shadows = LightShadows.Soft;
        }

        /// <summary>Свет костра.</summary>
        public Light Fire { get; }

        /// <summary>Стена из блоков на пути врага (громила ломает, ходуля перешагивает); null — нет стены.</summary>
        public Wall CurrentWall { get; private set; }

        /// <summary>Фонарь на столбе (тень обходит, мотыльки облепляют); null — нет фонаря.</summary>
        public Lamp CurrentLamp { get; private set; }

        // ---------- костёр ----------

        /// <summary>Пламя по топливу 0..1; choke — огонь задыхается от дыма тени.</summary>
        public void UpdateFire(float fuel, float choke, float time, bool night)
        {
            for (var i = 0; i < flames.Length; i++)
            {
                var s = Mathf.Max(0.02f, fuel) * (1f - choke * 0.35f * Mathf.Abs(Mathf.Sin(time * (11f + i * 3f))));
                flames[i].localScale = new Vector3(s, s * (1f + Mathf.Sin(time * 9f + i * 2f) * 0.15f), s);
                flames[i].gameObject.SetActive(fuel > 0.02f);
            }
            ember.SetColor(EmissionColorId, CreaturePalette.Hex(0xff5a1a) * (0.3f + fuel * 0.9f));
            Fire.intensity = (night ? 4f : 1f) * (0.08f + 0.92f * fuel) * (1f + Mathf.Sin(time * 13f) * 0.06f);
            if (CurrentLamp != null)
            {
                CurrentLamp.Light.intensity = (night ? 6f : 1.5f) * CurrentLamp.Dim * (1f + Mathf.Sin(time * 11f) * 0.04f);
                glass.SetColor(EmissionColorId, CreaturePalette.Hex(0xffb24a) * (0.2f + 1.2f * CurrentLamp.Dim));
            }
        }

        private void BuildGround()
        {
            const int cells = 60;
            const float size = 60f;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            Color light = CreaturePalette.Hex(0x6f7a3c), dark = CreaturePalette.Hex(0x56602f);
            for (var z = 0; z <= cells; z++)
            for (var x = 0; x <= cells; x++)
            {
                var p = new Vector3(x / (float)cells * size - size * 0.5f, 0f, z / (float)cells * size - size * 0.5f);
                var n = Mathf.Sin(p.x * 0.7f) * Mathf.Cos(p.z * 0.6f) * 0.5f + Mathf.Sin(p.x * 0.23f + p.z * 0.31f) * 0.5f;
                var c = Color.Lerp(light, dark, Mathf.Clamp01(n * 0.6f + 0.3f)).linear;
                c.a = 0f;
                vertices.Add(p);
                colors.Add(c);
            }
            for (var z = 0; z < cells; z++)
            for (var x = 0; x < cells; x++)
            {
                var i = z * (cells + 1) + x;
                triangles.AddRange(new[] { i, i + cells + 1, i + 1, i + 1, i + cells + 1, i + cells + 2 });
            }
            var mesh = new Mesh { name = "Bestiary ground" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Add(root, "Ground", mesh, solid, Vector3.zero, Quaternion.identity).GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildFire()
        {
            var fire = new GameObject("Fire").transform;
            fire.SetParent(root, false);
            var log = Faceted(m => m.AddPrism(6, new Vector3(0f, -0.45f, 0f), 0.11f, 0.09f, 0.9f, 0f, null, Paint(0x5a3b28)));
            var hot = Faceted(m => m.AddPrism(6, new Vector3(0f, -0.45f, 0f), 0.11f, 0.09f, 0.9f, 0f, null, Paint(0x3a1a0c)));
            for (var i = 0; i < 4; i++)
            {
                var rotation = Quaternion.AngleAxis(i * 90f, Vector3.up) * Quaternion.AngleAxis((Mathf.PI / 2f - 0.35f) * Mathf.Rad2Deg, Vector3.forward);
                Add(fire, "Log", i % 2 == 1 ? log : hot, i % 2 == 1 ? solid : ember, new Vector3(0f, 0.18f, 0f), rotation);
            }
            var stone = Faceted(m => m.AddBlob(Vector3.zero, Vector3.one * 0.14f, 0, 0.2f, new System.Random(3), Paint(0x8d9298)));
            for (var i = 0; i < 8; i++)
            {
                var a = i / 8f * Mathf.PI * 2f;
                Add(fire, "Stone", stone, solid, new Vector3(Mathf.Cos(a) * 0.55f, 0.08f, Mathf.Sin(a) * 0.55f), Quaternion.identity);
            }
            for (var i = 0; i < 3; i++)
            {
                var r = 0.22f - i * 0.05f;
                var h = 0.7f - i * 0.12f;
                var cone = Faceted(m => m.AddCone(5, new Vector3(0f, -h * 0.5f, 0f), r, h, 0f, null, Paint(0xffb347)));
                flames[i] = Add(fire, "Flame", cone, flame, new Vector3((i - 1) * 0.1f, 0.45f, i % 2 * 0.08f), Quaternion.identity).transform;
                flames[i].GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---------- стена и фонарь ----------

        /// <summary>Убирает стену, фонарь и обломки: сцена ставит реквизит заново.</summary>
        public void ClearStaged()
        {
            for (var i = staged.childCount - 1; i >= 0; i--)
                Object.Destroy(staged.GetChild(i).gameObject);
            debris.Clear();
            CurrentWall = null;
            CurrentLamp = null;
        }

        /// <summary>Стена width × height блоков поперёк пути, середина в center, длинной стороной поперёк направления yaw (радианы).</summary>
        public Wall MakeWall(Vector3 center, float yaw, int width, int height)
        {
            var group = new GameObject("Wall").transform;
            group.SetParent(staged, false);
            group.position = center;
            group.rotation = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up);
            var colors = new uint[] { 0x7c6a55, 0x6b5b48, 0x8a8f94 };
            var meshes = new Mesh[3];
            for (var i = 0; i < 3; i++)
            {
                var color = colors[i];
                meshes[i] = Faceted(m => m.AddBox(Vector3.zero, Vector3.one * 0.98f, Paint(color)));
            }
            var wall = new Wall(group, width);
            for (var u = 0; u < width; u++)
            for (var y = 0; y < height; y++)
            {
                var piece = Add(group, "Block", meshes[(u + y) % 3], solid, new Vector3(u - (width - 1) * 0.5f, y + 0.5f, 0f), Quaternion.identity);
                wall.Blocks.Add(new Wall.Block(piece.transform, u, y));
            }
            CurrentWall = wall;
            return wall;
        }

        /// <summary>Ломает блоки стены в радиусе от точки удара (или все): они разлетаются назад по пути.</summary>
        public void BreakBlocks(Vector3 at, float radius, bool all, Vector3 path, Vector3 lateral)
        {
            foreach (var b in CurrentWall.Blocks)
            {
                if (!b.Alive)
                    continue;
                var p = b.Transform.position;
                if (!all && Vector3.Distance(p, at) > radius)
                    continue;
                b.Alive = false;
                b.Transform.SetParent(staged, true);
                var v = -path * (1f + Random.value * 1.5f) + lateral * ((Random.value - 0.5f) * 2.5f);
                v.y = 1.5f + Random.value * 2.5f;
                debris.Add(new DebrisState { Transform = b.Transform, Velocity = v, Spin = Random.insideUnitSphere * 3f });
            }
        }

        public Lamp MakeLamp(Vector3 position)
        {
            var group = new GameObject("Lamp").transform;
            group.SetParent(staged, false);
            group.position = position;
            Add(group, "Post", Faceted(m => m.AddPrism(6, Vector3.zero, 0.08f, 0.06f, 2.3f, 0f, null, Paint(0x4a3626))), solid, Vector3.zero, Quaternion.identity);
            Add(group, "Arm", Faceted(m => m.AddBox(Vector3.zero, new Vector3(0.5f, 0.06f, 0.06f), Paint(0x4a3626))), solid, new Vector3(0.22f, 2.25f, 0f),
                Quaternion.identity);
            var lantern = Add(group, "Glass", Faceted(m => m.AddBlob(Vector3.zero, new Vector3(0.17f, 0.22f, 0.17f), 0, 0f, null, Paint(0xffe2a0))), glass,
                new Vector3(0.42f, 2.02f, 0f), Quaternion.identity);
            Add(group, "Cap", Faceted(m => m.AddCone(4, new Vector3(0f, -0.06f, 0f), 0.16f, 0.12f, 0f, null, Paint(0x2e3338))), solid, new Vector3(0.42f, 2.28f, 0f),
                Quaternion.identity);
            var light = new GameObject("Lamp light").AddComponent<Light>();
            light.transform.SetParent(group, false);
            light.transform.localPosition = new Vector3(0.42f, 1.95f, 0f);
            light.type = LightType.Point;
            light.color = CreaturePalette.Hex(0xffb45a);
            light.range = 9f;
            CurrentLamp = new Lamp(light, lantern.transform.position, position);
            return CurrentLamp;
        }

        // ---------- частицы ----------

        /// <summary>Искра жара летит по дуге от from к движущейся цели to.</summary>
        public void Spark(Vector3 from, System.Func<Vector3> to)
        {
            var piece = Add(root, "Spark", sparkMesh, spark, from, Quaternion.identity);
            piece.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparks.Add(new SparkState { Transform = piece.transform, From = from, To = to, Arc = 0.2f + Random.value * 0.25f, Side = (Random.value - 0.5f) * 0.3f });
        }

        /// <summary>Мягкий клуб тумана или дыма: растёт, поднимается и тает.</summary>
        public void Puff(Vector3 at, float size, Vector3? velocity, Color color, float life = 1.6f, float opacity = 0.35f, float grow = 1.2f, float glow = 0.35f)
        {
            var piece = Add(root, "Puff", quad, puff, at, Quaternion.identity);
            var renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            puffs.Add(new PuffState
            {
                Transform = piece.transform, Renderer = renderer, Life = life, Opacity = opacity, Size = size, Grow = grow, Color = color, Glow = glow,
                Velocity = velocity ?? new Vector3((Random.value - 0.5f) * 0.3f, 0.35f + Random.value * 0.3f, (Random.value - 0.5f) * 0.3f),
            });
        }

        public void Step(float dt, Quaternion facing)
        {
            for (var i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.T += dt * 1.8f;
                var p = Vector3.Lerp(s.From, s.To(), s.T);
                p.y += Mathf.Sin(s.T * Mathf.PI) * s.Arc;
                p.x += Mathf.Sin(s.T * Mathf.PI) * s.Side;
                s.Transform.position = p;
                s.Transform.localScale = Vector3.one * (1f - s.T * 0.5f);
                if (s.T >= 1f)
                {
                    Object.Destroy(s.Transform.gameObject);
                    sparks.RemoveAt(i);
                }
            }
            for (var i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i];
                p.T += dt;
                p.Transform.position += p.Velocity * dt;
                p.Transform.localScale = Vector3.one * (p.Size * 2f * (1f + p.T * p.Grow));
                var alpha = p.Opacity * Mathf.Min(1f, p.T * 5f) * (1f - p.T / p.Life);
                block.SetColor(BaseColorId, new Color(p.Color.r, p.Color.g, p.Color.b, Mathf.Max(0f, alpha)));
                block.SetColor(EmissionColorId, p.Color * (p.Glow * Mathf.Max(0f, alpha)));
                p.Renderer.SetPropertyBlock(block);
                if (p.T > p.Life)
                {
                    Object.Destroy(p.Transform.gameObject);
                    puffs.RemoveAt(i);
                }
            }
            for (var i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                d.T += dt;
                d.Velocity.y -= 9.8f * dt;
                var position = d.Transform.position + d.Velocity * dt;
                if (position.y < 0.45f)
                {
                    position.y = 0.45f;
                    d.Velocity *= 0.35f;
                    d.Velocity.y = Mathf.Abs(d.Velocity.y) * 0.3f;
                    d.Spin *= 0.5f;
                }
                d.Transform.position = position;
                d.Transform.rotation = Quaternion.Euler(d.Spin.x * dt * Mathf.Rad2Deg, 0f, d.Spin.z * dt * Mathf.Rad2Deg) * d.Transform.rotation;
                if (d.T > 2.5f)
                    d.Transform.localScale = Vector3.one * Mathf.Max(0.01f, 1f - (d.T - 2.5f) * 1.5f);
                if (d.T > 3.2f)
                {
                    Object.Destroy(d.Transform.gameObject);
                    debris.RemoveAt(i);
                }
            }
            foreach (var p in puffs)
                p.Transform.rotation = facing;
        }

        // ---------- внутреннее ----------

        private static Material Instance(Material template, string name, Color emission, float opacity)
        {
            var material = new Material(template) { name = name };
            material.SetColor(EmissionColorId, emission);
            material.SetColor(BaseColorId, new Color(1f, 1f, 1f, opacity));
            material.EnableKeyword("_EMISSION");
            return material;
        }

        private static GameObject Add(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static Mesh Faceted(System.Action<FacetedMesh> build)
        {
            var mesh = new FacetedMesh();
            build(mesh);
            return mesh.ToMesh("Bestiary prop");
        }

        private static System.Func<Vector3, Color> Paint(uint hex)
        {
            var c = CreaturePalette.Hex(hex).linear;
            c.a = 0f;
            return _ => c;
        }

        // Квадрат лицом к камере (−Z), белый с a = 1: цвет и прозрачность клубу даёт _BaseColor.
        private static Mesh Quad()
        {
            var mesh = new Mesh { name = "Puff" };
            mesh.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
            mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D RadialTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Puff" };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Min(1f, new Vector2(x - size * 0.5f + 0.5f, y - size * 0.5f + 0.5f).magnitude / (size * 0.5f));
                var a = d < 0.5f ? Mathf.Lerp(1f, 0.45f, d / 0.5f) : Mathf.Lerp(0.45f, 0f, (d - 0.5f) / 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            texture.Apply();
            return texture;
        }

        private sealed class PuffState
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Velocity;
            public Color Color;
            public float T;
            public float Life;
            public float Opacity;
            public float Size;
            public float Grow;
            public float Glow;
        }

        private sealed class SparkState
        {
            public Transform Transform;
            public Vector3 From;
            public System.Func<Vector3> To;
            public float T;
            public float Arc;
            public float Side;
        }

        private sealed class DebrisState
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float T;
        }

        /// <summary>Стена из блоков: высота над точкой мира по живым блокам её столбца.</summary>
        public sealed class Wall
        {
            private readonly Transform group;
            private readonly int width;

            public Wall(Transform group, int width)
            {
                this.group = group;
                this.width = width;
            }

            public List<Block> Blocks { get; } = new();
            public Vector3 Center => group.position;

            public int Alive => Blocks.FindAll(b => b.Alive).Count;

            public float HeightAt(Vector3 point)
            {
                var local = group.InverseTransformPoint(point);
                if (Mathf.Abs(local.z) > 0.75f || Mathf.Abs(local.x) > width * 0.5f + 0.25f)
                    return 0f;
                var u = Mathf.RoundToInt(local.x + (width - 1) * 0.5f);
                var height = 0f;
                foreach (var b in Blocks)
                    if (b.Alive && b.U == u && b.Y + 1 > height)
                        height = b.Y + 1;
                return height;
            }

            public sealed class Block
            {
                public Block(Transform transform, int u, int y)
                {
                    Transform = transform;
                    U = u;
                    Y = y;
                }

                public Transform Transform { get; }
                public int U { get; }
                public int Y { get; }
                public bool Alive { get; set; } = true;
            }
        }

        /// <summary>Фонарь: свет, верх (стекло) и основание столба; dim — насколько светит (мотыльки гасят).</summary>
        public sealed class Lamp
        {
            public Lamp(Light light, Vector3 top, Vector3 basePoint)
            {
                Light = light;
                Top = top;
                Base = basePoint;
            }

            public Light Light { get; }
            public Vector3 Top { get; }
            public Vector3 Base { get; }
            public float Dim { get; set; } = 1f;
        }
    }
}
