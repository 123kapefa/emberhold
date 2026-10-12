using UnityEngine;

namespace Emberhold.World.Generation
{
    /// <summary>
    /// Зона местности в сцене: круг или прямоугольник на карте, внутри которого рельеф строится по своему
    /// профилю. Положение и поворот — из Transform (только X, Z и поворот вокруг вертикали), размер меняется
    /// ручками в Scene view. <see cref="VoxelWorldView"/> собирает все зоны сцены при генерации.
    /// </summary>
    [AddComponentMenu("Emberhold/World/Terrain Zone")]
    public sealed class TerrainZone : MonoBehaviour
    {
        [Tooltip("Круг или прямоугольник.")]
        [SerializeField] private TerrainZoneShape shape = TerrainZoneShape.Circle;
        [Tooltip("Размер в блоках: ширина (X) и длина (Z) прямоугольника; у круга — диаметр (X).")]
        [SerializeField] private Vector2 size = new(32f, 32f);
        [Tooltip("Профиль местности внутри зоны. Пусто — зона не действует.")]
        [SerializeField] private TerrainProfile profile;
        [Tooltip("Ширина плавного перехода от границы внутрь, в блоках: на границе рельеф соседей, глубже — профиль зоны.")]
        [Min(0f)]
        [SerializeField] private float blendWidth = 8f;

        public TerrainZoneShape Shape
        {
            get => shape;
            set => shape = value;
        }

        public Vector2 Size
        {
            get => size;
            set => size = Vector2.Max(value, Vector2.zero);
        }

        public float BlendWidth
        {
            get => blendWidth;
            set => blendWidth = Mathf.Max(value, 0f);
        }

        public TerrainProfile Profile
        {
            get => profile;
            set => profile = value;
        }

        /// <summary>
        /// Зона в координатах сетки блоков: worldOrigin — где в сцене угол (0, 0) сетки мира.
        /// Без профиля зона не действует, тогда false.
        /// </summary>
        public bool TryGetArea(Vector3 worldOrigin, out TerrainZoneArea area)
        {
            area = default;
            if (profile == null || !enabled || !gameObject.activeInHierarchy)
                return false;
            var position = transform.position - worldOrigin;
            area = new TerrainZoneArea
            {
                Shape = shape,
                Center = new Vector2(position.x, position.z),
                Size = size,
                Yaw = transform.eulerAngles.y,
                BlendWidth = blendWidth,
                Profile = profile.Parameters,
            };
            return true;
        }

        private void OnValidate()
        {
            size = Vector2.Max(size, Vector2.zero);
            blendWidth = Mathf.Max(blendWidth, 0f);
        }
    }
}
