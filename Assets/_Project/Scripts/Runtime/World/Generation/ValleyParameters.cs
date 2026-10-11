using System;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Параметры генератора долины. Обычные данные, чтобы генератор можно было вызывать из тестов
    /// и из будущего редактора карт без ScriptableObject.
    /// </summary>
    [Serializable]
    public struct ValleyParameters
    {
        [Tooltip("Одинаковый seed — одинаковая долина.")]
        public int Seed;

        [Tooltip("Размер мира в чанках 16×16×16 (X — ширина, Y — высота, Z — длина долины).")]
        public Vector3Int SizeInChunks;

        [Tooltip("Высота дна долины в блоках.")]
        public float FloorHeight;

        [Tooltip("Высота склонов у краёв мира в блоках.")]
        public float RimHeight;

        [Tooltip("Половина ширины ровного дна, доля ширины мира (0..0.5).")]
        [Range(0f, 0.5f)]
        public float FloorHalfWidth;

        [Tooltip("Насколько русло петляет из стороны в сторону, доля ширины мира.")]
        [Range(0f, 0.3f)]
        public float MeanderAmplitude;

        [Tooltip("Сколько изгибов русла на всю длину долины.")]
        [Range(0f, 4f)]
        public float MeanderWaves;

        [Tooltip("Размер холмов шума в блоках.")]
        [Min(1f)]
        public float NoiseScale;

        [Tooltip("Высота холмов шума в блоках (±).")]
        [Min(0f)]
        public float NoiseAmplitude;

        [Tooltip("Шаг высоты поверхности в шестнадцатых блока: 16 — целые блоки, 4 — четверти.")]
        [Range(1, 16)]
        public int HeightStep;

        [Header("Террасы и скалы")]
        [Tooltip("Высота уровня террас в блоках: рельеф собирается в ровные площадки через эту высоту. 0 — без террас.")]
        [Min(0f)]
        public float TerraceHeight;

        [Tooltip("Какая доля перепада между уровнями занята склоном (0..1): меньше — шире ровные площадки и круче склоны.")]
        [Range(0.05f, 1f)]
        public float TerraceSlope;

        [Tooltip("Доля переходов между уровнями, которые становятся вертикальными скалами (0..1).")]
        [Range(0f, 1f)]
        public float CliffAmount;

        [Tooltip("Размер областей со скалами в блоках.")]
        [Min(1f)]
        public float CliffNoiseScale;

        [Tooltip("Скругление кромки скал и впадин: на сколько шестнадцатых опустить край над обрывом.")]
        [Range(0, 16)]
        public int CliffEdgeRounding;

        [Tooltip("Скругление у подножия скал: на сколько шестнадцатых поднять землю у стены.")]
        [Range(0, 16)]
        public int CliffFootRounding;

        [Tooltip("Купол на шапках скал: на сколько блоков верх опускается от середины к краю обрыва.")]
        [Range(0f, 6f)]
        public float CapDepth;

        [Tooltip("На сколько блоков от края обрыва вглубь тянется купол.")]
        [Range(1f, 16f)]
        public float CapWidth;

        [Tooltip("Толщина земли под травой в блоках, глубже — камень.")]
        [Min(0)]
        public int DirtDepth;

        public static ValleyParameters Default => new()
        {
            Seed = 12345,
            SizeInChunks = new Vector3Int(8, 3, 8),
            FloorHeight = 8f,
            RimHeight = 26f,
            FloorHalfWidth = 0.2f,
            MeanderAmplitude = 0.12f,
            MeanderWaves = 1.2f,
            NoiseScale = 28f,
            NoiseAmplitude = 6f,
            HeightStep = 4,
            TerraceHeight = 6f,
            TerraceSlope = 0.3f,
            CliffAmount = 0.45f,
            CliffNoiseScale = 16f,
            CliffEdgeRounding = 0,
            CliffFootRounding = 4,
            CapDepth = 2f,
            CapWidth = 6f,
            DirtDepth = 1,
        };
    }
}
