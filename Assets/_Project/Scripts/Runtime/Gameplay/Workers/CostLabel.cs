using Emberhold.World.Items;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Подписи цены постройки, пока у неё нет своего вида (#150): цена в подсказке инструмента — «6 брёвен»,
    /// ход сборки над чертежом — «бревно 2/6».
    /// </summary>
    public static class CostLabel
    {
        /// <summary>Сколько и чего: «1 бревно», «3 бревна», «6 брёвен».</summary>
        public static string Amount(ItemDrop cost) => $"{cost.Count} {CountedName(cost.ItemId, cost.Count)}";

        /// <summary>Сколько уже принесли из скольких: «бревно 2/6».</summary>
        public static string Progress(string itemId, int delivered, int needed) =>
            $"{PalletLabel.ItemName(itemId)} {delivered}/{needed}";

        /// <summary>Название ресурса в форме для числа: 1 бревно, 2 бревна, 5 брёвен.</summary>
        public static string CountedName(string itemId, int count)
        {
            var (one, few, many) = itemId switch
            {
                "wood" => ("бревно", "бревна", "брёвен"),
                "stone" => ("камень", "камня", "камней"),
                "grass" => ("трава", "травы", "травы"),
                _ => (itemId, itemId, itemId),
            };
            var n = System.Math.Abs(count) % 100;
            if (n is >= 11 and <= 14)
                return many;
            return (n % 10) switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many,
            };
        }
    }
}
