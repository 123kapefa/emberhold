using System;
using System.Collections.Generic;
using System.Linq;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>
    /// Ничто ни во что не залезает: во всех ролях, анимациях (и переходах между ними, какие бывают в игре), с любой ношей
    /// ни одна часть рабочего, его снаряжение или ноша не входят внутрь другой части и не проходят сквозь пелерину и подол.
    /// Носят только свободные рабочие, у ролей ноши нет.
    /// Жёсткие части проверяются по сеткам «внутри/снаружи» в их собственной системе; ткань — по тому, с какой стороны
    /// от неё точка: под тканью в покое — под ней и в движении. Вершины, которые уже в покое внутри (руки начинаются
    /// в плечах), и вершины у самих суставов не считаются. Касание до <see cref="Contact"/> вершин — не прорыв.
    /// </summary>
    public sealed class WorkerCollisionTests
    {
        private const float Height = 1.9f;

        /// <summary>Клетка сетки «внутри/снаружи», блоки; глубже одной клетки — уже внутри.</summary>
        private const float Cell = 0.006f;

        /// <summary>Столько вершин на одну пару частей в кадре ещё касание, а не прорыв.</summary>
        private const int Contact = 3;

        private static readonly WorkerCarry[] Loads = { WorkerCarry.None, WorkerCarry.Log, WorkerCarry.StoneBasket, WorkerCarry.GrassBasket };

        [Test]
        public void EveryRoleClipAndLoad_NothingGoesThroughAnything()
        {
            var failures = new List<string>();
            foreach (WorkerRole role in Enum.GetValues(typeof(WorkerRole)))
                CheckRole(role, failures);
            Assert.That(failures, Is.Empty, $"Прорывов: {failures.Count}\n" + string.Join("\n", failures.Take(40)));
        }

        private static void CheckRole(WorkerRole role, List<string> failures)
        {
            var look = new WorkerLook(new Color(0.9f, 0.75f, 0.6f), new Color(0.23f, 0.16f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                new Color(0.5f, 0.15f, 0.15f), true, role);
            var parts = WorkerMeshBuilder.BuildParts(look, Height);
            var loads = new Dictionary<WorkerCarry, (Mesh mesh, WorkerCarryBuilder.Shape shape)>();
            try
            {
                var body = new Body(parts, role);
                foreach (var carry in Loads.Skip(1).Append(WorkerCarry.Billet))
                {
                    var mesh = WorkerCarryBuilder.Build(carry, Height);
                    loads[carry] = (mesh, new WorkerCarryBuilder.Shape(mesh, carry, Height));
                }

                void Check(string label, WorkerPose pose, WorkerCarry carry)
                {
                    foreach (var (what, count) in body.Collisions(pose, carry == WorkerCarry.None ? default : loads[carry], carry))
                    {
                        if (count > Contact)
                            failures.Add($"{role} {label} {carry}: {what} — {count} вершин");
                    }
                }

                var carried = role == WorkerRole.Free ? Loads : new[] { WorkerCarry.None };
                // Рабочая анимация — только у своей роли: в руке нужный инструмент; у истопника в работе ещё полено.
                bool Foreign(WorkerClip clip) => WorkerAnimation.RoleOf(clip) is { } owner && owner != role;
                WorkerCarry[] LoadsIn(WorkerClip clip) => WorkerAnimation.RoleOf(clip) == null ? carried
                    : new[] { clip == WorkerClip.Stoke ? WorkerCarry.Billet : WorkerCarry.None };
                foreach (WorkerClip clip in Enum.GetValues(typeof(WorkerClip)))
                foreach (var carry in Foreign(clip) ? new WorkerCarry[0] : LoadsIn(clip))
                foreach (var speed in clip == WorkerClip.Carry ? new[] { 0f, 2.5f } : new[] { 2.5f })
                {
                    // Ноша видна в любой анимации, пока рабочий что-то несёт.
                    if (carry == WorkerCarry.None && clip == WorkerClip.Carry)
                        continue;
                    var motion = WorkerMotion.For(look, Height, speed, 0.3f, carry);
                    var oneShot = WorkerAnimation.IsOneShot(clip);
                    var cycle = WorkerAnimation.WorkCycle(clip);
                    var count = clip == WorkerClip.Idle ? 29 : cycle > 0f ? 25 : 11;
                    for (var k = 0; k < count; k++)
                    {
                        // Стойка — 7 секунд, чтобы попали «живые» вставки: оглянуться, переступить. Работа — весь цикл, чаще:
                        // удары быстрые.
                        var time = oneShot ? k / 10f : clip == WorkerClip.Idle ? k * 0.25f : cycle > 0f ? cycle * k / 24f : k * 0.13f;
                        Check($"{clip} {time:0.00}", WorkerAnimation.Sample(clip, time, motion), carry);
                    }
                }

                // Переходы между анимациями, какие бывают в WorkerCrew: садятся только греясь, сидя ношу не берут и не строят.
                foreach (var carry in carried)
                foreach (WorkerClip from in Enum.GetValues(typeof(WorkerClip)))
                foreach (WorkerClip to in Enum.GetValues(typeof(WorkerClip)))
                {
                    if (from == to || (to == WorkerClip.Sit && from != WorkerClip.Warm) ||
                        (from == WorkerClip.Sit && to is WorkerClip.PickUp or WorkerClip.Put or WorkerClip.Place))
                        continue;
                    // Работа роли — из стойки или ходьбы и обратно, без ноши (у истопника — с поленом).
                    var work = WorkerAnimation.RoleOf(from) != null ? from : WorkerAnimation.RoleOf(to) != null ? to : (WorkerClip?)null;
                    if (work is { } w && (Foreign(w) || (from == w ? to : from) is not (WorkerClip.Idle or WorkerClip.Walk) ||
                                          carry != LoadsIn(w)[0] && carry != WorkerCarry.None))
                        continue;
                    var motion = WorkerMotion.For(look, Height, 2.5f, 0.3f, carry);
                    var a = WorkerAnimation.Sample(from, WorkerAnimation.IsOneShot(from) ? 1f : 0.4f, motion);
                    var b = WorkerAnimation.Sample(to, 0f, motion);
                    Check($"{from}→{to}", WorkerAnimation.Blend(a, b, 0.5f, Height), carry);
                }
            }
            finally
            {
                foreach (var mesh in parts)
                    Object.DestroyImmediate(mesh);
                foreach (var load in loads.Values)
                    Object.DestroyImmediate(load.mesh);
            }
        }

        /// <summary>Рабочий одной роли: сетки «внутри/снаружи» жёстких частей, ткань и что где было в покое.</summary>
        private sealed class Body
        {
            private readonly int[] rigid;
            private readonly Grid[] grids = new Grid[WorkerPose.PartCount];
            private readonly Vector3[][] vertices = new Vector3[WorkerPose.PartCount][];
            private readonly WorkerSkin[] cloth;
            private readonly Shell[] shells;
            private readonly Dictionary<(int, int), bool[]> insideAtRest = new();
            private readonly Dictionary<(int, int), int[]> sideAtRest = new();
            private readonly Matrix4x4[] matrices = new Matrix4x4[WorkerPose.PartCount];

            /// <summary>
            /// Соседние части у сустава перекрываются по замыслу: вершины ближе radius к суставу не считаем
            /// (у плеча — вместе с подмышкой, у бёдер — пах).
            /// </summary>
            private static readonly Dictionary<(WorkerPart, WorkerPart), (WorkerPart joint, float radius)> Joints = BuildJoints();

            public Body(Mesh[] parts, WorkerRole role)
            {
                rigid = Enumerable.Range(0, WorkerPose.PartCount).Where(k => !WorkerSkin.IsSoft((WorkerPart)k)).ToArray();
                foreach (var k in rigid)
                {
                    grids[k] = new Grid(parts[k]);
                    vertices[k] = parts[k].vertices.Distinct().ToArray();
                }
                var soft = new[] { WorkerPart.Cape, WorkerPart.Hips };
                cloth = soft.Select(p => new WorkerSkin(parts[(int)p], p, Height, role)).ToArray();

                WorkerSkeleton.Evaluate(WorkerPose.Rest(), Height, matrices);
                foreach (var a in rigid)
                foreach (var b in rigid.Where(b => b != a))
                {
                    var toB = matrices[b].inverse * matrices[a];
                    insideAtRest[(a, b)] = vertices[a].Select(v => grids[b].Deep(toB.MultiplyPoint3x4(v))).ToArray();
                }
                shells = new Shell[cloth.Length];
                for (var s = 0; s < cloth.Length; s++)
                {
                    cloth[s].Apply(matrices);
                    shells[s] = new Shell(cloth[s].Mesh, Frame(s));
                    var shellVertices = cloth[s].Mesh.vertices;
                    foreach (var a in rigid)
                        sideAtRest[(s, a)] = vertices[a].Select(v => shells[s].Side(matrices[a].MultiplyPoint3x4(v), shellVertices, Frame(s))).ToArray();
                }
            }

            /// <summary>Пары «что во что / сквозь что» и сколько вершин прошло.</summary>
            public IEnumerable<(string what, int count)> Collisions(WorkerPose pose, (Mesh mesh, WorkerCarryBuilder.Shape shape) load,
                WorkerCarry carry)
            {
                WorkerSkeleton.Evaluate(pose, Height, matrices);
                foreach (var a in rigid)
                foreach (var b in rigid.Where(b => b != a))
                {
                    var toB = matrices[b].inverse * matrices[a];
                    var rest = insideAtRest[(a, b)];
                    var near = Joints.TryGetValue(((WorkerPart)a, (WorkerPart)b), out var joint);
                    Vector3 jointAt = near ? matrices[(int)joint.joint].GetColumn(3) : Vector3.zero;
                    var count = 0;
                    for (var i = 0; i < vertices[a].Length; i++)
                    {
                        if (rest[i] || !grids[b].Deep(toB.MultiplyPoint3x4(vertices[a][i])))
                            continue;
                        if (near && Vector3.Distance(matrices[a].MultiplyPoint3x4(vertices[a][i]), jointAt) < joint.radius)
                            continue;
                        count++;
                    }
                    if (count > 0)
                        yield return ($"{(WorkerPart)a} в {(WorkerPart)b}", count);
                }

                for (var s = 0; s < cloth.Length; s++)
                {
                    cloth[s].Apply(matrices);
                    var shellVertices = cloth[s].Mesh.vertices;
                    foreach (var a in rigid)
                    {
                        var rest = sideAtRest[(s, a)];
                        var through = 0;
                        for (var i = 0; i < vertices[a].Length; i++)
                        {
                            if (rest[i] != 0 && shells[s].Side(matrices[a].MultiplyPoint3x4(vertices[a][i]), shellVertices, Frame(s)) == -rest[i])
                                through++;
                        }
                        if (through > 0)
                            yield return ($"{(WorkerPart)a} сквозь {(s == 0 ? "пелерину" : "подол")}", through);
                    }
                }

                if (carry == WorkerCarry.None)
                    yield break;
                var placed = Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one);
                load.shape.Apply(placed, pose.LoadBend, 0f);
                var points = load.mesh.vertices.Distinct().Select(v => placed.MultiplyPoint3x4(v)).ToArray();
                // Кулаки, которые держат ношу (бревно — левый, корзину — оба), обхватывают её — это не прорыв.
                var fists = new Vector3[WorkerPose.PartCount];
                foreach (var (part, side) in new[] { (WorkerPart.ForearmLeft, -1f), (WorkerPart.ForearmRight, 1f) })
                    fists[(int)part] = matrices[(int)part].MultiplyPoint3x4(WorkerSkeleton.FistCenter(side, Height) - WorkerSkeleton.Pivot(part, Height));
                bool Held(int part, Vector3 p) => (part == (int)WorkerPart.ForearmLeft || (part == (int)WorkerPart.ForearmRight && carry != WorkerCarry.Log)) &&
                                                  Vector3.Distance(p, fists[part]) < 0.05f * Height;
                foreach (var b in rigid)
                {
                    var toB = matrices[b].inverse;
                    var count = points.Count(p => grids[b].Deep(toB.MultiplyPoint3x4(p)) && !Held(b, p));
                    if (count > 0)
                        yield return ($"{carry} в {(WorkerPart)b}", count);
                }
                for (var s = 0; s < cloth.Length; s++)
                {
                    var shellVertices = cloth[s].Mesh.vertices;
                    var under = points.Count(p => shells[s].Side(p, shellVertices, Frame(s)) == 1);
                    if (under > 0)
                        yield return ($"{carry} под {(s == 0 ? "пелериной" : "подолом")}", under);
                }
            }

            /// <summary>Система, от которой ткань «снаружи»: корпус для пелерины, подол (пояс) для подола.</summary>
            private Matrix4x4 Frame(int shell) => matrices[(int)(shell == 0 ? WorkerPart.Torso : WorkerPart.Hips)];

            private static Dictionary<(WorkerPart, WorkerPart), (WorkerPart, float)> BuildJoints()
            {
                var joints = new Dictionary<(WorkerPart, WorkerPart), (WorkerPart, float)>();
                void Joint(WorkerPart a, WorkerPart b, WorkerPart at, float radius)
                {
                    joints[(a, b)] = (at, radius * Height);
                    joints[(b, a)] = (at, radius * Height);
                }
                Joint(WorkerPart.Torso, WorkerPart.Head, WorkerPart.Head, 0.05f);
                Joint(WorkerPart.Torso, WorkerPart.UpperArmLeft, WorkerPart.UpperArmLeft, 0.08f);
                Joint(WorkerPart.Torso, WorkerPart.UpperArmRight, WorkerPart.UpperArmRight, 0.08f);
                Joint(WorkerPart.UpperArmLeft, WorkerPart.ForearmLeft, WorkerPart.ForearmLeft, 0.05f);
                Joint(WorkerPart.UpperArmRight, WorkerPart.ForearmRight, WorkerPart.ForearmRight, 0.05f);
                Joint(WorkerPart.Torso, WorkerPart.ThighLeft, WorkerPart.ThighLeft, 0.07f);
                Joint(WorkerPart.Torso, WorkerPart.ThighRight, WorkerPart.ThighRight, 0.07f);
                Joint(WorkerPart.ThighLeft, WorkerPart.ThighRight, WorkerPart.Hips, 0.07f);
                Joint(WorkerPart.ThighLeft, WorkerPart.ShinLeft, WorkerPart.ShinLeft, 0.05f);
                Joint(WorkerPart.ThighRight, WorkerPart.ShinRight, WorkerPart.ShinRight, 0.05f);
                Joint(WorkerPart.ShinLeft, WorkerPart.FootLeft, WorkerPart.FootLeft, 0.05f);
                Joint(WorkerPart.ShinRight, WorkerPart.FootRight, WorkerPart.FootRight, 0.05f);
                return joints;
            }
        }

        /// <summary>
        /// Что внутри сетки части, в её системе: клетки по трём осям, на каждой прямой считаются входы и выходы сквозь грани
        /// (по нормалям), клетка внутри, если так вышло хотя бы по двум осям. Перекрытые куски (шар локтя в трубе руки)
        /// и двусторонние грани так считаются верно.
        /// </summary>
        private sealed class Grid
        {
            private readonly Vector3 min;
            private readonly int nx, ny, nz;
            private readonly bool[] inside;

            public Grid(Mesh mesh)
            {
                var v = mesh.vertices;
                var n = mesh.normals;
                var t = mesh.triangles;
                var bounds = mesh.bounds;
                min = bounds.min - Vector3.one * Cell * 2f;
                nx = Mathf.CeilToInt((bounds.size.x + Cell * 4f) / Cell);
                ny = Mathf.CeilToInt((bounds.size.y + Cell * 4f) / Cell);
                nz = Mathf.CeilToInt((bounds.size.z + Cell * 4f) / Cell);
                var dims = new[] { nx, ny, nz };
                var votes = new byte[nx * ny * nz];
                for (var axis = 0; axis < 3; axis++)
                {
                    var a1 = (axis + 1) % 3;
                    var a2 = (axis + 2) % 3;
                    var lines = new Dictionary<(int, int), List<(float at, int step)>>();
                    for (var i = 0; i < t.Length; i += 3)
                    {
                        var p0 = v[t[i]];
                        var p1 = v[t[i + 1]];
                        var p2 = v[t[i + 2]];
                        var normal = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                        var from1 = Mathf.CeilToInt((Mathf.Min(p0[a1], Mathf.Min(p1[a1], p2[a1])) - min[a1]) / Cell - 0.5f);
                        var to1 = Mathf.FloorToInt((Mathf.Max(p0[a1], Mathf.Max(p1[a1], p2[a1])) - min[a1]) / Cell - 0.5f);
                        var from2 = Mathf.CeilToInt((Mathf.Min(p0[a2], Mathf.Min(p1[a2], p2[a2])) - min[a2]) / Cell - 0.5f);
                        var to2 = Mathf.FloorToInt((Mathf.Max(p0[a2], Mathf.Max(p1[a2], p2[a2])) - min[a2]) / Cell - 0.5f);
                        var d = (p1[a2] - p2[a2]) * (p0[a1] - p2[a1]) + (p2[a1] - p1[a1]) * (p0[a2] - p2[a2]);
                        if (Mathf.Abs(d) < 1e-12f)
                            continue;
                        for (var c1 = from1; c1 <= to1; c1++)
                        for (var c2 = from2; c2 <= to2; c2++)
                        {
                            // Где прямая вдоль axis через центр клетки (c1, c2) пересекает грань: барицентрика в проекции.
                            var u = min[a1] + (c1 + 0.5f) * Cell;
                            var w = min[a2] + (c2 + 0.5f) * Cell;
                            var l0 = ((p1[a2] - p2[a2]) * (u - p2[a1]) + (p2[a1] - p1[a1]) * (w - p2[a2])) / d;
                            var l1 = ((p2[a2] - p0[a2]) * (u - p2[a1]) + (p0[a1] - p2[a1]) * (w - p2[a2])) / d;
                            var l2 = 1f - l0 - l1;
                            if (l0 < 0f || l1 < 0f || l2 < 0f)
                                continue;
                            if (!lines.TryGetValue((c1, c2), out var list))
                                lines[(c1, c2)] = list = new List<(float, int)>();
                            list.Add((l0 * p0[axis] + l1 * p1[axis] + l2 * p2[axis], normal[axis] < 0f ? 1 : -1));
                        }
                    }
                    foreach (var line in lines)
                    {
                        var list = line.Value;
                        list.Sort((p, q) => p.at.CompareTo(q.at));
                        var winding = 0;
                        var k = 0;
                        var index = new int[3];
                        index[a1] = line.Key.Item1;
                        index[a2] = line.Key.Item2;
                        for (var c = 0; c < dims[axis]; c++)
                        {
                            var center = min[axis] + (c + 0.5f) * Cell;
                            while (k < list.Count && list[k].at < center)
                                winding += list[k++].step;
                            if (winding <= 0)
                                continue;
                            index[axis] = c;
                            votes[(index[2] * ny + index[1]) * nx + index[0]]++;
                        }
                    }
                }
                inside = votes.Select(x => x >= 2).ToArray();
            }

            /// <summary>Точка внутри глубже одной клетки: и её клетка, и все шесть соседних — внутри.</summary>
            public bool Deep(Vector3 p)
            {
                var x = Mathf.FloorToInt((p.x - min.x) / Cell);
                var y = Mathf.FloorToInt((p.y - min.y) / Cell);
                var z = Mathf.FloorToInt((p.z - min.z) / Cell);
                return At(x, y, z) && At(x + 1, y, z) && At(x - 1, y, z) && At(x, y + 1, z) && At(x, y - 1, z) && At(x, y, z + 1) &&
                       At(x, y, z - 1);
            }

            private bool At(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && inside[(z * ny + y) * nx + x];
        }

        /// <summary>Ткань: наружные грани (без изнанки) и её край — рёбра, которые есть только у одной наружной грани.</summary>
        private sealed class Shell
        {
            private readonly int[] outer;
            private readonly Vector3Int[] keys;
            private readonly HashSet<(Vector3Int, Vector3Int)> border = new();

            public Shell(Mesh mesh, Matrix4x4 frame)
            {
                var v = mesh.vertices;
                var n = mesh.normals;
                var t = mesh.triangles;
                var toFrame = frame.inverse;
                var faces = new List<int>();
                for (var i = 0; i < t.Length; i += 3)
                {
                    var local = toFrame.MultiplyPoint3x4((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f);
                    var away = new Vector3(local.x, Mathf.Max(0f, local.y - 0.3f), local.z);
                    if (Vector3.Dot(toFrame.MultiplyVector(n[t[i]] + n[t[i + 1]] + n[t[i + 2]]), away) > 0f)
                        faces.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
                }
                outer = faces.ToArray();
                keys = v.Select(p => Vector3Int.RoundToInt(p * 20000f)).ToArray();
                var uses = new Dictionary<(Vector3Int, Vector3Int), int>();
                for (var i = 0; i < outer.Length; i += 3)
                for (var k = 0; k < 3; k++)
                {
                    var edge = Edge(outer[i + k], outer[i + (k + 1) % 3]);
                    uses[edge] = uses.TryGetValue(edge, out var c) ? c + 1 : 1;
                }
                foreach (var pair in uses.Where(pair => pair.Value == 1))
                    border.Add(pair.Key);
            }

            /// <summary>
            /// С какой стороны ткани точка p: 1 — под ней (со стороны тела), −1 — снаружи, 0 — далеко, касается ткани
            /// (ближе 0,3 % роста) или напротив её края.
            /// </summary>
            public int Side(Vector3 p, Vector3[] v, Matrix4x4 frame)
            {
                var best = float.MaxValue;
                var face = -1;
                var nearest = Vector3.zero;
                var weights = Vector3.zero;
                for (var i = 0; i < outer.Length; i += 3)
                {
                    var q = Closest(p, v[outer[i]], v[outer[i + 1]], v[outer[i + 2]], out var bary);
                    var d = (p - q).sqrMagnitude;
                    if (d >= best)
                        continue;
                    best = d;
                    face = i;
                    nearest = q;
                    weights = bary;
                }
                if (face < 0 || best > 0.05f * Height * 0.05f * Height)
                    return 0;
                for (var k = 0; k < 3; k++)
                {
                    if (weights[k] <= 1e-4f && border.Contains(Edge(outer[face + (k + 1) % 3], outer[face + (k + 2) % 3])))
                        return 0;
                }
                var normal = Vector3.Cross(v[outer[face + 1]] - v[outer[face]], v[outer[face + 2]] - v[outer[face]]).normalized;
                if (Vector3.Dot(normal, nearest - (Vector3)frame.GetColumn(3)) < 0f)
                    normal = -normal;
                var side = Vector3.Dot(p - nearest, normal);
                if (Mathf.Abs(side) < 0.003f * Height)
                    return 0;
                return side > 0f ? -1 : 1;
            }

            private (Vector3Int, Vector3Int) Edge(int a, int b)
            {
                var ka = keys[a];
                var kb = keys[b];
                return ka.x < kb.x || (ka.x == kb.x && (ka.y < kb.y || (ka.y == kb.y && ka.z < kb.z))) ? (ka, kb) : (kb, ka);
            }

            /// <summary>Ближайшая к p точка треугольника abc и её барицентрика (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
            private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
            {
                var ab = b - a;
                var ac = c - a;
                var ap = p - a;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f)
                {
                    bary = new Vector3(1f, 0f, 0f);
                    return a;
                }
                var bp = p - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3)
                {
                    bary = new Vector3(0f, 1f, 0f);
                    return b;
                }
                var vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                {
                    var w = d1 / (d1 - d3);
                    bary = new Vector3(1f - w, w, 0f);
                    return a + w * ab;
                }
                var cp = p - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6)
                {
                    bary = new Vector3(0f, 0f, 1f);
                    return c;
                }
                var vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                {
                    var w = d2 / (d2 - d6);
                    bary = new Vector3(1f - w, 0f, w);
                    return a + w * ac;
                }
                var va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                {
                    var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
                    bary = new Vector3(0f, 1f - w, w);
                    return b + w * (c - b);
                }
                var denominator = 1f / (va + vb + vc);
                var vv = vb * denominator;
                var ww = vc * denominator;
                bary = new Vector3(1f - vv - ww, vv, ww);
                return a + ab * vv + ac * ww;
            }
        }
    }
}
