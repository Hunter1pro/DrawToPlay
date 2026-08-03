using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Builds task graphs IN CODE — the "+ New Task Graph…" half of the authoring loop. The main
    /// editor assembly cannot reference this one (the whole point of the GraphEditor firewall: Graph
    /// Toolkit is 0.5.0-exp and its API may move under us without the tools noticing), so it reaches
    /// these methods through <c>StateTreeGraphBridge</c>'s reflection, exactly the way it reaches
    /// <see cref="StateTreeGraphAuthoring"/>. That is why the surface is public static methods with
    /// plain parameter and return types — nothing in a signature here may be a Graph Toolkit type.
    ///
    /// WHAT THE SCAFFOLD IS, AND WHY IT IS NOT EMPTY. A brand-new file with a blank canvas gives the
    /// author no way to tell a working task from a broken one, and a task graph has a real trap
    /// waiting: a chain that never returns leaves the task Running forever, which looks exactly like
    /// a hang. So the scaffold is the smallest COMPLETE program — On Tick wired straight to Return
    /// Success — which does the honest thing for a task with nothing in it yet (finishes at once) and
    /// shows both halves of the model in two nodes. The author inserts work between them.
    ///
    /// Every Graph Toolkit call used here is one <see cref="StateTreeGraphAuthoring"/> already proved
    /// against the shipped module: <see cref="GraphDatabase.CreateGraph{T}"/>,
    /// <see cref="Graph.AddNode"/>, <see cref="Graph.Connect"/>, <see cref="Node.DefineNode"/>,
    /// <see cref="GraphDatabase.SaveGraph"/>.
    /// </summary>
    public static class TaskGraphAuthoring
    {
        /// <summary>Prefix on every message, so a console line says which system is talking.</summary>
        private const string k_Tag = "Draw To Play: ";

        private const float k_ColumnWidth = 320f;

        /// <summary>
        /// Create the scaffold graph behind "+ New Task Graph…": an <see cref="OnTickNode"/> wired to
        /// a <see cref="ReturnSuccessNode"/>, saved and imported, so the file is immediately a usable
        /// task.
        ///
        /// EXPECT NO WARNINGS on the import that follows — unlike the state-tree scaffold, this one
        /// bakes clean, because a two-node program with an entry and a return is complete.
        /// </summary>
        /// <param name="assetPath">Where to write the graph. The <c>.taskgraph</c> extension is added
        /// when missing, and missing folders along the path are created. An existing file here IS
        /// overwritten — the caller is expected to have asked
        /// (<c>EditorUtility.SaveFilePanelInProject</c> does).</param>
        /// <param name="name">Display name for the two scaffold nodes and the fallback file name.
        /// The baked task takes its NAME FROM THE FILE, so renaming the asset renames the task.</param>
        /// <returns>The baked <see cref="GraphTaskAsset"/> that is the new file's main asset — the
        /// object to wire into a state's task list — or null when the graph could not be created,
        /// saved or baked (every failure is logged with its cause).</returns>
        public static GraphTaskAsset CreateTaskGraphScaffold(string assetPath, string name)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError(k_Tag + "New task graph: no asset path was given.");
                return null;
            }

            string path = NormalizeGraphPath(assetPath);
            string label = string.IsNullOrEmpty(name)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : name;

            TaskGraph graph = CreateGraphAsset(path, out path, out string error);
            if (graph == null)
            {
                Debug.LogError(k_Tag + "New task graph: " + error);
                return null;
            }

            var problems = new List<string>();

            var tick = AddNode(graph, typeof(OnTickNode), Vector2.zero, problems) as OnTickNode;
            var success = AddNode(graph, typeof(ReturnSuccessNode), new Vector2(k_ColumnWidth, 0f),
                problems) as ReturnSuccessNode;

            SetTitle(tick, string.IsNullOrEmpty(label) ? null : label + " · Tick");
            Connect(graph, tick, TaskGraphPorts.ExecOutPortName,
                success, TaskGraphPorts.ExecInPortName, problems);

            SaveAndImport(graph, path);

            if (problems.Count > 0)
            {
                Debug.LogError(k_Tag + $"New task graph '{label}' ({path}) was authored with problems:"
                    + "\n  " + string.Join("\n  ", problems));
            }

            GraphTaskAsset baked = FindProgramAt(path);
            if (baked == null)
            {
                Debug.LogError(k_Tag + $"New task graph '{label}': {path} produced no "
                    + $"{nameof(GraphTaskAsset)} when it was imported. The console above carries the "
                    + "bake errors — the graph is on disk, open it and fix what it names.");
                return null;
            }

            return baked;
        }

        // ------------------------------------------------------------------ graph building

        /// <summary>One node on the canvas, positioned and DEFINED — ports exist only after a
        /// definition pass, and every caller wires or fills the node in the next statement.</summary>
        private static Node AddNode(TaskGraph graph, Type nodeType, Vector2 position, List<string> problems)
        {
            Node node;
            try
            {
                node = Activator.CreateInstance(nodeType, true) as Node;
            }
            catch (Exception exception)
            {
                problems.Add($"new {nodeType.Name}() failed: {Describe(exception)}");
                return null;
            }

            if (node == null)
            {
                problems.Add($"{nodeType.Name} is not a Graph Toolkit Node.");
                return null;
            }

            try
            {
                graph.AddNode(node);
                node.Position = position;
                node.DefineNode();
            }
            catch (Exception exception)
            {
                problems.Add($"Adding a {nodeType.Name} to the graph failed: {Describe(exception)}");
                return null;
            }

            return node;
        }

        private static bool Connect(TaskGraph graph, INode from, string fromPort, INode to, string toPort,
            List<string> problems)
        {
            if (from == null || to == null)
                return false;

            IPort output = from.GetOutputPortByName(fromPort);
            IPort input = to.GetInputPortByName(toPort);
            if (output == null || input == null)
            {
                problems.Add($"Cannot wire {from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort}: "
                    + $"{(output == null ? "the output" : "the input")} port does not exist.");
                return false;
            }

            try
            {
                if (graph.Connect(output, input))
                    return true;
            }
            catch (Exception exception)
            {
                problems.Add($"Wiring {from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort} failed: "
                    + Describe(exception));
                return false;
            }

            problems.Add($"{from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort} was refused (the "
                + "port types do not match, or the input already holds a wire).");
            return false;
        }

        /// <summary>Name the node on the canvas. Cosmetic and best-effort: <c>INode.Title</c> is a live
        /// view of the node's implementation, and a graph that is fine with an unnamed node is not
        /// worth failing a scaffold over.</summary>
        private static void SetTitle(INode node, string title)
        {
            if (node == null || string.IsNullOrEmpty(title))
                return;

            try
            {
                node.Title = title;
            }
            catch (Exception)
            {
                // Layout only.
            }
        }

        // ------------------------------------------------------------------ assets

        /// <summary>Create the graph file and hand back the graph AND the path it really landed on.
        /// <see cref="GraphDatabase.CreateGraph{T}"/> resolves the path itself and then loads the graph
        /// from the resolved one (module IL 330710-330773), so asking the graph where it lives is the
        /// only honest answer.</summary>
        private static TaskGraph CreateGraphAsset(string assetPath, out string realPath, out string error)
        {
            realPath = assetPath;
            error = null;

            EnsureFolder(DirectoryOf(assetPath));

            TaskGraph graph;
            try
            {
                graph = GraphDatabase.CreateGraph<TaskGraph>(assetPath);
            }
            catch (Exception exception)
            {
                error = $"GraphDatabase.CreateGraph({assetPath}) failed: {Describe(exception)}";
                return null;
            }

            if (graph == null)
            {
                error = $"GraphDatabase.CreateGraph({assetPath}) returned nothing; the file could not be "
                    + "written (check the folder exists and the name has no invalid characters).";
                return null;
            }

            try
            {
                string resolved = GraphDatabase.GetGraphAssetPath(graph);
                if (!string.IsNullOrEmpty(resolved))
                    realPath = resolved;
            }
            catch (Exception)
            {
                // GetGraphAssetPath reaches into the graph implementation; a graph with no backing file
                // answers with an exception. The requested path is then the best guess.
            }

            return graph;
        }

        private static void SaveAndImport(TaskGraph graph, string assetPath)
        {
            try
            {
                GraphDatabase.SaveGraph(graph);
            }
            catch (Exception exception)
            {
                Debug.LogError(k_Tag + $"Saving {assetPath} failed: {Describe(exception)}");
            }

            AssetDatabase.SaveAssets();
            // Forced: the file was written moments ago and a cached import artifact would bake the
            // version that was there before.
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>The baked program inside a graph file. It is the file's MAIN asset (see
        /// <see cref="TaskGraphImporter"/>), but the sub-object scan is kept as a fallback so a change
        /// of importer strategy degrades to "slower" rather than to "not found".</summary>
        private static GraphTaskAsset FindProgramAt(string assetPath)
        {
            var main = AssetDatabase.LoadAssetAtPath<GraphTaskAsset>(assetPath);
            if (main != null)
                return main;

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return null;

            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is GraphTaskAsset program)
                    return program;
            }
            return null;
        }

        private static string NormalizeGraphPath(string assetPath)
        {
            string suffix = "." + TaskGraph.Extension;
            if (assetPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return assetPath;

            string directory = DirectoryOf(assetPath);
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath) + suffix;
            return string.IsNullOrEmpty(directory) ? file : directory + "/" + file;
        }

        private static string DirectoryOf(string assetPath)
        {
            string directory = System.IO.Path.GetDirectoryName(assetPath);
            return directory != null ? directory.Replace('\\', '/') : string.Empty;
        }

        /// <summary>Create every missing folder along an "Assets/a/b" path. Graph Toolkit creates the
        /// directory on disk itself, but a folder the AssetDatabase has never seen has no .meta and no
        /// import record, which is a different (and worse) problem.</summary>
        private static void EnsureFolder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath))
                return;

            string[] segments = assetFolderPath.Split('/');
            string path = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = path + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    return;
                path = next;
            }
        }

        /// <summary>The message of the exception that actually went wrong. Graph Toolkit wraps a lot
        /// of its failures, and the wrapper's message is never the useful one.</summary>
        private static string Describe(Exception exception)
        {
            Exception inner = exception;
            for (int i = 0; i < 8 && inner.InnerException != null; i++)
                inner = inner.InnerException;
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }
}
