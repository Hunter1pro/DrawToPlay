using UnityEditor;
using UnityEditor.SceneManagement;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Edit-mode paint restore. ShapePaint deliberately has no [ExecuteAlways], so after
    /// a scene load or domain reload nothing rebuilds the paint layer from the asset's
    /// serialized mask bytes until the public API is touched — a painted shape would sit
    /// unpainted until first selected with the Paint tool. Rebuild every painted shape's
    /// layer whenever a scene opens or scripts reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class PaintLayerBootstrap
    {
        static PaintLayerBootstrap()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += SyncAll;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            SyncAll();
        }

        private static void SyncAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var stage = StageUtility.GetCurrentStageHandle();
            if (!stage.IsValid())
                return;
            foreach (var paint in stage.FindComponentsOfType<ShapePaint>())
            {
                if (paint != null && paint.hasMask)
                    paint.SyncLayer();
            }
            // Skins share the same DontSave-layer model: rebuild them after load too,
            // or a rigged shape reopens showing its undeformed base mesh.
            foreach (var skin in stage.FindComponentsOfType<DrawnShapeSkin>())
            {
                if (skin != null && skin.rig != null)
                    skin.RegenerateSkin();
            }
        }
    }
}
