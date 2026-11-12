using System.Collections.Generic;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Срочные приказы: на каждый свободный срочный приказ идёт ближайший подходящий рабочий (<see cref="Worker.CanAnswer"/>),
    /// даже если он занят обычной или высокой задачей — он её бросает, ноша падает у ног, задачу подхватит другой.
    /// Чистая логика, без сцены; отряд вызывает её раз в <see cref="WorkerSettings.IdleSearchSeconds"/>.
    /// </summary>
    public static class UrgentCall
    {
        /// <summary>Разослать рабочих по свободным срочным приказам. Возвращает, сколько рабочих отправлено.</summary>
        public static int Dispatch(WorkOrders orders, IReadOnlyList<Worker> workers)
        {
            var sent = 0;
            for (var i = 0; i < orders.Orders.Count; i++)
            {
                var order = orders.Orders[i];
                if (order.Priority != OrderPriority.Urgent || order.Worker != null)
                    continue;
                Worker nearest = null;
                var nearestCost = float.PositiveInfinity;
                foreach (var worker in workers)
                {
                    if (!worker.CanAnswer(order))
                        continue;
                    var cost = worker.CostTo(order);
                    if (cost >= nearestCost)
                        continue;
                    nearest = worker;
                    nearestCost = cost;
                }
                if (nearest != null && nearest.Answer(order))
                    sent++;
            }
            return sent;
        }
    }
}
