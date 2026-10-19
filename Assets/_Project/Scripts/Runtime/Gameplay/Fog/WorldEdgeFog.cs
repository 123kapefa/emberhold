using Emberhold.Gameplay.DayNight;
using Emberhold.World;
using UnityEngine;

namespace Emberhold.Gameplay.Fog
{
    /// <summary>
    /// Море тумана за краем мира (#102). Горы за краем (WorldSkirt) уходят в пелену, а дальше она тянется до горизонта,
    /// поэтому у мира не видно конца. Туман плотный внизу и редеет кверху (высоты из FogSettings), по горизонтали
    /// густеет от черты на inset блоков внутри края мира; внутри карты глубже этой черты его нет.
    /// Рисует его тот же полноэкранный проход, что и туман от света (LightFog.shader): компонент только передаёт
    /// шейдеру прямоугольник мира и вид тумана. Без компонента в сцене тумана за краем нет.
    /// </summary>
    public sealed class WorldEdgeFog : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_EmberholdEdgeFogColor");
        private static readonly int ParamsId = Shader.PropertyToID("_EmberholdEdgeFogParams");
        private static readonly int RectId = Shader.PropertyToID("_EmberholdEdgeFogRect");
        private static readonly int HeightId = Shader.PropertyToID("_EmberholdEdgeFogHeight");

        [Tooltip("Цвет, густота и высота пелены днём и ночью.")]
        [SerializeField] private FogSettings settings;
        [Tooltip("Мир, у края которого лежит туман. Если пусто — первый в сцене.")]
        [SerializeField] private VoxelWorldView view;
        [Tooltip("Смена дня и ночи. Если пусто — первая в сцене; без неё туман всегда дневной.")]
        [SerializeField] private DayNightCycle dayNight;
        [Tooltip("На сколько блоков внутрь от края мира заходит туман: низины у самого края тонут в нём.")]
        [Min(0f)] [SerializeField] private float inset = 12f;

        /// <summary>Вид тумана в этом кадре.</summary>
        public FogLook Look { get; private set; }

        /// <summary>Прямоугольник мира по X/Z в мировых координатах; пустой, пока мира нет.</summary>
        public Rect WorldRect { get; private set; }

        private void OnEnable()
        {
            if (settings == null)
            {
                Debug.LogError("[Fog] Не назначены FogSettings тумана за краем мира.", this);
                enabled = false;
                return;
            }
            if (view == null)
                view = FindAnyObjectByType<VoxelWorldView>();
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            Push();
        }

        private void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);

        // Мир может появиться позже (генерация), а время суток идёт: обновляем каждый кадр.
        private void LateUpdate() => Push();

        /// <summary>Плотность тумана за краем в точке мира, от 0 (глубже черты или выше пелены) до непрозрачности.</summary>
        public float DensityAt(Vector3 worldPoint)
        {
            if (WorldRect.width <= 0f)
                return 0f;
            return Look.DensityAt(DistanceOutside(WorldRect, worldPoint) + inset, worldPoint.y);
        }

        /// <summary>
        /// Расстояние по горизонтали от точки до прямоугольника мира: снаружи — до ближайшей точки края,
        /// внутри — со знаком минус до ближайшего края. Ту же формулу считает шейдер.
        /// </summary>
        public static float DistanceOutside(Rect rect, Vector3 point)
        {
            var dx = Mathf.Max(rect.xMin - point.x, point.x - rect.xMax);
            var dz = Mathf.Max(rect.yMin - point.z, point.z - rect.yMax);
            if (dx <= 0f && dz <= 0f)
                return Mathf.Max(dx, dz);
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dz, 0f)).magnitude;
        }

        private void Push()
        {
            Look = settings.LookAt(dayNight != null && dayNight.isActiveAndEnabled ? dayNight.Darkness : 0f);
            var world = view != null ? view.World : null;
            if (world == null)
            {
                WorldRect = default;
                Shader.SetGlobalVector(ParamsId, Vector4.zero);
                return;
            }

            var origin = view.transform.position;
            var size = world.SizeInBlocks;
            WorldRect = new Rect(origin.x, origin.z, size.x, size.z);
            Shader.SetGlobalColor(ColorId, Look.Color);
            Shader.SetGlobalVector(ParamsId, new Vector4(Look.Opacity, 1f / Look.Falloff, 1f, inset));
            Shader.SetGlobalVector(RectId, new Vector4(WorldRect.xMin, WorldRect.yMin, WorldRect.xMax, WorldRect.yMax));
            Shader.SetGlobalVector(HeightId, new Vector4(Look.DenseBelow, Look.ClearAbove, 0f, 0f));
        }
    }
}
