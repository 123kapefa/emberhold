using Emberhold.World;
using UnityEditor;
using UnityEngine;

namespace Emberhold.Editor.World
{
    /// <summary>
    /// Кнопки предпросмотра рельефа в инспекторе мира: построить без Play по текущим настройкам и зонам местности,
    /// убрать. Перед запуском Play предпросмотр убирается, чтобы в игре не было второго мира.
    /// </summary>
    [CustomEditor(typeof(VoxelWorldView))]
    [CanEditMultipleObjects]
    public sealed class VoxelWorldViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (Application.isPlaying)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Предпросмотр без Play", EditorStyles.boldLabel);
            var canGenerate = false;
            var hasPreview = false;
            foreach (var view in targets)
            {
                canGenerate |= ((VoxelWorldView)view).CanGenerate;
                hasPreview |= ((VoxelWorldView)view).HasPreview;
            }
            if (!canGenerate)
            {
                EditorGUILayout.HelpBox("Укажите Terrain, чтобы построить рельеф без Play.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "Строит рельеф по настройкам и зонам местности сцены. После изменений нажмите ещё раз. " +
                "Построенные чанки временные: в сцену не сохраняются и убираются перед Play.",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(hasPreview ? "Перестроить" : "Сгенерировать"))
                    foreach (var view in targets)
                        ((VoxelWorldView)view).BuildPreview();
                using (new EditorGUI.DisabledScope(!hasPreview))
                {
                    if (GUILayout.Button("Очистить"))
                        foreach (var view in targets)
                            ((VoxelWorldView)view).ClearPreview();
                }
            }
        }

        [InitializeOnLoadMethod]
        private static void ClearPreviewsBeforePlay() =>
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode)
                    return;
                VoxelWorldView.ClearAllPreviews();
            };
    }
}
