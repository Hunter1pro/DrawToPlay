using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The Draw-to-Play state tree authoring graph (brief §7.3): an editor-only front end over the
    /// M6 component library. A <c>.statetree</c> asset holds the authored graph; a BAKE step
    /// (agent-bake, <c>StateTreeGraphBaker</c>) turns it into the flat runtime
    /// <see cref="StateTreeAsset"/> that <see cref="StateTreeRunner"/> consumes. Nothing in
    /// <c>PowerOfFire.DrawToPlay</c> (runtime) ever references a graph type — that boundary is the
    /// whole point of the separate <c>PowerOfFire.DrawToPlay.GraphEditor</c> assembly.
    ///
    /// ====================================================================================
    /// GRAPH → RUNTIME MAPPING (the bake contract — every line below is load-bearing)
    /// ====================================================================================
    ///
    /// .statetree graph                    → <see cref="StateTreeAsset"/>
    ///   <see cref="EntryNode"/> port "treeName"   → <c>StateTreeAsset.treeName</c>
    ///                                               (empty ⇒ use the graph asset's file name)
    ///   <see cref="EntryNode"/> port "treeKind"   → <c>StateTreeAsset.treeKind</c>
    ///   root                              → ONE organizational <see cref="StateTreeNodeAsset"/>:
    ///                                       no tasks, no transitions, <c>children</c> = every
    ///                                       <see cref="StateNode"/> in the graph, and
    ///                                       <c>children[0]</c> = the state wired to
    ///                                       <see cref="EntryNode"/>'s "entry" port.
    ///                                       <c>StateTreeRunner.ResolveEntryNode</c> descends a
    ///                                       task-less, transition-less node to <c>children[0]</c>
    ///                                       (StateTreeRunner.cs:190-198), so "the entry wire"
    ///                                       IS the runtime entry with no extra concept.
    ///   <see cref="StateNode"/>            → <see cref="StateTreeNodeAsset"/>
    ///     port "nodeId"                    → <c>nodeId</c>   (unique, non-empty — validated here)
    ///     port "displayName"               → <c>displayName</c>
    ///     block nodes, in block order      → <c>tasks</c>   (ContextNode.BlockNodes order)
    ///     wires out of port "out"          → <c>transitions</c>
    ///   <see cref="StateTaskBlockNode"/>   → one instance of <c>node.taskType</c>
    ///                                        (ScriptableObject.CreateInstance + AddObjectToAsset,
    ///                                         exactly the sub-asset shape StateTreePresets builds)
    ///   <see cref="StateTreeConditionNode"/> → one instance of <c>node.conditionType</c>
    ///   <see cref="TransitionNode"/>       → <see cref="StateTreeTransition"/>
    ///     port "to" → StateNode "in"       → <c>targetNodeId</c> = that state's "nodeId" value
    ///     port "condition"                 → <c>condition</c>: instantiate the wired condition
    ///                                        node's <c>conditionType</c>; an unconnected port that
    ///                                        holds a project <see cref="StateTreeConditionAsset"/>
    ///                                        reference is used as-is (shared assets are safe: the
    ///                                        runner deep-copies the whole tree on StartTree);
    ///                                        empty ⇒ null ⇒ "always true".
    ///     port "checkWhileRunning"         → <c>checkWhileRunning</c>
    ///     port "order"                     → NOT a runtime field. Authoring-only sort key: bake
    ///                                        sorts a state's transitions by ascending "order"
    ///                                        (ties keep wire order) before writing them, because
    ///                                        the runner takes the FIRST matching transition
    ///                                        (StateTreeRunner.cs:106-148) and wire creation order
    ///                                        is invisible in the UI.
    ///
    /// PARAMETER PORTS ARE 1:1 WITH THE RUNTIME FIELDS, BY CONSTRUCTION.
    /// Task and condition nodes do not hand-list their parameters: both base classes call
    /// <see cref="LibraryParameterPorts.DefineParameterPorts"/>, which reflects over the runtime
    /// type's public instance fields and names each port EXACTLY after the field
    /// (<c>moveSpeed</c>, <c>useBlackboardRange</c>, …). The bake reads them back through the same
    /// helper (<see cref="LibraryParameterPorts.GetParameterFields"/> +
    /// <see cref="LibraryParameterPorts.TryReadValue"/>), so the two sides cannot drift and a new
    /// library task needs only a 6-line wrapper class. Field defaults become the port defaults, so
    /// a freshly dropped Chase node already reads <c>moveSpeed 2</c>.
    ///
    /// WHY PORTS AND NOT NODE OPTIONS. The m7 plan said "options". <c>INodeOption</c> in Graph
    /// Toolkit 0.5.0-exp.1 exposes <c>TryGetValue&lt;T&gt;</c> and NO setter
    /// (UnityEditor.GraphToolkitModule, INodeOption IL 21204-21249), while <c>IPort</c> exposes
    /// both <c>TryGetValue&lt;T&gt;</c> and <c>TrySetValue&lt;T&gt;</c> (IPort IL 21296-21305,
    /// PortModel implementation IL 76177-76200). Options therefore cannot be written
    /// programmatically, which would make agent-demo's "build the zombie graph in code, bake it,
    /// run the baked asset" verification impossible. Parameters as input ports also match the
    /// canonical sample (VisualNovelDirector <c>SetDialogueNode</c> puts every parameter on an
    /// input port and its importer reads them back), and they buy connectable parameters: a graph
    /// variable or constant node can drive <c>moveSpeed</c> for a whole family of states.
    ///
    /// BUILDING A GRAPH IN CODE (the M7 verifier's path). <c>GraphDatabase.CreateGraph&lt;StateTreeGraph&gt;</c>
    /// ("Assets/…/Zombie.statetree"), then per node <c>graph.AddNode(new StateNode())</c>,
    /// <c>state.CreateBlockNode&lt;ChaseTargetTaskNode&gt;()</c> for its tasks,
    /// <c>LibraryParameterPorts.TryWriteValue(node.GetInputPortByName("moveSpeed"), typeof(float), 1.25f)</c>
    /// for every parameter, and <c>graph.Connect(outputPort, inputPort)</c> for every wire; finish
    /// with <c>GraphDatabase.SaveGraph(graph)</c>. Nothing about the palette restricts this —
    /// <c>Graph.AddNode</c> is a straight delegation with no library check.
    ///
    /// ====================================================================================
    /// WIRING RULES AND HOW EACH ONE IS ENFORCED
    /// ====================================================================================
    /// 1. PALETTE (compile-time, unbreakable). The graph declares
    ///    <see cref="GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly"/>, so nothing is in
    ///    the item library unless it carries <c>[UseWithGraph(typeof(StateTreeGraph))]</c>; task
    ///    blocks additionally carry <c>[UseWithContext(typeof(StateNode))]</c> and are therefore
    ///    offered ONLY inside a State, never on the canvas (PublicGraphFactory.GetNodeTypes drops
    ///    every BlockNode subclass from the graph-level palette, module IL 345477-345501;
    ///    GetBlockTypes intersects [UseWithContext] with [UseWithGraph], IL 344541-344690).
    /// 2. CONDITION SLOTS (type system, unbreakable). Every condition node's output port is typed
    ///    with its concrete <see cref="StateTreeConditionAsset"/> subclass and
    ///    <see cref="TransitionNode"/>'s "condition" input is typed
    ///    <see cref="StateTreeConditionAsset"/>; a port pair connects only when
    ///    <c>input.DataType.IsAssignableFrom(output.DataType)</c> (PortModel.CanConnectPort, module
    ///    IL 75069-75110). So a condition fits a condition slot and nothing else fits either.
    ///    A typed input also has Single capacity (PortModel.get_Capacity, IL 74310-74354), which is
    ///    exactly <c>StateTreeTransition.condition</c> being one field.
    /// 3. FLOW WIRES (validated below, not typed). "in"/"out"/"entry"/"from"/"to" are UNTYPED
    ///    execution ports, the same shape the canonical sample uses for its execution wires
    ///    (VisualNovelNode.cs:20-31). They must be untyped because only an untyped input has Multi
    ///    capacity (same IL as above) and a state must accept many incoming transitions. The price
    ///    is that the type system alone cannot forbid a state→state wire, so <see cref="OnGraphChanged"/>
    ///    reports every structural violation as a graph error, the mechanism the canonical sample
    ///    uses for its own rules (VisualNovelDirectorGraph.CheckGraphErrors).
    ///
    /// ONE GRAPH TYPE, NOT TWO. The m7 plan floated a separate PlayerFlowGraph. A second palette
    /// needs a second <see cref="Graph"/> subclass and therefore a second file extension and a
    /// second importer, and would buy exactly one thing today: hiding the two perception nodes.
    /// Instead the tree KIND is data — <see cref="EntryNode"/>'s "treeKind" port, default
    /// <c>enemy_ai</c>, set to <c>player_flow</c> for the player tree — matching
    /// <c>StateTreeAsset.treeKind</c>. When M8 adds player-intent conditions to the runtime
    /// library, their wrappers carry <c>[UseWithGraph]</c> like everything else.
    ///
    /// NAMING CONVENTION FOR WRAPPERS: graph class name = runtime class name + "Node"
    /// (<c>ChaseTargetTask</c> → <c>ChaseTargetTaskNode</c>,
    /// <c>TargetInRangeCondition</c> → <c>TargetInRangeConditionNode</c>). The bake never keys off
    /// the name — it uses <c>taskType</c> / <c>conditionType</c> — but the convention keeps the
    /// two halves greppable.
    /// </summary>
    [Serializable]
    [Graph(Extension, GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    public class StateTreeGraph : Graph, IGraphDeclaredRegistries
    {
        /// <summary>File extension of a state tree graph asset. Registered with Unity's importer
        /// through <see cref="GraphAttribute"/>, so it must be unique in the project, must NOT
        /// start with a '.' and must not be "asset" — Graph Toolkit 0.5 logs an error for both
        /// (PublicGraphFactory.HandleGraphAttribute, module IL 345107-345256).</summary>
        public const string Extension = "statetree";

        /// <summary>Default tree kind, mirroring <c>StateTreeAsset.treeKind</c>'s default.</summary>
        public const string DefaultTreeKind = "enemy_ai";

        private const string k_DefaultAssetName = "StateTree";

        [MenuItem("Assets/Create/Draw To Play/State Tree Graph")]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<StateTreeGraph>(k_DefaultAssetName);
        }

        /// <summary>
        /// Structural validation. Everything the palette and the port types cannot make impossible
        /// is reported here, so an unbakeable graph is visibly wrong while it is being authored
        /// rather than at bake time.
        /// </summary>
        /// <param name="graphLogger">Sink for the errors and warnings shown on the graph.</param>
        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            base.OnGraphChanged(graphLogger);

            var nodes = GetNodes().ToList();
            ChoicePortRefresh.Refresh(nodes);
            // A pick that no def declares any more says so on the node (M38.4).
            DeclaredApiValidator.Validate(nodes, graphLogger);
            var states = nodes.OfType<StateNode>().ToList();
            var entries = nodes.OfType<EntryNode>().ToList();
            var transitions = nodes.OfType<TransitionNode>().ToList();

            ValidateEntry(graphLogger, entries);
            ValidateStates(graphLogger, states);
            ValidateTransitions(graphLogger, transitions);
            ValidateReachability(graphLogger, states, entries);

            // Library-mapping validation from the baker (block/condition with no runtime
            // type, parameter matching no field) — same messages authoring and import see.
            StateTreeGraphBaker.Validate(this, graphLogger);

            // Typed references name ROWS, and a canvas port cannot offer a picker — so the
            // "it must exist" half of the drawer's guarantee is enforced here instead.
            EntryRefValidator.Validate(this, nodes, graphLogger);
        }

        /// <inheritdoc />
        /// <remarks>A tree names its data on its Entry node, and the bake copies it onto
        /// <see cref="StateTreeAsset.registries"/> — so that registry is a root of this graph's
        /// scope without anything needing to point at the file.</remarks>
        public void CollectDeclaredRegistries(List<StateTreeRegistryAsset> into)
        {
            if (into == null)
                return;

            foreach (EntryNode entry in GetNodes().OfType<EntryNode>())
            {
                StateTreeRegistryAsset registry = entry.ResolveRegistry();
                if (registry != null && !into.Contains(registry))
                    into.Add(registry);
            }
        }

        private void ValidateEntry(GraphLogger graphLogger, IReadOnlyList<EntryNode> entries)
        {
            if (entries.Count == 0)
            {
                graphLogger.LogError("Add an Entry node: it names the tree and points at the state the runner starts in.", this);
                return;
            }

            for (int i = 1; i < entries.Count; i++)
                graphLogger.LogWarning("A state tree graph supports one Entry node; only the first is baked.", entries[i]);

            var entryPort = entries[0].GetOutputPortByName(EntryNode.EntryPortName);
            var targets = CollectConnectedNodes<StateNode>(entryPort);
            if (targets.Count == 0)
                graphLogger.LogError("Entry is not wired to a state; the baked tree would have no start state.", entries[0]);
            else if (targets.Count > 1)
                graphLogger.LogError("Entry is wired to more than one state; a tree has exactly one start state.", entries[0]);
        }

        private static void ValidateStates(GraphLogger graphLogger, IReadOnlyList<StateNode> states)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in states)
            {
                string nodeId = state.GetNodeId();
                if (string.IsNullOrEmpty(nodeId))
                    graphLogger.LogError("State has an empty nodeId. Transitions target states by id, so every state needs one.", state);
                else if (!seen.Add(nodeId))
                    graphLogger.LogError($"Duplicate state nodeId '{nodeId}'. Ids must be unique — the runner indexes states by id.", state);

                // A state's "out" port only ever feeds transitions. The untyped execution ports
                // that give a state many incoming wires also let a user drop a wire straight onto
                // another state, which the bake cannot express.
                var outPort = state.GetOutputPortByName(StateTreeFlowPorts.OutPortName);
                foreach (var connected in CollectConnectedNodes<INode>(outPort))
                {
                    if (connected is not TransitionNode)
                        graphLogger.LogError("A state's out port must feed Transition nodes. Insert a Transition between the two states.", state);
                }

                if (state.BlockCount == 0 && (outPort == null || !outPort.IsConnected))
                    graphLogger.LogWarning("State has no tasks and no outgoing transitions: the runner would sit in it forever.", state);
            }
        }

        private static void ValidateTransitions(GraphLogger graphLogger, IReadOnlyList<TransitionNode> transitions)
        {
            foreach (var transition in transitions)
            {
                var fromPort = transition.GetInputPortByName(TransitionNode.FromPortName);
                var sources = CollectConnectedNodes<INode>(fromPort);
                if (sources.Count == 0)
                    graphLogger.LogError("Transition has no source state; nothing would ever evaluate it.", transition);
                foreach (var source in sources)
                {
                    if (source is not StateNode)
                        graphLogger.LogError("A Transition's from port must come from a state's out port.", transition);
                }

                var toPort = transition.GetOutputPortByName(TransitionNode.ToPortName);
                var targets = CollectConnectedNodes<INode>(toPort);
                if (targets.Count == 0)
                {
                    graphLogger.LogError("Transition has no target state.", transition);
                }
                else if (targets.Count > 1)
                {
                    graphLogger.LogError("Transition targets more than one state; StateTreeTransition.targetNodeId is a single id.", transition);
                }
                else if (targets[0] is not StateNode)
                {
                    graphLogger.LogError("A Transition's to port must reach a state's in port.", transition);
                }
            }
        }

        private static void ValidateReachability(GraphLogger graphLogger, IReadOnlyList<StateNode> states, IReadOnlyList<EntryNode> entries)
        {
            if (entries.Count == 0 || states.Count == 0)
                return;

            var reached = new HashSet<StateNode>();
            var pending = new Stack<StateNode>();
            foreach (var start in CollectConnectedNodes<StateNode>(entries[0].GetOutputPortByName(EntryNode.EntryPortName)))
            {
                if (reached.Add(start))
                    pending.Push(start);
            }

            while (pending.Count > 0)
            {
                var state = pending.Pop();
                var outPort = state.GetOutputPortByName(StateTreeFlowPorts.OutPortName);
                foreach (var transition in CollectConnectedNodes<TransitionNode>(outPort))
                {
                    foreach (var target in CollectConnectedNodes<StateNode>(transition.GetOutputPortByName(TransitionNode.ToPortName)))
                    {
                        if (reached.Add(target))
                            pending.Push(target);
                    }
                }
            }

            foreach (var state in states)
            {
                if (!reached.Contains(state))
                    graphLogger.LogWarning("State is unreachable from Entry; it is baked but the runner can never enter it.", state);
            }
        }

        /// <summary>Nodes on the far side of every wire of <paramref name="port"/>, filtered to
        /// <typeparamref name="T"/>. Shared by the validators and safe on a null port.</summary>
        public static List<T> CollectConnectedNodes<T>(IPort port) where T : class
        {
            var result = new List<T>();
            if (port == null || !port.IsConnected)
                return result;

            var connected = new List<IPort>();
            port.GetConnectedPorts(connected);
            foreach (var other in connected)
            {
                if (other?.GetNode() is T typed)
                    result.Add(typed);
            }
            return result;
        }
    }
}
