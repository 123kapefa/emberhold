using UnityEditor;

namespace Emberhold.Editor
{
    /// <summary>
    /// Пока идёт Play, перезагрузка сборок ждёт выхода из игры. Иначе изменения скриптов, пришедшие во время Play
    /// (git pull, мерж, правка кода), перезагружают сборки прямо в игре: несериализуемые поля компонентов
    /// сбрасываются, Awake заново не вызывается, и системы падают с NullReferenceException каждый кадр.
    /// Скрипты при этом компилируются как обычно, новая версия подхватывается сразу после выхода из Play.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeReloadGuard
    {
        /// <summary>Перезагрузка сборок сейчас отложена этой защитой.</summary>
        public static bool IsLocked { get; private set; }

        static PlayModeReloadGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Если домен перезагрузился при входе в Play, конструктор уже в игре.
            if (EditorApplication.isPlaying)
                Lock();
        }

        public static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    Lock();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    Unlock();
                    break;
            }
        }

        private static void Lock()
        {
            if (IsLocked)
                return;
            EditorApplication.LockReloadAssemblies();
            IsLocked = true;
        }

        private static void Unlock()
        {
            if (!IsLocked)
                return;
            IsLocked = false;
            EditorApplication.UnlockReloadAssemblies();
        }
    }
}
