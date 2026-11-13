namespace Emberhold.Gameplay.Workers
{
    /// <summary>Чем занят рабочий, крупно: по этому вид выбирает цвет подписи и линии пути.</summary>
    public enum WorkerActivity
    {
        /// <summary>Без дела.</summary>
        Idle,
        /// <summary>Чертёж блока.</summary>
        Build,
        /// <summary>Приказ копать.</summary>
        Dig,
        /// <summary>Приказ рубить или корчевать.</summary>
        Chop,
        /// <summary>Топливо в огонь.</summary>
        Fuel,
        /// <summary>Чертёж костра.</summary>
        Campfire,
        /// <summary>Относит ничейный ресурс на поддон.</summary>
        Haul,
        /// <summary>Бежит из темноты к свету.</summary>
        Flee,
    }

    /// <summary>
    /// Что делает рабочий, словами для подписи над головой («идёт за бревном», «несёт камень к чертежу», «копает»).
    /// Чистая логика: только читает состояние и задачу рабочего, ничего в нём не меняет. Рисует подпись WorkerStatusView.
    /// </summary>
    public static class WorkerStatus
    {
        public static WorkerActivity Activity(Worker worker) =>
            Activity(worker.State, worker.Order?.Kind, worker.HaulTarget != null);

        public static WorkerActivity Activity(WorkerState state, WorkKind? kind, bool toPallet)
        {
            if (state == WorkerState.Fleeing)
                return WorkerActivity.Flee;
            if (toPallet)
                return WorkerActivity.Haul;
            return kind switch
            {
                WorkKind.Build => WorkerActivity.Build,
                WorkKind.Dig => WorkerActivity.Dig,
                WorkKind.Chop => WorkerActivity.Chop,
                WorkKind.Fuel => WorkerActivity.Fuel,
                WorkKind.Campfire => WorkerActivity.Campfire,
                _ => WorkerActivity.Idle,
            };
        }

        public static string Text(Worker worker) =>
            Text(worker.State, worker.Order?.Kind, worker.Order?.Stump != null, worker.WantedItemId, worker.Carrying?.ItemId,
                worker.HaulTarget != null);

        /// <summary>
        /// Подпись по состоянию и задаче. kind — вид задачи или null; stump — приказ корчевать пень; wantedItemId — за каким ресурсом
        /// идёт; carryingItemId — что несёт; toPallet — несёт ничейный ресурс на поддон.
        /// </summary>
        public static string Text(WorkerState state, WorkKind? kind, bool stump, string wantedItemId, string carryingItemId, bool toPallet)
        {
            switch (state)
            {
                case WorkerState.Fleeing:
                    return "бежит к свету";
                case WorkerState.ToFire:
                    return "идёт к огню";
                case WorkerState.ToPile:
                    return wantedItemId != null ? $"идёт за {Instrumental(wantedItemId)}" : "идёт";
                case WorkerState.PickingUp:
                    return wantedItemId != null ? $"берёт {Accusative(wantedItemId)}" : "берёт";
                case WorkerState.ToSite:
                    if (carryingItemId != null)
                        return $"несёт {Accusative(carryingItemId)} {Where(kind, toPallet)}";
                    return kind switch
                    {
                        WorkKind.Dig => "идёт копать",
                        WorkKind.Chop => stump ? "идёт корчевать" : "идёт рубить",
                        WorkKind.Build or WorkKind.Campfire => "идёт строить",
                        _ => "идёт",
                    };
                case WorkerState.Putting:
                    return toPallet ? "кладёт на поддон" : kind == WorkKind.Fuel ? "кладёт в костёр" : "кладёт на чертёж";
                case WorkerState.Working:
                    return kind switch
                    {
                        WorkKind.Dig => "копает",
                        WorkKind.Chop => stump ? "корчует" : "рубит",
                        WorkKind.Campfire => "ставит костёр",
                        _ => "строит",
                    };
                default:
                    return "без дела";
            }
        }

        private static string Where(WorkKind? kind, bool toPallet)
        {
            if (toPallet)
                return "на поддон";
            return kind switch
            {
                WorkKind.Fuel => "к костру",
                WorkKind.Campfire => "к чертежу костра",
                _ => "к чертежу",
            };
        }

        /// <summary>«за чем»: бревном, камнем, травой; у незнакомого ресурса — его имя из <see cref="PalletLabel"/>.</summary>
        private static string Instrumental(string itemId) => itemId switch
        {
            "wood" => "бревном",
            "stone" => "камнем",
            "grass" => "травой",
            _ => PalletLabel.ItemName(itemId),
        };

        /// <summary>«что»: бревно, камень, траву.</summary>
        private static string Accusative(string itemId) => itemId switch
        {
            "grass" => "траву",
            _ => PalletLabel.ItemName(itemId),
        };
    }
}
