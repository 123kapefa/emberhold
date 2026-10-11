namespace Emberhold.World.Items
{
    /// <summary>Что выпадает из разрушенного блока по его материалу.</summary>
    public interface IBlockDrops
    {
        /// <summary>Возвращает true и ресурс, если из материала что-то выпадает.</summary>
        bool TryGetDrop(byte material, out ItemDrop drop);
    }
}
