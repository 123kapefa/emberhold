namespace Emberhold.Core
{
    /// <summary>
    /// Номера слоёв из Project Settings → Tags and Layers. Держим константами, чтобы ими можно было
    /// задавать значения по умолчанию в ScriptableObject; совпадение с настройками проверяет EditMode-тест.
    /// </summary>
    public static class Layers
    {
        /// <summary>Земля мира: меши чанков. По ней камера ищет высоту фокуса.</summary>
        public const int Ground = 6;
        public const string GroundName = "Ground";
        public const int GroundMask = 1 << Ground;
    }
}
