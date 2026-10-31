using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Emberhold.CameraRig;
using Emberhold.Gameplay.Trees;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.Trees
{
    /// <summary>
    /// Замер для карточки #151: что стоит кадр, когда камера отъезжает от земли и возвращается. Ничего не
    /// проверяет, только пишет в лог строки [Perf151]. Временный: уберётся, когда причина просадок найдена.
    /// </summary>
    public sealed class ZoomPerformanceProbe
    {
        [UnityTest]
        public IEnumerator Zoom_MeasureFrameCost()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            for (var i = 0; i < 5; i++)
                yield return null;

            foreach (var rig in Object.FindObjectsByType<CameraRigController>())
                rig.enabled = false;
            var view = Object.FindAnyObjectByType<GroundCoverView>();
            var camera = Camera.main.transform;
            var start = camera.position;
            var back = -camera.forward;

            foreach (var coverOn in new[] { true, false, true })
            {
                view.enabled = coverOn;
                foreach (var distance in new[] { 0f, 240f })
                {
                    var label = $"{(coverOn ? "cover" : "nocover")} {(distance == 0f ? "out" : "in")}";
                    var from = distance;
                    var to = 240f - distance;
                    yield return Measure(label, view, frame =>
                    {
                        var t = Mathf.Clamp01(frame / 90f);
                        camera.position = start + back * Mathf.Lerp(from, to, t);
                    }, 150);
                }
            }
            camera.position = start;
            Assert.Pass();
        }

        private static IEnumerator Measure(string label, GroundCoverView view, System.Action<int> move, int frames)
        {
            using var late = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "PostLateUpdate.ScriptRunBehaviourLateUpdate", frames + 4);
            using var loop = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", frames + 4);
            using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", frames + 4);
            var lateMs = new List<double>();
            var loopMs = new List<double>();
            var levels = new List<string>();
            var level = view.ThinningLevel;
            long gcTotal = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                move(frame);
                yield return null;
                lateMs.Add(late.LastValue / 1e6);
                loopMs.Add(loop.LastValue / 1e6);
                gcTotal += gc.LastValue;
                if (view.ThinningLevel != level)
                {
                    level = view.ThinningLevel;
                    levels.Add($"f{frame}->L{level}");
                }
            }
            string Stats(List<double> v) =>
                $"max {v.Max():F2} avg {v.Average():F2} p95 {v.OrderBy(x => x).ElementAt((int)(v.Count * 0.95)):F2} >8ms {v.Count(x => x > 8)}";
            Debug.Log($"[Perf151] {label}: LateUpdate {Stats(lateMs)} | PlayerLoop {Stats(loopMs)} | GC {gcTotal / 1024} KB | regions {view.ShownRegions} | levels {string.Join(" ", levels)}");
        }
    }
}
