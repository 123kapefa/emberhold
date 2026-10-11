using System;
using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>Общая форма рельефа профиля; поверх неё ложатся шум, террасы и скалы.</summary>
    public enum TerrainShape
    {
        /// <summary>Ровно на базовой высоте, рельеф даёт только шум.</summary>
        Flat = 0,
        /// <summary>Вдоль оси Z петляет ровное дно, к краям по X поднимаются склоны.</summary>
        Valley = 1,
        /// <summary>Купол в центре карты поднимается над базовой высотой.</summary>
        Mountain = 2,
        /// <summary>Чаша в центре карты опускается ниже базовой высоты.</summary>
        Lake = 3,
    }

    /// <summary>
    /// Профиль местности: из чего генератор собирает высоту и материалы столбца. Обычные данные, чтобы
    /// генератор можно было вызывать из тестов и из будущего редактора карт без ScriptableObject.
    /// Размеры формы заданы долями карты, поэтому профиль не зависит от размера мира.
    /// </summary>
    [Serializable]
    public struct TerrainProfileParameters
    {
        [Header("Форма")]
        [Tooltip("Общая форма рельефа: ровная местность, долина, гора или озеро.")]
        public TerrainShape Shape;

        [Tooltip("Базовая высота поверхности в блоках: дно долины, подножие горы, берег озера.")]
        [Min(0f)]
        public float BaseHeight;

        [Tooltip("Перепад формы в блоках: подъём бортов долины, высота горы или глубина озера.")]
        [Min(0f)]
        public float ShapeHeight;

        [Tooltip("Ширина формы, доля ширины мира: половина ровного дна долины (0..0.5) или радиус горы и озера.")]
        [Range(0f, 1f)]
        public float ShapeWidth;

        [Tooltip("Только для долины: насколько русло петляет из стороны в сторону, доля ширины мира.")]
        [Range(0f, 0.3f)]
        public float MeanderAmplitude;

        [Tooltip("Только для долины: сколько изгибов русла на всю длину долины.")]
        [Range(0f, 4f)]
        public float MeanderWaves;

        [Header("Шум")]
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

        [Header("Материалы")]
        public TerrainMaterials Materials;

        [Tooltip("Уровень воды в блоках: поверхность ниже него — дно (материал дна). Воды пока нет. 0 — без дна.")]
        [Min(0f)]
        public float WaterLevel;

        /// <summary>Долина, с которой начинался прототип: профиль по умолчанию.</summary>
        public static TerrainProfileParameters Valley => new()
        {
            Shape = TerrainShape.Valley,
            BaseHeight = 8f,
            ShapeHeight = 18f,
            ShapeWidth = 0.2f,
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
            Materials = TerrainMaterials.Default,
            WaterLevel = 0f,
        };
    }
}
