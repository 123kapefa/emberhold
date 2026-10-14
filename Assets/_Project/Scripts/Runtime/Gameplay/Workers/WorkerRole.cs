namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Роль поселенца. Пока определяет только облик: цвет капюшона и пелерины, инструмент или оружие, снаряжение.
    /// Порядок — порядок в окне просмотра; новые роли добавлять в конец, номера записаны в данных.
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
}
