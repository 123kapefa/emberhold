> Снимок из облачного проекта (Claude Docs) от 2026-10-12, перенесён 2026-11-17 из бэкапа локальной истории. Дальше не обновлялся: этапы, поля и правила работы с доской сейчас — в [workflow.md](workflow.md); при расхождении верен он.

# GitHub Projects для Emberhold

> Статус: **создано 2026-10-11.**
> Репозиторий: https://github.com/123kapefa/emberhold (приватный, `origin` для ~/Documents/projects/emberhold)
> Проект: https://github.com/users/123kapefa/projects/6 (приватный, связан с репозиторием)
> Milestone «MVP: ядро» = #1, карточки MVP = issues #1–#11 (#1–#2 закрыты).

## Что создаём

| Что | Где | Зачем |
| --- | --- | --- |
| Приватный репозиторий `123kapefa/emberhold` | GitHub, из локального репо на Mac (ветка `main`) | Карточки — это issues, им нужен репозиторий. Заодно код будет храниться не только на Mac |
| Проект **Emberhold** | GitHub Projects, личный аккаунт `123kapefa`, приватный | Доска задач |

Карточка = issue в репозитории `emberhold`. Черновики (draft items) не используем: их нельзя назначить, связать с коммитом и закрыть коммитом.

## Типы карточек — метки `type:*`

Настоящие «Issue types» GitHub даёт только организациям, для личного аккаунта используем метки.

| Метка | Когда |
| --- | --- |
| `type:feature` | Новая игровая механика или система (костёр, туман, рабочие) |
| `type:bug` | Что-то работает не так |
| `type:tech` | Инфраструктура, рефакторинг, инструменты, производительность, настройки проекта |
| `type:design` | Геймдизайн: решение открытого вопроса, баланс, правка дизайн-документа |
| `type:art` | Визуал, шейдеры, звук, VFX |
| `type:research` | Изучить вопрос и записать вывод (spike) |

## Области — метки `area:*`

Повторяют сборки и папки из `docs/unity-project-structure.md`, чтобы по карточке было видно, какой код она трогает:
`area:core`, `area:world`, `area:camera`, `area:building`, `area:lighting`, `area:fog`, `area:daynight`, `area:workers`, `area:enemies`, `area:ui`, `area:editor`, `area:infra` (git, CI, настройки Unity).

## Этапы — milestones

- **MVP: ядро** — 11 пунктов из «Плана прототипа» дизайн-документа, по карточке на пункт. Пункты 1–2 (проект и git) заводятся сразу закрытыми.
- Следующие этапы (например «Вертикальный срез: первая башня») добавляем, когда дойдём.

## Поля проекта

| Поле | Значения | Кто ставит |
| --- | --- | --- |
| **Status** | Backlog → Ready → In progress → In review → Done | Автоматически (см. ниже) + вручную |
| **Priority** | P0 (блокирует) / P1 (нужно в этапе) / P2 (можно позже) | Вы при планировании |
| **Size** | S (до часа) / M (вечер) / L (несколько дней; лучше разбить) | Вы при планировании |

Итерации (спринты) не заводим: для соло-проекта хватает этапов и приоритета.

## Представления (views)

1. **Доска** — Board по Status, только текущий milestone.
2. **Бэклог** — таблица, группировка по milestone, сортировка по Priority.
3. **Баги** — таблица, фильтр `label:type:bug is:open`.

## Автоматика (встроенные workflows проекта)

- Auto-add: все issues и PR из `123kapefa/emberhold` сами попадают на доску.
- Item added → **Backlog**.
- Pull request linked to issue → **In progress**.
- Pull request merged / Item closed → **Done**.
- Item reopened → **Ready**.

## Шаблоны issues в репозитории

`.github/ISSUE_TEMPLATE/`: «Фича», «Баг», «Тех. задача». В каждом: цель, критерии готовности (чек-лист), ссылка на раздел дизайн-документа.

## Правила для Claude (после настройки)

1. Перед началом любой задачи — найти карточку или создать issue: заголовок глаголом («Камера: вращение, зум, панорамирование»), метки `type:*` + `area:*`, milestone, критерии готовности.
2. Назначить issue на `123kapefa`.
3. Ветка `issue-<номер>-<кратко>`, в коммитах и PR — `Closes #<номер>`; после слияния карточка уходит в Done сама.
4. Если задача оказалась больше — разбить на sub-issues.
5. Priority и Size Claude предлагает в тексте issue, а ставите их вы на доске.

## Ограничения, о которых важно знать

- Из облачных потоков доступны только API репозитория: issues, метки, milestones, PR. Создать проект, поля и менять поля карточек оттуда нельзя (GitHub GraphQL и API проектов закрыты). Поэтому Status двигается встроенной автоматикой, а Priority/Size — вручную.
- Представления и встроенные workflows GitHub не позволяет создавать через API — их включаете вы в интерфейсе по чек-листу (около 5 минут).

## Ручная настройка (один раз, в интерфейсе GitHub) — выполнено 2026-10-11

Проект → `…` → **Workflows**:
- [x] **Auto-add to project**: Edit → фильтр `is:issue,pr is:open`, репозиторий `123kapefa/emberhold`.
- [x] **Item added to project** → Status: Backlog.
- [x] **Item closed** → Status: Done (перевыбрать Done: workflow ссылается на старую опцию).
- [x] **Pull request merged** → Status: Done.
- [x] **Pull request linked to issue** → Status: In progress.
- [x] **Item reopened** → Status: Ready.
- [x] **Auto-close issue**: когда Status = Done → закрыть issue.
- Auto-add sub-issues включено; Auto-archive, Code changes requested, Code review approved выключены.

Представления созданы через API: «Все задачи», «Доска» (Board по Status, `milestone:"MVP: ядро"`), «Бэклог» (таблица, группа по Milestone, сортировка по Priority), «Баги» (`label:type:bug is:open`).

Облачные потоки Claude видят репозиторий через `list_repos` и подключают его к своей сессии через `add_repo` (`123kapefa/emberhold`), после чего создают issues через GitHub MCP / REST. Менять поля проекта (Priority, Size, Status вручную) из облака нельзя — только через сессию на Mac (`gh project item-edit`) или вручную.
