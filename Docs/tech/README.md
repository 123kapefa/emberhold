# Техническая документация Emberhold

Здесь — всё техническое: код, Unity, тесты, процесс. Про игру — в `Docs/global/`. Структура сборок и правила кода — в корневом `README.md`.

## Тесты и плейтест
- Только `Tools/unity-tests.sh <ветка> --name <карточка>` из основной папки: общий замок, одна папка `.worktrees/unity-tests`.
- Поиграть ветку: `Tools/unity-tests.sh --playtest <ветка>` → `.worktrees/playtest`, открыть в Unity Hub.
- Пересохранения Unity в материалах и ProjectSettings не откатывать (#160).

## Рабочие и задания (кратко)
`WorkOrders` (приказы) → `WorkAssignment` (задание: приказы + люди, MaxWorkers) → `Worker` (берёт приказ, класс из задания). Зоны — `WorkZones` (`ChopZone`, `LevelZone`), вид — `WorkZoneView`, отдельно от логики.

## Процесс
Задача из карточки → ветка → тесты зелёные → PR (Closes #N) → ревью → мерж. Решения по механикам дописывать в `Docs/global/` в том же PR.
