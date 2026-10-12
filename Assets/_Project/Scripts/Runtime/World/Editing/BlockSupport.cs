using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Editing
{
    /// <summary>
    /// Правила опоры для <see cref="BlockStability"/>: что считается землёй и какие соседние блоки держатся
    /// друг за друга. Всё, что форма блока меняет в устойчивости, задаётся здесь.
    /// </summary>
    public static class BlockSupport
    {
        /// <summary>
        /// Клетка лежит на земле: нижний слой мира. Рельеф заполняет столбцы с самого низа,
        /// поэтому всё, что через соседей доходит до дна мира, стоит на земле.
        /// </summary>
        public static bool IsGround(Vector3Int cell) => cell.y == 0;

        /// <summary>
        /// Держатся ли друг за друга блок <paramref name="from"/> и его сосед <paramref name="to"/>
        /// за гранью в направлении <paramref name="direction"/>.
        /// Сейчас любые два непустых соседа связаны, какой бы формы они ни были: полублок, скат, угол ската,
        /// лестница. Формы, которые держат соседей по-особому (балка, кронштейн, столб — #27), добавят
        /// свои правила сюда: по форме и повороту блока и по направлению грани.
        /// </summary>
        public static bool Connects(Block from, Vector3Int direction, Block to) => !from.IsAir && !to.IsAir;
    }
}
