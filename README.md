# Emberhold

Unity-проект игры Emberhold. Дизайн-документ и прочие материалы лежат в `Docs/`.

- Unity **6000.6.5f1** (Unity 6.6), Universal Render Pipeline (шаблон Universal 3D).
- Новый Input System, Linear color space, Force Text сериализация, видимые `.meta`.
- Стартовая сцена: `Assets/_Project/Scenes/Prototype.unity` (первая в Build Profiles).

## Структура

```
Docs/                         документация (вне Assets)
Assets/
  Settings/                   URP-ассеты из шаблона
  _Project/                   всё своё; сторонние ассеты кладём рядом, вне _Project
    Art/                      Materials, Shaders, Textures, Models, VFX, Audio
    Data/                     ScriptableObject-данные: Blocks, LightSources, Enemies, Balance
    Prefabs/                  World, Lights, Workers, Enemies, UI
    Scenes/                   Prototype.unity, Sandbox/ для экспериментов
    Input/                    EmberholdControls.inputactions
    Scripts/
      Runtime/Core            Emberhold.Core        (без зависимостей)
      Runtime/World           Emberhold.World       -> Core
      Runtime/Gameplay        Emberhold.Gameplay    -> World, Core
                              Building, Lighting, Fog, DayNight, Workers, Enemies
      Runtime/CameraRig       Emberhold.CameraRig   -> Gameplay, World, Core
      Editor                  Emberhold.Editor      (только Editor)
      Tests/EditMode          Emberhold.Tests.EditMode
      Tests/PlayMode          Emberhold.Tests.PlayMode
```

Правила:

- Namespace повторяет папку: `Emberhold.Core`, `Emberhold.World`, `Emberhold.Lighting`,
  `Emberhold.Fog` и т. д. (у Gameplay корневой namespace `Emberhold`, подпапки дают
  `Emberhold.<Подпапка>`).
- Не называть папки и namespace `Camera`, `Light`, `Time`: они перекрывают типы UnityEngine.
- Внутри `Emberhold.Editor` базовый класс инспектора писать полностью: `UnityEditor.Editor`.
- Пустые обязательные папки держатся в git файлом `.keep`. Папку `Resources` не заводим.
- Бинарные ассеты (png, psd, tga, exr, fbx, blend, wav, ogg, mp3, ttf) идут через Git LFS.

## Статика и Play Mode

Enter Play Mode настроен на **Reload Scene only**: при нажатии Play домен не
перезагружается, поэтому статические поля, синглтоны и статические события сохраняют
значения с прошлого запуска. Правило:

- сбрасывать статику в методе с
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`;
- отписываться от событий в `OnDisable` / `OnDestroy`.

```csharp
public static class GameEvents
{
    public static event System.Action NightStarted;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => NightStarted = null;
}
```

## Тесты и плейтест на Маке

Unity на Маке запускается строго по одному. Тесты ветки гоняются командой из основной папки:

```bash
Tools/unity-tests.sh <ветка> --name <номер карточки>
Tools/unity-tests.sh --status <номер карточки>
```

Прогон идёт в служебной папке `.worktrees/unity-tests` (одна на все прогоны, в `.gitignore`),
итог пишется в `Logs/unity-tests/<имя>/summary.txt`. Поиграть в ветку:
`Tools/unity-tests.sh --playtest <ветка>` ставит `.worktrees/playtest`, её открывают в Unity Hub.
Рядом с проектом, в `~/Documents/projects`, скрипт ничего не создаёт.
