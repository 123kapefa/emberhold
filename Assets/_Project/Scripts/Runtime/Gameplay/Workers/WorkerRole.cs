namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Класс поселенца. Постоянных ролей нет: рабочий свободный, пока не возьмёт задание, и получает класс задания
    /// (<see cref="WorkerRoles.For"/>), пока оно есть. Класс задаёт облик: цвет капюшона и пелерины, инструмент или оружие,
    /// снаряжение. Порядок — порядок в окне просмотра; новые роли добавлять в конец, номера записаны в данных.
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

    /// <summary>Какой класс даёт работа. Чистая логика, без сцены.</summary>
    public static class WorkerRoles
    {
        /// <summary>
        /// Класс по виду работы: копать — копатель, рубить и корчевать — лесоруб, чертежи блоков и костров (с подносом
        /// ресурса) — строитель, топливо — истопник.
        /// </summary>
        public static WorkerRole For(WorkKind kind) => kind switch
        {
            WorkKind.Dig => WorkerRole.Digger,
            WorkKind.Chop => WorkerRole.Woodcutter,
            WorkKind.Build or WorkKind.Campfire => WorkerRole.Builder,
            WorkKind.Fuel => WorkerRole.Stoker,
            _ => WorkerRole.Free,
        };
    }
}
