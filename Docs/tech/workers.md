# Рабочие, приказы, задания, зоны

Папка `Assets/_Project/Scripts/Runtime/Gameplay/Workers`. Логика — чистые классы без сцены (тестируются в EditMode), вид — отдельные компоненты.

- `WorkOrders` — все приказы (`WorkKind`: Build, Dig, Chop, Fuel, Campfire); у приказа уровень `OrderPriority` и задание `Assignment`.
- `WorkAssignment` — задание: приказы и кто их держит, `MaxWorkers`. Одиночный приказ — своё задание из одного приказа; зона — задание из многих (#161).
- `Worker` — берёт свободный приказ (высший уровень, потом ближайший), держит задание, пока в нём есть приказы; класс `Role` — из задания/приказа (`WorkerRoles.For`); добычу несёт сам (`WorkerContext.MinedBy`); без дела — к огню.
- `UrgentCall` — срочные приказы снимают ближайшего подходящего рабочего (#163).
- `WorkZones` — зоны (`ChopZone`, `LevelZone`): черновик, границы, ручки, `Update` раз в кадр ставит приказы (#167, #136). Вид — `WorkZoneView`, формы объёмов — `LevelZoneShape`, ручки — `ZoneHandleLayout`.
- `WorkerCrew` — сцена: спавн, вид рабочих, связь с инструментами (`Building/BlockEditController`).
