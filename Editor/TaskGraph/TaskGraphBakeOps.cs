using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// PUTTING THE BAKE ON DISK (M30.6) — the program lives INSIDE the document's file, as the
    /// baked graph always has.
    ///
    /// A registry row, a state's task, an ability: everything that points at a program points at
    /// the sub-asset, so re-baking has to keep the SAME object and refill it rather than make a
    /// new one. A bake that replaced the asset would silently unwire every caller, which is the
    /// one failure this whole milestone must not introduce.
    /// </summary>
    public static class TaskGraphBakeOps
    {
        /// <summary>
        /// Bake and save, returning the program that lives beside the document.
        /// </summary>
        public static GraphTaskAsset Bake(TaskGraphDocument document, List<string> problems)
        {
            if (document == null)
                return null;

            GraphTaskAsset fresh = TaskGraphDocBaker.Bake(document, problems);
            string path = AssetDatabase.GetAssetPath(document);
            if (string.IsNullOrEmpty(path))
                return fresh;   // an unsaved document still bakes, for a test or a preview

            GraphTaskAsset existing = ProgramOf(document);
            if (existing == null)
            {
                fresh.name = ProgramName(document);
                AssetDatabase.AddObjectToAsset(fresh, document);
                Save(fresh, document, path);
                return fresh;
            }

            // REFILLED IN PLACE, because callers hold a reference to this object and a new one
            // would leave them all pointing at nothing.
            ClearSubAssets(existing, path);
            existing.nodes = fresh.nodes;
            existing.parameters = fresh.parameters;
            existing.declaredOutputs = fresh.declaredOutputs;
            existing.keyBindings = fresh.keyBindings;
            existing.inputBindings = fresh.inputBindings;
            existing.enterEntry = fresh.enterEntry;
            existing.tickEntry = fresh.tickEntry;
            existing.exitEntry = fresh.exitEntry;
            existing.name = ProgramName(document);
            Object.DestroyImmediate(fresh);
            Save(existing, document, path);
            return existing;
        }

        /// <summary>The program baked from this document, or null before its first bake.</summary>
        public static GraphTaskAsset ProgramOf(TaskGraphDocument document)
        {
            string path = AssetDatabase.GetAssetPath(document);
            if (string.IsNullOrEmpty(path))
                return null;
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is GraphTaskAsset program)
                    return program;
            }
            return null;
        }

        private static string ProgramName(TaskGraphDocument document)
        {
            return string.IsNullOrEmpty(document.programName) ? document.name : document.programName;
        }

        /// <summary>
        /// The calls and conditions of the LAST bake are copies this file owns; the next bake
        /// makes new ones, and leaving the old ones behind would grow the file forever.
        /// </summary>
        private static void ClearSubAssets(GraphTaskAsset program, string path)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                Object sub = all[i];
                if (sub == null || sub is TaskGraphDocument || sub == program)
                    continue;
                // The DOCUMENT's own tasks and conditions are what an author edits — they are
                // referenced by the document's nodes and must survive. Only the program's copies
                // are cleared, and they are exactly the ones nothing in the document names.
                if (!Owned(program, sub))
                    continue;
                Object.DestroyImmediate(sub, true);
            }
        }

        private static bool Owned(GraphTaskAsset program, Object candidate)
        {
            for (int i = 0; i < program.nodes.Count; i++)
            {
                GraphTaskNode node = program.nodes[i];
                if (ReferenceEquals(node.task, candidate) || ReferenceEquals(node.condition, candidate))
                    return true;
            }
            return false;
        }

        private static void Save(GraphTaskAsset program, TaskGraphDocument document, string path)
        {
            // The bake makes fresh copies of every call; they belong to this file too, or the
            // program would reference objects that vanish when the domain reloads.
            for (int i = 0; i < program.nodes.Count; i++)
            {
                GraphTaskNode node = program.nodes[i];
                Adopt(node.task, program, path);
                Adopt(node.condition, program, path);
            }
            EditorUtility.SetDirty(program);
            EditorUtility.SetDirty(document);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void Adopt(Object sub, GraphTaskAsset program, string path)
        {
            if (sub == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sub)))
                return;
            sub.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(sub, program);
        }
    }
}
