namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Подпись уровня приказа над его рамкой, пока у приказов нет своего вида: «высокий», «срочный».
    /// Обычный приказ без подписи. Топливо для огня игрок не ставит и уровень не меняет — над огнём подписи нет.
    /// </summary>
    public static class PriorityLabel
    {
        /// <summary>Подпись уровня приказа или null, если подписывать нечего.</summary>
        public static string Text(WorkOrder order) =>
            order.Kind == WorkKind.Fuel ? null : Text(order.Priority);

        public static string Text(OrderPriority priority) => priority switch
        {
            OrderPriority.High => "высокий",
            OrderPriority.Urgent => "срочный",
            _ => null,
        };
    }
}
