# Рабочие, приказы, задания, зоны

Папка `Assets/_Project/Scripts/Runtime/Gameplay/Workers`. Логика — чистые классы без сцены (тестируются в EditMode), вид — отдельные компоненты.

- `WorkOrders` — все приказы (`WorkKind`: Build, Dig, Chop, Fuel, Campfire, Gather); у приказа уровень `OrderPriority` и задание `Assignment`.
- `WorkAssignment` — задание: приказы и кто их держит, `MaxWorkers`. Одиночный приказ — своё задание из одного приказа; зона — задание из многих (#161).
- `Worker` — берёт свободный приказ (высший уровень, потом ближайший), держит задание, пока в нём есть приказы; класс `Role` — из задания/приказа (`WorkerRoles.For`); добычу несёт сам (`WorkerContext.MinedBy`); без дела — к огню.
- `UrgentCall` — срочные приказы снимают ближайшего подходящего рабочего (#163).
- `WorkZones` — зоны (`ChopZone`, `GatherZone`, `LevelZone`): черновик, границы, ручки, `Update` раз в кадр ставит приказы (#167, #136). Вид — `WorkZoneView`, формы объёмов — `LevelZoneShape`, ручки — `ZoneHandleLayout`.
- `WorkerCrew` — сцена: спавн, вид рабочих, связь с инструментами (`Building/BlockEditController`).

## Кусты и сбор травы (#88)
- Модель — `Runtime/World/Bushes`: `BushSpecies` (ассет `Data/Bushes/Bush.asset`: прочность 50, выход 3 травы, `regrowDays` 2, вид), `Bush` (клетка, `IsGrown`, `RegrowLeft`), `Thicket` — все кусты: `Hit` срезает траву (кучка в `DroppedItems`, куст голый), `Grow(days)` растит голые, `Settle` убирает кусты без земли, `BlocksPlace`, `Raycast`.
- `Thicket` живёт в `Forest.Thicket`: так кусты доходят до рабочих (`WorkerContext.Forest`), зон (`ZoneRefresh.Forest`) и редактора без новых ссылок в сцене. `Forest.Settle` зовёт `Thicket.Settle`, `Forest.BlocksEdit` запрещает ставить блок в клетку куста (копать под ним можно). Наращивание зоны выравнивания клетку куста тоже пропускает.
- Разброс — `BushScatter` по `ForestMix.Bushes` (`BushMix`: густота и виды) в профиле местности: Lowland (пойма) 0.05, TerracedHills (сосновые холмы) 0.008, Valley (прототип) 0.012. Сажает после леса, не в столбец дерева; у каждого `ForestStart` — до `StartBushes` кустов в кольце 0.45–0.75 радиуса света. Мелочь на земле (`GroundCoverScatter`) в столбец куста не садится.
- Вид — `ForestView`: сетка `BushMeshBuilder` (зелёная — ветки и шапка листвы с a = 1 под оттенок травы, голая — только ветки), меняется по `Thicket.Version`. Время отрастания — по часам `DayNightCycle` (номер суток × длина + время суток, поэтому пропуск фазы N тоже растит), без смены суток — сутки 720 с.
- Приказ — `WorkKind.Gather`, `WorkOrders.AddGather(bush)` (только зелёный куст), `WorkOrder.Bush`; валиден, пока куст стоит и зелёный. Рабочий бьёт куст ударами копки (`Thicket.Hit` с `context.Miner`), трава записывается на него (`MinedBy`), и он несёт её на поддон.
- Класс — `WorkerRole.Gatherer` (`WorkerRoles.For(Gather)`): капюшон в `WorkerSettings.roleHoods`, снаряжение — сумка с травой на правом боку (`WorkerGearBuilder.GrassPouch`), руки свободны. Ноша травы — `WorkerCarry.Sheaf`: сноп на плече в габарите бревна, снимается тем же путём (`WorkerCarryBuilder.OnShoulder`); сноп носят свободные и сборщик со своей травой.
- Зона — `GatherZone : CircleZone` (общая база с `ChopZone`: центр, радиус, ручки, `SetRadius`/`Move`/`SetRadiusTo`). Ставит приказы только на зелёные кусты в круге и не уходит сама. Инструмент — `BlockTool.GatherZone`, действие `Building/ToolGatherZone` на H; куст под курсором — `BlockEditController.HoveredBush` (инструменты 5 и H).
