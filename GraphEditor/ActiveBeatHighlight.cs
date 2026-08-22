using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Tints the node a running task graph is AT, live, while play mode runs — the M38.4 "running
    /// beat", the task-graph twin of <see cref="ActiveStateHighlight"/>.
    ///
    /// WHAT IS WATCHED. Programs never run as the authored asset: <see cref="GraphTaskAsset.Copy"/>
    /// makes the copy and remembers the original as <see cref="GraphTaskAsset.source"/>, and every
    /// copy between OnEnter and OnExit is in <see cref="GraphTaskAsset.CollectRunning"/>. So there
    /// is nothing to find in the scene — the registry is walked, each running copy is mapped back to
    /// its <c>.taskgraph</c> (the importer makes the program the file's main object, so
    /// <c>AssetDatabase.GetAssetPath(source)</c> IS the graph path), and the copy's
    /// <see cref="GraphTaskAsset.activeNode"/> is the instruction index the baker assigned — in
    /// <c>graph.GetNodes()</c> order over the <see cref="ITaskGraphNode"/>s, which is the same walk
    /// <see cref="TaskGraphBaker"/> makes in its Collect pass.
    ///
    /// WHICH RUN, WHEN SEVERAL. One graph may be running in several copies at once (every keeper
    /// dialog, every dawn reaction). The most recently entered copy per graph is the one lit: it is
    /// the one the author just triggered. The colour is borrowed, not authored — every write is
    /// paired with a restore, exactly as the state highlight does it, and nothing here saves a graph.
    /// </summary>
    [InitializeOnLoad]
    public static class ActiveBeatHighlight
    {
        /// <summary>EditorPrefs key behind <see cref="enabled"/>.</summary>
        public const string EnabledPrefKey = "PowerOfFire.DrawToPlay.HighlightActiveBeat";

        /// <summary>Same green as the active state, so "running" means one colour across both
        /// canvases.</summary>
        private static readonly Color k_HighlightColor = new Color(0.24f, 0.72f, 0.36f);

        /// <summary>A beat can change every tick; the registry walk is cheap (a handful of running
        /// programs), the graph load is cached per path.</summary>
        private const double k_PollInterval = 0.1;

        private sealed class Canvas
        {
            public Graph graph;
            public List<INode> instructions;
            public INode tinted;
            public Color tintedOriginal;
        }

        private static readonly Dictionary<string, Canvas> s_Canvases = new Dictionary<string, Canvas>();
        private static readonly List<GraphTaskAsset> s_Running = new List<GraphTaskAsset>();
        private static readonly Dictionary<string, GraphTaskAsset> s_LitByPath =
            new Dictionary<string, GraphTaskAsset>();
        private static double s_NextPoll;

        static ActiveBeatHighlight()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Detach;
        }

        /// <summary>Master switch. Off restores any live tint immediately.</summary>
        public static bool enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, true);
            set
            {
                if (enabled == value)
                    return;
                EditorPrefs.SetBool(EnabledPrefKey, value);
                if (!value)
                    Detach();
            }
        }

        /// <summary>Graph paths with a beat currently lit — what a readout or a test asks.</summary>
        public static IEnumerable<string> litGraphPaths => s_LitByPath.Keys;

        /// <summary>Maps a running program's instruction index to the node that baked it, for the
        /// graph at <paramref name="graphPath"/>. Null when the path is not a task graph or the
        /// index is out of range — exposed so the mapping is testable without play mode.</summary>
        public static INode NodeAt(string graphPath, int instruction)
        {
            Canvas canvas = CanvasAt(graphPath);
            if (canvas == null || instruction < 0 || instruction >= canvas.instructions.Count)
                return null;
            return canvas.instructions[instruction];
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Restore while the copies still exist; by EnteredEditMode they are gone.
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Detach();
        }

        private static void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying || !enabled)
            {
                if (s_Canvases.Count > 0)
                    Detach();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < s_NextPoll)
                return;
            s_NextPoll = now + k_PollInterval;

            GraphTaskAsset.CollectRunning(s_Running);

            // Latest copy per graph wins — the registry is a set, so "latest" is the copy with the
            // highest entity id, which Unity hands out in creation order.
            s_LitByPath.Clear();
            for (int i = 0; i < s_Running.Count; i++)
            {
                GraphTaskAsset run = s_Running[i];
                GraphTaskAsset authored = run.source != null ? run.source : run;
                string path = AssetDatabase.GetAssetPath(authored);
                if (string.IsNullOrEmpty(path)
                    || !path.EndsWith("." + TaskGraphBaker.GraphExtension, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!s_LitByPath.TryGetValue(path, out GraphTaskAsset standing)
                    || run.GetEntityId() > standing.GetEntityId())
                    s_LitByPath[path] = run;
            }

            // Canvases whose program stopped go dark.
            foreach (KeyValuePair<string, Canvas> pair in s_Canvases)
            {
                if (!s_LitByPath.ContainsKey(pair.Key))
                    ClearTint(pair.Value);
            }

            foreach (KeyValuePair<string, GraphTaskAsset> pair in s_LitByPath)
            {
                Canvas canvas = CanvasAt(pair.Key);
                if (canvas == null)
                    continue;
                int beat = pair.Value.activeNode;
                INode node = beat >= 0 && beat < canvas.instructions.Count ? canvas.instructions[beat] : null;
                if (ReferenceEquals(node, canvas.tinted))
                    continue;
                ClearTint(canvas);
                if (node == null)
                    continue;
                try
                {
                    canvas.tintedOriginal = node.DefaultColor;
                    node.DefaultColor = k_HighlightColor;
                    canvas.tinted = node;
                }
                catch (Exception)
                {
                    // The graph was closed or reloaded under us; re-resolve it on the next poll.
                    s_Canvases.Remove(pair.Key);
                    break;
                }
            }
        }

        private static Canvas CanvasAt(string graphPath)
        {
            if (s_Canvases.TryGetValue(graphPath, out Canvas canvas))
                return canvas;

            Graph graph = TaskGraphBaker.LoadGraphAtPath(graphPath);
            if (graph == null)
                return null;

            canvas = new Canvas { graph = graph, instructions = new List<INode>() };
            foreach (INode node in graph.GetNodes())
            {
                if (node is ITaskGraphNode)
                    canvas.instructions.Add(node);
            }
            s_Canvases[graphPath] = canvas;
            return canvas;
        }

        private static void Detach()
        {
            foreach (KeyValuePair<string, Canvas> pair in s_Canvases)
                ClearTint(pair.Value);
            s_Canvases.Clear();
            s_LitByPath.Clear();
            s_Running.Clear();
        }

        private static void ClearTint(Canvas canvas)
        {
            if (canvas.tinted == null)
                return;
            try
            {
                canvas.tinted.DefaultColor = canvas.tintedOriginal;
            }
            catch (Exception)
            {
                // Graph already gone; nothing to restore.
            }
            canvas.tinted = null;
        }
    }
}
