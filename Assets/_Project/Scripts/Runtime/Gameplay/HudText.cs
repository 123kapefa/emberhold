using UnityEngine;

namespace Emberhold.Gameplay
{
    /// <summary>
    /// Строки подсказок в правом верхнем углу (заглушка интерфейса): у каждой системы своя строка, строки не налезают
    /// друг на друга и не обрезаются — ширина считается по тексту, строка прижата к правому краю.
    /// </summary>
    public static class HudText
    {
        public const int DayRow = 0;
        public const int FuelRow = 1;
        public const int CrawlerRow = 2;
        public const int ScoreRow = 3;
        public const int ButtonRow = 4;

        public const float RowHeight = 22f;
        private const float Top = 10f;
        private const float Margin = 10f;

        /// <summary>Верх строки row в координатах GUI (от верхнего края).</summary>
        public static float RowTop(int row) => Top + row * RowHeight;

        /// <summary>Нарисовать строку row, прижав к правому краю экрана.</summary>
        public static void Right(int row, string text) => Right(row, text, Color.white);

        public static void Right(int row, string text, Color color)
        {
            var style = GUI.skin.label;
            var size = style.CalcSize(new GUIContent(text));
            var previous = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(Screen.width - Margin - size.x, RowTop(row), size.x, RowHeight), text, style);
            GUI.contentColor = previous;
        }
    }
}
