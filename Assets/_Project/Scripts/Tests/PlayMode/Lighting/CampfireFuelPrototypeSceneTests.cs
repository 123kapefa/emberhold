using System.Collections;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Fog;
using Emberhold.Gameplay.Lighting;
using Emberhold.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Lighting
{
    public sealed class CampfireFuelPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_CampfireBurnsFuelAtNightAndGoesOut()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            var zone = Object.FindAnyObjectByType<BuildZone>();
            var fog = Object.FindAnyObjectByType<LightFog>();
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var campfire = zone.Sources[0];
            var fuel = campfire.Fuel;
            Assert.That(fuel, Is.Not.Null, "У костра есть запас топлива");
            Assert.That(campfire.GetComponent<CampfireFuelControls>(), Is.Not.Null, "Игрок видит топливо и может подбросить");
            Assert.That(campfire.IsLit, Is.True);
            Assert.That(campfire.Radius, Is.EqualTo(campfire.FullRadius));
            var start = fuel.Amount;
            Assert.That(start, Is.GreaterThan(0f));

            // День, сумерки и рассвет топливо не тратят.
            Assert.That(cycle.Phase, Is.EqualTo(DayPhase.Day));
            cycle.Advance(cycle.Cycle.Duration(DayPhase.Day) + cycle.Cycle.Duration(DayPhase.Dusk) * 0.5f);
            Assert.That(fuel.Amount, Is.EqualTo(start), "Днём и в сумерки не горит");

            // Ночью тратится, и шаг через границу фаз считает только ночную часть.
            cycle.Advance(cycle.Cycle.Duration(DayPhase.Dusk) * 0.5f + 30f);
            Assert.That(cycle.IsNight, Is.True);
            var perSecond = (start - fuel.Amount) / 30f;
            Assert.That(perSecond, Is.GreaterThan(0f), "Ночью топливо тратится");

            // Догорает: радиус сжимается, потом огонь гаснет.
            var shrinking = false;
            for (var i = 0; i < 400 && campfire.IsLit; i++)
            {
                cycle.Advance(1f);
                if (campfire.IsLit && campfire.Radius < campfire.FullRadius - 0.01f)
                    shrinking = true;
            }
            Assert.That(shrinking, Is.True, "На последних крохах радиус сжимается");
            Assert.That(campfire.IsLit, Is.False, "Без топлива огонь гаснет");
            Assert.That(cycle.IsNight, Is.True, "Стартового запаса не хватает на всю ночь");
            Assert.That(fuel.IsEmpty, Is.True);

            // Погасший костёр: строить нельзя, туман подступает к самому огню, света и пламени нет.
            var center = campfire.transform.position;
            Assert.That(zone.Contains(center), Is.False);
            Assert.That(fog.DensityAt(center + Vector3.right * 2f), Is.GreaterThan(0.5f), "Туман входит внутрь");
            Assert.That(campfire.GetComponentInChildren<Light>(true).enabled, Is.False);
            Assert.That(campfire.GetComponentInChildren<LineRenderer>(true).enabled, Is.False);
            var near = SurfaceBlock(center + Vector3.right * 2f);
            Assert.That(editor.CanEdit(near), Is.False, "Без огня строить нельзя");

            // Подбросили топливо — огонь снова горит, пока мало — радиус меньше полного.
            var lit = 0;
            campfire.LitChanged += isLit => lit += isLit ? 1 : 0;
            Assert.That(campfire.AddFuel(1f), Is.EqualTo(1f));
            Assert.That(campfire.IsLit, Is.True);
            Assert.That(lit, Is.EqualTo(1), "Событие «загорелся»");
            Assert.That(campfire.Radius, Is.GreaterThan(0f).And.LessThan(campfire.FullRadius));
            Assert.That(editor.CanEdit(near), Is.True);
            Assert.That(fog.DensityAt(center + Vector3.right * 2f), Is.Zero);

            campfire.AddFuel(fuel.Capacity);
            Assert.That(fuel.Amount, Is.EqualTo(fuel.Capacity), "Больше запаса не помещается");
            Assert.That(campfire.Radius, Is.EqualTo(campfire.FullRadius));
            yield return null;
            Assert.That(campfire.GetComponentInChildren<Light>().range, Is.EqualTo(campfire.FullRadius).Within(0.001f));
        }

        private static Vector3Int SurfaceBlock(Vector3 worldPoint)
        {
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            Assert.That(Physics.Raycast(new Vector3(worldPoint.x, 200f, worldPoint.z), Vector3.down, out var hit, 400f, Emberhold.Core.Layers.GroundMask), Is.True);
            Assert.That(view.TryGetBlock(hit.collider, hit.triangleIndex, out var block), Is.True);
            return block;
        }
    }
}
