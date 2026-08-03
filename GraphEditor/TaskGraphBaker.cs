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
    /// Walks an authored <see cref="TaskGraph"/> and produces the flat <see cref="GraphTaskAsset"/>
    /// program the runtime interpreter executes — the BAKE step that keeps draw-tool-port-brief.md
    /// §7.3's hard boundary intact: the runtime assembly never sees a graph type, and this editor
    /// assembly never leaks into a build.
    ///
    /// ====================================================================================
    /// THE PROGRAM MODEL, IN ONE PLACE
    /// ====================================================================================
    /// The baked program is a FLAT LIST of instructions plus three entry indices. A wire is an index:
    /// <c>exec[i]</c> is "what runs after my i-th outgoing pin", <c>data[i]</c> is "which instruction
    /// produces my i-th value", and -1 means nothing is wired. That is the entire encoding, and
    /// <see cref="Resolve"/> below is the only place that knows which port fills which slot — one
    /// switch, deliberately, because it IS the cross-assembly contract with the interpreter and a
    /// contract spread across forty node classes cannot be reviewed.
    ///
    /// Per instruction:
    /// <list type="bullet">
    /// <item>Branch — <c>data[0]</c> bool; <c>exec[0]</c> true, <c>exec[1]</c> false.</item>
    /// <item>SetBlackboardFloat — <c>stringValue</c> key, <c>data[0]</c> value or <c>floatValue</c>;
    /// <c>exec[0]</c> next.</item>
    /// <item>SetBlackboardString — <c>stringValue</c> key, <c>data[0]</c> value or
    /// <c>stringValue2</c>; <c>exec[0]</c> next.</item>
    /// <item>DoTask — <c>task</c> a configured sub-asset; <c>exec[0]</c> after Success,
    /// <c>exec[1]</c> after Failure. Latent.</item>
    /// <item>Wait — <c>data[0]</c> seconds or <c>floatValue</c>; <c>exec[0]</c> next. Latent.</item>
    /// <item>ReturnSuccess / ReturnFailure / ReturnRunning — no slots.</item>
    /// <item>FireCue — <c>stringValue</c> cue name; <c>exec[0]</c> next.</item>
    /// <item>ConstFloat / ConstString / ConstBool — <c>floatValue</c> / <c>stringValue</c> /
    /// <c>floatValue</c> non-zero.</item>
    /// <item>GetBlackboardFloat / GetBlackboardString / HasBlackboardKey — <c>stringValue</c> key.</item>
    /// <item>EvaluateCondition — <c>condition</c> a configured sub-asset.</item>
    /// <item>CompareFloat — <c>data[0]</c> left, <c>data[1]</c> right or <c>floatValue</c>,
    /// <c>stringValue</c> the operator symbol.</item>
    /// <item>BoolAnd / BoolOr — <c>data[0]</c>, <c>data[1]</c>. BoolNot — <c>data[0]</c>.</item>
    /// <item>ExitStatus — no slots.</item>
    /// </list>
    ///
    /// ====================================================================================
    /// THREE THINGS THIS BAKE DOES THAT ARE NOT MECHANICAL
    /// ====================================================================================
    /// 1. LITERALS THAT WOULD OTHERWISE BE LOST GET A CONSTANT INSTRUCTION. Only four data pins have
    ///    a literal slot in the program model (a Set's value, a Wait's seconds, a Compare's
    ///    right-hand side). Type <c>true</c> into a Branch's condition, or a number into a Compare's
    ///    LEFT side, and there is nowhere in the instruction to put it — so the bake appends a
    ///    ConstBool/ConstFloat instruction and wires the pin to it. The author's value survives, the
    ///    interpreter needs no special case, and the appended instructions are deterministic because
    ///    they are created in the order the pins are visited. A literal equal to the type's default
    ///    is skipped: "unwired" and "false" mean the same thing to the interpreter.
    /// 2. GRAPH VARIABLES ARE FLATTENED TO THEIR DEFAULTS, LOUDLY. Graph Toolkit's variable panel
    ///    works and a variable node connects like anything else, but the runtime program has no
    ///    per-graph variable storage — the blackboard is the shared mutable state. So a variable
    ///    reads as a constant taken from its default value, and every single one gets a warning
    ///    naming it. This is the v1 behaviour and the warning is how nobody finds out at runtime.
    /// 3. LIBRARY PARAMETERS ARE CONSTANTS, AND A WIRE INTO ONE IS AN ERROR. A task call bakes a
    ///    CONFIGURED COPY of the library task, so its parameters are baked values, not pins. Wiring a
    ///    computed number into a Chase node's <c>moveSpeed</c> would silently bake the typed-in value
    ///    instead; the bake refuses rather than lying. (The blackboard is the way to drive a library
    ///    task from graph logic: several of them read one — <c>useBlackboardSpeed</c>,
    ///    <c>useBlackboardRange</c>.)
    ///
    /// DETERMINISM. Instruction indices are <see cref="Graph.GetNodes"/> order (creation order),
    /// appended constants follow in pin-visit order, and sub-asset identifiers are derived from those
    /// indices — never from hash codes or dictionary iteration — so two bakes of an unchanged graph
    /// produce identical output and the importer's artifacts stay stable. The cost of index-keyed
    /// sub-asset identifiers is that inserting a node re-keys the ones after it; the alternative
    /// (authored ids, as the state-tree bake uses) needs an id on every node, which is a lot of
    /// typing for a graph whose nodes are mostly nameless arithmetic.
    /// </summary>
    public static class TaskGraphBaker
    {
        /// <summary>File extension of a task graph asset, WITHOUT the dot. Aliased from
        /// <see cref="TaskGraph.Extension"/> — the value the graph class passes to <c>[Graph(...)]</c>
        /// — because Unity picks the importer by extension and the two MUST be the same string.</summary>
        public const string GraphExtension = TaskGraph.Extension;

        /// <summary>Suffix of the standalone export written by
        /// <see cref="BakeSelectedGraphToAssetFile"/>. The importer path does not use it — there the
        /// baked program IS the graph file's main asset.</summary>
        public const string BakedAssetSuffix = "_Baked";

        /// <summary>Guard on the recursive data walk used by the dead-value diagnostics. Matches the
        /// interpreter's own 64-deep data-cycle guard, so a graph this baker accepts is a graph the
        /// interpreter can evaluate.</summary>
        private const int k_DataWalkDepth = 64;

        // ------------------------------------------------------------------ results

        /// <summary>
        /// Everything one bake produced, in memory and unattached: the program and the configured
        /// library sub-assets it points at. Attaching them is the CALLER's job, because
        /// <see cref="AssetDatabase"/> calls are illegal inside a
        /// <see cref="UnityEditor.AssetImporters.ScriptedImporter"/> — the importer uses
        /// <c>ctx.AddObjectToAsset</c>, the menu export uses <c>AssetDatabase.AddObjectToAsset</c>.
        /// </summary>
        public sealed class BakeResult
        {
            /// <summary>The baked program, or null when the graph produced nothing usable.</summary>
            public GraphTaskAsset program;

            /// <summary>The configured task and condition instances the program references, in a
            /// deterministic order, each with the stable identifier the importer needs.</summary>
            public readonly List<StateTreeGraphBaker.SubAsset> subAssets =
                new List<StateTreeGraphBaker.SubAsset>();

            /// <summary>Errors reported during the walk.</summary>
            public int errorCount;

            /// <summary>Warnings reported during the walk.</summary>
            public int warningCount;

            /// <summary>Whether the program is complete and correct.</summary>
            public bool succeeded => program != null && errorCount == 0;

            /// <summary>Destroy every object this bake created. Validation-only bakes MUST call this:
            /// <see cref="ScriptableObject.CreateInstance(Type)"/> objects live until domain reload
            /// otherwise, and OnGraphChanged fires on every keystroke.</summary>
            public void DestroyObjects()
            {
                for (int i = 0; i < subAssets.Count; i++)
                    DestroyObject(subAssets[i].asset);
                subAssets.Clear();
                DestroyObject(program);
                program = null;
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

        /// <summary>Adapter putting bake diagnostics on the graph's own nodes. Used from
        /// <see cref="TaskGraph.OnGraphChanged"/> through <see cref="Validate"/>.</summary>
        private sealed class GraphLoggerBakeLog : StateTreeGraphBaker.IBakeLog
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
        /// unavailable outside <c>OnGraphChanged</c> (the <see cref="GraphLogger"/> instance is created
        /// and owned by the framework), so the node is named in the message text and the ASSET is the
        /// Unity context object, which makes the console line click-to-ping.</summary>
        private sealed class ConsoleBakeLog : StateTreeGraphBaker.IBakeLog
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
                string node = nodeContext is INode named ? Describe(named) : string.Empty;
                return node.Length == 0
                    ? $"Task graph bake ({m_AssetPath}): {message}"
                    : $"Task graph bake ({m_AssetPath}) [{node}]: {message}";
            }
        }

        /// <summary>Console-reporting log for callers outside this file (the importer, the menu).</summary>
        /// <param name="assetPath">Path named in every message.</param>
        /// <param name="context">Object the console line pings, or null.</param>
        /// <returns>A diagnostics sink that writes to the console.</returns>
        public static StateTreeGraphBaker.IBakeLog CreateConsoleLog(string assetPath, UnityEngine.Object context)
            => new ConsoleBakeLog(assetPath, context);

        // ------------------------------------------------------------------ public entry points

        /// <summary>
        /// Walk <paramref name="graph"/> and build the runtime program in memory. Never touches the
        /// AssetDatabase, never logs by itself — every diagnostic goes through <paramref name="log"/>
        /// so the caller controls whether it lands on a node marker or in the console.
        /// </summary>
        /// <param name="graph">The authored graph. Any <see cref="Graph"/> subclass is accepted; the
        /// walk reads node classes, not the concrete graph type.</param>
        /// <param name="log">Diagnostics sink. May be null (diagnostics are then dropped).</param>
        /// <returns>The program, its sub-assets and the diagnostic counts.</returns>
        public static BakeResult Bake(Graph graph, StateTreeGraphBaker.IBakeLog log)
        {
            var result = new BakeResult();
            if (graph == null)
            {
                log?.Error("No graph to bake.", null);
                result.errorCount++;
                return result;
            }

            var context = new BakeContext(result, log);
            Collect(context, graph);

            // The pin pass may APPEND constant instructions, so the loop bound is captured first:
            // appended constants have no pins of their own and need no visit.
            int authored = context.program.Count;
            for (int i = 0; i < authored; i++)
                Resolve(context, i);

            var program = ScriptableObject.CreateInstance<GraphTaskAsset>();
            program.name = ReadProgramName(graph);
            program.nodes = context.program;
            program.enterEntry = ResolveEntry(context, graph, typeof(OnEnterNode));
            program.tickEntry = ResolveEntry(context, graph, typeof(OnTickNode));
            program.exitEntry = ResolveEntry(context, graph, typeof(OnExitNode));
            result.program = program;

            Diagnose(context, program);
            return result;
        }

        /// <summary>
        /// Validation-only bake, for <see cref="TaskGraph.OnGraphChanged"/> to call in one line.
        /// Running the REAL bake is the point: a graph that shows no markers is a graph that bakes,
        /// with no second implementation to drift. Creates and destroys a throwaway program.
        /// </summary>
        /// <param name="graph">The graph being edited.</param>
        /// <param name="logger">The logger Graph Toolkit handed to OnGraphChanged.</param>
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
        /// <c>&lt;Graph&gt;_Baked.asset</c> beside the graph.
        ///
        /// This mirrors <see cref="StateTreeGraphBaker.Bake(Graph, string)"/>, which is the signature
        /// the main editor assembly's bridge discovers by reflection, so the same discovery pattern
        /// works for task graphs.
        /// </summary>
        /// <param name="graph">The authored graph.</param>
        /// <param name="bakedAssetPath">Where to write it, or null for the default path.</param>
        /// <returns>The persisted program, or null when the graph did not bake cleanly.</returns>
        public static GraphTaskAsset Bake(Graph graph, string bakedAssetPath)
        {
            if (graph == null)
            {
                Debug.LogError("Task graph bake: no graph to bake.");
                return null;
            }

            string graphPath = SafeGraphAssetPath(graph);
            if (string.IsNullOrEmpty(bakedAssetPath))
            {
                if (string.IsNullOrEmpty(graphPath))
                {
                    Debug.LogError("Task graph bake: the graph has no asset path, so there is nowhere "
                        + "to write the baked program. Pass an explicit path.");
                    return null;
                }
                bakedAssetPath = GetBakedAssetPath(graphPath);
            }

            StateTreeGraphBaker.IBakeLog log = CreateConsoleLog(
                string.IsNullOrEmpty(graphPath) ? bakedAssetPath : graphPath, null);
            BakeResult result = Bake(graph, log);
            if (!result.succeeded)
            {
                Debug.LogError($"Task graph bake ({bakedAssetPath}): {result.errorCount} error(s); "
                    + "no asset written.");
                result.DestroyObjects();
                return null;
            }

            AssetDatabase.CreateAsset(result.program, bakedAssetPath);
            for (int i = 0; i < result.subAssets.Count; i++)
                AssetDatabase.AddObjectToAsset(result.subAssets[i].asset, result.program);
            EditorUtility.SetDirty(result.program);
            AssetDatabase.SaveAssets();
            return result.program;
        }

        /// <summary>
        /// Bake the selected graph asset to a STANDALONE <c>&lt;Graph&gt;_Baked.asset</c> next to it.
        /// The importer already keeps a baked program inside every graph file, so this menu exists
        /// for the cases where a separate file is wanted: diffing a bake, or holding a program that
        /// must survive the graph being deleted.
        /// </summary>
        [MenuItem("Tools/Draw To Play/Bake Task Graph")]
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

            GraphTaskAsset program = Bake(graph, GetBakedAssetPath(path));
            if (program == null)
                return;

            Selection.activeObject = program;
            EditorGUIUtility.PingObject(program);
        }

        [MenuItem("Tools/Draw To Play/Bake Task Graph", true)]
        private static bool ValidateBakeSelectedGraph()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path)
                && path.EndsWith("." + GraphExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>Assets/…/Patrol.taskgraph</c> → <c>Assets/…/Patrol_Baked.asset</c>.</summary>
        /// <param name="graphAssetPath">Path of the graph file.</param>
        /// <returns>Path of the standalone baked asset.</returns>
        public static string GetBakedAssetPath(string graphAssetPath)
        {
            string directory = System.IO.Path.GetDirectoryName(graphAssetPath) ?? string.Empty;
            string file = System.IO.Path.GetFileNameWithoutExtension(graphAssetPath) + BakedAssetSuffix + ".asset";
            return string.IsNullOrEmpty(directory) ? file : directory.Replace('\\', '/') + "/" + file;
        }

        /// <summary>Load a task graph by asset path without naming the concrete graph class:
        /// <see cref="GraphDatabase.LoadGraph{T}"/>'s constraint is satisfied by <see cref="Graph"/>
        /// itself and the extension registry resolves the real type. Returns null (never throws) when
        /// the path is not a registered task graph.</summary>
        /// <param name="assetPath">Path to load.</param>
        /// <returns>The graph, or null.</returns>
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
                Debug.LogWarning($"Could not load task graph '{assetPath}': {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------ the walk

        /// <summary>Scratch state of one bake: the instructions built so far, the graph node each one
        /// came from, and the index every wire resolves through.</summary>
        private sealed class BakeContext
        {
            public readonly List<GraphTaskNode> program = new List<GraphTaskNode>();
            public readonly List<INode> sources = new List<INode>();
            public readonly Dictionary<INode, int> indexByNode =
                new Dictionary<INode, int>(NodeIdentity.comparer);
            public readonly BakeResult result;

            private readonly StateTreeGraphBaker.IBakeLog m_Log;

            public BakeContext(BakeResult result, StateTreeGraphBaker.IBakeLog log)
            {
                this.result = result;
                m_Log = log;
            }

            public void Error(string message, INode node)
            {
                m_Log?.Error(message, node);
                result.errorCount++;
            }

            public void Warning(string message, INode node)
            {
                m_Log?.Warning(message, node);
                result.warningCount++;
            }
        }

        /// <summary>Graph nodes are compared by IDENTITY: two nodes of the same class with the same
        /// values are still two instructions.</summary>
        private sealed class NodeIdentity : IEqualityComparer<INode>
        {
            public static readonly IEqualityComparer<INode> comparer = new NodeIdentity();

            public bool Equals(INode a, INode b) => ReferenceEquals(a, b);

            public int GetHashCode(INode node) => System.Runtime.CompilerServices
                .RuntimeHelpers.GetHashCode(node);
        }

        /// <summary>Pass one: every <see cref="ITaskGraphNode"/> becomes an instruction, in graph
        /// order, so that pass two can resolve a wire to an index that already exists.</summary>
        private static void Collect(BakeContext context, Graph graph)
        {
            foreach (INode node in graph.GetNodes())
            {
                if (node is ITaskGraphNode instruction)
                {
                    context.indexByNode[node] = context.program.Count;
                    context.sources.Add(node);
                    context.program.Add(NewInstruction(instruction.nodeKind));
                    continue;
                }

                // Entry nodes are not instructions (they name where a chain starts), and Graph
                // Toolkit's own constant and variable nodes are read as literals wherever they are
                // wired. Anything else is something this graph cannot execute.
                if (node is OnEnterNode || node is OnTickNode || node is OnExitNode)
                    continue;
                if (node is IConstantNode || node is IVariableNode)
                    continue;

                context.Warning($"'{Describe(node)}' is not a task-graph instruction, so the bake "
                    + "ignores it. Delete it, or replace it with a node from the task graph palette.",
                    node);
            }
        }

        /// <summary>An instruction with its wire slots allocated and every one of them UNWIRED. The
        /// -1 fill is load-bearing: a fresh <c>int[]</c> is full of zeros, and zero is a valid
        /// instruction index.</summary>
        private static GraphTaskNode NewInstruction(GraphTaskNodeKind kind)
        {
            return new GraphTaskNode
            {
                kind = kind,
                stringValue = string.Empty,
                stringValue2 = string.Empty,
                exec = Unwired(ExecPinCount(kind)),
                data = Unwired(DataPinCount(kind))
            };
        }

        private static int[] Unwired(int count)
        {
            var slots = new int[count];
            for (int i = 0; i < count; i++)
                slots[i] = -1;
            return slots;
        }

        private static int ExecPinCount(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.Branch:
                case GraphTaskNodeKind.DoTask:
                    return 2;
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetBlackboardString:
                case GraphTaskNodeKind.Wait:
                case GraphTaskNodeKind.FireCue:
                    return 1;
                default:
                    return 0;
            }
        }

        private static int DataPinCount(GraphTaskNodeKind kind)
        {
            switch (kind)
            {
                case GraphTaskNodeKind.CompareFloat:
                case GraphTaskNodeKind.BoolAnd:
                case GraphTaskNodeKind.BoolOr:
                    return 2;
                case GraphTaskNodeKind.Branch:
                case GraphTaskNodeKind.SetBlackboardFloat:
                case GraphTaskNodeKind.SetBlackboardString:
                case GraphTaskNodeKind.Wait:
                case GraphTaskNodeKind.BoolNot:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Pass two, and THE CONTRACT: which port of which node class fills which slot of which
        /// instruction. Every line here has a counterpart in the interpreter; nothing else in this
        /// assembly knows the encoding.
        /// </summary>
        private static void Resolve(BakeContext context, int index)
        {
            INode source = context.sources[index];
            GraphTaskNode node = context.program[index];

            switch (node.kind)
            {
                case GraphTaskNodeKind.Branch:
                    node.data[0] = ResolveData(context, source, BranchNode.ConditionPortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.TrueExecPortName);
                    node.exec[1] = ResolveExec(context, source, TaskGraphPorts.FalseExecPortName);
                    break;

                case GraphTaskNodeKind.SetBlackboardFloat:
                    node.stringValue = ReadKey(context, source, SetBlackboardFloatNode.KeyPortName);
                    node.data[0] = ResolveData(context, source, SetBlackboardFloatNode.ValuePortName,
                        typeof(float), LiteralSlot.Float, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.SetBlackboardString:
                    node.stringValue = ReadKey(context, source, SetBlackboardStringNode.KeyPortName);
                    node.data[0] = ResolveData(context, source, SetBlackboardStringNode.ValuePortName,
                        typeof(string), LiteralSlot.SecondString, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.DoTask:
                    ConfigureTask(context, source, index, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.SuccessExecPortName);
                    node.exec[1] = ResolveExec(context, source, TaskGraphPorts.FailureExecPortName);
                    break;

                case GraphTaskNodeKind.Wait:
                    node.data[0] = ResolveData(context, source, WaitNode.SecondsPortName,
                        typeof(float), LiteralSlot.Float, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.ReturnSuccess:
                case GraphTaskNodeKind.ReturnFailure:
                case GraphTaskNodeKind.ReturnRunning:
                    break;

                case GraphTaskNodeKind.FireCue:
                    node.stringValue = ReadConstantString(context, source, FireCueNode.CueNamePortName);
                    if (string.IsNullOrEmpty(node.stringValue))
                    {
                        context.Error("Fire Cue has no cue name, so it would emit an unnamed cue that "
                            + "no listener can match.", source);
                    }
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.ConstFloat:
                    node.floatValue = ReadConstantFloat(context, source, ConstFloatNode.ValuePortName);
                    break;

                case GraphTaskNodeKind.ConstString:
                    node.stringValue = ReadConstantString(context, source, ConstStringNode.ValuePortName);
                    break;

                case GraphTaskNodeKind.ConstBool:
                    node.floatValue = ReadConstantBool(context, source, ConstBoolNode.ValuePortName) ? 1f : 0f;
                    break;

                case GraphTaskNodeKind.GetBlackboardFloat:
                    node.stringValue = ReadKey(context, source, GetBlackboardFloatNode.KeyPortName);
                    break;

                case GraphTaskNodeKind.GetBlackboardString:
                    node.stringValue = ReadKey(context, source, GetBlackboardStringNode.KeyPortName);
                    break;

                case GraphTaskNodeKind.HasBlackboardKey:
                    node.stringValue = ReadKey(context, source, HasBlackboardKeyNode.KeyPortName);
                    break;

                case GraphTaskNodeKind.EvaluateCondition:
                    ConfigureCondition(context, source, index, ref node);
                    break;

                case GraphTaskNodeKind.CompareFloat:
                    node.data[0] = ResolveData(context, source, CompareFloatNode.LeftPortName,
                        typeof(float), LiteralSlot.None, ref node);
                    node.data[1] = ResolveData(context, source, CompareFloatNode.RightPortName,
                        typeof(float), LiteralSlot.Float, ref node);
                    node.stringValue = CompareFloatNode.ToOperator(ReadOp(context, source));
                    break;

                case GraphTaskNodeKind.BoolAnd:
                    node.data[0] = ResolveData(context, source, BoolAndNode.LeftPortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    node.data[1] = ResolveData(context, source, BoolAndNode.RightPortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    break;

                case GraphTaskNodeKind.BoolOr:
                    node.data[0] = ResolveData(context, source, BoolOrNode.LeftPortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    node.data[1] = ResolveData(context, source, BoolOrNode.RightPortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    break;

                case GraphTaskNodeKind.BoolNot:
                    node.data[0] = ResolveData(context, source, BoolNotNode.ValuePortName,
                        typeof(bool), LiteralSlot.None, ref node);
                    break;

                case GraphTaskNodeKind.ExitStatus:
                    break;

                default:
                    context.Error($"'{Describe(source)}' bakes to {node.kind}, which this baker does "
                        + "not know how to wire. The node class and the baker are out of step.", source);
                    break;
            }

            // Written back so that a value-type GraphTaskNode would survive the same code path a
            // reference type does. Cheap, and it removes a whole class of "why did my edit vanish".
            context.program[index] = node;
        }

        /// <summary>Where a data pin's typed-in literal goes when nothing is wired into it. Only the
        /// pins listed in the class header have a slot; the rest get an appended constant
        /// instruction.</summary>
        private enum LiteralSlot
        {
            /// <summary>No slot: a non-default literal becomes an appended constant.</summary>
            None,

            /// <summary>The instruction's own <c>floatValue</c>.</summary>
            Float,

            /// <summary>The instruction's own <c>stringValue2</c> (<c>stringValue</c> holds the
            /// key).</summary>
            SecondString
        }

        // ------------------------------------------------------------------ pins

        /// <summary>
        /// Resolve one outgoing exec pin to the index of the instruction it runs, or -1 for "end of
        /// chain". An unwired pin is NOT an error — a Branch with only its true side wired is a
        /// perfectly ordinary early-out.
        /// </summary>
        private static int ResolveExec(BakeContext context, INode owner, string portName)
        {
            IPort port = owner.GetOutputPortByName(portName);
            if (port == null)
            {
                context.Error($"'{Describe(owner)}' has no '{portName}' exec pin. The node class and "
                    + "the baker are out of step.", owner);
                return -1;
            }

            if (!port.IsConnected)
                return -1;

            var connected = new List<IPort>();
            port.GetConnectedPorts(connected);

            int target = -1;
            int accepted = 0;
            for (int i = 0; i < connected.Count; i++)
            {
                INode next = connected[i]?.GetNode();
                if (next == null)
                    continue;

                if (next is ITaskGraphNode instruction
                    && context.indexByNode.TryGetValue(next, out int index)
                    && TaskGraphPorts.IsExecKind(instruction.nodeKind))
                {
                    accepted++;
                    if (target < 0)
                        target = index;
                    continue;
                }

                context.Error($"The '{portName}' pin of '{Describe(owner)}' runs "
                    + $"'{Describe(next)}', which is not something a chain can run. Exec wires go to "
                    + "instructions; values are wired into a node's value pins instead.", owner);
            }

            if (accepted > 1)
            {
                context.Error($"The '{portName}' pin of '{Describe(owner)}' runs {accepted} "
                    + "instructions. A pin runs ONE — the first wire is baked and the rest are "
                    + "dropped. Chain them, or branch.", owner);
            }

            return target;
        }

        /// <summary>
        /// Resolve one incoming data pin to the index of the instruction that produces its value, or
        /// -1 when the value is a literal (which this method also stores, in the instruction's own
        /// slot or in an appended constant — see the class header).
        /// </summary>
        private static int ResolveData(BakeContext context, INode owner, string portName,
            Type valueType, LiteralSlot slot, ref GraphTaskNode node)
        {
            IPort port = owner.GetInputPortByName(portName);
            if (port == null)
            {
                context.Error($"'{Describe(owner)}' has no '{portName}' value pin. The node class and "
                    + "the baker are out of step.", owner);
                return -1;
            }

            if (port.IsConnected)
            {
                INode producer = port.FirstConnectedPort?.GetNode();
                if (producer is ITaskGraphNode instruction
                    && context.indexByNode.TryGetValue(producer, out int index))
                {
                    if (TaskGraphPorts.IsDataKind(instruction.nodeKind))
                        return index;

                    context.Error($"The '{portName}' pin of '{Describe(owner)}' is wired to "
                        + $"'{Describe(producer)}', which produces no value — it is an instruction, "
                        + "not a value. Wire it into an exec pin instead.", owner);
                    return -1;
                }

                if (producer is IVariableNode variableNode)
                {
                    WarnFlattenedVariable(context, owner, portName, variableNode);
                }
                else if (!(producer is IConstantNode))
                {
                    context.Error($"The '{portName}' pin of '{Describe(owner)}' is wired to "
                        + $"'{Describe(producer)}', which this bake cannot read a value from.", owner);
                    return -1;
                }
            }

            if (!LibraryParameterPorts.TryReadValue(port, valueType, out object value))
                value = TaskGraphPorts.DefaultOf(valueType);

            return StoreLiteral(context, valueType, value, slot, ref node);
        }

        /// <summary>Put a literal where the instruction can carry it. Values equal to the type's
        /// default are dropped: the interpreter reads an unwired pin as exactly that, so storing it
        /// would only make the program bigger.</summary>
        private static int StoreLiteral(BakeContext context, Type valueType, object value,
            LiteralSlot slot, ref GraphTaskNode node)
        {
            switch (slot)
            {
                case LiteralSlot.Float:
                    node.floatValue = ToFloat(value);
                    return -1;

                case LiteralSlot.SecondString:
                    node.stringValue2 = value as string ?? string.Empty;
                    return -1;

                default:
                    if (IsDefault(valueType, value))
                        return -1;
                    return AppendConstant(context, valueType, value);
            }
        }

        /// <summary>Append the constant instruction that carries a literal the instruction itself has
        /// no slot for, and hand back its index. Appended in pin-visit order, which is deterministic,
        /// and never visited by <see cref="Resolve"/> because it has no pins of its own.</summary>
        private static int AppendConstant(BakeContext context, Type valueType, object value)
        {
            GraphTaskNode constant;
            if (valueType == typeof(bool))
            {
                constant = NewInstruction(GraphTaskNodeKind.ConstBool);
                constant.floatValue = value is bool flag && flag ? 1f : 0f;
            }
            else if (valueType == typeof(string))
            {
                constant = NewInstruction(GraphTaskNodeKind.ConstString);
                constant.stringValue = value as string ?? string.Empty;
            }
            else
            {
                constant = NewInstruction(GraphTaskNodeKind.ConstFloat);
                constant.floatValue = ToFloat(value);
            }

            int index = context.program.Count;
            context.program.Add(constant);
            // sources stays in step so that every diagnostic can still name a node; an appended
            // constant belongs to no node, which is what null means here.
            context.sources.Add(null);
            return index;
        }

        // ------------------------------------------------------------------ constants on ports

        /// <summary>A blackboard key: a constant string, required, and never a wire (the program
        /// model has no computed-key form — see <see cref="SetBlackboardFloatNode"/>).</summary>
        private static string ReadKey(BakeContext context, INode owner, string portName)
        {
            string key = ReadConstantString(context, owner, portName);
            if (string.IsNullOrEmpty(key))
            {
                context.Error($"'{Describe(owner)}' has no blackboard key, so it would read or write "
                    + "an unnamed slot. Type the key into its Key field.", owner);
            }
            return key;
        }

        private static string ReadConstantString(BakeContext context, INode owner, string portName)
            => ReadConstant(context, owner, portName, typeof(string)) as string ?? string.Empty;

        private static float ReadConstantFloat(BakeContext context, INode owner, string portName)
            => ToFloat(ReadConstant(context, owner, portName, typeof(float)));

        private static bool ReadConstantBool(BakeContext context, INode owner, string portName)
            => ReadConstant(context, owner, portName, typeof(bool)) is bool flag && flag;

        private static CompareFloatNode.Op ReadOp(BakeContext context, INode owner)
        {
            object raw = ReadConstant(context, owner, CompareFloatNode.OpPortName,
                typeof(CompareFloatNode.Op));
            return raw is CompareFloatNode.Op op ? op : CompareFloatNode.Op.GreaterOrEqual;
        }

        /// <summary>
        /// Read a port whose value is BAKED IN rather than pulled at runtime. Wiring an instruction
        /// into one of these is an error rather than a silent fallback to the typed-in value: the
        /// author asked for something the program model cannot express, and the honest answer is to
        /// say so.
        /// </summary>
        private static object ReadConstant(BakeContext context, INode owner, string portName, Type valueType)
        {
            IPort port = owner.GetInputPortByName(portName);
            if (port == null)
            {
                context.Error($"'{Describe(owner)}' has no '{portName}' field. The node class and the "
                    + "baker are out of step.", owner);
                return null;
            }

            if (port.IsConnected)
            {
                INode producer = port.FirstConnectedPort?.GetNode();
                if (producer is ITaskGraphNode)
                {
                    context.Error($"The '{portName}' of '{Describe(owner)}' is wired to "
                        + $"'{Describe(producer)}', but it is baked as a CONSTANT and the wire is "
                        + "ignored. Type the value in, or keep the value on the blackboard.", owner);
                    return null;
                }
                if (producer is IVariableNode variableNode)
                    WarnFlattenedVariable(context, owner, portName, variableNode);
            }

            return LibraryParameterPorts.TryReadValue(port, valueType, out object value) ? value : null;
        }

        private static void WarnFlattenedVariable(BakeContext context, INode owner, string portName,
            IVariableNode variableNode)
        {
            string name = SafeVariableName(variableNode);
            context.Warning($"Graph variable '{name}' feeds the '{portName}' of "
                + $"'{Describe(owner)}'. It is baked as a CONSTANT taken from the variable's default "
                + "value — a task graph has no live variable storage. Use the blackboard nodes for a "
                + "value that has to change while the task runs.", owner);
        }

        private static string SafeVariableName(IVariableNode variableNode)
        {
            try
            {
                IVariable variable = variableNode?.Variable;
                return variable != null && !string.IsNullOrEmpty(variable.Name) ? variable.Name : "(unnamed)";
            }
            catch (Exception)
            {
                return "(unnamed)";
            }
        }

        // ------------------------------------------------------------------ library sub-assets

        /// <summary>Create the configured task instance a call node bakes into, and register it as a
        /// sub-asset of the imported file. Each call node gets its OWN copy, so two Chase nodes with
        /// different speeds never share instance state.</summary>
        private static void ConfigureTask(BakeContext context, INode source, int index, ref GraphTaskNode node)
        {
            Type taskType = (source as TaskCallNode)?.taskType;
            if (taskType == null || !typeof(StateTreeTaskAsset).IsAssignableFrom(taskType) || taskType.IsAbstract)
            {
                context.Error($"'{Describe(source)}' does not name a task in "
                    + "Runtime/StateTree/Library, so there is nothing for it to call.", source);
                return;
            }

            var task = (StateTreeTaskAsset)ScriptableObject.CreateInstance(taskType);
            task.name = $"Task {index.ToString(CultureInfo.InvariantCulture)} {taskType.Name}";
            ApplyParameters(context, source, task, taskType);
            node.task = task;
            context.result.subAssets.Add(new StateTreeGraphBaker.SubAsset(task,
                "Task:" + index.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>Create the configured condition instance a value node bakes into, and register it
        /// as a sub-asset.</summary>
        private static void ConfigureCondition(BakeContext context, INode source, int index, ref GraphTaskNode node)
        {
            Type conditionType = (source as ConditionValueNode)?.conditionType;
            if (conditionType == null || !typeof(StateTreeConditionAsset).IsAssignableFrom(conditionType)
                || conditionType.IsAbstract)
            {
                context.Error($"'{Describe(source)}' does not name a condition in "
                    + "Runtime/StateTree/Library, so it would always read true.", source);
                return;
            }

            var condition = (StateTreeConditionAsset)ScriptableObject.CreateInstance(conditionType);
            condition.name = $"Cond {index.ToString(CultureInfo.InvariantCulture)} {conditionType.Name}";
            ApplyParameters(context, source, condition, conditionType);
            node.condition = condition;
            context.result.subAssets.Add(new StateTreeGraphBaker.SubAsset(condition,
                "Cond:" + index.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Copy the parameter ports of a call/value node onto the library instance it configures. The
        /// field list comes from <see cref="LibraryParameterPorts.GetParameterFields"/> — the same
        /// call the node used to DECLARE those ports — so the two cannot drift and adding a library
        /// task costs a wrapper class and no baker change.
        /// </summary>
        private static void ApplyParameters(BakeContext context, INode source, ScriptableObject target,
            Type libraryType)
        {
            IReadOnlyList<FieldInfo> fields = LibraryParameterPorts.GetParameterFields(libraryType);
            for (int i = 0; i < fields.Count; i++)
            {
                FieldInfo field = fields[i];
                IPort port = source.GetInputPortByName(field.Name);
                if (port == null)
                    continue;

                if (port.IsConnected)
                {
                    INode producer = port.FirstConnectedPort?.GetNode();
                    if (producer is ITaskGraphNode)
                    {
                        context.Error($"'{field.Name}' on '{Describe(source)}' is wired to "
                            + $"'{Describe(producer)}', but a library call's parameters are baked as "
                            + "values, not pulled each tick — the wire would be silently ignored. Type "
                            + "the value in, or put it on the blackboard and turn on the task's "
                            + "blackboard option where it has one.", source);
                        continue;
                    }
                    if (producer is IVariableNode variableNode)
                        WarnFlattenedVariable(context, source, field.Name, variableNode);
                }

                if (!LibraryParameterPorts.TryReadValue(port, field.FieldType, out object value))
                    continue;

                try
                {
                    field.SetValue(target, value);
                }
                catch (Exception e)
                {
                    context.Error($"Could not write '{field.Name}' on {libraryType.Name}: {e.Message}",
                        source);
                }
            }
        }

        // ------------------------------------------------------------------ entries and diagnostics

        /// <summary>The instruction one lifecycle chain starts at. More than one entry node of a kind
        /// is reported by <see cref="TaskGraph.OnGraphChanged"/> — where it can be fixed — and the
        /// first in graph order wins here, silently, so the same complaint is not made twice.</summary>
        private static int ResolveEntry(BakeContext context, Graph graph, Type entryNodeType)
        {
            foreach (INode node in graph.GetNodes())
            {
                if (entryNodeType.IsInstanceOfType(node))
                    return ResolveExec(context, node, TaskGraphPorts.ExecOutPortName);
            }
            return -1;
        }

        /// <summary>
        /// Everything that is legal to bake but almost certainly not what the author meant. All
        /// warnings except the latent one, which the runtime treats as an error and steps past, so
        /// saying it here — where the fix is — is the whole point of saying it at all.
        /// </summary>
        private static void Diagnose(BakeContext context, GraphTaskAsset program)
        {
            List<GraphTaskNode> nodes = context.program;

            var enterChain = new HashSet<int>();
            var tickChain = new HashSet<int>();
            var exitChain = new HashSet<int>();
            WalkExec(nodes, program.enterEntry, enterChain);
            WalkExec(nodes, program.tickEntry, tickChain);
            WalkExec(nodes, program.exitEntry, exitChain);

            var exitPulled = new HashSet<int>();
            var allPulled = new HashSet<int>();
            CollectPulled(nodes, exitChain, exitPulled);
            CollectPulled(nodes, enterChain, allPulled);
            CollectPulled(nodes, tickChain, allPulled);
            foreach (int pulled in exitPulled)
                allPulled.Add(pulled);

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphTaskNode node = nodes[i];
                INode source = context.sources[i];
                if (source == null)
                    continue; // an appended constant: it exists because a pin asked for it.

                if (TaskGraphPorts.IsDataKind(node.kind))
                {
                    if (!allPulled.Contains(i))
                    {
                        context.Warning($"Nothing reads '{Describe(source)}', so it never runs. Wire "
                            + "its result into a pin, or delete it.", source);
                    }
                    else if (node.kind == GraphTaskNodeKind.ExitStatus && !exitPulled.Contains(i))
                    {
                        context.Warning($"'{Describe(source)}' is read outside the On Exit chain, "
                            + "where there is no exit status: it reads 0 (Success) every time.", source);
                    }
                    continue;
                }

                bool reached = enterChain.Contains(i) || tickChain.Contains(i) || exitChain.Contains(i);
                if (!reached)
                {
                    context.Warning($"Nothing runs '{Describe(source)}'. Wire an exec pin into it, or "
                        + "delete it.", source);
                    continue;
                }

                bool lifecycleOnly = !tickChain.Contains(i);
                if (!lifecycleOnly)
                    continue;

                if (TaskGraphPorts.IsLatentKind(node.kind))
                {
                    context.Error($"'{Describe(source)}' can wait, and the On Enter / On Exit chains "
                        + "run to completion inside one call. The runtime steps straight past it with "
                        + "an error. Move it to the On Tick chain.", source);
                }
                else if (TaskGraphPorts.IsReturnKind(node.kind))
                {
                    context.Warning($"'{Describe(source)}' is in the On Enter / On Exit chain, which "
                        + "has no status to return: it ends the chain and nothing else.", source);
                }
            }
        }

        /// <summary>Every instruction an exec chain can reach from <paramref name="entry"/>. Iterative
        /// and visited-guarded, so a loop in the graph — which is legal and useful — terminates.</summary>
        private static void WalkExec(List<GraphTaskNode> nodes, int entry, HashSet<int> reached)
        {
            if (entry < 0 || entry >= nodes.Count)
                return;

            var pending = new Stack<int>();
            pending.Push(entry);
            reached.Add(entry);

            while (pending.Count > 0)
            {
                GraphTaskNode node = nodes[pending.Pop()];
                int[] exec = node.exec;
                if (exec == null)
                    continue;
                for (int i = 0; i < exec.Length; i++)
                {
                    int next = exec[i];
                    if (next >= 0 && next < nodes.Count && reached.Add(next))
                        pending.Push(next);
                }
            }
        }

        /// <summary>Every data instruction the given exec instructions pull, transitively.</summary>
        private static void CollectPulled(List<GraphTaskNode> nodes, HashSet<int> execNodes, HashSet<int> pulled)
        {
            foreach (int index in execNodes)
                CollectPulled(nodes, index, pulled, 0);
        }

        private static void CollectPulled(List<GraphTaskNode> nodes, int index, HashSet<int> pulled, int depth)
        {
            if (index < 0 || index >= nodes.Count || depth > k_DataWalkDepth)
                return;

            int[] data = nodes[index].data;
            if (data == null)
                return;

            for (int i = 0; i < data.Length; i++)
            {
                int source = data[i];
                if (source < 0 || source >= nodes.Count)
                    continue;
                if (pulled.Add(source))
                    CollectPulled(nodes, source, pulled, depth + 1);
            }
        }

        // ------------------------------------------------------------------ small helpers

        /// <summary>The program's asset name. The FILE names the task — that is what the picker, the
        /// inspector and the project window all show — so the graph's own name is only a
        /// fallback.</summary>
        private static string ReadProgramName(Graph graph)
        {
            string path = SafeGraphAssetPath(graph);
            if (!string.IsNullOrEmpty(path))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(file))
                    return file;
            }

            try
            {
                if (!string.IsNullOrEmpty(graph.Name))
                    return graph.Name;
            }
            catch (Exception)
            {
                // A graph with no backing file answers with an exception rather than an empty string.
            }

            return "TaskGraph";
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

        /// <summary>A node's name for a diagnostic: its canvas title when it has one, else its class
        /// name. Titles come from the node implementation and can throw on a half-built node, which
        /// must never be what takes down a bake.</summary>
        private static string Describe(INode node)
        {
            if (node == null)
                return "nothing";
            try
            {
                if (!string.IsNullOrEmpty(node.Title))
                    return node.Title;
            }
            catch (Exception)
            {
                // Fall through to the type name.
            }
            return node.GetType().Name;
        }

        private static float ToFloat(object value)
        {
            switch (value)
            {
                case float f: return f;
                case double d: return (float)d;
                case int i: return i;
                case bool b: return b ? 1f : 0f;
                default:
                    return value is IConvertible convertible
                        ? Convert.ToSingle(convertible, CultureInfo.InvariantCulture)
                        : 0f;
            }
        }

        private static bool IsDefault(Type valueType, object value)
        {
            if (value == null)
                return true;
            if (valueType == typeof(bool))
                return value is bool flag && !flag;
            if (valueType == typeof(string))
                return string.IsNullOrEmpty(value as string);
            return Mathf.Approximately(ToFloat(value), 0f);
        }
    }
}
