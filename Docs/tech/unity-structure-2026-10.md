> Снимок из облачного проекта (Claude Docs) от 2026-10-12, перенесён 2026-11-17 из бэкапа локальной истории. Дальше не обновлялся: нынешняя структура сборок и правила кода — в корневом `README.md`; при расхождении верен он.

# Emberhold — структура Unity-проекта и правила

Собрано 11.10.2026 из официальных гайдов Unity, документации Unity 6.6 и материалов разработчиков (ссылки внизу). Это рабочая шпаргалка: что создать, как назвать, что не забыть. Предложения из раздела 9 внесены в дизайн-документ 11.10.2026.

---

## 1. Главные выводы коротко

1. **Всё своё — в `Assets/_Project/`.** Подчёркивание держит папку наверху, сторонние ассеты и пакеты лежат отдельно и не перемешиваются с нашими.
2. **Скрипты — по системам (фичам), а не по типам.** Для кода так советуют почти все опытные разработчики; для арта (материалы, текстуры, звук) удобнее по типам.
3. **Assembly Definitions (`.asmdef`) с первого дня.** Быстрее перекомпиляция, нельзя случайно сделать циклическую зависимость, отдельные сборки для Editor и тестов.
4. **Нельзя называть папки/неймспейсы `Camera`, `Light`, `Time`.** Неймспейс `Emberhold.Light` перекрывает класс `UnityEngine.Light`, и внутри него код с `Light`, `Camera`, `Time.deltaTime` ломается или требует полных имён. Это прямая проблема текущей раскладки в дизайн-документе (см. раздел 3).
5. **Unity 6.6: перезагрузка домена при входе в Play Mode выключена по умолчанию.** Статические поля и подписки на статические события переживают перезапуск Play Mode. Unity советует так и оставить, а статику сбрасывать явно (раздел 5).
6. **Git настроить до первого коммита**: `.gitignore`, `.gitattributes` с LFS, `.meta`-файлы коммитить всегда, файлы перемещать только внутри Unity.
7. **Воксели: данные отдельно от отображения.** Чанк — это чистые данные (массив блоков) + один GameObject с мешем. Тогда переход на Jobs/Burst потом не потребует переписывать всё.

---

## 2. Рекомендуемое дерево проекта

```
Emberhold/                       ← корень git-репозитория = корень Unity-проекта
  .gitignore
  .gitattributes
  README.md
  Docs/                          ← дизайн-документ и эти заметки (вне Assets, см. раздел 9)
  Assets/
    _Project/
      Art/
        Materials/
        Shaders/                 ← шейдер тумана, свечения
        Textures/
        Models/
        VFX/
        Audio/
      Data/                      ← ScriptableObject-ассеты: типы блоков, источники света, враги, баланс
        Blocks/
        LightSources/
        Enemies/
        Balance/
      Prefabs/
        World/                   ← Chunk.prefab
        Lights/                  ← Campfire, Lantern, Brazier, Tower
        Workers/
        Enemies/
        UI/
      Scenes/
        Prototype.unity          ← основная сцена MVP
        Sandbox/                 ← тестовые сцены: камера, чанки, туман по отдельности
      Input/
        EmberholdControls.inputactions
      Scripts/
        Runtime/
          Core/                  Emberhold.Core.asmdef
          World/                 Emberhold.World.asmdef
          Gameplay/              Emberhold.Gameplay.asmdef
            Building/
            Lighting/
            Fog/
            DayNight/
            Workers/
            Enemies/
          CameraRig/             Emberhold.CameraRig.asmdef
          UI/                    (позже)
        Editor/                  Emberhold.Editor.asmdef
        Tests/
          EditMode/              Emberhold.Tests.EditMode.asmdef
          PlayMode/              Emberhold.Tests.PlayMode.asmdef
    Settings/                    ← создаёт шаблон Universal 3D (ассеты URP). Не удалять.
    Plugins/                     ← только если появятся нативные плагины
  Packages/
  ProjectSettings/
```

Что здесь важно:

- **Не создавать пустые папки «на будущее».** Git не хранит пустые папки, у других участников (или на другом компьютере) пропадёт папка, а Unity удалит её осиротевший `.meta`. Если папка нужна пустой, кладут файл `.keep`.
- **Никаких пробелов в именах** файлов и папок (`PascalCase`). Командная строка Unity и скрипты сборки плохо переносят пробелы.
- **`Settings/` от URP оставить там, где его создал шаблон.** В нём ассеты рендер-пайплайна; удаление ломает графику.
- Сторонние ассеты (если когда-нибудь появятся, даже бесплатные инструменты) не переносить внутрь `_Project`: некоторые зависят от своих путей.

---

## 3. Скрипты: системы, сборки, неймспейсы

### Почему переименовать папки из дизайн-документа

| В дизайн-документе | Проблема | Предложение |
| --- | --- | --- |
| `Scripts/Camera/` | `Emberhold.Camera` конфликтует с `UnityEngine.Camera` | `CameraRig/` → `Emberhold.CameraRig` |
| `Scripts/Light/` | конфликт с `UnityEngine.Light` | `Lighting/` → `Emberhold.Lighting` |
| `Scripts/Time/` | конфликт с `UnityEngine.Time` (`Time.deltaTime` перестанет компилироваться внутри неймспейса) | `DayNight/` → `Emberhold.DayNight` |
| `World/`, `Building/`, `Fog/`, `Workers/`, `Enemies/` | конфликтов нет | оставить |

Правило: **неймспейс повторяет путь папки** (`Scripts/Runtime/Gameplay/Lighting/LightSource.cs` → `namespace Emberhold.Lighting`). В Rider и VS Code это можно включить как автоподсказку.

### Сборки (asmdef) и направление зависимостей

```
Emberhold.Core        ← ничего не знает об игре: события, утилиты, математика сетки, общие интерфейсы
   ▲
Emberhold.World       ← чанки, блоки, меш-генерация, raycast по вокселям. Зависит только от Core
   ▲
Emberhold.Gameplay    ← Building, Lighting, Fog, DayNight, Workers, Enemies. Зависит от World и Core
   ▲
Emberhold.CameraRig   ← камера и ввод (можно и внутри Gameplay, если не хочется отдельной сборки)

Emberhold.Editor            ← только редакторные инструменты, платформа Editor
Emberhold.Tests.EditMode    ← юнит-тесты логики (Core, World, расчёт поля света)
Emberhold.Tests.PlayMode
```

- Для прототипа **4 рантайм-сборки достаточно**. Делать отдельную сборку на каждую систему (Fog, Workers…) пока рано: больше бюрократии, чем пользы. Внутри `Gameplay` системы разделены папками и неймспейсами; дробить можно потом, когда зависимости станут понятны.
- Внутри `Gameplay` есть естественные связи: Building спрашивает у Lighting «освещено ли место», Fog читает поле света, Workers боятся тьмы, Enemies ищут источники света. Чтобы это не превратилось в клубок, **все читают одну систему света** (как уже задумано в дизайн-документе: зона застройки и туман из списка активных `LightSource`), а события (огонь погас, наступила ночь) идут через Core.
- Подвох из документации: если в папке со своим `.asmdef` лежит подпапка `Editor`, её скрипты попадут в рантайм-сборку, а не в редакторную. Поэтому все редакторные скрипты — в `Scripts/Editor/` со своим asmdef.
- В asmdef тестов включить ссылки на `UnityEngine.TestRunner` / `UnityEditor.TestRunner` (Unity Test Framework).

### Данные — в ScriptableObject

Параметры, которые хочется крутить без перекомпиляции, оформляются как ScriptableObject-ассеты в `Data/`:

- `BlockDefinition` — тип блока, материал, прочность, горючесть.
- `LightSourceDefinition` — радиус, ёмкость топлива, расход в секунду. Компонент `LightSource` ссылается на определение.
- `EnemyDefinition` — скорость, здоровье, цель (ползун → костры).
- `DayNightConfig` — длительность фаз (сразу закрывает открытый вопрос «сколько длится день», его можно подбирать в редакторе).

Подвох: **не менять значения ScriptableObject-ассета во время игры**. В редакторе изменения сохранятся в файл. Значение копируют в рантайм-поле.

Новое в 6.6: `Dictionary<TKey, TValue>` с `[SerializeField]` теперь сериализуется и редактируется в инспекторе без самописных обёрток (удобно для таблиц «тип блока → данные»).

---

## 4. Настройки при создании проекта (чек-лист)

1. **Unity Hub → Unity 6.6 → шаблон Universal 3D**, имя проекта `Emberhold`, без пробелов в пути.
2. **Edit → Project Settings → Editor:**
   - Asset Serialization: **Force Text** (обычно уже по умолчанию — проверить). Сцены и префабы станут текстом, конфликты в git решаемы.
   - Version Control: **Visible Meta Files** (тоже по умолчанию — проверить).
   - Enter Play Mode Settings: оставить **Reload Scene only** (новый дефолт 6.6), см. раздел 5.
3. **Input:** в новых проектах Unity 6 включён пакет Input System и создаётся ассет действий. Использовать его, старый `Input.GetKey` не трогать. Ассет можно переименовать в `EmberholdControls` и перенести в `_Project/Input/`.
4. **Player → Other Settings:** Color Space Linear (у URP по умолчанию).
5. **Build Profiles → Scene List:** первой поставить основную сцену (`Prototype`).
6. **IDE на macOS:** JetBrains Rider (лучшая поддержка Unity) или VS Code с расширением Unity. Указать в Preferences → External Tools.
7. Intel-маки в 6.6 объявлены устаревшими; на Apple Silicon всё работает нативно, ничего делать не нужно.

---

## 5. Unity 6.6: статика без перезагрузки домена

Начиная с 6.6 в новых проектах при нажатии Play домен **не перезагружается**: статические поля и подписчики статических событий остаются с прошлого запуска. Unity прямо рекомендует так работать, потому что в будущих версиях механизм перезагрузки домена уберут.

Что это значит для кода:

- Любой `static`-кэш, синглтон (`Instance`), счётчик или `static event` нужно **сбрасывать явно**:
  ```csharp
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() { Instance = null; OnFireExtinguished = null; }
  ```
- В 6.6 есть атрибуты `[AutoStaticsCleanup]` / `[NoAutoStaticsCleanup]`, а Project Auditor ищет «опасную» статику (предупреждения с префиксом `UAL`).
- Отписываться от событий в `OnDisable`/`OnDestroy` — обязательно.
- Проще всего вообще **минимизировать статику**: системы (`LightSystem`, `TaskQueue`) — обычные объекты, которые создаёт и связывает один корневой объект сцены.

---

## 6. Сцены, префабы, иерархия

- **Сцена хранит расстановку, префаб хранит содержимое.** Костёр, рабочий, ползун, чанк — префабы. Правки делаются в префабе, а не в сцене.
- **Вложенные префабы и варианты** (Prefab Variants): `Lantern` может быть вариантом базового `LightSourceBase`.
- **Песочницы:** отдельные маленькие сцены в `Scenes/Sandbox/` для проверки камеры, вокселей, тумана по отдельности. Это быстро окупается.
- **Корневой объект-«композиция»** в сцене (`GameRoot`/`Bootstrapper`): создаёт системы в заданном порядке и передаёт им зависимости через `Initialize(...)`, а не через поиск в `Awake`. В `Awake` — только настройка самого себя. Отдельная сцена Bootstrap понадобится позже (меню, кампания из нескольких карт), для MVP хватит одной сцены.
- Иерархию сцены удобно делить пустыми объектами-разделителями: `--- Systems ---`, `--- World ---`, `--- Lights ---`, `--- Units ---`. В 6.6 новое окно Hierarchy по умолчанию умеет показывать колонки со слоями и тегами.
- **Resources-папку не использовать.** Всё, что в ней, попадает в билд целиком, а пути-строки ломаются молча. Для MVP хватает прямых ссылок в инспекторе. Если позже понадобится управляемая подгрузка — Addressables (в 6.6 они умеют новые content directories вместо AssetBundles).

---

## 7. Воксели: архитектурные советы для MVP

- **Не делать GameObject на каждый блок.** Чанк 16×16×16 = один GameObject с `MeshFilter`, `MeshRenderer`, `MeshCollider`.
- **Данные чанка — плоский массив** (`byte`/`ushort` id блока), индекс `x + 16 * (y + 16 * z)`. Класс `ChunkData` без MonoBehaviour, `ChunkView` — MonoBehaviour, который только строит меш. Это готовит код к Jobs/Burst: позже `ChunkData` переносится в `NativeArray`, а меш строится через `Mesh.AllocateWritableMeshData` в фоне.
- **Burst в 6.6 встроен в движок** (неймспейс `Unity.Burst`, пакет ставить не нужно). Для прототипа обычного C# хватит, как и записано в дизайн-документе.
- **Перестраивать только изменённый чанк** (и соседа, если блок на границе), а не весь мир.
- **Выбор блока мышью** быстрее и надёжнее делать собственным воксельным raycast (алгоритм DDA по сетке), а не через `MeshCollider`: не нужно ждать пересборки коллайдера.
- Индексный буфер `UInt16` держит до 65 535 вершин; для чанка 16³ с отсечением невидимых граней это с запасом. Если размер чанка вырастет — `IndexFormat.UInt32`.
- Поле света и туман удобно считать на **2D-сетке по колонкам** (x, z): одна запись на колонку «яркость/плотность тумана». Её читают и строительство, и шейдер тумана (через текстуру), и ИИ рабочих.
- Типы блоков — в реестре ScriptableObject (`BlockRegistry`), где id = индекс. В массиве чанка хранятся только id.

---

## 8. Git на macOS

**`.gitignore`** — взять официальный шаблон GitHub `Unity.gitignore` и добавить строки для macOS и редакторов:

```
# из шаблона github/gitignore (главное)
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/
*.csproj
*.sln
*.slnx
.vs/
*.DotSettings.user
/[Aa]ssets/Plugins/Editor/JetBrains*
/[Mm]emoryCaptures/
/[Rr]ecordings/
*.log
# добавить
.DS_Store
.idea/
```

**`.gitattributes` с Git LFS** для бинарников (по расширениям, а не по папкам, иначе в LFS попадут и текстовые `.meta`):

```
*.png filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.tga filter=lfs diff=lfs merge=lfs -text
*.exr filter=lfs diff=lfs merge=lfs -text
*.fbx filter=lfs diff=lfs merge=lfs -text
*.blend filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text
*.ogg filter=lfs diff=lfs merge=lfs -text
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.ttf filter=lfs diff=lfs merge=lfs -text
# сцены и префабы — текст; конфликты решать через git mergetool (ниже)
*.unity text eol=lf
*.prefab text eol=lf
*.asset text eol=lf
```

**Smart Merge (UnityYAMLMerge)** — в `~/.gitconfig`; путь при установке через Unity Hub:

```
[merge]
    tool = unityyamlmerge
[mergetool "unityyamlmerge"]
    cmd = '/Applications/Unity/Hub/Editor/<версия>/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"
    trustExitCode = false
    keepBackup = false
```

Правила:

- **`.meta` коммитить всегда**, вместе с файлом. В них GUID и настройки импорта; без них ссылки в сценах рвутся.
- **Переименовывать и перемещать только в окне Project в Unity**, не в Finder. Иначе `.meta` отстанет, и все ссылки на ассет сломаются.
- Сейчас разработка соло, конфликтов в сценах почти не будет, но привычка «правлю префаб, а не сцену» пригодится.

---

## 9. Изменения в дизайн-документе

Внесены 11.10.2026 по согласию автора:

1. Переименовать папки `Camera` → `CameraRig`, `Light` → `Lighting`, `Time` → `DayNight` (конфликты с классами Unity).
2. Перенести `Docs/` из `Assets/` в корень репозитория: `.md` внутри `Assets` импортируется как TextAsset, получает `.meta` и не нужен игре.
3. Добавить в структуру `Data/` (ScriptableObject), `Input/`, `Scripts/Editor/`, `Scripts/Tests/`, `Scenes/Sandbox/` и разделение `Scripts/Runtime/...` с asmdef.
4. В план MVP первым пунктом добавить «настроить git (.gitignore, LFS) и asmdef», до кода.
5. В технические заметки — «Unity 6.6 без перезагрузки домена: никакой несброшенной статики».

---

## Источники

- Unity, «Best practices for organizing your Unity project»: https://unity.com/how-to/organizing-your-project
- Unity 6.6 Manual, Introduction to assemblies: https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/script-compilation/assembly-definition-files/assembly-definitions-intro.md
- Unity 6.6 Manual, Reserved folder names: https://docs.unity.com/en-us/engine/6000.6/manual/get-started/project-configuration/special-folders.md
- Unity 6.6, What's new (домен-релоад выключен, Burst встроен, сериализация Dictionary, content directories): https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity66.md
- Unity Manual, Enter Play mode with domain reload disabled: https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/code-reload-serialization/code-reloading-editor/domain-reloading.md
- Unity, «Architect game code with ScriptableObjects» (по докладу Ryan Hipple, Unite 2017): https://unity.com/how-to/architect-game-code-scriptable-objects
- Unity Blog, «Content directories: Beyond the AssetBundle»: https://unity.com/blog/content-directories-beyond-the-assetbundle
- Samuel Asher Rivello, «Unity Project Structure» (дерево Runtime/Editor/Tests с asmdef): https://samuel-asher-rivello.medium.com/unity-project-structure-a694792cefed
- Обсуждение «Ordering scripts by type or by system» (Unity Discussions): https://discussions.unity.com/t/ordering-scripts-by-type-or-by-system/924973
- uhiyama-lab: структура сцен и папок, Bootstrap/Composition Root, Resources vs Addressables:
  https://uhiyama-lab.com/en/notes/unity/unity-project-organization-scene-folder/ ,
  https://uhiyama-lab.com/en/notes/unity/unity-bootstrap-scene-composition-root/ ,
  https://uhiyama-lab.com/en/notes/unity/unity-addressables-asset-management-guide/
- Game Developer, «The complete guide to Unity + Git»: https://gamedeveloper.com/programming/the-complete-guide-to-unity-git
- GitHub, Unity.gitignore: https://github.com/github/gitignore/blob/main/Unity.gitignore
- Unity Scripting API, Mesh.AllocateWritableMeshData: https://docs.unity3d.com/ScriptReference/Mesh.AllocateWritableMeshData.html
