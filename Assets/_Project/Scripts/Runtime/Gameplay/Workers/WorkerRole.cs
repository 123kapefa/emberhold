namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Роль поселенца: задаёт облик (цвет капюшона и пелерины, инструмент или оружие, снаряжение) и какую работу он берёт
    /// (<see cref="WorkerRoles"/>). Порядок — порядок в окне просмотра; новые роли добавлять в конец, номера записаны в данных.
    /// </summary>
    public enum WorkerRole
    {
        Free,
        Woodcutter,
        Digger,
        Builder,
        Stoker,
        Swordsman,
        Archer,
        Spearman,
    }

    /// <summary>Какую работу берёт роль. Чистая логика, без сцены.</summary>
    public static class WorkerRoles
    {
        /// <summary>
        /// Берёт ли роль задачу вида kind: копатель — копать, лесоруб — рубить и корчевать, строитель — чертежи блоков и костров
        /// (с подносом ресурса), истопник — топливо, воины — ничего, свободный — всё.
        /// </summary>
        public static bool Takes(WorkerRole role, WorkKind kind) => role switch
        {
            WorkerRole.Free => true,
            WorkerRole.Digger => kind == WorkKind.Dig,
            WorkerRole.Woodcutter => kind == WorkKind.Chop,
            WorkerRole.Builder => kind is WorkKind.Build or WorkKind.Campfire,
            WorkerRole.Stoker => kind == WorkKind.Fuel,
            _ => false,
        };

        /// <summary>
        /// Носит ли роль ничейный ресурс itemId на поддон: добытое носит тот, кто добыл — лесоруб поленья (logItemId),
        /// копатель всё, что выпадает из блоков (остальное). Свободный носит всё, остальные роли — ничего.
        /// </summary>
        public static bool Hauls(WorkerRole role, string itemId, string logItemId) => role switch
        {
            WorkerRole.Free => true,
            WorkerRole.Woodcutter => itemId == logItemId,
            WorkerRole.Digger => itemId != logItemId,
            _ => false,
        };
    }
}
