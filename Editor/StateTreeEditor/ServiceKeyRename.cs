using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// RENAMING A KEY THAT SOMETHING ALREADY CALLS — the operation that has to exist before a key
    /// can honestly be locked.
    ///
    /// A request key and an announcement key travel as TEXT: a task writes the string, a reaction
    /// names it, a component reads it. So renaming one on the def is only half a rename, and the
    /// other half — every caller — is exactly what a field the author can freely retype does not
    /// do. Locking the field without offering this would be a cage; offering this without locking
    /// the field would leave the trap open. They ship together.
    ///
    /// It rewrites by VALUE, not by field name: any serialized string on a caller that equals the
    /// old key becomes the new one. That is the same rule the usage index found them by, which is
    /// what makes the two agree — a caller the ⛓ can show is a caller this can repoint.
    /// </summary>
    internal static class ServiceKeyRename
    {
        /// <summary>Every authored place that names this key, through the usage index.</summary>
        internal static List<AssetWireScan.WireUse> Callers(string key)
        {
            AssetWireScan.Index index = AssetWireScan.Get();
            return !string.IsNullOrEmpty(key)
                && index.requestCallers.TryGetValue(key, out List<AssetWireScan.WireUse> callers)
                ? callers
                : new List<AssetWireScan.WireUse>();
        }

        /// <summary>
        /// Rewrite one caller's mentions of a key, returning how many fields changed.
        ///
        /// Public and small so the operation can be tested on an object rather than only observed
        /// on a project: a rename that silently changed nothing would look exactly like a rename
        /// that worked.
        /// </summary>
        internal static int Rewrite(Object caller, string from, string to)
        {
            if (caller == null || string.IsNullOrEmpty(from) || from == to)
                return 0;

            var serialized = new SerializedObject(caller);
            SerializedProperty property = serialized.GetIterator();
            var changed = 0;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.String
                    || property.stringValue != from)
                    continue;
                property.stringValue = to ?? "";
                changed++;
            }
            if (changed > 0)
                serialized.ApplyModifiedProperties();
            serialized.Dispose();
            return changed;
        }

        /// <summary>
        /// The whole rename: the def's own row, then everybody who named it — one undo step, and
        /// a saved project, because half of this on disk is worse than none of it.
        /// </summary>
        internal static int Apply(Object owner, System.Action rename, string from, string to)
        {
            List<AssetWireScan.WireUse> callers = Callers(from);
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rename '" + from + "'");

            Undo.RecordObject(owner, "Rename Key");
            rename?.Invoke();
            EditorUtility.SetDirty(owner);

            var touched = 0;
            var seen = new HashSet<Object>();
            for (int i = 0; i < callers.Count; i++)
            {
                Object caller = callers[i].context;
                if (caller == null || !seen.Add(caller))
                    continue;
                Undo.RecordObject(caller, "Rename Key");
                if (Rewrite(caller, from, to) > 0)
                {
                    EditorUtility.SetDirty(caller);
                    touched++;
                }
            }

            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            AssetWireScan.Invalidate();
            return touched;
        }
    }
}
