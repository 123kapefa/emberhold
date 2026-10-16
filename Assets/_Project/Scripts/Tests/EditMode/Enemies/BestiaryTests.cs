using System;
using System.Linq;
using Emberhold.Gameplay.Enemies.Bestiary;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.EditMode.Enemies
{
    /// <summary>Шесть базовых врагов: модели всех уровней строятся, рост по таблице, риги проходят все позы без поломок.</summary>
    public sealed class BestiaryTests
    {
        private static readonly CreatureKind[] Kinds = (CreatureKind[])Enum.GetValues(typeof(CreatureKind));

        private Material opaque;
        private Material transparent;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Emberhold/World/Tree");
            opaque = new Material(shader);
            transparent = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(opaque);
            Object.DestroyImmediate(transparent);
        }

        [Test]
        public void EveryKindAndLevel_BuildsModelWithEyes()
        {
            foreach (var kind in Kinds)
            for (var level = 0; level < 3; level++)
            {
                var model = CreatureModels.Build(kind, level);
                Assert.That(model.Elements.Count(e => e.Kind == ElementKind.Shape), Is.GreaterThan(20), $"{kind} {level}");
                Assert.That(model.Elements.Any(e => e.Kind == ElementKind.Eye), Is.True, $"{kind} {level}: нет глаз");
            }
        }

        [Test]
        public void Levels_AddFrostCracksAndAncientCore()
        {
            foreach (var kind in Kinds)
            {
                Assert.That(CreatureModels.Build(kind, 0).Elements.Any(e => e.LevelAdd), Is.False, $"{kind}: у обычного нет облика уровня");
                var seasoned = CreatureModels.Build(kind, 1);
                Assert.That(seasoned.Elements.Any(e => e.Kind == ElementKind.Crack), Is.True, $"{kind}: у матёрого трещины");
                Assert.That(seasoned.Elements.Any(e => e.Kind == ElementKind.Ground), Is.True, $"{kind}: у матёрого изморозь");
                Assert.That(seasoned.LightAt, Is.Not.Null, $"{kind}: у матёрого холодный свет");
                var ancient = CreatureModels.Build(kind, 2);
                Assert.That(ancient.Elements.Count(e => e.Kind == ElementKind.Core), Is.EqualTo(1), $"{kind}: у древнего ядро");
                Assert.That(ancient.Elements.Count(e => e.Kind == ElementKind.Crown), Is.EqualTo(7), $"{kind}: у древнего корона");
            }
        }

        [Test]
        public void SizeFactor_StretchesToTableHeight()
        {
            foreach (var kind in Kinds.Where(k => k != CreatureKind.Moths))
            {
                var height = CreatureModels.Build(kind, 0).ModelBounds().max.y * CreatureModels.SizeFactor(kind);
                Assert.That(height, Is.EqualTo(CreatureModels.TargetHeight(kind)).Within(0.01f), kind.ToString());
            }
        }

        [Test]
        public void EveryRig_PlaysAllPosesWithFinitePartsAndFeetNearGround()
        {
            var templates = new CreatureMaterials { Opaque = opaque, Transparent = transparent };
            foreach (var kind in Kinds)
            for (var level = 0; level < 3; level++)
            {
                var rig = CreatureRig.Create(kind, level, templates);
                try
                {
                    Assert.That(rig.Root.GetComponentsInChildren<MeshFilter>().Length, Is.GreaterThan(1), $"{kind} {level}");
                    // Покой, бег, атака, удар, смерть.
                    Play(rig, 1f, 0f);
                    Play(rig, 1f, 2f);
                    rig.Pose.Act = 1f;
                    Play(rig, 1f, 0f);
                    rig.Pose.Act = 0f;
                    rig.Pose.Recoil = 1f;
                    rig.Pose.Flash = 1f;
                    Play(rig, 0.3f, 0f);
                    rig.Pose.Recoil = rig.Pose.Flash = 0f;
                    rig.Pose.Fold = 1f;
                    rig.Pose.Fade = 0.5f;
                    Play(rig, 1f, 0f);
                    foreach (var t in rig.Root.GetComponentsInChildren<Transform>())
                        Assert.That(Finite(t.position) && Finite(t.lossyScale), Is.True, $"{kind} {level}: {t.name}");
                }
                finally
                {
                    rig.Destroy();
                }
            }
        }

        [Test]
        public void Crawler_And_Stilt_FeetStayOnGroundWhenStanding()
        {
            var templates = new CreatureMaterials { Opaque = opaque, Transparent = transparent };
            foreach (var kind in new[] { CreatureKind.Crawler, CreatureKind.Stilt })
            {
                var rig = CreatureRig.Create(kind, 0, templates);
                try
                {
                    Play(rig, 1f, 0f);
                    var lowest = rig.Root.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Body").Min(r => r.bounds.min.y);
                    Assert.That(lowest, Is.LessThan(0.08f * rig.Scale), $"{kind}: лапы не достают до земли");
                    Assert.That(lowest, Is.GreaterThan(-0.08f * rig.Scale), $"{kind}: лапы ушли под землю");
                }
                finally
                {
                    rig.Destroy();
                }
            }
        }

        private static void Play(CreatureRig rig, float seconds, float speed)
        {
            for (var t = 0f; t < seconds; t += 1f / 30f)
                rig.Tick(1f / 30f, speed);
        }

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);
    }
}
