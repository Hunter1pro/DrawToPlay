using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// The Draw-to-Play LOGIC graph: a single <see cref="StateTreeTaskAsset"/> authored as a
    /// Blueprint-style program rather than as a state machine. A <c>.taskgraph</c> file holds the
    /// authored graph; <see cref="TaskGraphImporter"/> bakes it into the flat
    /// <see cref="GraphTaskAsset"/> program that the runtime interpreter executes, and that program
    /// is the file's MAIN asset — so a <c>.taskgraph</c> drags into any task slot and shows up in
    /// the task picker like any other library task.
    ///
    /// ====================================================================================
    /// TWO GRAPH TYPES, TWO JOBS (why this is not <see cref="StateTreeGraph"/>)
    /// ====================================================================================
    /// <see cref="StateTreeGraph"/> authors a STATE MACHINE: states own parallel tasks, transitions
    /// carry conditions, and the runner decides what runs from where it is. This graph authors a
    /// PROGRAM: one entry point per task lifecycle hook, exec wires that say what happens next, and
    /// data wires that say what a value is. Both bake to things the runtime already understands
    /// (<see cref="StateTreeAsset"/> there, <see cref="GraphTaskAsset"/> here) and both are pickable
    /// as a task, so an author picks the shape that fits the behaviour instead of bending one shape
    /// into the other. The M7c/M7d sub-tree flavour is unchanged and unaffected.
    ///
    /// ====================================================================================
    /// GRAPH → PROGRAM MAPPING (the bake contract — see TaskGraphBaker for the byte-precise form)
    /// ====================================================================================
    ///
    /// .taskgraph graph                → <see cref="GraphTaskAsset"/>
    ///   <see cref="OnEnterNode"/>      → <c>enterEntry</c>  (index of the node its exec pin reaches)
    ///   <see cref="OnTickNode"/>       → <c>tickEntry</c>   (-1 ⇒ the task returns Success at once)
    ///   <see cref="OnExitNode"/>       → <c>exitEntry</c>
    ///   every other node               → one <c>GraphTaskNode</c> in <c>nodes</c>, in
    ///                                    <see cref="Graph.GetNodes"/> order
    ///   exec wire out of pin <c>i</c>  → <c>node.exec[i]</c> = the target's index (-1 = end of chain)
    ///   data wire into pin <c>i</c>    → <c>node.data[i]</c> = the source's index (-1 = unwired)
    ///
    /// The entry nodes are NOT program nodes: they name a chain's first instruction and nothing else,
    /// which is why the three entry fields hold an index into <c>nodes</c> rather than an index OF an
    /// entry node.
    ///
    /// ====================================================================================
    /// WIRING RULES AND HOW EACH ONE IS ENFORCED
    /// ====================================================================================
    /// 1. PALETTE (compile-time, unbreakable). <see cref="GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly"/>
    ///    means nothing is offered unless it carries <c>[UseWithGraph(typeof(TaskGraph))]</c>, so the
    ///    state-machine palette (<see cref="StateNode"/>, <see cref="TransitionNode"/>, the M7 task
    ///    blocks) can never be dropped here and vice versa.
    /// 2. DATA PINS (type system, unbreakable). Every data pin is TYPED (float/string/bool), and a
    ///    port pair connects only when <c>input.DataType.IsAssignableFrom(output.DataType)</c>
    ///    (PortModel.CanConnectPort, UnityEditor.GraphToolkitModule IL 75069-75110). A bool cannot
    ///    reach a float pin, and a typed INPUT has Single capacity (PortModel.get_Capacity, module IL
    ///    74310-74354 — Single only when the data type is not Untyped AND the direction is Input),
    ///    which is exactly "a value pin has one source".
    /// 3. EXEC PINS (validated, not typed). Exec pins are UNTYPED, because only an untyped port has
    ///    Multi capacity (same IL) and an instruction must accept many predecessors — every branch of
    ///    a diamond rejoining one node is that. Untyped also means an exec OUT pin accepts several
    ///    wires, which the program model cannot express (one exec slot, one target), so
    ///    <see cref="TaskGraphBaker"/> reports the extra wires as graph errors. Untyped exec ports are
    ///    the same shape the canonical Graph Toolkit sample uses for its execution wires
    ///    (VisualNovelDirector, VisualNovelNode.cs:20-31) and the same shape M7 uses for state flow.
    /// 4. EVERYTHING STRUCTURAL (validated here and in the baker). Entry counts below; unreachable
    ///    instructions, latent nodes in the enter/exit chains, Return nodes outside the tick chain and
    ///    unresolvable pins in <see cref="TaskGraphBaker.Validate"/>, which runs the REAL bake so a
    ///    graph that shows no errors is a graph that bakes.
    ///
    /// ====================================================================================
    /// GRAPH VARIABLES ARE CONSTANTS IN v1
    /// ====================================================================================
    /// Graph Toolkit's own variable panel works, and a variable node wired into a data pin bakes as
    /// the variable's DEFAULT VALUE — a constant. The runtime program has no per-graph variable
    /// storage; the blackboard is the shared mutable state and it has real nodes
    /// (<see cref="SetBlackboardFloatNode"/>, <see cref="GetBlackboardFloatNode"/>, …). The bake says
    /// so with a warning every time it flattens one, so nobody discovers this at runtime.
    /// </summary>
    [Serializable]
    [Graph(Extension, GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    public class TaskGraph : Graph
    {
        /// <summary>File extension of a task graph asset, WITHOUT the dot. Registered with Unity's
        /// importer through <see cref="GraphAttribute"/>, so it must be unique in the project, must
        /// not start with a '.' and must not be "asset" — Graph Toolkit logs an error for both
        /// (PublicGraphFactory.HandleGraphAttribute, module IL 345107-345256).</summary>
        public const string Extension = "taskgraph";

        private const string k_DefaultAssetName = "TaskGraph";

        [MenuItem("Assets/Create/Draw To Play/Task Graph")]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<TaskGraph>(k_DefaultAssetName);
        }

        /// <summary>
        /// Structural validation. Entry-node cardinality is checked here because it is a property of
        /// the GRAPH rather than of any one node; everything else is checked by running the bake, so
        /// the two can never disagree about what is wrong.
        /// </summary>
        /// <param name="graphLogger">Sink for the errors and warnings shown on the graph.</param>
        public override void OnGraphChanged(GraphLogger graphLogger)
        {
            base.OnGraphChanged(graphLogger);

            var nodes = GetNodes().ToList();
            ValidateEntries<OnEnterNode>(graphLogger, nodes, "On Enter");
            ValidateEntries<OnTickNode>(graphLogger, nodes, "On Tick");
            ValidateEntries<OnExitNode>(graphLogger, nodes, "On Exit");

            if (nodes.OfType<OnTickNode>().FirstOrDefault() == null)
            {
                graphLogger.LogWarning("No On Tick node: this task succeeds the instant it is entered. "
                    + "Add On Tick and wire it to the first thing the task should do.", this);
            }

            TaskGraphBaker.Validate(this, graphLogger);
        }

        /// <summary>One entry node per lifecycle hook. A second one is not an error the bake can
        /// resolve — there is a single <c>enterEntry</c>/<c>tickEntry</c>/<c>exitEntry</c> field — so
        /// the extras are reported and the first in graph order wins, which is what the bake does.
        /// </summary>
        private void ValidateEntries<T>(GraphLogger graphLogger, IReadOnlyList<INode> nodes, string label)
            where T : class
        {
            int seen = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not T)
                    continue;
                seen++;
                if (seen > 1)
                {
                    graphLogger.LogError($"A task graph has one '{label}' entry; this one is ignored. "
                        + "Delete it, or merge the two chains with a Branch.", nodes[i]);
                }
            }
        }

        /// <summary>Nodes on the far side of every wire of <paramref name="port"/>, filtered to
        /// <typeparamref name="T"/>. Safe on a null or unconnected port.</summary>
        /// <param name="port">The port to walk.</param>
        /// <returns>The connected nodes that are a <typeparamref name="T"/>.</returns>
        public static List<T> CollectConnectedNodes<T>(IPort port) where T : class
        {
            var result = new List<T>();
            if (port == null || !port.IsConnected)
                return result;

            var connected = new List<IPort>();
            port.GetConnectedPorts(connected);
            for (int i = 0; i < connected.Count; i++)
            {
                if (connected[i]?.GetNode() is T typed)
                    result.Add(typed);
            }
            return result;
        }
    }
}
