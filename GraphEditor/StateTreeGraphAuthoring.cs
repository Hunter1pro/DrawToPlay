using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Writes <c>.statetree</c> graphs from code — the two authoring gestures the M7d loop needs
    /// that no window can perform for the user:
    /// <list type="bullet">
    /// <item><see cref="CreateTaskScaffold"/> — "+ New Graph Task…": a named, ready-to-extend task
    /// graph, created and baked in one call so the state that asked for it can be wired to the
    /// result immediately.</item>
    /// <item><see cref="ConvertTreeToGraph"/> — "Convert to Graph…": the REVERSE of
    /// <see cref="StateTreeGraphBaker"/>. A tree that was built by hand (a preset, or anything
    /// authored in the direct editor window) becomes a graph the same tree can be re-baked out
    /// of.</item>
    /// </list>
    ///
    /// WHY THIS FILE LIVES INSIDE THE GRAPH ASSEMBLY. Everything here calls Graph Toolkit directly
    /// (<c>GraphDatabase.CreateGraph</c>, <c>Graph.AddNode</c>, <c>Graph.Connect</c>,
    /// <c>ContextNode.CreateBlockNode</c>, <c>IPort.TrySetValue</c>). The main editor assembly
    /// cannot: brief §7.3 keeps it clear of both the frontend and the experimental toolkit, so it
    /// reaches these two methods through <c>Editor/Flow/StateTreeGraphBridge.cs</c> by reflection.
    /// That is why the surface is exactly two public static methods with plain parameter types —
    /// they are a reflection target, and every extra overload is one more thing for the bridge to
    /// pick wrong.
    ///
    /// WHAT "THE PATH" MEANS ON THE WAY OUT. Both methods return the BAKED
    /// <see cref="StateTreeAsset"/> — the graph file's main asset, which is the object a
    /// <see cref="RunSubTreeTask"/> has to point at — and the caller reads the graph's real path
    /// back off it with <c>AssetDatabase.GetAssetPath(result)</c>. That indirection is not
    /// decoration: <c>GraphDatabase.CreateGraph</c> resolves the path it is given
    /// (<c>GraphObject.AttachToAssetFile</c>, module IL 91565-91660) and
    /// <see cref="ConvertTreeToGraph"/> deliberately side-steps an occupied path rather than
    /// overwrite it, so the file that ends up on disk is not always the file that was asked for.
    ///
    /// THE ROUND-TRIP CHECK IS THE POINT OF THE CONVERTER, not a nicety.
    /// A conversion that quietly loses a transition produces a zombie that stops attacking, three
    /// steps later, with nothing to point at. So <see cref="ConvertTreeToGraph"/> bakes what it
    /// just wrote and compares it against the tree it read — state ids, per-state task type
    /// sequences, transition targets and interrupt flags in evaluation order, and every authored
    /// parameter within a float epsilon — then logs the divergences one per line, naming the state,
    /// the index and both values. The graph is NEVER deleted when the check fails: a graph you can
    /// open and inspect beats a rollback that leaves you guessing.
    ///
    /// TRANSFORMATIONS THE CONVERTER APPLIES ON PURPOSE (each reported as a warning, not a
    /// divergence, because the round-trip model expects them):
    /// <list type="number">
    /// <item>NESTING IS FLATTENED. <see cref="StateTreeNodeAsset.children"/> is organizational
    /// nesting; a graph has no such concept. Nodes that are purely organizational (no tasks, no
    /// transitions, at least one child) are dropped, and any transition that targeted one is
    /// re-pointed at the state <c>StateTreeExecutor.ResolveEntryNode</c> would have descended to
    /// (StateTreeExecutor.cs:198-206). The runtime behaviour is identical because that is the same
    /// resolution the runner performs at the moment of the transition.</item>
    /// <item>IDS ARE SLUGGED. The bake normalises every state id ("Chase Target" → "chase_target",
    /// <see cref="StateTreeGraphBaker"/>'s <c>Slug</c>), so the converter writes the SLUGGED id into
    /// the graph — what you read on the canvas is what the runner will use. A renamed id is
    /// reported, because <see cref="RunSubTreeTask.successStates"/> in a parent tree names sub-tree
    /// states by string.</item>
    /// <item>A TASK OR CONDITION WITH NO GRAPH WRAPPER CANNOT BE AUTHORED. One wrapper class per
    /// library type is what makes a node exist at all; a library type without one (today:
    /// <see cref="RunSubTreeTask"/>, which also keeps its result lists in <c>List&lt;string&gt;</c>
    /// fields that no port can carry) is skipped, warned about, and then shows up in the
    /// round-trip report as the missing task it is.</item>
    /// </list>
    /// </summary>
    public static class StateTreeGraphAuthoring
    {
        /// <summary><c>StateTreeAsset.treeKind</c> that marks a tree as a reusable task — the value
        /// the authored-task picker filters on (m7c) and the one a scaffold is born with.</summary>
        public const string TaskTreeKind = "task";

        /// <summary>Entry state of a scaffold. Neutral on purpose: a new task starts somewhere, and
        /// naming that "start" costs the author nothing to rename.</summary>
        public const string ScaffoldEntryStateId = "start";

        /// <summary>Exit state of a scaffold. Matches <see cref="RunSubTreeTask.successStates"/>'s
        /// default so a freshly created graph task already ends the way its parent reads it —
        /// entering this state IS how a sub-tree says "done".</summary>
        public const string ScaffoldExitStateId = "success";

        private const string k_Tag = "[Draw To Play] ";

        // --- layout ------------------------------------------------------------------------------
        // Graph Toolkit node positions are in graph-space points; a default node is roughly 200
        // wide, and a state grows with its blocks. These leave room for a state with a handful of
        // tasks plus the transitions stacked under it.

        private const float k_ColumnWidth = 380f;
        private const float k_RowHeight = 560f;
        private const int k_StatesPerRow = 6;
        private const float k_EntryOffsetX = -320f;
        private const float k_TransitionOffsetX = 200f;
        private const float k_TransitionOffsetY = 160f;
        private const float k_TransitionStackY = 130f;
        private const float k_ConditionOffsetX = -200f;
        private const float k_ConditionOffsetY = 55f;

        /// <summary>Gap between the authored evaluation orders of one state's transitions. Spaced
        /// rather than consecutive so an author can slot a new transition in between two of them
        /// without renumbering the rest.</summary>
        private const int k_TransitionOrderStep = 10;

        /// <summary>Round-trip tolerance for float-ish fields. A port stores what it was handed, so
        /// the only error in the loop is serialisation rounding; this is loose enough for that and
        /// tight enough that a real retune (0.4 → 0.5) is still a divergence.</summary>
        private const float k_FloatEpsilon = 1e-4f;

        // ------------------------------------------------------------------ public entry points

        /// <summary>
        /// Create the scaffold graph behind "+ New Graph Task…": an
        /// <see cref="EntryNode"/> naming the tree and marking it <see cref="TaskTreeKind"/>, wired
        /// to a "<see cref="ScaffoldEntryStateId"/>" state, wired on through an unconditional
        /// transition to a "<see cref="ScaffoldExitStateId"/>" state.
        ///
        /// The exit state is there so the CONVENTION IS VISIBLE. A sub-tree reports its result by
        /// entering a state whose id is in the parent task's success/failure list
        /// (<see cref="RunSubTreeTask"/>), and nothing about an empty canvas would ever tell an
        /// author that. Shipping the wire as well as the state means the new task is also
        /// immediately RUNNABLE — it succeeds at once, which is the correct behaviour for a task
        /// with nothing in it yet — so the author fills the middle in rather than debugging a
        /// composite that hangs on its first tick.
        ///
        /// EXPECT TWO WARNINGS on the import that follows: both scaffold states are task-less, and
        /// the bake says so. They stop as soon as the first task block is dropped into "start".
        /// </summary>
        /// <param name="assetPath">Where to write the graph. The <c>.statetree</c> extension is
        /// added when missing. An existing file here IS overwritten — the caller is expected to
        /// have asked (EditorUtility.SaveFilePanelInProject does).</param>
        /// <param name="treeName">Tree name for the entry node, and the fallback asset name. Empty
        /// falls back to the file name.</param>
        /// <returns>The baked <see cref="StateTreeAsset"/> that is the new file's main asset — the
        /// object to point a <see cref="RunSubTreeTask"/> at — or null when the graph could not be
        /// created, saved or baked (every failure is logged with its cause).</returns>
        public static StateTreeAsset CreateTaskScaffold(string assetPath, string treeName)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError(k_Tag + "New graph task: no asset path was given.");
                return null;
            }

            string path = NormalizeGraphPath(assetPath);
            string name = string.IsNullOrEmpty(treeName)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : treeName;

            StateTreeGraph graph = CreateGraphAsset(path, true, out path, out string error);
            if (graph == null)
            {
                Debug.LogError(k_Tag + "New graph task: " + error);
                return null;
            }

            var problems = new List<string>();

            Vector2 startPosition = StatePosition(0);
            Vector2 exitPosition = StatePosition(1);

            StateNode start = AddState(graph, ScaffoldEntryStateId, "Start", startPosition, problems);
            StateNode exit = AddState(graph, ScaffoldExitStateId, "Success", exitPosition, problems);

            var entry = AddNode(graph, typeof(EntryNode), startPosition + new Vector2(k_EntryOffsetX, 0f), problems) as EntryNode;
            if (entry != null)
            {
                WritePort(entry, EntryNode.TreeNamePortName, typeof(string), name, problems);
                WritePort(entry, EntryNode.TreeKindPortName, typeof(string), TaskTreeKind, problems);
                Connect(graph, entry, EntryNode.EntryPortName, start, StateTreeFlowPorts.InPortName, problems);
            }

            // The wire out of "start" is unconditional: a task with nothing in it yet succeeds
            // immediately, which is the only honest thing for it to do.
            AddTransition(graph, start, exit, null, null, null, false, k_TransitionOrderStep,
                TransitionPosition(startPosition, 0), problems);

            SaveAndImport(graph, path);

            StateTreeAsset baked = FindTreeAt(path);
            if (problems.Count > 0)
                Debug.LogError(BuildMessage($"New graph task '{name}' ({path}) was authored with problems:", problems));

            if (baked == null)
            {
                Debug.LogError(k_Tag + $"New graph task '{name}': {path} produced no {nameof(StateTreeAsset)} "
                    + "when it was imported. The console above carries the bake errors — the graph is on disk, "
                    + "open it and fix what it names.");
                return null;
            }

            return baked;
        }

        /// <summary>
        /// Author a graph that bakes back into <paramref name="tree"/> — the "Convert to Graph…"
        /// half of the M7d loop, for the hand-authored trees that predate the graph frontend.
        ///
        /// The source asset is NEVER touched: the graph is a second, independent copy of the same
        /// behaviour, and the caller decides whether to re-point anything at it. If something
        /// already occupies <paramref name="assetPath"/> the converter steps aside to a unique path
        /// rather than overwrite it (converting the same tree twice therefore yields "Tree 1", not a
        /// lost graph).
        ///
        /// Every conversion verifies itself; see the class comment for what is compared and for the
        /// three transformations that are applied on purpose. Divergences are logged as errors and
        /// the graph is kept.
        /// </summary>
        /// <param name="tree">The tree to read. Its <c>root</c> is the organizational node; states
        /// are found by walking <c>children</c> exactly the way the runner indexes them.</param>
        /// <param name="assetPath">Preferred path for the graph, conventionally beside the tree with
        /// the same name. The <c>.statetree</c> extension is added when missing.</param>
        /// <returns>The baked <see cref="StateTreeAsset"/> that is the graph file's main asset, or
        /// null when the graph could not be created, saved or baked. NOTE: a tree that baked but
        /// diverges is still returned — the divergences are on the console, and a caller that
        /// re-points at it can be undone, whereas a null would leave the user with a graph nothing
        /// references.</returns>
        public static StateTreeAsset ConvertTreeToGraph(StateTreeAsset tree, string assetPath)
        {
            if (tree == null)
            {
                Debug.LogError(k_Tag + "Convert to graph: no tree was given.");
                return null;
            }
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError(k_Tag + $"Convert to graph ('{tree.name}'): no asset path was given.");
                return null;
            }

            var notes = new List<string>();
            var problems = new List<string>();

            TreeModel model = BuildModel(tree, notes);
            if (model == null || model.states.Count == 0)
            {
                Debug.LogError(k_Tag + $"Convert to graph ('{tree.name}'): the tree has no states to "
                    + "convert. A tree needs a root with at least one child state (or a root that is "
                    + "itself a state).");
                return null;
            }

            string path = NormalizeGraphPath(assetPath);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                string unique = AssetDatabase.GenerateUniqueAssetPath(path);
                notes.Add($"{path} already exists, so the graph was written to {unique} instead.");
                path = unique;
            }

            StateTreeGraph graph = CreateGraphAsset(path, false, out path, out string error);
            if (graph == null)
            {
                Debug.LogError(k_Tag + $"Convert to graph ('{tree.name}'): {error}");
                return null;
            }

            AuthorModel(graph, model, problems);
            SaveAndImport(graph, path);

            var divergences = new List<string>();
            StateTreeAsset baked = VerifyRoundTrip(path, model, divergences);

            if (notes.Count > 0)
                Debug.LogWarning(BuildMessage($"Convert to graph ('{tree.name}' → {path}) — what changed on the way:", notes));

            if (problems.Count > 0)
                Debug.LogError(BuildMessage($"Convert to graph ('{tree.name}' → {path}) could not author part of the tree:", problems));

            if (divergences.Count > 0)
            {
                Debug.LogError(BuildMessage(
                    $"Convert to graph ('{tree.name}' → {path}): the graph does NOT bake back into the "
                    + "tree it came from. The graph was kept so you can open it and see for yourself. "
                    + "Divergences:", divergences)
                    + $"\n  The original tree is untouched at {AssetDatabase.GetAssetPath(tree)}.");
            }
            else if (problems.Count == 0)
            {
                Debug.Log(k_Tag + $"Converted '{tree.name}' to {path}: {model.states.Count} state(s), "
                    + $"{CountTransitions(model)} transition(s), round-trip verified against the source tree.");
            }

            return baked;
        }

        // ------------------------------------------------------------------ the model

        /// <summary>One task of one state: the authored instance, plus the block class that can
        /// carry it (null when the library type has no graph wrapper).</summary>
        private sealed class TaskModel
        {
            public StateTreeTaskAsset source;
            public Type taskType;
            public Type nodeType;
        }

        /// <summary>One way out of a state, with the target already resolved through the runner's
        /// own entry-descent rule and the id already normalised to what the bake will produce.</summary>
        private sealed class TransitionModel
        {
            public string targetId;
            public bool checkWhileRunning;
            public StateTreeConditionAsset condition;
            public Type conditionType;
            public Type conditionNodeType;
        }

        /// <summary>One state, as it will exist in the graph and again in the re-baked tree.</summary>
        private sealed class StateModel
        {
            public string id;
            public string displayName;
            public string sourceId;
            public readonly List<TaskModel> tasks = new List<TaskModel>();
            public readonly List<TransitionModel> transitions = new List<TransitionModel>();
            public Vector2 position;
            public StateNode node;
        }

        /// <summary>The whole conversion, decided before a single node is created. Authoring reads
        /// it and the round-trip check compares against it, so "what the graph should contain" is
        /// stated once.</summary>
        private sealed class TreeModel
        {
            public string treeName;
            public string treeKind;
            public readonly List<StateModel> states = new List<StateModel>();
            public StateModel entry;
        }

        /// <summary>
        /// Flatten <paramref name="tree"/> into the states a graph can hold.
        ///
        /// The walk mirrors <c>StateTreeExecutor.BuildIndex</c> (every node, recursively, keyed by
        /// id) and <c>ResolveEntryNode</c> (a task-less, transition-less node with children stands
        /// for its first leaf) because those two rules are what the runtime actually does with
        /// nesting — reproducing them here is what makes the flattened graph behave identically
        /// rather than merely look similar.
        /// </summary>
        private static TreeModel BuildModel(StateTreeAsset tree, List<string> notes)
        {
            if (tree.root == null)
                return null;

            var model = new TreeModel();
            model.treeName = string.IsNullOrEmpty(tree.treeName) ? tree.name : tree.treeName;
            model.treeKind = string.IsNullOrEmpty(tree.treeKind) ? StateTreeGraph.DefaultTreeKind : tree.treeKind;
            if (string.IsNullOrEmpty(tree.treeName))
                notes.Add($"The tree had no treeName; the graph's Entry node was named after the asset ('{model.treeName}').");

            var ordered = new List<StateTreeNodeAsset>();
            var byId = new Dictionary<string, StateTreeNodeAsset>(StringComparer.Ordinal);
            CollectNodes(tree.root, ordered, byId, 0);

            // Organizational nodes have no graph face; their transitions and tasks are (by
            // definition) empty, and the runner only ever passes through them.
            var sources = new List<StateTreeNodeAsset>();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (IsOrganizational(ordered[i]))
                {
                    if (!ReferenceEquals(ordered[i], tree.root))
                    {
                        notes.Add($"Node '{ordered[i].nodeId}' only groups other nodes (no tasks, no transitions); "
                            + "it was dropped and everything that targeted it now targets the state the runner "
                            + "would have descended into.");
                    }
                    continue;
                }
                sources.Add(ordered[i]);
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var models = new Dictionary<StateTreeNodeAsset, StateModel>();
            for (int i = 0; i < sources.Count; i++)
            {
                StateTreeNodeAsset source = sources[i];
                var state = new StateModel
                {
                    sourceId = source.nodeId ?? string.Empty,
                    id = MakeStateId(source.nodeId, i, used),
                    displayName = source.displayName ?? string.Empty,
                    position = StatePosition(i)
                };

                if (!string.Equals(state.id, state.sourceId, StringComparison.Ordinal))
                {
                    notes.Add($"State id '{state.sourceId}' is not in the form the bake writes, so the graph "
                        + $"uses '{state.id}'. A parent RunSubTreeTask that names this state in its "
                        + "success/failure list has to be updated.");
                }

                model.states.Add(state);
                models[source] = state;
            }

            for (int i = 0; i < sources.Count; i++)
                BuildStateContent(sources[i], models[sources[i]], byId, models, notes);

            StateTreeNodeAsset entrySource = ResolveEntryNode(tree.root);
            if (entrySource != null && models.TryGetValue(entrySource, out StateModel entryState))
            {
                model.entry = entryState;
            }
            else
            {
                model.entry = model.states.Count > 0 ? model.states[0] : null;
                if (model.entry != null)
                {
                    notes.Add("The tree's entry could not be resolved to a state, so the graph starts at "
                        + $"'{model.entry.id}' (the first state).");
                }
            }

            return model;
        }

        private static void BuildStateContent(StateTreeNodeAsset source, StateModel state,
            Dictionary<string, StateTreeNodeAsset> byId, Dictionary<StateTreeNodeAsset, StateModel> models,
            List<string> notes)
        {
            for (int i = 0; i < source.tasks.Count; i++)
            {
                StateTreeTaskAsset task = source.tasks[i];
                if (task == null)
                {
                    notes.Add($"State '{state.id}' task {i} is missing (a deleted sub-asset); it was skipped.");
                    continue;
                }

                Type taskType = task.GetType();
                Type nodeType = FindTaskNodeType(taskType);
                if (nodeType == null)
                {
                    notes.Add($"State '{state.id}' task {i} is a {taskType.Name}, which no graph block wraps, "
                        + "so it could not be authored. Add a StateTaskBlockNode subclass naming that type to "
                        + "put it on the canvas.");
                }

                state.tasks.Add(new TaskModel { source = task, taskType = taskType, nodeType = nodeType });
            }

            for (int i = 0; i < source.transitions.Count; i++)
            {
                StateTreeTransition transition = source.transitions[i];
                if (transition == null)
                {
                    notes.Add($"State '{state.id}' transition {i} is null; it was skipped.");
                    continue;
                }

                if (string.IsNullOrEmpty(transition.targetNodeId)
                    || !byId.TryGetValue(transition.targetNodeId, out StateTreeNodeAsset target))
                {
                    notes.Add($"State '{state.id}' transition {i} points at '{transition.targetNodeId}', which is "
                        + "not a node of this tree (the runner logs the same thing at run time); it was dropped.");
                    continue;
                }

                StateTreeNodeAsset resolved = ResolveEntryNode(target);
                if (resolved == null || !models.TryGetValue(resolved, out StateModel targetState))
                {
                    notes.Add($"State '{state.id}' transition {i} targets '{transition.targetNodeId}', which does not "
                        + "resolve to a state; it was dropped.");
                    continue;
                }

                if (!ReferenceEquals(resolved, target))
                {
                    notes.Add($"State '{state.id}' transition {i} targeted the grouping node "
                        + $"'{target.nodeId}'; the graph wires it straight to '{targetState.id}', which is where "
                        + "the runner would have ended up anyway.");
                }

                Type conditionType = transition.condition != null ? transition.condition.GetType() : null;
                Type conditionNodeType = conditionType != null ? FindConditionNodeType(conditionType) : null;
                if (conditionType != null && conditionNodeType == null)
                {
                    notes.Add($"State '{state.id}' transition {i} is gated by a {conditionType.Name}, which no graph "
                        + "node wraps, so the transition was authored WITHOUT its condition — it would fire "
                        + "unconditionally. Add a StateTreeConditionNode subclass naming that type.");
                }

                state.transitions.Add(new TransitionModel
                {
                    targetId = targetState.id,
                    checkWhileRunning = transition.checkWhileRunning,
                    condition = transition.condition,
                    conditionType = conditionType,
                    conditionNodeType = conditionNodeType
                });
            }
        }

        /// <summary>Every node of the tree in a stable pre-order, plus the id index the runner
        /// builds (<c>StateTreeExecutor.BuildIndex</c>: first node wins on a duplicate id).</summary>
        private static void CollectNodes(StateTreeNodeAsset node, List<StateTreeNodeAsset> ordered,
            Dictionary<string, StateTreeNodeAsset> byId, int depth)
        {
            // 256 matches the authored-children guard in StateTreeAsset.DeepCopyNode: a cycle in
            // children must not hang the editor.
            if (node == null || depth > 256 || ordered.Contains(node))
                return;

            ordered.Add(node);
            if (!string.IsNullOrEmpty(node.nodeId) && !byId.ContainsKey(node.nodeId))
                byId[node.nodeId] = node;

            for (int i = 0; i < node.children.Count; i++)
                CollectNodes(node.children[i], ordered, byId, depth + 1);
        }

        /// <summary>A node that only groups others: no tasks, no transitions, at least one child.
        /// This is the exact predicate <c>StateTreeExecutor.ResolveEntryNode</c> descends
        /// through.</summary>
        private static bool IsOrganizational(StateTreeNodeAsset node)
        {
            return node != null && node.tasks.Count == 0 && node.transitions.Count == 0
                && node.children.Count > 0;
        }

        /// <summary>Mirror of <c>StateTreeExecutor.ResolveEntryNode</c> (StateTreeExecutor.cs:198-206),
        /// so a converted transition lands where the runner would have landed.</summary>
        private static StateTreeNodeAsset ResolveEntryNode(StateTreeNodeAsset node)
        {
            StateTreeNodeAsset current = node;
            int guard = 0;
            while (current != null && current.tasks.Count == 0 && current.transitions.Count == 0
                && current.children.Count > 0 && guard++ < 256)
            {
                current = current.children[0];
            }
            return current;
        }

        private static int CountTransitions(TreeModel model)
        {
            int count = 0;
            for (int i = 0; i < model.states.Count; i++)
                count += model.states[i].transitions.Count;
            return count;
        }

        // ------------------------------------------------------------------ authoring

        /// <summary>Lay the model out on the canvas: states left to right in tree order, the entry
        /// marker to the left of the first one, each transition stacked under the state it leaves
        /// with its condition tucked in beside it.</summary>
        private static void AuthorModel(StateTreeGraph graph, TreeModel model, List<string> problems)
        {
            for (int i = 0; i < model.states.Count; i++)
            {
                StateModel state = model.states[i];
                state.node = AddState(graph, state.id, state.displayName, state.position, problems);
                if (state.node == null)
                    continue;

                for (int t = 0; t < state.tasks.Count; t++)
                    AddTask(state, state.tasks[t], t, problems);
            }

            if (model.entry != null && model.entry.node != null)
            {
                var entry = AddNode(graph, typeof(EntryNode),
                    model.entry.position + new Vector2(k_EntryOffsetX, 0f), problems) as EntryNode;
                if (entry != null)
                {
                    WritePort(entry, EntryNode.TreeNamePortName, typeof(string), model.treeName, problems);
                    WritePort(entry, EntryNode.TreeKindPortName, typeof(string), model.treeKind, problems);
                    Connect(graph, entry, EntryNode.EntryPortName, model.entry.node,
                        StateTreeFlowPorts.InPortName, problems);
                }
            }

            // Transitions last: every state node has to exist before anything can be wired to it.
            for (int i = 0; i < model.states.Count; i++)
            {
                StateModel state = model.states[i];
                if (state.node == null)
                    continue;

                for (int t = 0; t < state.transitions.Count; t++)
                {
                    TransitionModel transition = state.transitions[t];
                    StateModel target = FindState(model, transition.targetId);
                    if (target == null || target.node == null)
                    {
                        problems.Add($"State '{state.id}' transition {t}: '{transition.targetId}' has no node to "
                            + "wire to.");
                        continue;
                    }

                    AddTransition(graph, state.node, target.node, transition.conditionNodeType,
                        transition.conditionType, transition.condition, transition.checkWhileRunning,
                        (t + 1) * k_TransitionOrderStep, TransitionPosition(state.position, t), problems);
                }
            }
        }

        /// <summary>One state node, carrying its identity on the two ports the bake reads.</summary>
        private static StateNode AddState(StateTreeGraph graph, string nodeId, string displayName,
            Vector2 position, List<string> problems)
        {
            var state = AddNode(graph, typeof(StateNode), position, problems) as StateNode;
            if (state == null)
                return null;

            WritePort(state, StateNode.NodeIdPortName, typeof(string), nodeId, problems);
            WritePort(state, StateNode.DisplayNamePortName, typeof(string), displayName ?? string.Empty, problems);
            SetTitle(state, string.IsNullOrEmpty(displayName) ? nodeId : displayName);
            return state;
        }

        /// <summary>One task block inside a state, with every parameter copied off the authored
        /// asset. Blocks are appended, so the graph's block order IS the baked <c>tasks</c>
        /// order.</summary>
        private static void AddTask(StateModel state, TaskModel task, int index, List<string> problems)
        {
            if (task.nodeType == null)
                return;

            int before = state.node.BlockCount;
            try
            {
                state.node.CreateBlockNode(task.nodeType, -1);
            }
            catch (Exception exception)
            {
                problems.Add($"State '{state.id}' task {index}: {task.nodeType.Name} could not be added to the "
                    + $"state ({Describe(exception)}).");
                return;
            }

            if (state.node.BlockCount <= before)
            {
                problems.Add($"State '{state.id}' task {index}: {task.nodeType.Name} was not added to the state "
                    + "(the context refused it).");
                return;
            }

            BlockNode block = state.node.GetBlock(state.node.BlockCount - 1);
            if (block == null)
            {
                problems.Add($"State '{state.id}' task {index}: the new {task.nodeType.Name} block could not be "
                    + "read back.");
                return;
            }

            block.DefineNode();
            WriteParameters(block, task.taskType, task.source, problems);
        }

        /// <summary>
        /// One arrow, with its condition node when it has one. <paramref name="order"/> is the
        /// authoring-only sort key the bake uses to reproduce the source tree's EVALUATION ORDER —
        /// the runner takes the first transition whose condition passes, so the order of a state's
        /// transitions is behaviour, and wire creation order is invisible on a canvas.
        ///
        /// The condition's parameters are written onto the node this method creates rather than onto
        /// whatever is found on the wire afterwards: one less thing that has to be true (the wire)
        /// for the values to arrive.
        /// </summary>
        private static TransitionNode AddTransition(StateTreeGraph graph, StateNode from, StateNode to,
            Type conditionNodeType, Type conditionType, ScriptableObject conditionSource,
            bool checkWhileRunning, int order, Vector2 position, List<string> problems)
        {
            if (from == null || to == null)
            {
                problems.Add("A transition was skipped because one of the two states it connects does not "
                    + "exist on the canvas.");
                return null;
            }

            var transition = AddNode(graph, typeof(TransitionNode), position, problems) as TransitionNode;
            if (transition == null)
                return null;

            WritePort(transition, TransitionNode.CheckWhileRunningPortName, typeof(bool), checkWhileRunning, problems);
            WritePort(transition, TransitionNode.OrderPortName, typeof(int), order, problems);

            Connect(graph, from, StateTreeFlowPorts.OutPortName, transition, TransitionNode.FromPortName, problems);
            Connect(graph, transition, TransitionNode.ToPortName, to, StateTreeFlowPorts.InPortName, problems);

            if (conditionNodeType != null)
            {
                var condition = AddNode(graph, conditionNodeType,
                    position + new Vector2(k_ConditionOffsetX, k_ConditionOffsetY), problems) as StateTreeConditionNode;
                if (condition != null)
                {
                    WriteParameters(condition, conditionType, conditionSource, problems);
                    Connect(graph, condition, StateTreeConditionNode.ConditionPortName,
                        transition, TransitionNode.ConditionPortName, problems);
                }
            }

            return transition;
        }

        /// <summary>Copy an authored library asset into the ports of the node that wraps it. The
        /// field list comes from <see cref="LibraryParameterPorts.GetParameterFields"/> — the same
        /// call the node used to DECLARE those ports and the bake uses to read them back — so this
        /// cannot drift from either side.</summary>
        private static void WriteParameters(INode node, Type libraryType, ScriptableObject source,
            List<string> problems)
        {
            if (node == null || libraryType == null || source == null)
                return;

            IReadOnlyList<FieldInfo> fields = LibraryParameterPorts.GetParameterFields(libraryType);
            for (int i = 0; i < fields.Count; i++)
            {
                FieldInfo field = fields[i];
                object value = field.GetValue(source);

                // A null reference is already what a fresh port holds, and TrySetValue on a null
                // asset reference is the one write that can fail for a reason nobody can act on.
                if (value == null)
                    continue;

                IPort port = node.GetInputPortByName(field.Name);
                if (port == null)
                {
                    problems.Add($"{node.GetType().Name} has no '{field.Name}' port for "
                        + $"{libraryType.Name}.{field.Name}; that parameter was not carried over.");
                    continue;
                }

                if (!LibraryParameterPorts.TryWriteValue(port, field.FieldType, value))
                {
                    problems.Add($"{libraryType.Name}.{field.Name} = {Describe(value)} was refused by the "
                        + $"'{field.Name}' port of {node.GetType().Name}.");
                }
            }
        }

        private static Node AddNode(StateTreeGraph graph, Type nodeType, Vector2 position, List<string> problems)
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
                // Ports exist only after a definition pass, and the caller wires and fills this node
                // in the next statement.
                node.DefineNode();
            }
            catch (Exception exception)
            {
                problems.Add($"Adding a {nodeType.Name} to the graph failed: {Describe(exception)}");
                return null;
            }

            return node;
        }

        private static bool Connect(StateTreeGraph graph, INode from, string fromPort, INode to, string toPort,
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

            problems.Add($"{from.GetType().Name}.{fromPort} → {to.GetType().Name}.{toPort} was refused (the port "
                + "types do not match, or the input already holds a wire).");
            return false;
        }

        private static bool WritePort(INode node, string portName, Type valueType, object value,
            List<string> problems)
        {
            IPort port = node?.GetInputPortByName(portName);
            if (port == null)
            {
                problems.Add($"{(node == null ? "A node" : node.GetType().Name)} has no '{portName}' port to write "
                    + $"{Describe(value)} into.");
                return false;
            }

            if (LibraryParameterPorts.TryWriteValue(port, valueType, value))
                return true;

            problems.Add($"{node.GetType().Name}.{portName} refused {Describe(value)}.");
            return false;
        }

        /// <summary>Name the node on the canvas. Cosmetic and best-effort: <c>INode.Title</c> is a
        /// live view of the node's implementation, and a graph that is fine with an unnamed node is
        /// not worth failing a conversion over. The bake never reads a title unless the id port is
        /// empty, and this file always writes that port.</summary>
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

        private static Vector2 StatePosition(int index)
        {
            int column = index % k_StatesPerRow;
            int row = index / k_StatesPerRow;
            return new Vector2(column * k_ColumnWidth, row * k_RowHeight);
        }

        private static Vector2 TransitionPosition(Vector2 statePosition, int index)
        {
            return statePosition + new Vector2(k_TransitionOffsetX,
                k_TransitionOffsetY + index * k_TransitionStackY);
        }

        private static StateModel FindState(TreeModel model, string id)
        {
            for (int i = 0; i < model.states.Count; i++)
            {
                if (string.Equals(model.states[i].id, id, StringComparison.Ordinal))
                    return model.states[i];
            }
            return null;
        }

        // ------------------------------------------------------------------ round trip

        /// <summary>Bake diagnostics collected instead of logged, so a failed bake can be reported
        /// as part of ONE round-trip message rather than as loose console noise the user has to
        /// correlate.</summary>
        private sealed class CollectingBakeLog : StateTreeGraphBaker.IBakeLog
        {
            public readonly List<string> messages = new List<string>();

            public void Error(string message, object nodeContext) => Add("error", message, nodeContext);

            public void Warning(string message, object nodeContext) => Add("warning", message, nodeContext);

            private void Add(string kind, string message, object nodeContext)
            {
                string node = nodeContext is INode typed ? typed.GetType().Name : string.Empty;
                messages.Add(node.Length == 0 ? $"bake {kind}: {message}" : $"bake {kind} [{node}]: {message}");
            }
        }

        /// <summary>
        /// Re-read what was written and prove it means the same thing.
        ///
        /// The IMPORTED tree is preferred over a fresh in-memory bake because it is the artifact
        /// that will actually be referenced — it went through serialisation, the importer and the
        /// asset database, and any of the three could have lost something. The in-memory bake is the
        /// fallback for the case where no tree came out at all, and exists so that failure is
        /// reported with the bake's own error list instead of a shrug.
        /// </summary>
        private static StateTreeAsset VerifyRoundTrip(string graphPath, TreeModel model, List<string> divergences)
        {
            StateTreeAsset imported = FindTreeAt(graphPath);
            if (imported != null)
            {
                Compare(model, imported, divergences);
                return imported;
            }

            divergences.Add($"importing {graphPath} produced no {nameof(StateTreeAsset)}.");

            Graph reloaded = StateTreeGraphBaker.LoadGraphAtPath(graphPath);
            if (reloaded == null)
            {
                divergences.Add("the graph could not be loaded back from disk either — nothing was saved.");
                return null;
            }

            var log = new CollectingBakeLog();
            StateTreeGraphBaker.BakeResult result = StateTreeGraphBaker.Bake(reloaded, log);
            try
            {
                divergences.AddRange(log.messages);
                if (result.tree != null)
                    Compare(model, result.tree, divergences);
            }
            finally
            {
                // A bake outside the importer owns every object it created; leaving them alive
                // would leak a whole tree per conversion until the next domain reload.
                result.DestroyObjects();
            }

            return null;
        }

        /// <summary>Compare the baked tree against the model, one precise line per difference.
        /// Everything compared here is behaviour: which states exist, which one the runner starts
        /// in, what each state runs, and — in evaluation order — where each state can go and what
        /// gates it.</summary>
        private static void Compare(TreeModel model, StateTreeAsset baked, List<string> divergences)
        {
            if (!string.Equals(model.treeName, baked.treeName, StringComparison.Ordinal))
                divergences.Add($"treeName: expected '{model.treeName}', baked '{baked.treeName}'.");
            if (!string.Equals(model.treeKind, baked.treeKind, StringComparison.Ordinal))
                divergences.Add($"treeKind: expected '{model.treeKind}', baked '{baked.treeKind}'.");

            if (baked.root == null)
            {
                divergences.Add("the baked tree has no root node.");
                return;
            }

            List<StateTreeNodeAsset> bakedStates = baked.root.children;
            var bakedById = new Dictionary<string, StateTreeNodeAsset>(StringComparer.Ordinal);
            for (int i = 0; i < bakedStates.Count; i++)
            {
                StateTreeNodeAsset state = bakedStates[i];
                if (state == null)
                {
                    divergences.Add($"the baked tree's child {i} is missing.");
                    continue;
                }
                if (bakedById.ContainsKey(state.nodeId))
                    divergences.Add($"the baked tree has two states called '{state.nodeId}'.");
                else
                    bakedById[state.nodeId] = state;
            }

            string expectedEntry = model.entry != null ? model.entry.id : string.Empty;
            string bakedEntry = bakedStates.Count > 0 && bakedStates[0] != null ? bakedStates[0].nodeId : string.Empty;
            if (!string.Equals(expectedEntry, bakedEntry, StringComparison.Ordinal))
            {
                divergences.Add($"entry state: expected '{expectedEntry}', baked '{bakedEntry}' "
                    + "(the runner enters the root's first child).");
            }

            for (int i = 0; i < model.states.Count; i++)
            {
                StateModel state = model.states[i];
                if (!bakedById.TryGetValue(state.id, out StateTreeNodeAsset bakedState))
                {
                    divergences.Add($"state '{state.id}' is missing from the baked tree.");
                    continue;
                }

                bakedById.Remove(state.id);
                CompareState(state, bakedState, divergences);
            }

            foreach (KeyValuePair<string, StateTreeNodeAsset> extra in bakedById)
                divergences.Add($"the baked tree has an extra state '{extra.Key}' that the source tree does not.");
        }

        private static void CompareState(StateModel state, StateTreeNodeAsset baked, List<string> divergences)
        {
            if (!string.IsNullOrEmpty(state.displayName)
                && !string.Equals(state.displayName, baked.displayName, StringComparison.Ordinal))
            {
                divergences.Add($"state '{state.id}' displayName: expected '{state.displayName}', "
                    + $"baked '{baked.displayName}'.");
            }

            int taskCount = Math.Max(state.tasks.Count, baked.tasks.Count);
            for (int i = 0; i < taskCount; i++)
            {
                TaskModel expected = i < state.tasks.Count ? state.tasks[i] : null;
                StateTreeTaskAsset actual = i < baked.tasks.Count ? baked.tasks[i] : null;

                if (expected == null)
                {
                    divergences.Add($"state '{state.id}' task {i}: the baked tree has an extra "
                        + $"{DescribeType(actual)}.");
                    continue;
                }
                if (actual == null)
                {
                    divergences.Add($"state '{state.id}' task {i}: expected {expected.taskType.Name}, the baked "
                        + "tree has nothing.");
                    continue;
                }
                if (actual.GetType() != expected.taskType)
                {
                    divergences.Add($"state '{state.id}' task {i}: expected {expected.taskType.Name}, baked "
                        + $"{actual.GetType().Name}.");
                    continue;
                }

                CompareParameters($"state '{state.id}' task {i} ({expected.taskType.Name})",
                    expected.source, actual, divergences);
            }

            int transitionCount = Math.Max(state.transitions.Count, baked.transitions.Count);
            for (int i = 0; i < transitionCount; i++)
            {
                TransitionModel expected = i < state.transitions.Count ? state.transitions[i] : null;
                StateTreeTransition actual = i < baked.transitions.Count ? baked.transitions[i] : null;

                if (expected == null)
                {
                    divergences.Add($"state '{state.id}' transition {i}: the baked tree has an extra transition "
                        + $"to '{(actual != null ? actual.targetNodeId : "?")}'.");
                    continue;
                }
                if (actual == null)
                {
                    divergences.Add($"state '{state.id}' transition {i}: expected a transition to "
                        + $"'{expected.targetId}', the baked tree has none.");
                    continue;
                }

                if (!string.Equals(expected.targetId, actual.targetNodeId, StringComparison.Ordinal))
                {
                    divergences.Add($"state '{state.id}' transition {i} target: expected '{expected.targetId}', "
                        + $"baked '{actual.targetNodeId}'.");
                }

                if (expected.checkWhileRunning != actual.checkWhileRunning)
                {
                    divergences.Add($"state '{state.id}' transition {i} → '{expected.targetId}' is "
                        + $"{(expected.checkWhileRunning ? "an interrupt" : "a completion transition")} in the tree "
                        + $"and {(actual.checkWhileRunning ? "an interrupt" : "a completion transition")} in the "
                        + "baked graph.");
                }

                Type expectedCondition = expected.conditionType;
                Type actualCondition = actual.condition != null ? actual.condition.GetType() : null;
                if (expectedCondition != actualCondition)
                {
                    divergences.Add($"state '{state.id}' transition {i} → '{expected.targetId}' condition: expected "
                        + $"{(expectedCondition != null ? expectedCondition.Name : "none")}, baked "
                        + $"{(actualCondition != null ? actualCondition.Name : "none")}.");
                    continue;
                }

                if (expectedCondition != null)
                {
                    CompareParameters(
                        $"state '{state.id}' transition {i} → '{expected.targetId}' condition ({expectedCondition.Name})",
                        expected.condition, actual.condition, divergences);
                }
            }
        }

        /// <summary>Field-by-field comparison of an authored library asset against the one the bake
        /// produced. Reflection selects the same surface <see cref="LibraryParameterPorts"/> does —
        /// public, serialized, not <see cref="HideInInspector"/> — and a field whose TYPE no port can
        /// carry says so in the message, because that is a permanent limit of the graph rather than a
        /// bug in this conversion.</summary>
        private static void CompareParameters(string label, ScriptableObject expected, ScriptableObject actual,
            List<string> divergences)
        {
            if (expected == null || actual == null)
                return;

            Type type = expected.GetType();
            if (actual.GetType() != type)
                return;

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsInitOnly || field.IsNotSerialized || field.IsStatic)
                    continue;
                if (field.GetCustomAttribute<HideInInspector>() != null)
                    continue;

                object a = field.GetValue(expected);
                object b = field.GetValue(actual);
                if (ValuesEqual(a, b, field.FieldType))
                    continue;

                string why = LibraryParameterPorts.IsSupportedParameterType(field.FieldType)
                    ? string.Empty
                    : $" (a {field.FieldType.Name} cannot be carried by a graph port)";
                divergences.Add($"{label}.{field.Name}: tree has {Describe(a)}, baked graph has {Describe(b)}{why}");
            }
        }

        private static bool ValuesEqual(object a, object b, Type type)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return (UnityEngine.Object)a == (UnityEngine.Object)b;

            if (type == typeof(string))
                return string.Equals((string)a ?? string.Empty, (string)b ?? string.Empty, StringComparison.Ordinal);

            if (a == null || b == null)
                return a == null && b == null;

            if (type == typeof(float))
                return Approximately((float)a, (float)b);
            if (type == typeof(double))
                return Math.Abs((double)a - (double)b) <= k_FloatEpsilon;
            if (type == typeof(Vector2))
                return Approximately((Vector2)a, (Vector2)b);
            if (type == typeof(Vector3))
                return Approximately((Vector3)a, (Vector3)b);
            if (type == typeof(Vector4))
                return Approximately((Vector4)a, (Vector4)b);
            if (type == typeof(Color))
            {
                var ca = (Color)a;
                var cb = (Color)b;
                return Approximately(ca.r, cb.r) && Approximately(ca.g, cb.g)
                    && Approximately(ca.b, cb.b) && Approximately(ca.a, cb.a);
            }

            return Equals(a, b);
        }

        /// <summary>Relative tolerance: a range of 130 px ÷ 32 and a cooldown of 0.15 s cannot share
        /// one absolute epsilon and both stay meaningful.</summary>
        private static bool Approximately(float a, float b)
            => Math.Abs(a - b) <= k_FloatEpsilon * Math.Max(1f, Math.Max(Math.Abs(a), Math.Abs(b)));

        private static bool Approximately(Vector2 a, Vector2 b)
            => Approximately(a.x, b.x) && Approximately(a.y, b.y);

        private static bool Approximately(Vector3 a, Vector3 b)
            => Approximately(a.x, b.x) && Approximately(a.y, b.y) && Approximately(a.z, b.z);

        private static bool Approximately(Vector4 a, Vector4 b)
            => Approximately(a.x, b.x) && Approximately(a.y, b.y) && Approximately(a.z, b.z)
                && Approximately(a.w, b.w);

        // ------------------------------------------------------------------ library ↔ node types

        private static Dictionary<Type, Type> s_TaskNodeTypes;
        private static Dictionary<Type, Type> s_ConditionNodeTypes;

        /// <summary>The block class that authors <paramref name="taskType"/>, or null when the
        /// library type has no wrapper. Resolved from the wrappers' own
        /// <see cref="StateTaskBlockNode.taskType"/> rather than from their names, so it agrees with
        /// the bake even for a class that ignores the naming convention.</summary>
        private static Type FindTaskNodeType(Type taskType)
        {
            if (s_TaskNodeTypes == null)
            {
                s_TaskNodeTypes = new Dictionary<Type, Type>();
                foreach (Type candidate in TypeCache.GetTypesDerivedFrom<StateTaskBlockNode>())
                {
                    if (candidate.IsAbstract || candidate.IsGenericTypeDefinition)
                        continue;
                    var instance = TryCreate(candidate) as StateTaskBlockNode;
                    if (instance == null)
                        continue;

                    Type runtime = TryReadLibraryType(() => instance.taskType);
                    if (runtime != null && !s_TaskNodeTypes.ContainsKey(runtime))
                        s_TaskNodeTypes[runtime] = candidate;
                }
            }

            return taskType != null && s_TaskNodeTypes.TryGetValue(taskType, out Type node) ? node : null;
        }

        /// <summary>The node class that authors <paramref name="conditionType"/>, or null.</summary>
        private static Type FindConditionNodeType(Type conditionType)
        {
            if (s_ConditionNodeTypes == null)
            {
                s_ConditionNodeTypes = new Dictionary<Type, Type>();
                foreach (Type candidate in TypeCache.GetTypesDerivedFrom<StateTreeConditionNode>())
                {
                    if (candidate.IsAbstract || candidate.IsGenericTypeDefinition)
                        continue;
                    var instance = TryCreate(candidate) as StateTreeConditionNode;
                    if (instance == null)
                        continue;

                    Type runtime = TryReadLibraryType(() => instance.conditionType);
                    if (runtime != null && !s_ConditionNodeTypes.ContainsKey(runtime))
                        s_ConditionNodeTypes[runtime] = candidate;
                }
            }

            return conditionType != null && s_ConditionNodeTypes.TryGetValue(conditionType, out Type node)
                ? node
                : null;
        }

        private static object TryCreate(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (Exception)
            {
                // A wrapper that cannot be instantiated off-graph cannot be authored either; the
                // task using it is reported as unconvertible further up.
                return null;
            }
        }

        private static Type TryReadLibraryType(Func<Type> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ assets

        /// <summary>Create the graph file and hand back the graph AND the path it really landed on.
        /// <c>GraphDatabase.CreateGraph</c> resolves the path itself and then loads the graph from
        /// the resolved one (module IL 330710-330773), so asking the graph where it lives is the
        /// only honest answer.</summary>
        private static StateTreeGraph CreateGraphAsset(string assetPath, bool allowOverwrite,
            out string realPath, out string error)
        {
            realPath = assetPath;
            error = null;

            if (!allowOverwrite && AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                error = $"{assetPath} already exists.";
                return null;
            }

            EnsureFolder(DirectoryOf(assetPath));

            StateTreeGraph graph;
            try
            {
                graph = GraphDatabase.CreateGraph<StateTreeGraph>(assetPath);
            }
            catch (Exception exception)
            {
                error = $"GraphDatabase.CreateGraph({assetPath}) failed: {Describe(exception)}";
                return null;
            }

            if (graph == null)
            {
                error = $"GraphDatabase.CreateGraph({assetPath}) returned nothing; the file could not be written "
                    + "(check the folder exists and the name has no invalid characters).";
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
                // GetGraphAssetPath reaches into the graph implementation; a graph with no backing
                // file answers with an exception. The requested path is then the best guess.
            }

            return graph;
        }

        private static void SaveAndImport(StateTreeGraph graph, string assetPath)
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

        /// <summary>The baked tree inside a graph file. It is the file's MAIN asset (see
        /// <see cref="StateTreeGraphImporter"/>), but the sub-object scan is kept as a fallback so a
        /// change of importer strategy degrades to "slower" rather than to "not found".</summary>
        private static StateTreeAsset FindTreeAt(string assetPath)
        {
            var main = AssetDatabase.LoadAssetAtPath<StateTreeAsset>(assetPath);
            if (main != null)
                return main;

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                return null;

            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is StateTreeAsset tree)
                    return tree;
            }
            return null;
        }

        private static string NormalizeGraphPath(string assetPath)
        {
            string suffix = "." + StateTreeGraph.Extension;
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
        /// directory on disk itself, but a folder the AssetDatabase has never seen has no .meta and
        /// no import record, which is a different (and worse) problem.</summary>
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

        // ------------------------------------------------------------------ ids and messages

        /// <summary>
        /// The id the BAKE will give this state — mirror of <c>StateTreeGraphBaker.MakeStateId</c>
        /// (its <c>Slug</c> + duplicate suffixing are private). Writing the baked form into the graph
        /// is what makes the loop closed: what the canvas shows is what the runner indexes.
        ///
        /// The title fallback that the baker has after the id is deliberately not mirrored — this
        /// converter always writes a non-empty id, so the baker never reaches it.
        /// </summary>
        private static string MakeStateId(string authoredId, int index, HashSet<string> used)
        {
            string id = Slug(authoredId);
            if (id.Length == 0)
                id = "state_" + (index + 1).ToString(CultureInfo.InvariantCulture);

            string candidate = id;
            int suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = id + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }
            return candidate;
        }

        /// <summary>Verbatim copy of the baker's private <c>Slug</c>: lower-case words joined by
        /// '_'. If the two ever drift the round-trip check reports it as a state id divergence,
        /// which is exactly the failure mode that check exists for.</summary>
        private static string Slug(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var builder = new StringBuilder(raw.Length);
            bool lastWasSeparator = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    lastWasSeparator = false;
                }
                else if (builder.Length > 0 && !lastWasSeparator)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            string slug = builder.ToString();
            return slug.EndsWith("_", StringComparison.Ordinal) ? slug.Substring(0, slug.Length - 1) : slug;
        }

        private static string BuildMessage(string header, List<string> lines)
        {
            var builder = new StringBuilder(k_Tag);
            builder.Append(header);
            for (int i = 0; i < lines.Count; i++)
                builder.Append("\n  • ").Append(lines[i]);
            return builder.ToString();
        }

        private static string Describe(object value)
        {
            if (value == null)
                return "<none>";
            if (value is float f)
                return f.ToString("0.######", CultureInfo.InvariantCulture);
            if (value is double d)
                return d.ToString("0.######", CultureInfo.InvariantCulture);
            if (value is UnityEngine.Object unityObject)
                return unityObject == null ? "<none>" : $"'{unityObject.name}'";
            if (value is string text)
                return $"'{text}'";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string DescribeType(object value)
            => value == null ? "<none>" : value.GetType().Name;

        /// <summary>Unwrap the TargetInvocationException reflection wraps everything in — the inner
        /// message is the one worth reading.</summary>
        private static string Describe(Exception exception)
        {
            Exception inner = exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException
                : exception;
            return $"{inner.GetType().Name}: {inner.Message}";
        }
    }
}
