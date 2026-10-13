namespace Emberhold.World.Voxels
{
    /// <summary>
    /// Макет #79: «пиксельные» скосы. Steps > 0 — сколько ступеней на блок (16, 8 или 4): верх скосов рисуется
    /// ступенями по сетке Steps × Steps, а гладкая поверхность (<see cref="TerrainSurface"/>) не строится — земля
    /// заполняется слоями блоков со скосами по поверхности. 0 — как в игре сейчас.
    /// </summary>
    public static class PixelSlopes
    {
        public static int Steps;

        public static bool Enabled => Steps > 0;
    }
}
