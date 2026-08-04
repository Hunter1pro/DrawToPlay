using System;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Bakes every <c>.taskgraph</c> file on import, so the runtime program is always in step with the
    /// graph and the file itself can be dropped anywhere a task is expected.
    ///
    /// THE MAIN ASSET IS THE BAKED PROGRAM, and that one line is what makes a logic graph a first-class
    /// task. A <c>.taskgraph</c> file IS a <see cref="GraphTaskAsset"/> as far as the rest of the
    /// project is concerned: it satisfies a <see cref="StateTreeTaskAsset"/> field, it is found by
    /// <c>AssetDatabase.FindAssets("t:StateTreeTaskAsset")</c>, it shows up in the task picker, and
    /// double-clicking it still opens the graph editor (the graph is loaded by path, not by main
    /// object). No separate "bake" step, no second file to keep in sync, no way to ship a stale
    /// program.
    ///
    /// This is the pattern BOTH Graph Toolkit samples use to ship runtime data —
    /// <c>Samples~/VisualNovelDirector/Editor/AssetImport/VisualNovelDirectorImporter.cs</c> loads the
    /// graph with <see cref="GraphDatabase.LoadGraphForImporter{T}"/> ("a clean copy of the graph as it
    /// exists on disk"), translates it into plain runtime objects, then
    /// <c>ctx.AddObjectToAsset</c> + <c>ctx.SetMainObject</c> with the comment "This allows the same
    /// asset to be used in inspectors wherever a runtime asset is expected" — and it is the same shape
    /// <see cref="StateTreeGraphImporter"/> uses for <c>.statetree</c> files.
    /// </summary>
    [ScriptedImporter(k_Version, TaskGraphBaker.GraphExtension)]
    internal sealed class TaskGraphImporter : ScriptedImporter
    {
        /// <summary>Bump to force every task graph in the project to re-bake after a change to the
        /// baker (the import result is cached against this number).</summary>
        // 2: parameters carry stable ids (M7h) — bump forces the one-shot reimport that
        // mints ids on every existing baked graph.
        private const int k_Version = 2;

        /// <summary>Identifier of the baked program inside the imported asset. Sub-objects are keyed
        /// by instruction index (see <see cref="TaskGraphBaker"/>), so they cannot collide with
        /// it.</summary>
        private const string k_ProgramIdentifier = "TaskGraph";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            Graph graph;
            try
            {
                // LoadGraphForImporter, not LoadGraph: the import pipeline must see the file on disk,
                // never an in-memory edit, or the artifact stops being reproducible.
                graph = GraphDatabase.LoadGraphForImporter<Graph>(ctx.assetPath);
            }
            catch (Exception e)
            {
                // Thrown when the extension is not registered to a Graph subclass — i.e. the graph
                // class is missing or declares a different extension than the importer.
                Debug.LogError($"Task graph import ({ctx.assetPath}): {e.Message}");
                return;
            }

            if (graph == null)
            {
                // Same failure list the samples document: wrong path, wrong graph type, or a file that
                // will not deserialise.
                Debug.LogError($"Task graph import ({ctx.assetPath}): the graph could not be loaded.");
                return;
            }

            TaskGraphBaker.BakeResult result =
                TaskGraphBaker.Bake(graph, TaskGraphBaker.CreateConsoleLog(ctx.assetPath, null));

            if (result.program == null)
            {
                result.DestroyObjects();
                return;
            }

            if (result.errorCount > 0)
            {
                // DEVIATION from the samples, which return without a main object when the graph is
                // invalid — the same deviation StateTreeGraphImporter makes and for the same reason. A
                // .taskgraph file that stops producing a GraphTaskAsset silently clears every reference
                // to it across the project, so a partial program — with every problem already logged as
                // an error above — is the less destructive outcome while the author is mid-edit.
                Debug.LogError($"Task graph import ({ctx.assetPath}): baked with {result.errorCount} "
                    + "error(s); the task is incomplete until they are fixed.");
            }

            ctx.AddObjectToAsset(k_ProgramIdentifier, result.program);
            for (int i = 0; i < result.subAssets.Count; i++)
            {
                StateTreeGraphBaker.SubAsset sub = result.subAssets[i];
                ctx.AddObjectToAsset(sub.identifier, sub.asset);
            }
            ctx.SetMainObject(result.program);
        }
    }
}
