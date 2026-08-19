using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE SAME GATE, ONE LEVEL UP (M31) — an ASSET that something still points at does not get
    /// deleted either.
    ///
    /// A row and a file break the same way: the reference stays, the thing it names is gone, and
    /// nothing fails at the moment of the mistake. Deleting a registry is worse than deleting one
    /// of its rows, because every row goes with it — so the check has to cover the file's whole
    /// contents, sub-assets and rows included, which is exactly what the usage index already
    /// knows.
    ///
    /// REFUSED BY DEFAULT, with a way through that is deliberate rather than accidental: the
    /// dialog names who is holding on, offers to take you there, and keeps "delete anyway" as the
    /// third button for the case where the author knows better than the scan — a stale index must
    /// not be able to trap somebody, because the Project window is the only way an asset can be
    /// deleted at all. Taking that road logs exactly what was left dangling.
    ///
    /// Scene objects are not scanned (the index says so), so this catches asset-to-asset wiring —
    /// which is where this project's references live.
    /// </summary>
    public sealed class StateTreeAssetDeleteGuard : UnityEditor.AssetModificationProcessor
    {
        /// <summary>
        /// A DELETE THAT IS PART OF A REBUILD IS NOT A MISTAKE, and a modal dialog inside a
        /// builder is a hung editor. Anything that deletes an asset it is about to write again
        /// wraps the call in this, which is the difference between a gate for authors and a
        /// gate against the tools.
        /// </summary>
        public static System.IDisposable Silenced()
        {
            return new Silence();
        }

        private sealed class Silence : System.IDisposable
        {
            private readonly bool m_Was;

            internal Silence()
            {
                m_Was = s_Silenced;
                s_Silenced = true;
            }

            public void Dispose()
            {
                s_Silenced = m_Was;
            }
        }

        private static bool s_Silenced;

        private static AssetDeleteResult OnWillDeleteAsset(string path,
            RemoveAssetOptions options)
        {
            // Batch mode has nobody to ask, and a headless build that stops on a dialog is a
            // build that never finishes.
            if (s_Silenced || Application.isBatchMode)
                return AssetDeleteResult.DidNotDelete;
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return AssetDeleteResult.DidNotDelete;

            Object[] contents = AssetDatabase.LoadAllAssetsAtPath(path);
            if (contents == null || contents.Length == 0)
                return AssetDeleteResult.DidNotDelete;

            var breakers = new List<AssetWireScan.WireUse>();
            StateTreeRowGuard.Breakers(AssetWireScan.Get(), contents, path, breakers);
            if (breakers.Count == 0)
                return AssetDeleteResult.DidNotDelete;

            var where = new List<string>();
            for (int i = 0; i < breakers.Count && where.Count < 8; i++)
            {
                string named = breakers[i].context != null ? breakers[i].context.name : "?";
                if (breakers[i].viaRow != null)
                    named += " · " + breakers[i].viaRow.name;
                if (!where.Contains(named))
                    where.Add(named);
            }

            string message = System.IO.Path.GetFileName(path) + " is still wired into "
                + breakers.Count + " place" + (breakers.Count == 1 ? "" : "s") + ":\n\n  "
                + string.Join("\n  ", where) + (breakers.Count > where.Count ? "\n  …" : "")
                + "\n\nUnpick those first. Deleting it does not fail anywhere — the references "
                + "stay, pointing at nothing.";

            int choice = EditorUtility.DisplayDialogComplex("Still in use", message,
                "Cancel", "Delete anyway", "Show me");
            if (choice == 1)
            {
                // SAID OUT LOUD, because this is the road that leaves damage: the console keeps
                // the list after the dialog is gone.
                Debug.LogWarning("[Draw To Play] deleted '" + path + "' while "
                    + breakers.Count + " reference(s) still named it: "
                    + string.Join(", ", where));
                return AssetDeleteResult.DidNotDelete;
            }
            if (choice == 2)
                Reveal(breakers);
            return AssetDeleteResult.FailedDelete;
        }

        private static void Reveal(List<AssetWireScan.WireUse> breakers)
        {
            for (int i = 0; i < breakers.Count; i++)
            {
                if (breakers[i].context == null)
                    continue;
                Selection.activeObject = breakers[i].context;
                EditorGUIUtility.PingObject(breakers[i].context);
                return;
            }
        }
    }
}
