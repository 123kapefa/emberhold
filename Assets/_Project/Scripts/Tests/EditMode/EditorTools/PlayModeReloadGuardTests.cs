using Emberhold.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Emberhold.Tests.EditMode.EditorTools
{
    public sealed class PlayModeReloadGuardTests
    {
        [TearDown]
        public void TearDown()
        {
            // Не оставлять редактор с заблокированной перезагрузкой, даже если тест упал.
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
        }

        [Test]
        public void NotLockedInEditMode()
        {
            Assert.That(PlayModeReloadGuard.IsLocked, Is.False);
        }

        [Test]
        public void EnteredPlayMode_LocksReload()
        {
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.That(PlayModeReloadGuard.IsLocked, Is.True);
        }

        [Test]
        public void ExitingPlayMode_UnlocksReload()
        {
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(PlayModeReloadGuard.IsLocked, Is.False);
        }

        [Test]
        public void RepeatedEnter_NeedsOneExitToUnlock()
        {
            // Блокировка в Unity считается по счётчику: лишний Lock оставил бы перезагрузку запертой навсегда.
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            PlayModeReloadGuard.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(PlayModeReloadGuard.IsLocked, Is.False);
        }
    }
}
