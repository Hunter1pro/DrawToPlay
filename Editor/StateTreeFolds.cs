using System.Collections.Generic;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>What the State Tree window remembers per user and per tree asset, in EditorPrefs — never in the
    /// asset: the rows folded shut and the selected row. Folded is what is stored, so a row never seen is open.</summary>
    internal static class StateTreeFolds
    {
        private const string k_FoldsPrefix = "PowerOfFire.DrawToPlay.StateTreeFolds.";
        private const string k_SelectionPrefix = "PowerOfFire.DrawToPlay.StateTreeSelection.";
        private const char k_Separator = '\n';

        internal static string FoldsKey(string treeGuid) => k_FoldsPrefix + treeGuid;

        internal static string SelectionKey(string treeGuid) => k_SelectionPrefix + treeGuid;

        /// <summary>The asset's GUID, or "" for a tree not saved as an asset (nothing is remembered for it).</summary>
        internal static string GuidOf(StateTreeAsset tree)
        {
            if (tree == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tree, out string guid, out long _))
                return string.Empty;
            return guid;
        }

        internal static HashSet<string> Folded(string treeGuid)
        {
            var folded = new HashSet<string>();
            if (string.IsNullOrEmpty(treeGuid))
                return folded;
            foreach (string nodeId in EditorPrefs.GetString(FoldsKey(treeGuid), string.Empty).Split(k_Separator))
            {
                if (nodeId.Length > 0)
                    folded.Add(nodeId);
            }
            return folded;
        }

        internal static void SetFolded(string treeGuid, ICollection<string> nodeIds)
        {
            if (string.IsNullOrEmpty(treeGuid))
                return;
            if (nodeIds.Count == 0)
                EditorPrefs.DeleteKey(FoldsKey(treeGuid));
            else
                EditorPrefs.SetString(FoldsKey(treeGuid), string.Join(k_Separator, nodeIds));
        }

        internal static string Selected(string treeGuid) =>
            string.IsNullOrEmpty(treeGuid) ? string.Empty : EditorPrefs.GetString(SelectionKey(treeGuid), string.Empty);

        internal static void SetSelected(string treeGuid, string nodeId)
        {
            if (string.IsNullOrEmpty(treeGuid))
                return;
            if (string.IsNullOrEmpty(nodeId))
                EditorPrefs.DeleteKey(SelectionKey(treeGuid));
            else
                EditorPrefs.SetString(SelectionKey(treeGuid), nodeId);
        }
    }
}
