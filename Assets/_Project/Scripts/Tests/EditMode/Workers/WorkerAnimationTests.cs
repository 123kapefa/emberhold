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
            var motion = new WorkerMotion(0.3f, 2.5f, Height, true, false, WorkerCarry.StoneBasket);
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
        public void CarriedLoad_IsHeldByLeftHand_BasketHangsOnStraps_AndPutOnGround()
        {
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.GrassBasket, WorkerCarry.StoneBasket })
            {
                var motion = new WorkerMotion(0f, 0f, Height, true, false, carry);
                var pose = WorkerAnimation.Sample(WorkerClip.Carry, 0.3f, motion);
                WorkerSkeleton.Evaluate(pose, Height, matrices);
                var fist = matrices[(int)WorkerPart.ForearmLeft].MultiplyPoint3x4(
                    WorkerSkeleton.FistCenter(-1f, Height) - WorkerSkeleton.Pivot(WorkerPart.ForearmLeft, Height));
                var grip = Matrix4x4.TRS(pose.LoadPosition, pose.LoadRotation, Vector3.one).MultiplyPoint3x4(WorkerCarryBuilder.Grip(Height));
                // Бревно рука держит, корзина висит на лямках — рука опущена.
                if (WorkerCarryBuilder.HeldWhileCarried(carry))
                    Assert.That(Vector3.Distance(fist, grip), Is.LessThan(0.02f), $"{carry}: кисть на ноше");
                else
                    Assert.That(fist.y, Is.LessThan(0.45f * Height), $"{carry}: руки свободны");

                var put = WorkerAnimation.Sample(WorkerClip.Put, 0.9f, motion);
                var ground = WorkerCarryBuilder.Ground(carry, Height);
                Assert.That(put.LoadPosition.y, Is.EqualTo(ground.GetColumn(3).y).Within(1e-3f), $"{carry}: положил на землю");
                Assert.That(Vector3.Distance(put.LoadPosition, WorkerAnimation.Sample(WorkerClip.Put, 1f, motion).LoadPosition), Is.LessThan(1e-3f),
                    $"{carry}: лежит на месте");
            }
        }

        [Test]
        public void PutAndPickUp_LoadGoesAroundBody()
        {
            // Точки ноши (середина и концы бревна, середина корзины) не заходят внутрь туловища ни в какой момент.
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.GrassBasket, WorkerCarry.StoneBasket })
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
        public void Basket_StrapsOnShoulders_SlideOffWhenTakenOff()
        {
            // На спине лямки поднимаются до плеч; у снятой корзины они втянуты в узелки у края — выше всего камни.
            var mesh = WorkerCarryBuilder.Build(WorkerCarry.StoneBasket, Height);
            created.Add(mesh);
            var shape = new WorkerCarryBuilder.Shape(mesh, WorkerCarry.StoneBasket, Height);
            shape.Apply(1f);
            var onBack = mesh.bounds.max.y;
            shape.Apply(0f);
            var off = mesh.bounds.max.y;
            Assert.That(onBack, Is.GreaterThan(0.28f * Height), "лямки на плечах");
            Assert.That(off, Is.LessThan(0.2f * Height), "лямки втянуты");
        }

        [Test]
        public void RoleWork_LoopsAndSwingsTool()
        {
            // У каждой рабочей анимации своя роль и цикл; поза в конце цикла та же, что в начале, а правая рука
            // за цикл поднимается (замах) заметно выше, чем опускается (удар).
            var matrices = new Matrix4x4[WorkerPose.PartCount];
            foreach (var (clip, role) in new[] { (WorkerClip.Chop, WorkerRole.Woodcutter), (WorkerClip.Dig, WorkerRole.Digger),
                         (WorkerClip.Hammer, WorkerRole.Builder), (WorkerClip.Stoke, WorkerRole.Stoker) })
            {
                Assert.That(WorkerAnimation.RoleOf(clip), Is.EqualTo(role), $"{clip}: роль");
                var cycle = WorkerAnimation.WorkCycle(clip);
                Assert.That(cycle, Is.GreaterThan(1f), $"{clip}: цикл");
                var motion = WorkerMotion.For(Look(role), Height, 0f, 0f);
                // Самое начало цикла и миг перед его концом — разные моменты, но поза почти та же: цикл замкнут.
                var start = WorkerAnimation.Sample(clip, 0f, motion);
                var end = WorkerAnimation.Sample(clip, cycle - 1e-3f, motion);
                for (var i = 0; i < WorkerPose.PartCount; i++)
                    Assert.That(Vector3.Distance(start.Angles[i], end.Angles[i]), Is.LessThan(1f), $"{clip}: цикл замкнут ({(WorkerPart)i})");
                var low = float.MaxValue;
                var high = float.MinValue;
                for (var k = 0; k < 40; k++)
                {
                    WorkerSkeleton.Evaluate(WorkerAnimation.Sample(clip, cycle * k / 40f, motion), Height, matrices);
                    var fist = matrices[(int)WorkerPart.ForearmRight].MultiplyPoint3x4(
                        WorkerSkeleton.FistCenter(1f, Height) - WorkerSkeleton.Pivot(WorkerPart.ForearmRight, Height));
                    low = Mathf.Min(low, fist.y);
                    high = Mathf.Max(high, fist.y);
                }
                Assert.That(high - low, Is.GreaterThan(0.12f * Height), $"{clip}: рука с инструментом ходит");
            }
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
            // Следующий раз начинается с того, что ушло за конец (1,05 с при длине 0,7 с), — удары не сбиваются по фазе.
            animator.Restart();
            Assert.That(animator.Progress, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Carry_DependsOnResource_OnlyFreeWorkersCarry()
        {
            Assert.That(WorkerCarryBuilder.CarryFor("wood", WorkerRole.Free), Is.EqualTo(WorkerCarry.Log));
            Assert.That(WorkerCarryBuilder.CarryFor("stone", WorkerRole.Free), Is.EqualTo(WorkerCarry.StoneBasket));
            Assert.That(WorkerCarryBuilder.CarryFor("grass", WorkerRole.Free), Is.EqualTo(WorkerCarry.GrassBasket));
            Assert.That(WorkerCarryBuilder.CarryFor(null, WorkerRole.Free), Is.EqualTo(WorkerCarry.None));
            foreach (var role in new[] { WorkerRole.Digger, WorkerRole.Stoker, WorkerRole.Archer })
                Assert.That(WorkerCarryBuilder.CarryFor("stone", role), Is.EqualTo(WorkerCarry.None), $"{role}: с ролью не носят");
            foreach (var carry in new[] { WorkerCarry.Log, WorkerCarry.StoneBasket, WorkerCarry.GrassBasket })
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
