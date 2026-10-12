using System.Collections;
using System.Collections.Generic;
using Emberhold.Core;
using Emberhold.Gameplay.DayNight;
using Emberhold.Gameplay.Fog;
using Emberhold.Gameplay.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.DayNight
{
    public sealed class DayNightPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_NightIsDarkerAndFoggier()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            var fog = Object.FindAnyObjectByType<LightFog>();
            var campfire = Object.FindAnyObjectByType<BuildZone>().Sources[0];
            Assert.That(cycle, Is.Not.Null, "В сцене Prototype нет DayNightCycle");
            Assert.That(fog, Is.Not.Null, "В сцене Prototype нет LightFog");
            Assert.That(cycle.Cycle.Length, Is.EqualTo(720f), "Сутки 12 минут");
            Assert.That(cycle.Phase, Is.EqualTo(DayPhase.Day), "Игра начинается днём");
            Assert.That(cycle.IsNight, Is.False);

            var center = campfire.transform.position;
            var near = center + Vector3.right * (campfire.Radius - 1f);
            var far = center + Vector3.right * (campfire.Radius + 20f);
            var dayAmbient = RenderSettings.ambientLight.grayscale;
            var dayFogNear = fog.DensityAt(near);
            var dayFogFar = fog.DensityAt(far);
            Assert.That(dayFogNear, Is.Zero, "Внутри света тумана нет");
            Assert.That(dayFogFar, Is.GreaterThan(0.2f), "За светом туман есть и днём");
            Assert.That(fog.DensityAt(center + Vector3.right * (campfire.Radius + 2f)), Is.LessThan(dayFogFar), "У края света туман слабее");

            var phases = new List<DayPhase>();
            cycle.PhaseChanged += phases.Add;
            cycle.SkipToNextPhase();
            cycle.SkipToNextPhase();
            yield return null;

            Assert.That(phases, Is.EqualTo(new[] { DayPhase.Dusk, DayPhase.Night }));
            Assert.That(cycle.IsNight, Is.True);
            Assert.That(cycle.Darkness, Is.EqualTo(1f));
            Assert.That(RenderSettings.ambientLight.grayscale, Is.LessThan(dayAmbient * 0.5f), "Ночью темнее");
            Assert.That(fog.DensityAt(near), Is.Zero, "Костёр держит туман и ночью");
            Assert.That(fog.DensityAt(far), Is.GreaterThan(dayFogFar), "Ночью туман гуще");
            Assert.That(Shader.GetGlobalVector("_EmberholdFogParams").z, Is.EqualTo(1f), "Туман передан шейдеру");
            Assert.That(Shader.GetGlobalFloat("_EmberholdLightCount"), Is.EqualTo(1f));

            // Погасший костёр — туман подступает вплотную.
            campfire.enabled = false;
            Assert.That(fog.DensityAt(near), Is.GreaterThan(0.5f));
            campfire.enabled = true;

            // Полные сутки за раз: фазы приходят по порядку, номер суток растёт.
            phases.Clear();
            cycle.Advance(cycle.Cycle.Length);
            Assert.That(phases, Is.EqualTo(new[] { DayPhase.Dawn, DayPhase.Day, DayPhase.Dusk, DayPhase.Night }));
            Assert.That(cycle.DayNumber, Is.EqualTo(2));
            cycle.PhaseChanged -= phases.Add;
        }

        [UnityTest]
        public IEnumerator PrototypeScene_FogCoversWorldOutsideLight()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var fog = Object.FindAnyObjectByType<LightFog>();
            var zone = Object.FindAnyObjectByType<BuildZone>();
            var camera = UnityEngine.Camera.main;
            var circles = new LightZone();
            zone.CollectCircles(circles);

            // Ищем на экране пиксель с землёй глубоко внутри света и пиксель далеко за ним.
            Vector2? inside = null, outside = null;
            for (var x = 0.05f; x < 1f; x += 0.05f)
            {
                for (var y = 0.05f; y < 1f; y += 0.05f)
                {
                    var ray = camera.ViewportPointToRay(new Vector3(x, y, 0f));
                    if (!Physics.Raycast(ray, out var hit, 1000f, Layers.GroundMask))
                        continue;
                    var radius = zone.Sources[0].Radius;
                    var toCenter = Vector2.Distance(new Vector2(hit.point.x, hit.point.z), new Vector2(zone.Sources[0].transform.position.x, zone.Sources[0].transform.position.z));
                    if (inside == null && toCenter < radius - 3f && toCenter > 2f)
                        inside = new Vector2(x, y);
                    if (outside == null && circles.DistanceOutside(hit.point) > 15f)
                        outside = new Vector2(x, y);
                }
            }
            Assert.That(inside, Is.Not.Null, "Камера не видит землю внутри света");
            Assert.That(outside, Is.Not.Null, "Камера не видит землю далеко за светом");

            var withFog = Render(camera);
            fog.enabled = false;
            var withoutFog = Render(camera);
            fog.enabled = true;

            var insideChange = Difference(withFog, withoutFog, inside.Value);
            var outsideChange = Difference(withFog, withoutFog, outside.Value);
            Assert.That(insideChange, Is.LessThan(0.02f), "Внутри света туман не рисуется");
            Assert.That(outsideChange, Is.GreaterThan(0.1f), "За светом туман виден");

            Object.Destroy(withFog);
            Object.Destroy(withoutFog);
        }

        private static Texture2D Render(UnityEngine.Camera camera)
        {
            var target = RenderTexture.GetTemporary(320, 180, 24);
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previous;

            var active = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
            return texture;
        }

        private static float Difference(Texture2D a, Texture2D b, Vector2 viewport)
        {
            var x = Mathf.RoundToInt(viewport.x * (a.width - 1));
            var y = Mathf.RoundToInt(viewport.y * (a.height - 1));
            var ca = a.GetPixel(x, y);
            var cb = b.GetPixel(x, y);
            return Mathf.Max(Mathf.Abs(ca.r - cb.r), Mathf.Abs(ca.g - cb.g), Mathf.Abs(ca.b - cb.b));
        }
    }
}
