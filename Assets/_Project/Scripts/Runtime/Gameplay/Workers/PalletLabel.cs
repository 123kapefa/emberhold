using Emberhold.World.Items;

namespace Emberhold.Gameplay.Workers
{
    /// <summary>
    /// Подпись над поддоном, пока у него нет своего вида: что лежит, сколько и сколько влезает, например «бревно 2-20».
    /// Пустой и свободный поддон подписан «пусто».
    /// </summary>
    public static class PalletLabel
    {
        public const string Empty = "пусто";

        public static string Text(Pallet pallet, int capacity) =>
            pallet.ItemId == null ? Empty : $"{ItemName(pallet.ItemId)} {pallet.Count}-{capacity}";

        /// <summary>Как ресурс называется в подписи; у незнакомого — его id.</summary>
        public static string ItemName(string itemId) => itemId switch
        {
            "wood" => "бревно",
            "stone" => "камень",
            "grass" => "трава",
            _ => itemId,
        };
    }
}
