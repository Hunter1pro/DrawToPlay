using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Menu entry points for the Draw-to-Play authoring flow: create an empty drawn shape
    /// (the Unity equivalent of adding a CurveShape2D node before scribbling into it) and
    /// activate the Draw tool. The shape starts with no <see cref="DrawnShapeAsset"/> — the
    /// first stroke creates and assigns one, so browsing the menu never writes asset files.
    /// </summary>
    internal static class DrawToPlayMenu
    {
        [MenuItem("GameObject/2D Object/Drawn Shape", false, 10)]
        private static void CreateDrawnShapeMenuItem(MenuCommand command)
        {
            CreateDrawnShape(command != null ? command.context as GameObject : null);
        }

        /// <summary>Create an empty DrawnShapeRenderer GameObject, undo-registered and selected.</summary>
        internal static GameObject CreateDrawnShape(GameObject parent)
        {
            var go = new GameObject("Drawn Shape");
            StageUtility.PlaceGameObjectInCurrentStage(go);
            GameObjectUtility.SetParentAndAlign(go, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(go);

            // MeshFilter/MeshRenderer come along via [RequireComponent].
            go.AddComponent<DrawnShapeRenderer>();

            Undo.RegisterCreatedObjectUndo(go, $"Create {go.name}");
            Selection.activeGameObject = go;
            return go;
        }

        [MenuItem("Tools/Draw To Play/Draw Shape Tool")]
        private static void ActivateDrawShapeTool()
        {
            ToolManager.SetActiveTool<DrawShapeTool>();
        }
    }
}
