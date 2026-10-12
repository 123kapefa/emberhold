using Emberhold.World.Generation;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Emberhold.Editor.World
{
    /// <summary>
    /// Зона местности в Scene view: граница зоны и внутренняя линия, где кончается плавный переход,
    /// у выбранной зоны — ручки размера (прямоугольник тянется за стороны, круг — за край).
    /// </summary>
    [CustomEditor(typeof(TerrainZone))]
    [CanEditMultipleObjects]
    public sealed class TerrainZoneEditor : UnityEditor.Editor
    {
        private static readonly Color OutlineColor = new(1f, 0.65f, 0.2f, 1f);
        private static readonly Color BlendColor = new(1f, 0.65f, 0.2f, 0.45f);

        private readonly BoxBoundsHandle boxHandle = new() { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Z };
        private readonly SphereBoundsHandle circleHandle = new() { axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Z };

        [MenuItem("GameObject/Emberhold/Terrain Zone", false, 10)]
        private static void Create(MenuCommand command)
        {
            var go = new GameObject("Terrain Zone", typeof(TerrainZone));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Terrain Zone");
            Selection.activeGameObject = go;
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawZone(TerrainZone zone, GizmoType type)
        {
            using (new Handles.DrawingScope(ZoneMatrix(zone)))
            {
                var extents = Extents(zone);
                var inner = extents - Vector2.one * zone.BlendWidth;
                Handles.color = OutlineColor;
                DrawOutline(zone.Shape, extents);
                if (zone.BlendWidth > 0f && inner.x > 0f && inner.y > 0f)
                {
                    Handles.color = BlendColor;
                    DrawOutline(zone.Shape, inner);
                }
            }
        }

        private void OnSceneGUI()
        {
            var zone = (TerrainZone)target;
            using (new Handles.DrawingScope(OutlineColor, ZoneMatrix(zone)))
            {
                EditorGUI.BeginChangeCheck();
                Vector2 size;
                Vector3 center;
                if (zone.Shape == TerrainZoneShape.Circle)
                {
                    circleHandle.center = Vector3.zero;
                    circleHandle.radius = zone.Size.x * 0.5f;
                    circleHandle.DrawHandle();
                    center = circleHandle.center;
                    size = new Vector2(circleHandle.radius * 2f, zone.Size.y);
                }
                else
                {
                    boxHandle.center = Vector3.zero;
                    boxHandle.size = new Vector3(zone.Size.x, 0f, zone.Size.y);
                    boxHandle.DrawHandle();
                    center = boxHandle.center;
                    size = new Vector2(boxHandle.size.x, boxHandle.size.z);
                }
                if (!EditorGUI.EndChangeCheck())
                    return;

                // Ручка тянет одну сторону, поэтому центр сдвигается: переносим его в Transform.
                Undo.RecordObjects(new Object[] { zone, zone.transform }, "Resize Terrain Zone");
                zone.transform.position = Handles.matrix.MultiplyPoint3x4(center);
                zone.Size = size;
            }
        }

        /// <summary>Оси зоны: положение и поворот вокруг вертикали, без масштаба — размер только в блоках.</summary>
        private static Matrix4x4 ZoneMatrix(TerrainZone zone) =>
            Matrix4x4.TRS(zone.transform.position, Quaternion.Euler(0f, zone.transform.eulerAngles.y, 0f), Vector3.one);

        private static Vector2 Extents(TerrainZone zone) => zone.Shape == TerrainZoneShape.Circle
            ? Vector2.one * zone.Size.x * 0.5f
            : zone.Size * 0.5f;

        private static void DrawOutline(TerrainZoneShape shape, Vector2 extents)
        {
            if (shape == TerrainZoneShape.Circle)
            {
                Handles.DrawWireDisc(Vector3.zero, Vector3.up, extents.x);
                return;
            }
            var a = new Vector3(-extents.x, 0f, -extents.y);
            var b = new Vector3(extents.x, 0f, -extents.y);
            var c = new Vector3(extents.x, 0f, extents.y);
            var d = new Vector3(-extents.x, 0f, extents.y);
            Handles.DrawPolyLine(a, b, c, d, a);
        }
    }
}
