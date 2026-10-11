using System.Collections;
using Emberhold.CameraRig;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberhold.Tests.PlayMode.CameraRig
{
    public sealed class CameraRigPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_CameraRigStartsAndLooksAtFocus()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rig = Object.FindAnyObjectByType<CameraRigController>();
            Assert.That(rig, Is.Not.Null, "В сцене Prototype нет CameraRigController");
            Assert.That(rig.enabled, Is.True, "Риг выключился: скорее всего не найдена карта ввода Camera");

            var cam = UnityEngine.Camera.main;
            var target = rig.Target;
            var toFocus = target.Focus - cam.transform.position;
            Assert.That(toFocus.magnitude, Is.EqualTo(target.Distance).Within(0.5f));
            Assert.That(Vector3.Angle(cam.transform.forward, toFocus), Is.LessThan(1f));
        }
    }
}
