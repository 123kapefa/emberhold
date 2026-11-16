# Тесты и плейтест на Маке

- Unity на Маке — строго один на всех. Тесты ветки — только так, из основной папки `emberhold`:
  `Tools/unity-tests.sh <ветка> --name <карточка>`; ход и итог — `Tools/unity-tests.sh --status <карточка>`, файлы в `Logs/unity-tests/<имя>/`.
- Скрипт уходит в фон, ждёт общий замок `/tmp/emberhold-unity.lock`, гоняет EditMode и PlayMode в одной служебной папке `.worktrees/unity-tests` (в `.gitignore`, Library сохраняется). Правки, которые Unity пересохраняет, сбрасывает сам (`discarded.diff`) (#159).
- Поиграть ветку: `Tools/unity-tests.sh --playtest <ветка>` ставит `.worktrees/playtest`, её открывают в Unity Hub. Если там открыт Unity — скрипт остановится.
- Рядом с проектом, в `~/Documents/projects`, ничего не создаётся.
- Пересохранения Unity в материалах (`disabledShaderPasses`) и `ProjectSettings` не откатывать: проект хранит их в виде Unity 6.6 (#160).
- После смены `EmberholdControls.inputactions` открытый редактор может ругаться «Action count / Binding count…» — перезапустить Unity.
- Снимки из Unity агенты не делают: в отчёте пишут, что проверить самому (сцена, клавиши, на что смотреть).
