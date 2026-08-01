using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Tints the state node the running tree is actually IN, live, while play mode runs — the M7
    /// exit criterion's "active state highlights during play".
    ///
    /// WHICH RUNG OF THE FALLBACK LADDER THIS IS, AND WHY. The M7 plan expected node tinting to be
    /// impossible and an overlay readout to be the fallback. It is possible, but only on the API this
    /// project actually compiles against — and the two are not the same API:
    /// <list type="bullet">
    /// <item>The Graph Toolkit that ships in the editor (com.unity.graphtoolkit 0.5.0-exp.1, resolved
    /// as <c>source: builtin</c> in <c>Packages/packages-lock.json</c>; the code lives in
    /// <c>UnityEditor.GraphToolkitModule</c>) exposes <c>INode.DefaultColor</c> with a SETTER. Its
    /// implementation stores the colour and registers a <c>ChangeHint.Style</c> change on the graph
    /// model, which is exactly the path the graph view repaints from — so writing it re-tints an
    /// open graph window immediately, with no internal API.</item>
    /// <item>The standalone 0.4.0-exp.2 package (the source read during M7 design) has no such
    /// member, no badge/marker API, and its only node-attached visuals come from
    /// <see cref="GraphLogger"/> markers, which only exist inside <c>Graph.OnGraphChanged</c>.</item>
    /// <item>An <c>[Overlay]</c> docked into the graph window is NOT possible either way:
    /// <c>GraphViewEditorWindow</c> is internal, so its type cannot be named at compile time.</item>
    /// </list>
    /// So the highlight is rung 1 (true node tinting) with <see cref="ActiveStateHighlightOverlay"/>
    /// as the always-visible readout for when the graph window is closed — or when the runner's tree
    /// came from somewhere other than a graph.
    ///
    /// THE COLOUR IS BORROWED, NOT AUTHORED. <c>DefaultColor</c> is authored data, so every write is
    /// paired with a restore: on the next transition, when the tree stops, when play mode exits and
    /// before an assembly reload. This class never calls <c>GraphDatabase.SaveGraph</c>. The one way
    /// a highlight colour could reach disk is the author hitting save on the graph mid-transition; if
    /// that ever bites, turn the highlight off with <see cref="enabled"/>.
    ///
    /// WHAT LINKS A RUNNER TO A GRAPH. The importer (<see cref="StateTreeGraphImporter"/>) makes the
    /// baked tree the main object of the <c>.statetree</c> file, so
    /// <c>AssetDatabase.GetAssetPath(runner.data)</c> IS the graph path. A tree exported to a
    /// standalone <c>&lt;Graph&gt;_Baked.asset</c> is matched back to its sibling graph by name. Ids
    /// come from <see cref="StateTreeGraphBaker.BuildStateIdMap"/> — the same resolution the bake
    /// used, so "chase" always means the same node in both directions.
    /// </summary>
    [InitializeOnLoad]
    public static class ActiveStateHighlight
    {
        /// <summary>EditorPrefs key behind <see cref="enabled"/>, so the toggle survives domain
        /// reloads and can be driven from a panel elsewhere.</summary>
        public const string EnabledPrefKey = "PowerOfFire.DrawToPlay.HighlightActiveState";

        /// <summary>Node tint for the active state. Green rather than the editor's selection blue so
        /// it cannot be mistaken for "this node is selected".</summary>
        private static readonly Color k_HighlightColor = new Color(0.24f, 0.72f, 0.36f);

        /// <summary>How often the tracker looks for new runners. Runners are usually spawned in
        /// Start(), long before the author looks at the graph, so a lazy scan is enough and keeps the
        /// editor update loop free of a FindObjectsByType every frame.</summary>
        private const double k_RescanInterval = 0.5;

        private static StateTreeRunner s_Runner;
        private static Graph s_Graph;
        private static string s_GraphPath = "";
        private static Dictionary<string, INode> s_NodeById;
        private static INode s_TintedNode;
        private static Color s_TintedOriginalColor;
        private static string s_ActiveNodeId = "";
        private static string s_PreviousNodeId = "";
        private static double s_NextRescan;

        /// <summary>Raised whenever the tracked runner, the active state or the transition changes —
        /// the overlay repaints from this instead of polling.</summary>
        public static event Action changed;

        static ActiveStateHighlight()
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
                changed?.Invoke();
            }
        }

        /// <summary>The runner being followed, or null.</summary>
        public static StateTreeRunner runner => s_Runner;

        /// <summary>The graph the tint is applied to, or null when the runner's tree did not come
        /// from one.</summary>
        public static Graph graph => s_Graph;

        /// <summary>Node id the tracked runner is in, "" when nothing is running.</summary>
        public static string activeNodeId => s_ActiveNodeId;

        /// <summary>Node id it came from — the "last transition" half of the readout.</summary>
        public static string previousNodeId => s_PreviousNodeId;

        /// <summary>Asset path of the graph the tint is being applied to, "" when the runner's tree
        /// did not come from a graph.</summary>
        public static string graphPath => s_GraphPath;

        /// <summary>True when the active state was found in the graph and tinted (as opposed to
        /// merely being reported by the overlay).</summary>
        public static bool isTinting => s_TintedNode != null;

        // ------------------------------------------------------------------ tracking

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Restore before the scene goes away: ExitingPlayMode still has live objects, whereas by
            // EnteredEditMode the runner (and its events) are already gone.
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Detach();
        }

        private static void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying || !enabled)
            {
                if (s_Runner != null || s_TintedNode != null)
                    Detach();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now >= s_NextRescan)
            {
                s_NextRescan = now + k_RescanInterval;
                Track(PickRunner());
            }

            // Safety net for the transition the runner reports before this tracker is attached:
            // StartTree fires activeNodeChanged from Start(), which is normally the very first frame
            // of play mode.
            if (s_Runner != null && s_Runner.activeNodeId != s_ActiveNodeId)
                SetActive(s_ActiveNodeId, s_Runner.activeNodeId);
        }

        /// <summary>The selected object's runner wins — that is how an author says "this one" with
        /// several enemies in the scene. Otherwise the first running one, so the common
        /// single-enemy demo needs no selection at all.</summary>
        private static StateTreeRunner PickRunner()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                var onSelection = selected.GetComponentInParent<StateTreeRunner>();
                if (onSelection == null)
                    onSelection = selected.GetComponentInChildren<StateTreeRunner>();
                if (onSelection != null && onSelection.isRunning)
                    return onSelection;
            }

            // FindObjectsInactive.Exclude, no sort mode: the FindObjectsSortMode overloads are
            // [Obsolete] in 6000.5 and a disabled runner is not running anyway.
            StateTreeRunner[] runners =
                UnityEngine.Object.FindObjectsByType<StateTreeRunner>(FindObjectsInactive.Exclude);
            for (int i = 0; i < runners.Length; i++)
                if (runners[i] != null && runners[i].isRunning)
                    return runners[i];
            return null;
        }

        private static void Track(StateTreeRunner next)
        {
            if (ReferenceEquals(next, s_Runner))
                return;

            if (s_Runner != null)
            {
                s_Runner.activeNodeChanged -= OnActiveNodeChanged;
                s_Runner.treeStopped -= OnTreeStopped;
            }

            ClearTint();
            s_Runner = next;
            s_ActiveNodeId = "";
            s_PreviousNodeId = "";
            s_Graph = null;
            s_NodeById = null;
            s_GraphPath = "";

            if (s_Runner != null)
            {
                s_Runner.activeNodeChanged += OnActiveNodeChanged;
                s_Runner.treeStopped += OnTreeStopped;
                ResolveGraph(s_Runner);
                SetActive("", s_Runner.activeNodeId);
            }

            changed?.Invoke();
        }

        private static void Detach()
        {
            if (s_Runner != null)
            {
                s_Runner.activeNodeChanged -= OnActiveNodeChanged;
                s_Runner.treeStopped -= OnTreeStopped;
                s_Runner = null;
            }
            ClearTint();
            s_Graph = null;
            s_NodeById = null;
            s_GraphPath = "";
            s_ActiveNodeId = "";
            s_PreviousNodeId = "";
            changed?.Invoke();
        }

        private static void OnActiveNodeChanged(string previous, string current)
            => SetActive(previous, current);

        private static void OnTreeStopped() => SetActive(s_ActiveNodeId, "");

        private static void SetActive(string previous, string current)
        {
            if (s_ActiveNodeId == current && s_PreviousNodeId == previous)
                return;
            s_PreviousNodeId = previous ?? "";
            s_ActiveNodeId = current ?? "";
            ApplyTint();
            changed?.Invoke();
        }

        // ------------------------------------------------------------------ graph resolution + tint

        private static void ResolveGraph(StateTreeRunner target)
        {
            if (target == null || target.data == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(target.data);
            if (string.IsNullOrEmpty(assetPath))
                return;

            string graphPathCandidate = assetPath.EndsWith("." + StateTreeGraphBaker.GraphExtension,
                StringComparison.OrdinalIgnoreCase)
                ? assetPath
                : GuessGraphPathForBakedAsset(assetPath);

            Graph graph = StateTreeGraphBaker.LoadGraphAtPath(graphPathCandidate);
            if (graph == null)
                return;

            s_Graph = graph;
            s_GraphPath = graphPathCandidate;
            s_NodeById = StateTreeGraphBaker.BuildStateIdMap(graph);
        }

        /// <summary>A tree exported with <see cref="StateTreeGraphBaker.BakeSelectedGraphToAssetFile"/>
        /// lives beside its graph and is named after it, so the graph is one string operation away.
        /// Returns null for a tree that never came from a graph (the M6 presets, for instance).</summary>
        private static string GuessGraphPathForBakedAsset(string assetPath)
        {
            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return null;
            string withoutExtension = assetPath.Substring(0, assetPath.Length - ".asset".Length);
            if (!withoutExtension.EndsWith(StateTreeGraphBaker.BakedAssetSuffix, StringComparison.Ordinal))
                return null;
            return withoutExtension.Substring(0,
                       withoutExtension.Length - StateTreeGraphBaker.BakedAssetSuffix.Length)
                   + "." + StateTreeGraphBaker.GraphExtension;
        }

        private static void ApplyTint()
        {
            ClearTint();
            if (!enabled || s_NodeById == null || string.IsNullOrEmpty(s_ActiveNodeId))
                return;
            if (!s_NodeById.TryGetValue(s_ActiveNodeId, out INode node) || node == null)
                return;

            try
            {
                s_TintedOriginalColor = node.DefaultColor;
                node.DefaultColor = k_HighlightColor;
                s_TintedNode = node;
            }
            catch (Exception)
            {
                // The graph was closed or reloaded under us: the node's implementation is gone. Drop
                // the cached map so the next transition re-resolves it.
                s_TintedNode = null;
                s_NodeById = null;
                s_Graph = null;
            }
        }

        private static void ClearTint()
        {
            if (s_TintedNode == null)
                return;
            try
            {
                s_TintedNode.DefaultColor = s_TintedOriginalColor;
            }
            catch (Exception)
            {
                // Graph already gone; nothing to restore.
            }
            s_TintedNode = null;
        }
    }
}
