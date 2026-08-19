using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// YOU CANNOT DELETE WHAT IS STILL WIRED (M31) — the foreign key, applied to registry rows.
    ///
    /// Every reference in this toolset is a row, and until now deleting a row was allowed and
    /// left its users pointing at nothing: they warn in place, which is a warning an author
    /// meets weeks later on somebody else's screen. A database refuses the delete instead and
    /// makes you unpick the uses first, and it refuses it because it KNOWS the references. We
    /// know them too — the usage index is the same one the ⛓ reads.
    ///
    /// So the rule is: a row with uses does not go. The way through it is to remove the uses,
    /// which is a real edit somebody has to think about, and that is the point rather than the
    /// friction.
    ///
    /// TAGS ARE WHY THIS EXISTS. They are the most-wired thing in the project and the only wire
    /// that is not an id — a deleted tag row leaves 21 placements carrying a word no vocabulary
    /// holds, and nothing fails until a quest silently never completes.
    /// </summary>
    internal static class StateTreeRowGuard
    {
        /// <summary>Everywhere this row is used — entry references AND, for a tag row, every
        /// field wearing or asking for its name.</summary>
        internal static List<AssetWireScan.WireUse> Uses(StateTreeRegistryEntry row)
        {
            var uses = new List<AssetWireScan.WireUse>();
            if (row == null)
                return uses;

            AssetWireScan.Index index = AssetWireScan.Get();
            uses.AddRange(AssetWireScan.UsersOfRow(index, row));
            if (row is WorldTagDef)
                uses.AddRange(AssetWireScan.UsersOfTag(index, row.name));
            return uses;
        }

        /// <summary>Why this row may not be deleted, or null when it may.</summary>
        internal static string WhyNotRemove(StateTreeRegistryEntry row)
        {
            List<AssetWireScan.WireUse> uses = Uses(row);
            if (uses.Count == 0)
                return null;

            var where = new List<string>();
            for (int i = 0; i < uses.Count && where.Count < 6; i++)
            {
                string named = uses[i].context != null ? uses[i].context.name : "?";
                if (uses[i].viaRow != null)
                    named += " · " + uses[i].viaRow.name;
                if (!where.Contains(named))
                    where.Add(named);
            }

            return "'" + row.name + "' is still wired into " + uses.Count + " place"
                + (uses.Count == 1 ? "" : "s") + ":\n\n  " + string.Join("\n  ", where)
                + (uses.Count > where.Count ? "\n  …" : "")
                + "\n\nRemove those uses first. A row deleted while something names it does not "
                + "fail — it goes quiet, which is worse.";
        }

        /// <summary>The gate as the inspector uses it: true when the delete may proceed, and it
        /// says why when it may not.</summary>
        internal static bool MayRemove(StateTreeRegistryEntry row)
        {
            string why = WhyNotRemove(row);
            if (why == null)
                return true;

            if (EditorUtility.DisplayDialog("Still in use", why, "Show me", "Cancel"))
                Reveal(row);
            return false;
        }

        private static void Reveal(StateTreeRegistryEntry row)
        {
            List<AssetWireScan.WireUse> uses = Uses(row);
            for (int i = 0; i < uses.Count; i++)
            {
                if (uses[i].context == null)
                    continue;
                Selection.activeObject = uses[i].context;
                EditorGUIUtility.PingObject(uses[i].context);
                return;
            }
        }
    }
}
