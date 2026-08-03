using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Selection hook — port of the HeavenlyTreasures AIDraftAutoOpen pattern: clicking a
    /// <see cref="StateTreeAsset"/> (or one of its sub-assets, or an object carrying a
    /// <see cref="StateTreeRunner"/>) brings up the direct editor, so a tree is edited where it
    /// is edited best rather than through the generic inspector.
    ///
    /// Two differences from the original, both because "always steal focus" is a habit users
    /// grow to resent: the behaviour is behind
    /// <see cref="StateTreeEditorWindow.autoOpenOnSelection"/> (a preference, toggled in the
    /// window's own toolbar), and the window is only CREATED here — when it is already open it
    /// follows the selection through its own OnSelectionChange and is never re-focused.
    /// </summary>
    [InitializeOnLoad]
    internal static class StateTreeEditorAutoOpen
    {
        static StateTreeEditorAutoOpen()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static void OnSelectionChanged()
        {
            if (!StateTreeEditorWindow.autoOpenOnSelection)
                return;

            // Already open: the window handles the selection itself, and opening it again here
            // would yank focus away from the Project window mid-click.
            if (EditorWindow.HasOpenInstances<StateTreeEditorWindow>())
                return;

            var tree = StateTreeEditorWindow.ResolveTree(Selection.activeObject);
            if (tree == null)
                return;

            StateTreeEditorWindow.Open(tree);
        }
    }
}
