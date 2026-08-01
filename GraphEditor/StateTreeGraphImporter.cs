using System;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Bakes every <c>.statetree</c> graph on import, so the runtime tree is always in step with the
    /// graph and a <see cref="StateTreeRunner"/> can reference the graph file itself.
    ///
    /// WHY AN IMPORTER RATHER THAN A "BAKE" BUTTON. This is the pattern BOTH Graph Toolkit samples
    /// use to ship runtime data, and it is the reason the runtime never learns about graphs:
    /// <c>Samples~/VisualNovelDirector/Editor/AssetImport/VisualNovelDirectorImporter.cs</c> loads
    /// the graph with <see cref="GraphDatabase.LoadGraphForImporter{T}"/> ("a clean copy of the graph
    /// as it exists on disk"), translates it into plain runtime objects, then
    /// <c>ctx.AddObjectToAsset("RuntimeAsset", runtimeAsset)</c> + <c>ctx.SetMainObject(runtimeAsset)</c>
    /// with the comment "This allows the same asset to be used in inspectors wherever a runtime asset
    /// is expected". <c>Samples~/TextureMaker/Editor/AssetImport/TextureMakerImporter.cs</c> does the
    /// same with a Texture2D. Ours produces a <see cref="StateTreeAsset"/> plus one sub-object per
    /// node, task and condition — the exact shape <c>Editor/StateTreePresets.cs</c> builds by hand.
    ///
    /// The consequence worth knowing: the MAIN asset of a <c>.statetree</c> file is the baked
    /// StateTreeAsset, so you drag the graph file straight onto a runner's <c>data</c> field, and
    /// double-clicking it still opens the graph editor (the graph is loaded by path, not by main
    /// object — the same arrangement as the samples).
    ///
    /// A standalone <c>&lt;Graph&gt;_Baked.asset</c> is available too, via
    /// <see cref="StateTreeGraphBaker.BakeSelectedGraphToAssetFile"/>, for when a separate file is
    /// wanted.
    /// </summary>
    [ScriptedImporter(k_Version, StateTreeGraphBaker.GraphExtension)]
    internal sealed class StateTreeGraphImporter : ScriptedImporter
    {
        /// <summary>Bump to force every graph in the project to re-bake after a change to the baker
        /// (the import result is cached against this number).</summary>
        private const int k_Version = 1;

        /// <summary>Identifier of the baked tree inside the imported asset. Sub-objects are keyed by
        /// node id (see <see cref="StateTreeGraphBaker.SubAsset.identifier"/>), so editing one state
        /// does not re-key the others.</summary>
        private const string k_TreeIdentifier = "StateTree";

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
                Debug.LogError($"State tree import ({ctx.assetPath}): {e.Message}");
                return;
            }

            if (graph == null)
            {
                // Same failure list the samples document: wrong path, wrong graph type, or a file
                // that will not deserialise.
                Debug.LogError($"State tree import ({ctx.assetPath}): the graph could not be loaded.");
                return;
            }

            StateTreeGraphBaker.BakeResult result =
                StateTreeGraphBaker.Bake(graph, StateTreeGraphBaker.CreateConsoleLog(ctx.assetPath, null));

            if (result.tree == null)
            {
                result.DestroyObjects();
                return;
            }

            if (result.errorCount > 0)
            {
                // DEVIATION from the samples, which return without a main object when the graph is
                // invalid. A .statetree file that stops producing a StateTreeAsset silently clears
                // every scene reference to it, so a partial tree — with every problem already logged
                // as an error above — is the less destructive outcome while the author is mid-edit.
                Debug.LogError($"State tree import ({ctx.assetPath}): baked with {result.errorCount} "
                    + "error(s); the tree is incomplete until they are fixed.");
            }

            ctx.AddObjectToAsset(k_TreeIdentifier, result.tree);
            for (int i = 0; i < result.subAssets.Count; i++)
            {
                StateTreeGraphBaker.SubAsset sub = result.subAssets[i];
                ctx.AddObjectToAsset(sub.identifier, sub.asset);
            }
            ctx.SetMainObject(result.tree);
        }
    }
}
