using System;
using System.Collections.Generic;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class WorkerAnimationTests
    {
        private const float Height = 1.9f;
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void Parts_InRestPose_AssembleIntoWholeWorker()
        {
            foreach (WorkerRole role in Enum.GetValues(typeof(WorkerRole)))
            {
                var look = Look(role);
                var whole = Track(WorkerMeshBuilder.Build(look, Height));
                var parts = WorkerMeshBuilder.BuildParts(look, Height);
                var matrices = new Matrix4x4[WorkerPose.PartCount];
                WorkerSkeleton.Evaluate(WorkerPose.Rest(), Height, matrices);
                var vertices = 0;
                var bounds = new Bounds();
                var first = true;
                for (var i = 0; i < parts.Length; i++)
                {
                    Track(parts[i]);
                    vertices += parts[i].vertexCount;
                    foreach (var v in parts[i].vertices)
                    {
                        var p = matrices[i].MultiplyPoint3x4(v);
                        if (first)
                            bounds = new Bounds(p, Vector3.zero);
                        else
                            bounds.Encapsulate(p);
                        first = false;
                    }
                }
                Assert.That(vertices, Is.EqualTo(whole.vertexCount), $"{role}: все грани разошлись по частям");
                Assert.That(Vector3.Distance(bounds.min, whole.bounds.min), Is.LessThan(1e-3f), $"{role}: части собираются на место");
                Assert.That(Vector3.Distance(bounds.max, whole.bounds.max), Is.LessThan(1e-3f), $"{role}: части собираются на место");
            }
        }

        [Test]
        public void EveryClip_KeepsLowestSupportOnGround()
        {
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            var motion = new WorkerMotion(0.3f, 2.5f, Height, true, false, WorkerCarry.StoneSack);
            foreach (WorkerClip clip in Enum.GetValues(typeof(WorkerClip)))
            {
                for (var k = 0; k <= 20; k++)
                {
                    var time = WorkerAnimation.IsOneShot(clip) ? k / 20f : k * 0.13f;
                    var pose = WorkerAnimation.Sample(clip, time, motion);
                    WorkerSkeleton.Evaluate(pose, Height, matrices);
                    var lowest = WorkerSkeleton.LowestSupport(matrices, Height);
                    foreach (var foot in new[] { WorkerPart.FootLeft, WorkerPart.FootRight })
                        Assert.That(Vector3.Dot(matrices[(int)foot].rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.999f), $"{clip}: стопа ровно");
                    Assert.That(lowest, Is.EqualTo(0f).Within(1e-3f), $"{clip} в {time}: опора на земле");
                    foreach (var m in matrices)
                        Assert.That(float.IsNaN(m.m03) || float.IsNaN(m.m13), Is.False, $"{clip}: без NaN");
                }
            }
        }

        [Test]
        public void Walk_LegsAlternate_AndCrouchLowersHead()
        {
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            var motion = new WorkerMotion(0f, 2.5f, Height, false, false);
            float FootZ(WorkerPart foot) => matrices[(int)foot].GetColumn(3).z;

            var quarter = 0.25f / (2.5f / 1.45f);
            WorkerSkeleton.Evaluate(WorkerAnimation.Sample(WorkerClip.Walk, quarter, motion), Height, matrices);
            Assert.That(FootZ(WorkerPart.FootLeft), Is.GreaterThan(FootZ(WorkerPart.FootRight)), "Левая нога впереди");
            WorkerSkeleton.Evaluate(WorkerAnimation.Sample(WorkerClip.Walk, quarter * 3f, motion), Height, matrices);
            Assert.That(FootZ(WorkerPart.FootRight), Is.GreaterThan(FootZ(WorkerPart.FootLeft)), "Правая нога впереди");

            WorkerSkeleton.Evaluate(WorkerAnimation.Sample(WorkerClip.Idle, 0f, motion), Height, matrices);
            var standing = matrices[(int)WorkerPart.Head].GetColumn(3).y;
            WorkerSkeleton.Evaluate(WorkerAnimation.Sample(WorkerClip.PickUp, 0.5f, motion), Height, matrices);
            Assert.That(matrices[(int)WorkerPart.Head].GetColumn(3).y, Is.LessThan(standing - 0.3f), "Присел за ресурсом");
        }

        [Test]
        public void CarriedLoad_IsHeldByLeftHand_AndPutOnGround()
        {
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.StoneSack })
            {
                var motion = new WorkerMotion(0f, 0f, Height, true, false, carry);
                var pose = WorkerAnimation.Sample(WorkerClip.Carry, 0.3f, motion);
                WorkerSkeleton.Evaluate(pose, Height, matrices);
                var fist = matrices[(int)WorkerPart.ForearmLeft].MultiplyPoint3x4(
                    WorkerSkeleton.FistCenter(-1f, Height) - WorkerSkeleton.Pivot(WorkerPart.ForearmLeft, Height));
                var grip = Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one).MultiplyPoint3x4(WorkerCarryBuilder.Grip(carry, Height));
                Assert.That(Vector3.Distance(fist, grip), Is.LessThan(0.02f), $"{carry}: кисть на ноше");

                var put = WorkerAnimation.Sample(WorkerClip.Put, 0.9f, motion);
                var ground = WorkerCarryBuilder.Ground(carry, Height);
                Assert.That(Vector3.Distance(put.LoadPosition, ground.GetColumn(3)), Is.LessThan(1e-3f), $"{carry}: положил на землю");
            }
        }

        [Test]
        public void PutAndPickUp_LoadGoesAroundBody()
        {
            // Точки ноши (середина и концы бревна, середина мешка) не заходят внутрь туловища ни в какой момент.
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.StoneSack })
            {
                var motion = new WorkerMotion(0f, 0f, Height, true, false, carry);
                foreach (var clip in new[] { WorkerClip.Put, WorkerClip.PickUp })
                {
                    for (var k = 0; k <= 40; k++)
                    {
                        var pose = WorkerAnimation.Sample(clip, k / 40f, motion);
                        var torso = WorkerSkeleton.Torso(pose, Height).inverse;
                        var load = Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one);
                        var points = carry == WorkerCarry.Log
                            ? new[] { Vector3.zero, Vector3.forward * 0.2f * Height, Vector3.back * 0.2f * Height }
                            : new[] { Vector3.zero };
                        foreach (var p in points)
                        {
                            // В системе корпуса: середина корпуса — на оси Y, от пояса вверх до плеч.
                            var local = torso.MultiplyPoint3x4(load.MultiplyPoint3x4(p)) / Height;
                            if (local.y < -0.15f || local.y > 0.22f)
                                continue;
                            var inside = local.x * local.x / (0.14f * 0.14f) + local.z * local.z / (0.09f * 0.09f);
                            Assert.That(inside, Is.GreaterThan(1f), $"{carry} {clip} в {k / 40f}: ноша внутри тела {local}");
                        }
                    }
                }
            }
        }

        [Test]
        public void Sack_HangsDownUnderGravity_WhateverItsTilt()
        {
            var mesh = WorkerCarryBuilder.Build(WorkerCarry.StoneSack, Height);
            created.Add(mesh);
            var shape = new WorkerCarryBuilder.Shape(mesh, WorkerCarry.StoneSack, Height);
            foreach (var tilt in new[] { 0f, 50f, -40f })
            {
                // Горловина вверх и наклонена; тело мешка (низ сетки) всё равно висит под горловиной.
                var world = Matrix4x4.Rotate(Quaternion.Euler(tilt, 0f, tilt * 0.5f));
                shape.Apply(world, 0f, 0f);
                var neck = world.MultiplyPoint3x4(new Vector3(0f, 0.05f * Height, 0f));
                var lowest = Vector3.zero;
                foreach (var v in mesh.vertices)
                {
                    var p = world.MultiplyPoint3x4(v);
                    if (p.y < lowest.y)
                        lowest = p;
                }
                var hang = (lowest - neck).normalized;
                Assert.That(Vector3.Dot(hang, Vector3.down), Is.GreaterThan(0.9f), $"Наклон {tilt}: мешок висит вниз");
            }
        }

        [Test]
        public void EveryClip_SleeveTopStaysUnderCape()
        {
            // Верх рукава у плеча (выше точки вращения плеча и снаружи от неё), закрытый в покое пелериной, ни в какой анимации
            // и ни с какой ношей не выходит из-под неё: луч от вершины наружу от оси корпуса или вверх упирается в пелерину.
            var parts = WorkerMeshBuilder.BuildParts(Look(WorkerRole.Free), Height);
            foreach (var part in parts)
                Track(part);
            var cape = new WorkerSkin(parts[(int)WorkerPart.Cape], WorkerPart.Cape, Height);
            var triangles = cape.Mesh.triangles;
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            var arms = new[] { WorkerPart.UpperArmLeft, WorkerPart.UpperArmRight };
            var sleeveTop = new List<Vector3>[arms.Length];

            WorkerSkeleton.Evaluate(WorkerPose.Rest(), Height, matrices);
            cape.Apply(matrices);
            for (var a = 0; a < arms.Length; a++)
            {
                sleeveTop[a] = new List<Vector3>();
                var arm = matrices[(int)arms[a]];
                Vector3 pivot = arm.GetColumn(3);
                foreach (var v in parts[(int)arms[a]].vertices)
                {
                    var p = arm.MultiplyPoint3x4(v);
                    if (p.y >= pivot.y && Mathf.Abs(p.x) >= Mathf.Abs(pivot.x) && Covered(p, matrices, cape.Mesh.vertices, triangles))
                        sleeveTop[a].Add(v);
                }
                Assert.That(sleeveTop[a].Count, Is.GreaterThan(10), $"{arms[a]}: в покое верх рукава под пелериной");
            }

            var failures = new List<string>();
            foreach (var carry in new[] { WorkerCarry.None, WorkerCarry.Log, WorkerCarry.StoneSack, WorkerCarry.GrassSack })
            foreach (var speed in new[] { 0f, 2.5f })
            foreach (WorkerClip clip in Enum.GetValues(typeof(WorkerClip)))
            {
                var motion = new WorkerMotion(0.3f, speed, Height, false, false, carry);
                for (var k = 0; k <= 20; k++)
                {
                    var time = WorkerAnimation.IsOneShot(clip) ? k / 20f : k * 0.13f;
                    WorkerSkeleton.Evaluate(WorkerAnimation.Sample(clip, time, motion), Height, matrices);
                    cape.Apply(matrices);
                    var capeVertices = cape.Mesh.vertices;
                    for (var a = 0; a < arms.Length; a++)
                    {
                        var outside = 0;
                        foreach (var v in sleeveTop[a])
                        {
                            if (!Covered(matrices[(int)arms[a]].MultiplyPoint3x4(v), matrices, capeVertices, triangles))
                                outside++;
                        }
                        if (outside > 0)
                            failures.Add($"{clip} {carry} скорость {speed} в {time}: {arms[a]} — {outside} из {sleeveTop[a].Count} вершин рукава поверх пелерины");
                    }
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>Точка p под пелериной: луч наружу от оси корпуса (вбок) или вверх по корпусу упирается в пелерину.</summary>
        private static bool Covered(Vector3 p, Matrix4x4[] matrices, Vector3[] capeVertices, int[] triangles)
        {
            var torso = matrices[(int)WorkerPart.Torso];
            var local = torso.inverse.MultiplyPoint3x4(p);
            var outward = torso.MultiplyVector(new Vector3(local.x, 0f, local.z).normalized);
            var up = torso.MultiplyVector(Vector3.up);
            return Hits(p, outward, capeVertices, triangles) || Hits(p, up, capeVertices, triangles);
        }

        /// <summary>Луч из origin вдоль direction пересекает треугольник сетки ближе 0,12 роста.</summary>
        private static bool Hits(Vector3 origin, Vector3 direction, Vector3[] vertices, int[] triangles)
        {
            const float maxDistance = 0.12f * Height;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var e1 = vertices[triangles[i + 1]] - a;
                var e2 = vertices[triangles[i + 2]] - a;
                var h = Vector3.Cross(direction, e2);
                var det = Vector3.Dot(e1, h);
                if (Mathf.Abs(det) < 1e-9f)
                    continue;
                var s = origin - a;
                var u = Vector3.Dot(s, h) / det;
                if (u < 0f || u > 1f)
                    continue;
                var q = Vector3.Cross(s, e1);
                var w = Vector3.Dot(direction, q) / det;
                if (w < 0f || u + w > 1f)
                    continue;
                var t = Vector3.Dot(e2, q) / det;
                if (t > 0f && t < maxDistance)
                    return true;
            }
            return false;
        }

        [Test]
        public void Animator_BlendsBetweenClips_AndOneShotsFinish()
        {
            var animator = new WorkerAnimator();
            var motion = new WorkerMotion(0f, 0f, Height, false, false);
            animator.Update(0.1f, motion);
            animator.Play(WorkerClip.PickUp, 0.7f);
            animator.Update(0.05f, motion);
            Assert.That(animator.Progress, Is.InRange(0.05f, 0.1f));
            for (var i = 0; i < 20; i++)
                animator.Update(0.05f, motion);
            Assert.That(animator.Progress, Is.EqualTo(1f));
            animator.Restart();
            Assert.That(animator.Progress, Is.EqualTo(0f));
        }

        [Test]
        public void Carry_DependsOnResource_StokerCarriesBundleOnBack()
        {
            Assert.That(WorkerCarryBuilder.CarryFor("wood", WorkerRole.Free), Is.EqualTo(WorkerCarry.Log));
            Assert.That(WorkerCarryBuilder.CarryFor("stone", WorkerRole.Digger), Is.EqualTo(WorkerCarry.StoneSack));
            Assert.That(WorkerCarryBuilder.CarryFor("grass", WorkerRole.Free), Is.EqualTo(WorkerCarry.GrassSack));
            Assert.That(WorkerCarryBuilder.CarryFor("wood", WorkerRole.Stoker), Is.EqualTo(WorkerCarry.None));
            Assert.That(WorkerCarryBuilder.CarryFor(null, WorkerRole.Free), Is.EqualTo(WorkerCarry.None));
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.StoneSack, WorkerCarry.GrassSack })
                Assert.That(Track(WorkerCarryBuilder.Build(carry, Height)).vertexCount, Is.GreaterThan(0), $"{carry}: есть сетка");
        }

        private Mesh Track(Mesh mesh)
        {
            created.Add(mesh);
            return mesh;
        }

        private static WorkerLook Look(WorkerRole role) =>
            new(new Color(0.9f, 0.75f, 0.6f), new Color(0.23f, 0.16f, 0.11f), new Color(0.54f, 0.35f, 0.21f),
                new Color(0.5f, 0.15f, 0.15f), true, role);
    }
}
