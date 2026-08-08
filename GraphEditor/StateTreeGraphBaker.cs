using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Walks an authored state-tree Graph and produces the flat runtime <see cref="StateTreeAsset"/>
    /// the <see cref="StateTreeRunner"/> executes — the BAKE step that keeps draw-tool-port-brief.md
    /// §7.3's hard boundary intact: the runtime assembly never sees a graph type, and this editor
    /// assembly never leaks into a build.
    ///
    /// WHY A BAKE AND NOT A RUNTIME GRAPH READER. Graph Toolkit is an editor-only authoring API
    /// (namespace <c>Unity.GraphToolkit.Editor</c>, editor module). Everything it knows how to do —
    /// port connections, node options, blocks — is edit-time data. The runner already has a complete,
    /// tested data model (nodes + tasks + conditions as sub-assets, M6), so the graph's only job is
    /// to produce that model. Both Graph Toolkit samples ship their runtime data exactly this way:
    /// <c>Samples~/VisualNovelDirector/Editor/AssetImport/VisualNovelDirectorImporter.cs</c> and
    /// <c>Samples~/TextureMaker/Editor/AssetImport/TextureMakerImporter.cs</c> load the graph with
    /// <see cref="GraphDatabase.LoadGraphForImporter{T}"/>, translate it to a plain runtime object,
    /// and <c>ctx.AddObjectToAsset</c> + <c>ctx.SetMainObject</c> the result. See
    /// <see cref="StateTreeGraphImporter"/> for our copy of that pattern.
    ///
    /// STRUCTURE THIS BAKER ACCEPTS. The graph is read STRUCTURALLY, not by node class name, so the
    /// palette (owned by the node/block/condition classes beside this file) can evolve without
    /// touching the baker:
    /// <list type="bullet">
    /// <item>A STATE is any <see cref="ContextNode"/>. Its <c>nodeId</c> / <c>displayName</c> values
    /// name it; its blocks are its tasks, in block order.</item>
    /// <item>A TASK is a block whose node type resolves to a <see cref="StateTreeTaskAsset"/>
    /// subclass; a CONDITION is a node that resolves to a <see cref="StateTreeConditionAsset"/>
    /// subclass. Resolution is by explicit <c>taskType</c>/<c>conditionType</c> declaration if the
    /// node exposes one, else by name (<c>ChaseTargetTaskNode</c> → <see cref="ChaseTargetTask"/>).</item>
    /// <item>A TRANSITION is whatever sits on the wire between two states: nothing (unconditional),
    /// a condition node, or a dedicated transition node carrying a <c>checkWhileRunning</c> value
    /// with a condition wired into one of its inputs. All three shapes bake identically.</item>
    /// <item>The ENTRY is the state fed by an entry/start node, else the only state nothing points
    /// at, else the first state in creation order.</item>
    /// </list>
    ///
    /// AUTHORED VALUE ↔ FIELD IS 1:1 BY NAME (the M7 convention). A task block exposes one authored
    /// value per serialized field of its library task, with the SAME name —
    /// <see cref="ChaseTargetTask"/>'s <c>moveSpeed</c>/<c>stopRange</c> are named
    /// <c>moveSpeed</c>/<c>stopRange</c>. This baker therefore never hard-codes a field list: it
    /// reflects over the library type's public fields and pulls each one from the node option, input
    /// port or plain node member of that name. Adding a task to the library and a block for it is
    /// all it takes; the baker needs no edit.
    ///
    /// WHY ALL THREE SOURCES. The plan said "node options", but the palette declares parameters as
    /// input PORTS: <see cref="INodeOption"/> in Graph Toolkit 0.5 has <c>TryGetValue</c> and no
    /// setter, so an option cannot be written programmatically and the M7 verifier could not build a
    /// zombie graph in code. Ports also match the canonical sample, where every parameter of
    /// <c>SetDialogueNode</c> is an input port its importer reads back. Reading option-then-port
    /// -then-member means either authoring style bakes correctly, including a port driven by a
    /// constant or variable node.
    ///
    /// DETERMINISM. Ids, sub-asset names and sub-asset identifiers are derived from authored node
    /// ids and from <see cref="Graph.GetNodes"/> order (documented as creation order), never from
    /// hash codes or dictionary iteration, so two bakes of an unchanged graph produce byte-identical
    /// output and the importer's artifacts stay stable.
    /// </summary>
    public static class StateTreeGraphBaker
    {
        /// <summary>File extension of a state-tree graph asset, WITHOUT the dot. Aliased from
        /// <see cref="StateTreeGraph.Extension"/> — the value the graph class passes to
        /// <c>[Graph(...)]</c> — because Unity picks the importer by extension and the two MUST be
        /// the same string. Everything else here reads the graph structurally, never by type.</summary>
        public const string GraphExtension = StateTreeGraph.Extension;

        /// <summary>Suffix of the standalone export written by
        /// <see cref="BakeSelectedGraphToAssetFile"/>. The importer path does not use it — there the
        /// baked tree IS the graph file's main asset.</summary>
        public const string BakedAssetSuffix = "_Baked";

        /// <summary>Name of the state's id value (option or input port). Empty/missing falls back to
        /// the node title, then to a positional id.</summary>
        public const string NodeIdOption = "nodeId";

        /// <summary>Name of the human label copied to
        /// <see cref="StateTreeNodeAsset.displayName"/>.</summary>
        public const string DisplayNameOption = "displayName";

        /// <summary>Name of the transition's interrupt flag — the runner's
        /// <see cref="StateTreeTransition.checkWhileRunning"/>.</summary>
        public const string CheckWhileRunningOption = "checkWhileRunning";

        /// <summary>Optional int ordering the transitions leaving one state. The runner takes the
        /// FIRST matching transition, so order is behaviour; without it the order is the output port
        /// order (top to bottom), which is what an author reads.</summary>
        public const string OrderOption = "order";

        /// <summary>Optional entry-node values overriding <see cref="StateTreeAsset.treeName"/> /
        /// <see cref="StateTreeAsset.treeKind"/>.</summary>
        public const string TreeNameOption = "treeName";

        /// <summary>See <see cref="TreeNameOption"/>.</summary>
        public const string TreeKindOption = "treeKind";

        /// <summary>The entry marker's kind map for the graph's declared keys — see
        /// <see cref="EntryNode.KeyKindsPortName"/> and <see cref="KeyVariables"/>.</summary>
        public const string KeyKindsOption = "keyKinds";

        /// <summary>Node id of the organizational root. Mirrors
        /// <c>StateTreePresets.TreeBuilder</c>: no tasks and no transitions, so the runner's
        /// ResolveEntryNode descends past it into <c>children[0]</c>.</summary>
        public const string RootNodeId = "root";

        /// <summary>Guard on condition/transition chains while following one wire. A hand-authored
        /// loop of transition nodes must not hang the importer; 8 is far past any legitimate chain
        /// (state → transition → condition → state is 3).</summary>
        private const int k_EdgeFollowDepth = 8;

        // ------------------------------------------------------------------ logging

        /// <summary>Where bake diagnostics go. Two implementations exist because the same walk runs
        /// in two places with two reporting mechanisms: inside <c>Graph.OnGraphChanged</c>, where
        /// Graph Toolkit hands us a <see cref="GraphLogger"/> that puts a marker ON the offending
        /// node, and inside the importer, where no logger exists and the console with the asset as
        /// context is all there is.</summary>
        public interface IBakeLog
        {
            void Error(string message, object nodeContext);
            void Warning(string message, object nodeContext);
        }

        /// <summary>Adapter putting bake diagnostics on the graph's own nodes. Use from
        /// <c>StateTreeGraph.OnGraphChanged(GraphLogger)</c> via <see cref="Validate"/>.</summary>
        private sealed class GraphLoggerBakeLog : IBakeLog
        {
            private readonly GraphLogger m_Logger;
            private readonly object m_GraphContext;

            public GraphLoggerBakeLog(GraphLogger logger, object graphContext)
            {
                m_Logger = logger;
                m_GraphContext = graphContext;
            }

            public void Error(string message, object nodeContext)
                => m_Logger.LogError(message, nodeContext ?? m_GraphContext);

            public void Warning(string message, object nodeContext)
                => m_Logger.LogWarning(message, nodeContext ?? m_GraphContext);
        }

        /// <summary>Console adapter for the importer and the menu. Graph Toolkit's node markers are
        /// unavailable outside <c>OnGraphChanged</c> (the <see cref="GraphLogger"/> instance is
        /// created and owned by the framework), so the node is named in the message text and the
        /// ASSET is the Unity context object, which makes the console line click-to-ping.</summary>
        private sealed class ConsoleBakeLog : IBakeLog
        {
            private readonly string m_AssetPath;
            private readonly UnityEngine.Object m_Context;

            public ConsoleBakeLog(string assetPath, UnityEngine.Object context)
            {
                m_AssetPath = assetPath;
                m_Context = context;
            }

            public void Error(string message, object nodeContext)
                => Debug.LogError(Format(message, nodeContext), m_Context);

            public void Warning(string message, object nodeContext)
                => Debug.LogWarning(Format(message, nodeContext), m_Context);

            private string Format(string message, object nodeContext)
            {
                string node = DescribeNode(nodeContext);
                return node.Length == 0
                    ? $"State tree bake ({m_AssetPath}): {message}"
                    : $"State tree bake ({m_AssetPath}) [{node}]: {message}";
            }
        }

        // ------------------------------------------------------------------ result

        /// <summary>One baked tree: the root asset plus every sub-asset that must be persisted with
        /// it. The baker never touches the AssetDatabase itself — it cannot, because it also runs
        /// inside a <see cref="UnityEditor.AssetImporters.ScriptedImporter"/> where AssetDatabase
        /// calls are illegal — so the caller decides between <c>ctx.AddObjectToAsset</c> (importer)
        /// and <c>AssetDatabase.AddObjectToAsset</c> (menu export).</summary>
        public sealed class BakeResult
        {
            /// <summary>The baked tree, or null when the graph had no usable state.</summary>
            public StateTreeAsset tree;

            /// <summary>Every node/task/condition instance belonging to <see cref="tree"/>, in a
            /// deterministic order, each with the stable identifier the importer needs.</summary>
            public readonly List<SubAsset> subAssets = new List<SubAsset>();

            public int errorCount;
            public int warningCount;

            public bool succeeded => tree != null && errorCount == 0;

            /// <summary>Destroy every object this bake created. Validation-only bakes MUST call
            /// this: <see cref="ScriptableObject.CreateInstance(Type)"/> objects live until domain
            /// reload otherwise, and OnGraphChanged fires on every keystroke.</summary>
            public void DestroyObjects()
            {
                for (int i = 0; i < subAssets.Count; i++)
                    DestroyObject(subAssets[i].asset);
                subAssets.Clear();
                DestroyObject(tree);
                tree = null;
            }

            private static void DestroyObject(UnityEngine.Object target)
            {
                if (target == null)
                    return;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(target);
                else
                    UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>A sub-asset and the identifier it is registered under. <see cref="identifier"/>
        /// is derived from node ids rather than from indices so that inserting a state does not
        /// renumber (and therefore re-key) every other sub-asset in the imported file.</summary>
        public readonly struct SubAsset
        {
            public readonly ScriptableObject asset;
            public readonly string identifier;

            public SubAsset(ScriptableObject asset, string identifier)
            {
                this.asset = asset;
                this.identifier = identifier;
            }
        }

        // ------------------------------------------------------------------ public entry points

        /// <summary>
        /// Walk <paramref name="graph"/> and build the runtime tree in memory. Never touches the
        /// AssetDatabase, never logs by itself — every diagnostic goes through
        /// <paramref name="log"/> so the caller controls whether it lands on a node marker or in the
        /// console.
        /// </summary>
        /// <param name="graph">The authored graph. Any <see cref="Graph"/> subclass is accepted; the
        /// baker reads structure, not the concrete graph type.</param>
        /// <param name="log">Diagnostics sink. May be null (diagnostics are then dropped).</param>
        public static BakeResult Bake(Graph graph, IBakeLog log)
        {
            var result = new BakeResult();
            if (graph == null)
            {
                log?.Error("No graph to bake.", null);
                result.errorCount++;
                return result;
            }

            List<StateBuild> states = CollectStates(graph, log, result);
            if (states.Count == 0)
            {
                log?.Error("The graph has no state nodes, so there is nothing to run. Add a state and "
                    + "put at least one task block in it.", null);
                result.errorCount++;
                return result;
            }

            // Keyed by node IDENTITY, not by id string: a state whose id was auto-generated or
            // de-duplicated must still be a valid transition target, and following a wire already
            // hands us the node itself.
            var byNode = new Dictionary<INode, StateBuild>(states.Count, NodeIdentity.comparer);
            for (int i = 0; i < states.Count; i++)
                byNode[states[i].node] = states[i];

            for (int i = 0; i < states.Count; i++)
                BuildTasks(states[i], log, result);

            for (int i = 0; i < states.Count; i++)
                BuildTransitions(states[i], byNode, log, result);

            StateBuild entry = ResolveEntry(graph, states, log);

            // The tree's settings live on the ENTRY MARKER's ports. ResolveEntry returns
            // the entry STATE (what the runner enters); the marker itself is found here —
            // reading these off the state instead was the old dead-letter path that made
            // every converted graph fall back to file-name/default-kind.
            EntryNode marker = null;
            foreach (INode graphNode in graph.GetNodes())
            {
                if (graphNode is EntryNode entryMarker)
                {
                    marker = entryMarker;
                    break;
                }
            }

            var tree = ScriptableObject.CreateInstance<StateTreeAsset>();
            string markerName = marker != null
                ? ReadStringValue(marker, TreeNameOption) : string.Empty;
            tree.treeName = !string.IsNullOrEmpty(markerName)
                ? markerName
                : ReadTreeName(entry, graph);
            string markerKind = marker != null
                ? ReadStringValue(marker, TreeKindOption) : string.Empty;
            tree.treeKind = !string.IsNullOrEmpty(markerKind)
                ? markerKind
                : ReadTreeKind(entry);
            if (marker != null
                && TryReadAuthoredValue(marker, EntryNode.RegistryPortName,
                    typeof(StateTreeRegistryAsset), out object registryValue)
                && registryValue is StateTreeRegistryAsset registry && registry != null)
                tree.registries.Add(registry);
            BakeKeyDeclarations(graph, marker, tree, log, result);
            tree.name = tree.treeName;
            result.tree = tree;

            // Root is organizational (no tasks, no transitions) exactly like the preset builder's,
            // and the entry state is children[0] because that is what ResolveEntryNode descends to.
            var root = ScriptableObject.CreateInstance<StateTreeNodeAsset>();
            root.nodeId = RootNodeId;
            root.displayName = tree.treeName;
            root.name = "Node 0 root";
            tree.root = root;
            result.subAssets.Add(new SubAsset(root, "Node:" + RootNodeId));

            root.children.Add(entry.asset);
            for (int i = 0; i < states.Count; i++)
                if (!ReferenceEquals(states[i], entry))
                    root.children.Add(states[i].asset);

            // Sub-assets are emitted in the order the Project window will show them: the entry state
            // first, then the rest in graph order, each followed by its own tasks and conditions.
            AppendStateSubAssets(entry, result);
            for (int i = 0; i < states.Count; i++)
                if (!ReferenceEquals(states[i], entry))
                    AppendStateSubAssets(states[i], result);

            return result;
        }

        /// <summary>
        /// The graph's variables → the baked tree's KEY HEADER (M12, canvas flavor): every
        /// string variable is one <see cref="StateTreeKeyDeclaration"/>, named by the variable,
        /// id'd by the variable's stable identity (so a rename re-bakes to the SAME id and every
        /// wire survives it), kinded by the marker's Key Kinds port with use-derivation filling
        /// the gaps. Non-string variables are refused with a warning: a tree's canvas variables
        /// are its declared keys, and a key is a name.
        /// </summary>
        private static void BakeKeyDeclarations(Graph graph, EntryNode marker, StateTreeAsset tree,
            IBakeLog log, BakeResult result)
        {
            IEnumerable<IVariable> variables;
            try
            {
                variables = graph.GetVariables();
            }
            catch (Exception)
            {
                return;
            }
            if (variables == null)
                return;

            string kindsText = marker != null ? ReadStringValue(marker, KeyKindsOption) : null;
            var kindProblems = new List<string>();
            Dictionary<string, StateTreeKeyKind> kinds =
                KeyVariables.ParseKindOverrides(kindsText, kindProblems);
            for (int i = 0; i < kindProblems.Count; i++)
            {
                log?.Warning(kindProblems[i], marker);
                result.warningCount++;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (IVariable variable in variables)
            {
                if (variable == null)
                    continue;
                string name = KeyVariables.NameOf(variable);
                if (string.IsNullOrEmpty(name))
                    continue;

                Type type = KeyVariables.TypeOf(variable);
                if (type != typeof(string))
                {
                    log?.Warning($"Graph variable '{name}' is a "
                        + $"{(type != null ? type.Name : "unknown type")}. A state tree's canvas "
                        + "variables are its DECLARED KEYS, which are names — it was not "
                        + "declared. Blackboard values belong on the blackboard, through the "
                        + "context atoms.", marker);
                    result.warningCount++;
                    continue;
                }

                if (!seen.Add(name))
                {
                    log?.Warning($"Two graph variables are called '{name}'; a key is declared "
                        + "once. The second was skipped.", marker);
                    result.warningCount++;
                    continue;
                }

                StateTreeKeyKind kind;
                if (!kinds.TryGetValue(name, out kind))
                {
                    kind = KeyVariables.DeriveKind(variable, out string disagreement);
                    if (disagreement != null)
                    {
                        log?.Warning($"Declared key '{name}': {disagreement}. Pin it on the "
                            + "Entry node's Key Kinds port ('" + name + "=Event' and so on).",
                            marker);
                        result.warningCount++;
                    }
                }

                tree.keys.Add(new StateTreeKeyDeclaration
                {
                    id = TaskGraphBaker.ReadParameterId(variable, name),
                    name = name,
                    kind = kind,
                    description = string.Empty
                });
            }
        }

        /// <summary>
        /// Validation-only bake, for <c>StateTreeGraph.OnGraphChanged(GraphLogger)</c> to call in one
        /// line. Every problem the bake would hit is reported as a marker on the node that causes it,
        /// which is how a Graph Toolkit tool is expected to report (see
        /// <c>VisualNovelDirectorGraph.CheckGraphErrors</c>). Creates and destroys a throwaway tree.
        /// </summary>
        public static void Validate(Graph graph, GraphLogger logger)
        {
            if (graph == null || logger == null)
                return;
            BakeResult result = Bake(graph, new GraphLoggerBakeLog(logger, graph));
            result.DestroyObjects();
        }

        /// <summary>
        /// Bake <paramref name="graph"/> and WRITE it as a standalone asset, reporting to the
        /// console. Pass null for <paramref name="bakedAssetPath"/> to get
        /// <c>&lt;Graph&gt;_Baked.asset</c> beside the graph. Returns the persisted tree, or null
        /// when the graph did not bake cleanly.
        ///
        /// This is the signature the main editor assembly's <c>StateTreeGraphBridge</c> discovers by
        /// reflection ("any public static Bake whose first parameter accepts the graph, optionally
        /// taking the asset path, returning a StateTreeAsset"), which is how the M7 demo verifier
        /// drives graph → bake → runner without referencing this assembly.
        /// </summary>
        public static StateTreeAsset Bake(Graph graph, string bakedAssetPath)
        {
            if (graph == null)
            {
                Debug.LogError("State tree bake: no graph to bake.");
                return null;
            }

            string graphPath = SafeGraphAssetPath(graph);
            if (string.IsNullOrEmpty(bakedAssetPath))
            {
                if (string.IsNullOrEmpty(graphPath))
                {
                    Debug.LogError("State tree bake: the graph has no asset path, so there is nowhere "
                        + "to write the baked tree. Pass an explicit path.");
                    return null;
                }
                bakedAssetPath = GetBakedAssetPath(graphPath);
            }

            var log = new ConsoleBakeLog(string.IsNullOrEmpty(graphPath) ? bakedAssetPath : graphPath, null);
            BakeResult result = Bake(graph, log);
            if (!result.succeeded)
            {
                Debug.LogError($"State tree bake ({bakedAssetPath}): {result.errorCount} error(s); "
                    + "no asset written.");
                result.DestroyObjects();
                return null;
            }

            WriteAssetFile(result, bakedAssetPath);
            return result.tree;
        }

        /// <summary>
        /// Bake the selected graph asset to a STANDALONE <c>&lt;Graph&gt;_Baked.asset</c> next to it,
        /// using the sub-asset pattern from <c>Editor/StateTreePresets.cs</c> verbatim.
        ///
        /// The importer already keeps a baked tree inside every graph file, so this menu exists for
        /// the cases where a separate, hand-editable file is wanted: diffing a bake, tweaking a
        /// generated tree by hand, or holding a tree that must survive the graph being deleted.
        /// </summary>
        [MenuItem("Tools/Draw To Play/Bake State Tree Graph")]
        public static void BakeSelectedGraphToAssetFile()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            Graph graph = LoadGraphAtPath(path);
            if (graph == null)
            {
                Debug.LogError($"Select a .{GraphExtension} graph asset first (selected: "
                    + $"'{(string.IsNullOrEmpty(path) ? "nothing" : path)}').");
                return;
            }

            StateTreeAsset tree = Bake(graph, GetBakedAssetPath(path));
            if (tree == null)
                return;

            Selection.activeObject = tree;
            EditorGUIUtility.PingObject(tree);
        }

        /// <summary>Persist a baked tree the way <c>StateTreePresets.TreeBuilder</c> does: the main
        /// asset first (CreateAsset replaces whatever was at the path), then every node, task and
        /// condition as a named sub-asset of it.</summary>
        private static void WriteAssetFile(BakeResult result, string bakedAssetPath)
        {
            AssetDatabase.CreateAsset(result.tree, bakedAssetPath);
            for (int i = 0; i < result.subAssets.Count; i++)
                AssetDatabase.AddObjectToAsset(result.subAssets[i].asset, result.tree);
            EditorUtility.SetDirty(result.tree);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The graph's own asset path. <see cref="GraphDatabase.GetGraphAssetPath"/> is the
        /// documented way — its remarks say NOT to use <c>AssetDatabase.GetAssetPath</c> on graph
        /// objects — but it reaches into the graph implementation, so a graph that is not attached to
        /// a file answers with an exception rather than an empty string.</summary>
        private static string SafeGraphAssetPath(Graph graph)
        {
            try
            {
                return GraphDatabase.GetGraphAssetPath(graph) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        [MenuItem("Tools/Draw To Play/Bake State Tree Graph", true)]
        private static bool ValidateBakeSelectedGraph()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && path.EndsWith("." + GraphExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Console-reporting log for callers outside this file (the importer, the demo
        /// verifier).</summary>
        public static IBakeLog CreateConsoleLog(string assetPath, UnityEngine.Object context)
            => new ConsoleBakeLog(assetPath, context);

        /// <summary><c>Assets/…/Zombie.statetree</c> → <c>Assets/…/Zombie_Baked.asset</c>.</summary>
        public static string GetBakedAssetPath(string graphAssetPath)
        {
            string directory = System.IO.Path.GetDirectoryName(graphAssetPath) ?? string.Empty;
            string file = System.IO.Path.GetFileNameWithoutExtension(graphAssetPath) + BakedAssetSuffix + ".asset";
            return string.IsNullOrEmpty(directory) ? file : directory.Replace('\\', '/') + "/" + file;
        }

        /// <summary>Load a state-tree graph by asset path without naming the concrete graph class:
        /// <see cref="GraphDatabase.LoadGraph{T}"/>'s constraint is satisfied by <see cref="Graph"/>
        /// itself, and the extension registry resolves the real type. Returns null (never throws)
        /// when the path is not a registered graph.</summary>
        public static Graph LoadGraphAtPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.EndsWith("." + GraphExtension, StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                return GraphDatabase.LoadGraph<Graph>(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load state tree graph '{assetPath}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The id → state node map the bake produces, without baking. The play-mode highlight uses it
        /// to turn a runner's <see cref="StateTreeRunner.activeNodeId"/> back into the graph node the
        /// author is looking at, and it is the SAME code path the bake uses, so the two can never
        /// disagree about which node is "chase".
        /// </summary>
        public static Dictionary<string, INode> BuildStateIdMap(Graph graph)
        {
            var map = new Dictionary<string, INode>(StringComparer.Ordinal);
            if (graph == null)
                return map;
            var used = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (INode node in graph.GetNodes())
            {
                if (!(node is ContextNode))
                    continue;
                map[MakeStateId(node, index, used)] = node;
                index++;
            }
            return map;
        }

        // ------------------------------------------------------------------ states

        /// <summary>Per-state scratch: the graph node, the runtime node it became, and the id both
        /// agree on.</summary>
        private sealed class StateBuild
        {
            public INode node;
            public ContextNode context;
            public string id;
            public int order;
            public StateTreeNodeAsset asset;
            public readonly List<SubAsset> ownedSubAssets = new List<SubAsset>();
        }

        private static List<StateBuild> CollectStates(Graph graph, IBakeLog log, BakeResult result)
        {
            var states = new List<StateBuild>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;

            foreach (INode node in graph.GetNodes())
            {
                if (!(node is ContextNode context))
                    continue;

                string id = MakeStateId(node, index, used);
                if (!IsAuthoredId(node, id))
                {
                    log?.Warning($"State has no unique '{NodeIdOption}', so the bake named it '{id}'. "
                        + "Transitions still work, but naming the state makes the baked asset readable.",
                        node);
                    result.warningCount++;
                }

                var state = new StateBuild
                {
                    node = node,
                    context = context,
                    id = id,
                    order = index + 1,
                    asset = ScriptableObject.CreateInstance<StateTreeNodeAsset>()
                };
                state.asset.nodeId = id;
                state.asset.displayName = ReadDisplayName(node, id);
                state.asset.name = $"Node {state.order} {id}";
                states.Add(state);
                index++;
            }

            return states;
        }

        /// <summary>Id resolution, in the order the author would expect: the explicit option, then
        /// the node title, then a positional fallback. Collisions get a numeric suffix rather than
        /// silently overwriting, because two states sharing an id would make every transition to it
        /// ambiguous.</summary>
        private static string MakeStateId(INode node, int index, HashSet<string> used)
        {
            string id = Slug(ReadStringValue(node, NodeIdOption));
            if (id.Length == 0)
                id = Slug(SafeTitle(node));
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

        private static bool IsAuthoredId(INode node, string finalId)
        {
            string authored = Slug(ReadStringValue(node, NodeIdOption));
            return authored.Length > 0 && authored == finalId;
        }

        private static string ReadDisplayName(INode node, string fallback)
        {
            string name = ReadStringValue(node, DisplayNameOption);
            if (!string.IsNullOrEmpty(name))
                return name;
            string title = SafeTitle(node);
            return string.IsNullOrEmpty(title) ? fallback : title;
        }

        private static void AppendStateSubAssets(StateBuild state, BakeResult result)
        {
            result.subAssets.Add(new SubAsset(state.asset, "Node:" + state.id));
            for (int i = 0; i < state.ownedSubAssets.Count; i++)
                result.subAssets.Add(state.ownedSubAssets[i]);
        }

        // ------------------------------------------------------------------ tasks

        private static void BuildTasks(StateBuild state, IBakeLog log, BakeResult result)
        {
            int blockIndex = 0;
            foreach (BlockNode block in state.context.BlockNodes)
            {
                if (block == null)
                {
                    blockIndex++;
                    continue;
                }

                Type taskType = ResolveLibraryType(block, typeof(StateTreeTaskAsset));
                if (taskType == null)
                {
                    log?.Error($"Block '{SafeTitle(block)}' does not map to any task in "
                        + "Runtime/StateTree/Library. Name the block class after its task "
                        + "(ChaseTargetTaskNode → ChaseTargetTask) or expose a 'taskType' property.",
                        block);
                    result.errorCount++;
                    blockIndex++;
                    continue;
                }

                var task = (StateTreeTaskAsset)ScriptableObject.CreateInstance(taskType);
                task.name = $"Task {state.id} {LabelFor(block, taskType, "Task")}";
                ApplyAuthoredValues(block, task, log, result);
                state.asset.tasks.Add(task);
                state.ownedSubAssets.Add(new SubAsset(task, $"Task:{state.id}:{blockIndex}"));
                blockIndex++;
            }

            if (state.asset.tasks.Count == 0)
            {
                // Legal but almost never intended: a task-less state takes its completion transition
                // on the tick it is entered, so it either loops instantly or does nothing at all.
                log?.Warning($"State '{state.id}' has no task blocks: the runner will evaluate its "
                    + "completion transitions immediately on entry.", state.node);
                result.warningCount++;
            }
        }

        // ------------------------------------------------------------------ transitions

        private static void BuildTransitions(StateBuild state, Dictionary<INode, StateBuild> byNode,
            IBakeLog log, BakeResult result)
        {
            var candidates = new List<TransitionBuild>();
            var buffer = new List<IPort>();
            int portIndex = 0;

            foreach (IPort port in state.node.GetOutputPorts())
            {
                if (port != null && port.IsConnected)
                {
                    buffer.Clear();
                    port.GetConnectedPorts(buffer);
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        IPort other = buffer[i];
                        INode next = other?.GetNode();
                        if (next == null)
                            continue;
                        FollowEdge(state, next, null, false, portIndex, i, 0, candidates, log, result);
                    }
                }
                portIndex++;
            }

            // The runner takes the FIRST transition whose condition passes, so this order is
            // behaviour. Port order (what the author reads top-to-bottom) is the default; an
            // explicit 'order' option overrides it. List.Sort is not stable, so the port/wire index
            // is folded into the comparison as the tie-break.
            candidates.Sort(CompareTransitions);

            for (int i = 0; i < candidates.Count; i++)
            {
                TransitionBuild candidate = candidates[i];
                if (candidate.targetNode == null || !byNode.TryGetValue(candidate.targetNode, out StateBuild target))
                {
                    log?.Error($"A transition out of '{state.id}' ends on "
                        + $"'{DescribeNode(candidate.targetNode)}', which is not a state in this graph.",
                        candidate.sourceNode ?? state.node);
                    result.errorCount++;
                    continue;
                }

                StateTreeConditionAsset condition = null;
                Type conditionType = candidate.conditionNode != null
                    ? ResolveLibraryType(candidate.conditionNode, typeof(StateTreeConditionAsset))
                    : null;
                if (candidate.conditionNode != null && conditionType == null)
                {
                    log?.Error($"Condition '{SafeTitle(candidate.conditionNode)}' on the transition "
                        + $"'{state.id}' → '{target.id}' does not map to any condition in "
                        + "Runtime/StateTree/Library, so the transition would fire unconditionally.",
                        candidate.conditionNode);
                    result.errorCount++;
                    continue;
                }
                if (conditionType != null)
                {
                    condition = (StateTreeConditionAsset)ScriptableObject.CreateInstance(conditionType);
                    condition.name = $"Cond {state.id}->{target.id} "
                        + LabelFor(candidate.conditionNode, conditionType, "Condition");
                    ApplyAuthoredValues(candidate.conditionNode, condition, log, result);
                    state.ownedSubAssets.Add(new SubAsset(condition,
                        $"Cond:{state.id}->{target.id}:{i}"));
                }

                state.asset.transitions.Add(new StateTreeTransition
                {
                    targetNodeId = target.id,
                    condition = condition,
                    checkWhileRunning = candidate.checkWhileRunning
                });
            }
        }

        /// <summary>One resolved wire out of a state, before it becomes a
        /// <see cref="StateTreeTransition"/>.</summary>
        private struct TransitionBuild
        {
            public INode targetNode;
            public INode conditionNode;
            public INode sourceNode;
            public bool checkWhileRunning;
            public int order;
            public bool hasOrder;
            public int portIndex;
            public int wireIndex;
        }

        private static int CompareTransitions(TransitionBuild a, TransitionBuild b)
        {
            if (a.hasOrder != b.hasOrder)
                return a.hasOrder ? -1 : 1;
            if (a.hasOrder && a.order != b.order)
                return a.order.CompareTo(b.order);
            if (a.portIndex != b.portIndex)
                return a.portIndex.CompareTo(b.portIndex);
            return a.wireIndex.CompareTo(b.wireIndex);
        }

        /// <summary>
        /// Follow one wire leaving <paramref name="state"/> until it lands on another state,
        /// collecting whatever it passed through on the way. Three shapes all end up here and all
        /// bake to the same thing:
        /// <list type="bullet">
        /// <item>state → state: unconditional completion transition.</item>
        /// <item>state → condition → state: conditional transition (interrupt if the condition node
        /// carries a <c>checkWhileRunning</c> option set true).</item>
        /// <item>state → transition node → state, with a condition wired into the transition node:
        /// the transition node owns <c>checkWhileRunning</c> and <c>order</c>.</item>
        /// </list>
        /// </summary>
        private static void FollowEdge(StateBuild state, INode node, INode conditionNode,
            bool checkWhileRunning, int portIndex, int wireIndex, int depth,
            List<TransitionBuild> candidates, IBakeLog log, BakeResult result)
        {
            if (node == null || depth > k_EdgeFollowDepth)
            {
                log?.Error($"Transition chain out of state '{state.id}' is too deep or loops back on "
                    + "itself without reaching a state.", state.node);
                result.errorCount++;
                return;
            }

            // Landed on a state: emit. Priority is NOT read here — it belongs to the transition or
            // condition node that produced this wire, and is stamped on below by whichever node
            // declared it.
            if (node is ContextNode)
            {
                candidates.Add(new TransitionBuild
                {
                    targetNode = node,
                    conditionNode = conditionNode,
                    sourceNode = conditionNode,
                    checkWhileRunning = checkWhileRunning,
                    portIndex = portIndex,
                    wireIndex = wireIndex
                });
                return;
            }

            bool isCondition = ResolveLibraryType(node, typeof(StateTreeConditionAsset)) != null;
            if (isCondition)
            {
                if (conditionNode != null)
                {
                    log?.Error($"Two conditions are chained on one transition out of '{state.id}'. "
                        + "The runner evaluates a single condition per transition; use one condition "
                        + "node per transition.", node);
                    result.errorCount++;
                    return;
                }
                conditionNode = node;
            }

            // A transition node (or the condition node itself) may carry the interrupt flag and the
            // explicit priority.
            bool nodeInterrupt = ReadBoolValue(node, CheckWhileRunningOption, out bool hasInterrupt);
            if (hasInterrupt)
                checkWhileRunning = nodeInterrupt;
            int nodeOrder = ReadIntValue(node, OrderOption, out bool nodeHasOrder);

            // A transition node takes its condition from an INPUT port. Ignore the input the wire
            // arrived on: that one points back at the source state.
            if (!isCondition)
            {
                foreach (IPort input in node.GetInputPorts())
                {
                    if (input == null || !input.IsConnected)
                        continue;
                    INode upstream = input.FirstConnectedPort?.GetNode();
                    if (upstream == null || upstream is ContextNode)
                        continue;
                    if (ResolveLibraryType(upstream, typeof(StateTreeConditionAsset)) == null)
                        continue;
                    if (conditionNode != null && !ReferenceEquals(conditionNode, upstream))
                    {
                        log?.Warning($"Transition out of '{state.id}' has more than one condition "
                            + "wired in; only the first is baked.", node);
                        result.warningCount++;
                        continue;
                    }
                    conditionNode = upstream;
                }
            }

            // Index of the first candidate this node is responsible for: the priority stamp below
            // must cover exactly what the walk below adds, and a nested error can add fewer
            // candidates than there are wires.
            int firstCandidate = candidates.Count;
            int wiresFollowed = 0;
            foreach (IPort output in node.GetOutputPorts())
            {
                if (output == null || !output.IsConnected)
                    continue;
                var buffer = new List<IPort>();
                output.GetConnectedPorts(buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    // A wire back to the source state is a SELF transition, not a cycle: the zombie's
                    // idle heartbeat re-enters idle exactly that way (StateTreePresets.BuildZombie).
                    INode next = buffer[i]?.GetNode();
                    if (next == null)
                        continue;
                    FollowEdge(state, next, conditionNode, checkWhileRunning, portIndex,
                        wireIndex, depth + 1, candidates, log, result);
                    wiresFollowed++;
                }
            }

            if (wiresFollowed == 0)
            {
                log?.Error($"'{SafeTitle(node)}' leaving state '{state.id}' is not connected to a "
                    + "target state, so that transition would go nowhere.", node);
                result.errorCount++;
            }
            else if (nodeHasOrder)
            {
                // Apply the explicit priority to everything this node produced.
                for (int i = firstCandidate; i < candidates.Count; i++)
                {
                    TransitionBuild candidate = candidates[i];
                    candidate.order = nodeOrder;
                    candidate.hasOrder = true;
                    candidates[i] = candidate;
                }
            }
        }

        /// <summary>Reference identity for <see cref="INode"/> dictionary keys. The node objects
        /// handed out by the graph are models whose equality is theirs to define; a transition must
        /// resolve to the node the wire actually reached, not to one that merely compares equal.</summary>
        private sealed class NodeIdentity : IEqualityComparer<INode>
        {
            public static readonly IEqualityComparer<INode> comparer = new NodeIdentity();

            public bool Equals(INode a, INode b) => ReferenceEquals(a, b);

            public int GetHashCode(INode node)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node);
        }

        // ------------------------------------------------------------------ entry

        private static StateBuild ResolveEntry(Graph graph, List<StateBuild> states, IBakeLog log)
        {
            // 1. A node that is not a state and not a condition, with an output wired into a state,
            //    is the entry marker (an EntryNode / StartNode — the sample's StartNode role).
            foreach (INode node in graph.GetNodes())
            {
                if (node is ContextNode || node is BlockNode)
                    continue;
                if (!LooksLikeEntry(node))
                    continue;
                foreach (IPort port in node.GetOutputPorts())
                {
                    if (port == null || !port.IsConnected)
                        continue;
                    INode target = port.FirstConnectedPort?.GetNode();
                    StateBuild match = FindState(states, target);
                    if (match != null)
                        return match;
                }
                log?.Warning("The entry node is not connected to a state; the first state in the "
                    + "graph is used as the entry instead.", node);
            }

            // 2. Exactly one state nothing points at is unambiguous enough to use.
            var targeted = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < states.Count; i++)
                for (int t = 0; t < states[i].asset.transitions.Count; t++)
                    targeted.Add(states[i].asset.transitions[t].targetNodeId);

            StateBuild orphan = null;
            for (int i = 0; i < states.Count; i++)
            {
                if (targeted.Contains(states[i].id))
                    continue;
                if (orphan != null)
                    return states[0];
                orphan = states[i];
            }

            // 3. Creation order.
            return orphan ?? states[0];
        }

        private static bool LooksLikeEntry(INode node)
        {
            string name = node.GetType().Name;
            return name.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static StateBuild FindState(List<StateBuild> states, INode node)
        {
            if (node == null)
                return null;
            for (int i = 0; i < states.Count; i++)
                if (ReferenceEquals(states[i].node, node))
                    return states[i];
            return null;
        }

        private static string ReadTreeName(StateBuild entry, Graph graph)
        {
            string name = entry != null ? ReadStringValue(entry.node, TreeNameOption) : null;
            if (!string.IsNullOrEmpty(name))
                return name;
            try
            {
                if (!string.IsNullOrEmpty(graph.Name))
                    return graph.Name;
            }
            catch (Exception)
            {
                // Graph.Name reaches into the graph implementation; a graph loaded outside the editor
                // window can legitimately have none. The entry id is a fine fallback.
            }
            return entry != null ? entry.id : "StateTree";
        }

        private static string ReadTreeKind(StateBuild entry)
        {
            string kind = entry != null ? ReadStringValue(entry.node, TreeKindOption) : null;
            return string.IsNullOrEmpty(kind) ? "enemy_ai" : kind;
        }

        // ------------------------------------------------------------------ option → field copy

        /// <summary>
        /// Copy every authored value off a graph node onto the library asset it became. This is the
        /// whole of the M7 "options mirror the task's serialized fields, 1:1 by name" convention: the
        /// baker walks the LIBRARY type's public fields and asks the node for each name, so a new
        /// task type needs no baker change at all.
        ///
        /// Three sources are tried per field, in order: a node option (the convention), an input port
        /// of the same name (for values an author may want to wire rather than type), and finally a
        /// public field/property on the node class itself (for a block that stores its data plainly).
        /// </summary>
        private static void ApplyAuthoredValues(INode node, ScriptableObject target, IBakeLog log,
            BakeResult result)
        {
            Type type = target.GetType();
            var matched = new HashSet<string>(StringComparer.Ordinal);

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsInitOnly || field.IsNotSerialized || field.IsStatic)
                    continue;

                // A key-semantic port WIRED TO A VARIABLE is the id-wire, canvas flavor: the
                // variable is the declaration BakeKeyDeclarations wrote, so the field gets the
                // declaration's name AND its id — rename the variable, re-bake, and every use
                // follows. An unconnected port stays the free-typed constant below.
                if (field.FieldType == typeof(StateTreeKeyField))
                {
                    IPort keyPort = FindInputPort(node, field.Name);
                    if (keyPort != null
                        && KeyVariables.TryConnectedVariable(keyPort, out IVariable keyVariable))
                    {
                        string keyName = KeyVariables.NameOf(keyVariable);
                        if (!(field.GetValue(target) is StateTreeKeyField wrapper))
                        {
                            wrapper = new StateTreeKeyField();
                            field.SetValue(target, wrapper);
                        }
                        wrapper.text = keyName;
                        wrapper.keyId = TaskGraphBaker.ReadParameterId(keyVariable, keyName);
                        matched.Add(field.Name);
                        continue;
                    }
                }

                if (!TryReadAuthoredValue(node, field.Name,
                    LibraryParameterPorts.PortDataType(field.FieldType), out object value))
                    continue;
                matched.Add(field.Name);
                try
                {
                    LibraryParameterPorts.WriteFieldValue(field, target, value);
                }
                catch (Exception e)
                {
                    log?.Error($"Could not write '{field.Name}' on {type.Name}: {e.Message}", node);
                    result.errorCount++;
                }
            }

            // An authored value nothing consumed is nearly always a rename that half-landed (the
            // library field was renamed, the node was not), which would silently bake the field's
            // default and look like the task ignoring its own parameter.
            foreach (INodeOption option in node.NodeOptions)
            {
                string name = option?.Name;
                if (string.IsNullOrEmpty(name) || matched.Contains(name) || IsReservedName(name))
                    continue;
                log?.Warning($"Option '{name}' has no matching field on {type.Name}, so it is ignored. "
                    + "Parameters must be named exactly like the task/condition field they set.", node);
                result.warningCount++;
            }

            foreach (IPort port in node.GetInputPorts())
            {
                string name = port?.Name;
                if (string.IsNullOrEmpty(name) || matched.Contains(name) || IsReservedName(name))
                    continue;
                // Untyped ports are execution/flow wires (the sample's ExecutionPort shape), never
                // parameters.
                if (port.DataType == null || port.DataType == typeof(object))
                    continue;
                log?.Warning($"Input port '{name}' has no matching field on {type.Name}, so it is "
                    + "ignored. Parameters must be named exactly like the task/condition field they "
                    + "set.", node);
                result.warningCount++;
            }
        }

        /// <summary>Names the baker consumes itself (state/transition metadata) or that belong to the
        /// palette's untyped flow wires — neither is a task parameter.</summary>
        private static bool IsReservedName(string name)
            => name == NodeIdOption || name == DisplayNameOption || name == CheckWhileRunningOption
                || name == OrderOption || name == TreeNameOption || name == TreeKindOption
                || name == KeyKindsOption
                || name == "in" || name == "out" || name == "from" || name == "to"
                || name == "entry" || name == "condition";

        private static bool TryReadAuthoredValue(INode node, string name, Type wanted, out object value)
        {
            value = null;

            INodeOption option = FindOption(node, name);
            if (option != null && TryReadOption(option, wanted, out value))
                return true;

            IPort port = FindInputPort(node, name);
            if (port != null && TryReadPort(port, wanted, out value))
                return true;

            return TryReadNodeMember(node, name, wanted, out value);
        }

        private static INodeOption FindOption(INode node, string name)
        {
            foreach (INodeOption option in node.NodeOptions)
                if (option != null && string.Equals(option.Name, name, StringComparison.Ordinal))
                    return option;
            return null;
        }

        private static IPort FindInputPort(INode node, string name)
        {
            foreach (IPort port in node.GetInputPorts())
                if (port != null && string.Equals(port.Name, name, StringComparison.Ordinal))
                    return port;
            return null;
        }

        /// <summary><see cref="INodeOption.TryGetValue{T}"/> is generic over a type only known at
        /// bake time, so it is invoked reflectively on the option's own declared data type and the
        /// result is coerced to the field type (int option → float field, and so on).</summary>
        private static bool TryReadOption(INodeOption option, Type wanted, out object value)
        {
            value = null;
            Type declared = SafeDataType(option.DataType, wanted);
            object[] args = new object[1];
            try
            {
                var method = typeof(INodeOption).GetMethod(nameof(INodeOption.TryGetValue));
                bool ok = (bool)method.MakeGenericMethod(declared).Invoke(option, args);
                if (!ok)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            return TryCoerce(args[0], wanted, out value);
        }

        /// <summary>Port values follow the sample's priority order
        /// (<c>VisualNovelDirectorImporter.GetInputPortValue</c>): a connected constant or variable
        /// node first, then the port's own embedded field.</summary>
        private static bool TryReadPort(IPort port, Type wanted, out object value)
        {
            value = null;
            // Read at the port's OWN declared type, then coerce: a port declared int feeding a float
            // field must not fail just because the generic argument would not match.
            Type declared = SafeDataType(port.DataType, wanted);
            if (port.IsConnected)
            {
                INode source = port.FirstConnectedPort?.GetNode();
                switch (source)
                {
                    case IConstantNode constant:
                        return TryInvokeTryGet(typeof(IConstantNode), nameof(IConstantNode.TryGetValue),
                            constant, declared, wanted, out value);
                    case IVariableNode variableNode when variableNode.Variable != null:
                        return TryInvokeTryGet(typeof(IVariable), nameof(IVariable.TryGetDefaultValue),
                            variableNode.Variable, declared, wanted, out value);
                    default:
                        return false;
                }
            }
            return TryInvokeTryGet(typeof(IPort), nameof(IPort.TryGetValue), port, declared, wanted, out value);
        }

        private static bool TryInvokeTryGet(Type declaringType, string methodName, object instance,
            Type generic, Type wanted, out object value)
        {
            value = null;
            object[] args = new object[1];
            try
            {
                MethodInfo method = declaringType.GetMethod(methodName);
                if (method == null)
                    return false;
                bool ok = (bool)method.MakeGenericMethod(generic).Invoke(instance, args);
                if (!ok)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            return TryCoerce(args[0], wanted, out value);
        }

        /// <summary>Last resort: a block that keeps its data in plain public fields/properties rather
        /// than in node options. Costs nothing to support and makes the baker survive a palette that
        /// chooses the other authoring style.</summary>
        private static bool TryReadNodeMember(INode node, string name, Type wanted, out object value)
        {
            value = null;
            Type type = node.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return TryCoerce(field.GetValue(node), wanted, out value);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead)
                return TryCoerce(property.GetValue(node), wanted, out value);
            return false;
        }

        private static Type SafeDataType(Type declared, Type fallback)
            => declared == null || declared == typeof(void) ? fallback : declared;

        private static bool TryCoerce(object raw, Type wanted, out object value)
        {
            value = null;
            if (raw == null)
                return !wanted.IsValueType;
            if (wanted.IsInstanceOfType(raw))
            {
                value = raw;
                return true;
            }
            try
            {
                if (wanted.IsEnum)
                {
                    value = raw is string text ? Enum.Parse(wanted, text, true) : Enum.ToObject(wanted, raw);
                    return true;
                }
                if (raw is IConvertible)
                {
                    value = Convert.ChangeType(raw, wanted, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        // ------------------------------------------------------------------ node type → library type

        private static Dictionary<string, Type> s_TaskTypes;
        private static Dictionary<string, Type> s_ConditionTypes;
        private static readonly Dictionary<Type, Type> k_ResolvedTaskTypes = new Dictionary<Type, Type>();
        private static readonly Dictionary<Type, Type> k_ResolvedConditionTypes = new Dictionary<Type, Type>();

        /// <summary>
        /// Map a graph node class to the library task/condition it authors. Two ways in, both
        /// non-invasive so the palette can pick either:
        /// <list type="bullet">
        /// <item>The node declares it: a public <c>taskType</c> / <c>conditionType</c> /
        /// <c>libraryType</c> member of type <see cref="Type"/> (static or instance).</item>
        /// <item>By name: the node's class name with the <c>Node</c>/<c>Block</c> decoration stripped
        /// matches a library type, optionally after appending <c>Task</c> / <c>Condition</c>
        /// (<c>ChaseTargetNode</c>, <c>ChaseTargetTaskNode</c> and <c>ChaseTargetTaskBlock</c> all
        /// resolve to <see cref="ChaseTargetTask"/>).</item>
        /// </list>
        /// </summary>
        private static Type ResolveLibraryType(INode node, Type baseType)
        {
            if (node == null)
                return null;
            bool wantTask = baseType == typeof(StateTreeTaskAsset);
            Dictionary<Type, Type> cache = wantTask ? k_ResolvedTaskTypes : k_ResolvedConditionTypes;
            Type nodeType = node.GetType();
            if (cache.TryGetValue(nodeType, out Type cached))
                return cached;

            Type resolved = ReadDeclaredLibraryType(node, nodeType, baseType) ?? ResolveByName(nodeType, wantTask);
            cache[nodeType] = resolved;
            return resolved;
        }

        private static Type ReadDeclaredLibraryType(INode node, Type nodeType, Type baseType)
        {
            string[] names = baseType == typeof(StateTreeTaskAsset)
                ? new[] { "taskType", "TaskType", "libraryType", "LibraryType" }
                : new[] { "conditionType", "ConditionType", "libraryType", "LibraryType" };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = nodeType.GetField(names[i], flags);
                if (field != null && typeof(Type).IsAssignableFrom(field.FieldType))
                {
                    var value = field.GetValue(field.IsStatic ? null : node) as Type;
                    if (value != null && baseType.IsAssignableFrom(value) && !value.IsAbstract)
                        return value;
                }
                PropertyInfo property = nodeType.GetProperty(names[i], flags);
                if (property != null && property.CanRead && typeof(Type).IsAssignableFrom(property.PropertyType))
                {
                    var value = property.GetValue(property.GetMethod.IsStatic ? null : node) as Type;
                    if (value != null && baseType.IsAssignableFrom(value) && !value.IsAbstract)
                        return value;
                }
            }
            return null;
        }

        private static Type ResolveByName(Type nodeType, bool wantTask)
        {
            Dictionary<string, Type> library = wantTask ? TaskTypes() : ConditionTypes();
            string suffix = wantTask ? "Task" : "Condition";
            string stem = StripNodeDecoration(nodeType.Name);

            if (library.TryGetValue(stem, out Type direct))
                return direct;
            if (library.TryGetValue(stem + suffix, out Type suffixed))
                return suffixed;
            return null;
        }

        private static string StripNodeDecoration(string name)
        {
            string[] decorations = { "BlockNode", "TaskNode", "ConditionNode", "Block", "Node" };
            for (int i = 0; i < decorations.Length; i++)
            {
                if (name.Length > decorations[i].Length && name.EndsWith(decorations[i], StringComparison.Ordinal))
                    return name.Substring(0, name.Length - decorations[i].Length);
            }
            return name;
        }

        private static Dictionary<string, Type> TaskTypes()
            => s_TaskTypes ?? (s_TaskTypes = BuildLibraryIndex(typeof(StateTreeTaskAsset)));

        private static Dictionary<string, Type> ConditionTypes()
            => s_ConditionTypes ?? (s_ConditionTypes = BuildLibraryIndex(typeof(StateTreeConditionAsset)));

        private static Dictionary<string, Type> BuildLibraryIndex(Type baseType)
        {
            var index = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                index[type.Name] = type;
            }
            return index;
        }

        // ------------------------------------------------------------------ small helpers

        private static string LabelFor(INode node, Type libraryType, string suffix)
        {
            string title = SafeTitle(node);
            if (!string.IsNullOrEmpty(title))
                return title;
            string name = libraryType.Name;
            return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }

        // The baker's own metadata (nodeId, displayName, checkWhileRunning, order, treeName,
        // treeKind) is read through the SAME ladder as task parameters — node option, then input
        // port, then a plain member on the node class. The palette declares parameters as input
        // PORTS rather than options (INodeOption has TryGetValue but no setter in Graph Toolkit
        // 0.5, so an option cannot be written programmatically and the M7 verifier could not build
        // a graph in code), and it declares nodeId/displayName/checkWhileRunning/order the same
        // way for consistency. Reading through the ladder means either choice bakes correctly.

        private static string ReadStringValue(INode node, string name)
            => TryReadAuthoredValue(node, name, typeof(string), out object value) && value is string text
                ? text
                : string.Empty;

        private static bool ReadBoolValue(INode node, string name, out bool present)
        {
            if (TryReadAuthoredValue(node, name, typeof(bool), out object value) && value is bool flag)
            {
                present = true;
                return flag;
            }
            present = false;
            return false;
        }

        private static int ReadIntValue(INode node, string name, out bool present)
        {
            if (TryReadAuthoredValue(node, name, typeof(int), out object value) && value is int number)
            {
                present = true;
                return number;
            }
            present = false;
            return 0;
        }

        /// <summary><see cref="INode.Title"/> is a live view of the node's implementation; a node
        /// that has been removed from the graph mid-walk would throw rather than answer.</summary>
        private static string SafeTitle(INode node)
        {
            try
            {
                return node.Title ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string DescribeNode(object nodeContext)
        {
            if (nodeContext is INode node)
            {
                string title = SafeTitle(node);
                return string.IsNullOrEmpty(title) ? node.GetType().Name : $"{title} ({node.GetType().Name})";
            }
            return nodeContext != null ? nodeContext.GetType().Name : string.Empty;
        }

        /// <summary>Node ids end up in serialized transitions and in sub-asset names, so they are
        /// normalised the way the presets write them by hand: lower-case words joined by '_'.</summary>
        private static string Slug(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            var builder = new System.Text.StringBuilder(raw.Length);
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
    }
}
