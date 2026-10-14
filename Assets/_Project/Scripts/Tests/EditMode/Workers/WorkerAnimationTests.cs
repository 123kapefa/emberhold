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
