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
    /// <item>SetOutputFloat — <c>stringValue</c> output NAME, <c>data[0]</c> value or
    /// <c>floatValue</c>; <c>exec[0]</c> next.</item>
    /// <item>SetOutputString — <c>stringValue</c> output NAME, <c>data[0]</c> value or
    /// <c>stringValue2</c>; <c>exec[0]</c> next.</item>
    /// <item>SetOutputBool — <c>stringValue</c> output NAME, <c>data[0]</c> value or
    /// <c>floatValue</c> (1/0); <c>exec[0]</c> next.</item>
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
    /// <item>GetParamFloat / GetParamString / GetParamBool — <c>stringValue</c> parameter name.</item>
    /// </list>
    ///
    /// ====================================================================================
    /// FOUR THINGS THIS BAKE DOES THAT ARE NOT MECHANICAL
    /// ====================================================================================
    /// 1. LITERALS THAT WOULD OTHERWISE BE LOST GET A CONSTANT INSTRUCTION. Only the Set nodes' value
    ///    pins, a Wait's seconds and a Compare's right-hand side have a literal slot in the program
    ///    model. Type <c>true</c> into a Branch's condition, or a number into a Compare's
    ///    LEFT side, and there is nowhere in the instruction to put it — so the bake appends a
    ///    ConstBool/ConstFloat instruction and wires the pin to it. The author's value survives, the
    ///    interpreter needs no special case, and the appended instructions are deterministic because
    ///    they are created in the order the pins are visited. A literal equal to the type's default
    ///    is skipped: "unwired" and "false" mean the same thing to the interpreter.
    /// 2. GRAPH VARIABLES ARE PARAMETERS, AND THE STATE THAT RUNS THE TASK OVERRIDES THEM. Every
    ///    float/string/bool variable in Graph Toolkit's variable panel bakes into
    ///    <c>GraphTaskAsset.parameters</c> carrying its DEFAULT value, and a variable NODE on the
    ///    canvas bakes to a GetParam pull naming it — so the number typed in the graph is what runs
    ///    unless the state overrides it, which is the UE Blueprint-instance model. Wiring the SAME
    ///    variable into three Waits and re-tuning all three from one inspector field is the point.
    ///    Variables of any other type keep the old behaviour (flattened to a constant, with a warning
    ///    naming the variable), because the parameter model has three kinds and no fourth.
    ///    Each parameter also carries an ID, which is what a state's override binds to — see
    ///    PARAMETER IDENTITY below.
    /// 3. LIBRARY PARAMETERS ARE CONSTANTS, AND A WIRE INTO ONE IS AN ERROR. A task call bakes a
    ///    CONFIGURED COPY of the library task, so its parameters are baked values, not pins. Wiring a
    ///    computed number into a Chase node's <c>moveSpeed</c> would silently bake the typed-in value
    ///    instead; the bake refuses rather than lying. A graph VARIABLE is the one thing accepted
    ///    there, and it takes the BAKE-TIME route: the embedded sub-asset's field gets the variable's
    ///    default and a per-state override never reaches it. That is a real difference from every
    ///    other use of the same variable, so the bake says which route was taken, once per use, as a
    ///    canvas note. (The blackboard is the way to drive a library task from graph logic: several of
    ///    them read one — <c>useBlackboardSpeed</c>, <c>useBlackboardRange</c>.)
    /// 4. THE SET OUTPUT INSTRUCTIONS ARE ALSO THE DECLARATION OF WHAT THIS TASK RETURNS. There is no
    ///    output panel to fill in and no second list to keep in step: a Set Output node names an
    ///    output, and the bake collects the distinct names it finds into
    ///    <c>GraphTaskAsset.declaredOutputs</c> so the transition inspector can offer them as a
    ///    dropdown. Distinct BY NAME — setting <c>result</c> on both sides of a branch is one return
    ///    value written from two paths — and the first instruction's type wins if two disagree, with a
    ///    warning, because a name is one contract. The list is display-only; the runtime routes by the
    ///    names the instructions actually wrote.
    ///
    /// ====================================================================================
    /// PARAMETER IDENTITY, AND WHERE IT COMES FROM
    /// ====================================================================================
    /// A state's override of a parameter binds to <c>GraphTaskParameter.id</c>, never to the name, so
    /// that renaming a variable on the canvas does not silently unbind every state that tuned it. The
    /// name stays the runtime key — <c>GetParam*</c> instructions carry it and the interpreter
    /// resolves through it — so the id has exactly one job: survive a rename.
    ///
    /// GRAPH TOOLKIT HAS SUCH AN IDENTITY, AND DOES NOT EXPOSE IT. The public
    /// <see cref="IVariable"/> surface is name, type, kind, connectivity, graph, defaults, nodes —
    /// no id (UnityEditor.GraphToolkitModule IL, interface at line 21339, members through 21455).
    /// One layer down there is: the concrete model is <c>VariableDeclarationModelBase</c> (IL 84833,
    /// <c>implements IVariable</c>) → <c>DeclarationModel</c> (IL 43831) → <c>GraphElementModel</c>
    /// (IL 45582) → <c>Model</c> (IL 63815), and <c>Model</c> carries a serialized
    /// <c>m_HashGuid</c> (IL 63824, <c>[SerializeField][HideInInspector]</c>, with the obsolete
    /// <c>m_Guid</c> at IL 63820 as its migration source) behind a public <c>Guid</c> property
    /// (property IL 63952, getter IL 63831). It is assigned once in the constructor (IL 63855 calling
    /// <c>AssignNewGuid</c>, IL 63884) and round-tripped through
    /// <c>OnBeforeSerialize</c>/<c>OnAfterDeserialize</c> (IL 63915 / 63928) — so it is stable across
    /// saves, reloads and reimports, and independent of the variable's name. Every one of those types
    /// is <c>private</c> in IL, i.e. internal, which is why this is read by REFLECTION on a public
    /// property rather than by a cast: the property is accessible, the type is not.
    ///
    /// THE FALLBACK IS THE NAME, AS A VALUE. If that property ever stops being there — a future Graph
    /// Toolkit release, a variable model this bake has not met — the id becomes
    /// <c>"name:&lt;variable name&gt;</c>". That is an ID-VALUE choice, not a second matching route:
    /// nothing anywhere falls back to comparing names, the id is simply derived from one. The
    /// consequence is the one this whole mechanism exists to avoid, and it comes back only in that
    /// case: renaming a variable and re-baking mints a different id, so overrides of it strand and the
    /// inspector reports them as stale rows to delete and re-tick.
    ///
    /// DETERMINISM. Instruction indices are <see cref="Graph.GetNodes"/> order (creation order),
    /// appended constants follow in pin-visit order, parameters are
    /// <see cref="Graph.GetVariables()"/> order (also creation order — the no-argument overload is
    /// <c>SortMethod.Creation</c>, not the panel's display sort), and sub-asset identifiers are
    /// derived from those indices — never from hash codes or dictionary iteration — so two bakes of
    /// an unchanged graph produce identical output and the importer's artifacts stay stable. The cost
    /// of index-keyed sub-asset identifiers is that inserting a node re-keys the ones after it; the
    /// alternative (authored ids, as the state-tree bake uses) needs an id on every node, which is a
    /// lot of typing for a graph whose nodes are mostly nameless arithmetic.
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

        /// <summary>The property carrying a graph element's stable guid on Graph Toolkit's internal
        /// model — see PARAMETER IDENTITY in the type doc for the IL that says so.</summary>
        private const string k_IdentityProperty = "Guid";

        /// <summary>Marks an id DERIVED from a name, so a baked asset says which route it took. Not a
        /// matching rule: nothing compares names, this is just what the id is made of when the
        /// variable model has no identity to offer.</summary>
        private const string k_NameIdPrefix = "name:";

        /// <summary>Identity property per variable-model type, or null for a type that has none. The
        /// bake runs on every keystroke, so the reflection lookup happens once per session per type.
        /// </summary>
        private static readonly Dictionary<Type, PropertyInfo> s_IdentityProperties =
            new Dictionary<Type, PropertyInfo>();

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

            /// <summary>Informational notes reported during the walk. Never affects
            /// <see cref="succeeded"/> — a note says which of two legal routes was taken.</summary>
            public int noteCount;

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

        /// <summary>
        /// The third severity, which <see cref="StateTreeGraphBaker.IBakeLog"/> does not have: a NOTE
        /// is "this is legal, it did what you asked, and here is which of two routes you got". It
        /// exists for exactly one thing — a graph variable on a library call's parameter port, which
        /// bakes at BAKE TIME while the same variable anywhere else is a live parameter — and a
        /// warning would be wrong for it (nothing is wrong) while silence would be worse (the author
        /// would find out when a per-state override did nothing).
        ///
        /// A sink opts in by implementing it; <see cref="BakeContext.Note"/> drops the message when it
        /// does not. That is not a shortcut, it is the routing: notes belong on the CANVAS, where the
        /// author is editing and can see which node they attach to, and nowhere near the console,
        /// where the importer would re-emit one per variable use on every reimport of every graph.
        /// </summary>
        private interface IBakeNote
        {
            void Note(string message, object nodeContext);
        }

        /// <summary>Adapter putting bake diagnostics on the graph's own nodes. Used from
        /// <see cref="TaskGraph.OnGraphChanged"/> through <see cref="Validate"/>. The only sink that
        /// carries notes — see <see cref="IBakeNote"/>.</summary>
        private sealed class GraphLoggerBakeLog : StateTreeGraphBaker.IBakeLog, IBakeNote
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

            public void Note(string message, object nodeContext)
                => m_Logger.Log(message, nodeContext ?? m_GraphContext);
        }

        /// <summary>Console adapter for the importer and the menu. Graph Toolkit's node markers are
        /// unavailable outside <c>OnGraphChanged</c> (the <see cref="GraphLogger"/> instance is created
        /// and owned by the framework), so the node is named in the message text and the ASSET is the
        /// Unity context object, which makes the console line click-to-ping. Deliberately NOT an
        /// <see cref="IBakeNote"/>: an import is not an authoring session and has no one to tell.
        /// </summary>
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
            return Bake(graph, log, null);
        }

        /// <summary>The same, told where the graph lives — what the importer calls, because a
        /// graph loaded for import cannot answer that itself and the registry scope depends on
        /// it.</summary>
        /// <param name="graph">The canvas to bake.</param>
        /// <param name="log">Where diagnostics go.</param>
        /// <param name="assetPath">The graph's asset path, or null to ask the graph.</param>
        /// <returns>The baked program and the diagnostic counts.</returns>
        public static BakeResult Bake(Graph graph, StateTreeGraphBaker.IBakeLog log, string assetPath)
        {
            var result = new BakeResult();
            if (graph == null)
            {
                log?.Error("No graph to bake.", null);
                result.errorCount++;
                return result;
            }

            var context = new BakeContext(result, log) { graph = graph, assetPath = assetPath };
            Collect(context, graph);

            // The pin pass may APPEND constant instructions, so the loop bound is captured first:
            // appended constants have no pins of their own and need no visit.
            int authored = context.program.Count;
            for (int i = 0; i < authored; i++)
                Resolve(context, i);

            var program = ScriptableObject.CreateInstance<GraphTaskAsset>();
            program.name = ReadProgramName(graph);
            program.nodes = context.program;
            program.parameters = context.parameters;
            program.keyBindings = context.keyBindings;
            program.inputBindings = context.inputBindings;
            // Filled by the pin pass above (every Set Output declares itself as it resolves its name),
            // so this assignment has to follow the Resolve loop, not precede it.
            program.declaredOutputs = context.declaredOutputs;
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

            /// <summary>The baked parameter list, in graph-variable order.</summary>
            public readonly List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

            /// <summary>Key-semantic fields on embedded library calls that a String parameter
            /// feeds — the one baked-call value the interpreter re-applies per activation, so a
            /// state's override CAN retarget a key. See
            /// <see cref="GraphTaskAsset.keyBindings"/>.</summary>
            public readonly List<GraphTaskKeyBinding> keyBindings = new List<GraphTaskKeyBinding>();

            /// <summary>Value pins wired into embedded calls' plain fields, pulled at every
            /// ENTER of the call — the input mirror of the task-output pins. See
            /// <see cref="GraphTaskInputBinding"/>.</summary>
            public readonly List<GraphTaskInputBinding> inputBindings =
                new List<GraphTaskInputBinding>();

            /// <summary>Nodes whose value the BAKE itself consumed (a variable wired into a
            /// library call's parameter port, whether it froze there or became a key binding).
            /// Their pull instruction is legitimately unread at run time, so the dead-value
            /// diagnostic must not flag them.</summary>
            public readonly HashSet<INode> consumedAtBake = new HashSet<INode>(NodeIdentity.comparer);

            /// <summary>Synthesized GetTaskOutput pulls, one per consumed RETURN PIN of a
            /// call node — keyed so two readers of the same pin share one instruction.</summary>
            public readonly Dictionary<(INode call, string pin), int> taskOutputPulls =
                new Dictionary<(INode, string), int>();

            /// <summary>Name → kind for the parameters that MADE IT INTO <see cref="parameters"/>.
            /// A variable node only bakes to a GetParam pull when its name is in here, which is what
            /// makes a dangling pull (a name the interpreter cannot resolve) unrepresentable rather
            /// than merely unlikely.</summary>
            public readonly Dictionary<string, GraphTaskParameterKind> parameterKinds =
                new Dictionary<string, GraphTaskParameterKind>(StringComparer.Ordinal);

            /// <summary>What this graph RETURNS, one row per distinct output name, in the order the
            /// Set Output instructions were visited (graph order, so it is deterministic). Values are
            /// unused — a declaration is a name and a type, and the value only exists at runtime.
            /// </summary>
            public readonly List<TaskOutputValue> declaredOutputs = new List<TaskOutputValue>();

            /// <summary>Position in <see cref="declaredOutputs"/> of each name already declared.
            /// Needed because a merge has to find the existing row to compare its kind, and because a
            /// graph with fifty Set Outputs should not be a fifty-squared scan.</summary>
            private readonly Dictionary<string, int> m_OutputIndices =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public readonly BakeResult result;

            private readonly StateTreeGraphBaker.IBakeLog m_Log;

            /// <summary>The canvas being baked — needed by the checks that ask what data this
            /// graph can reach (<see cref="GraphRegistryScope"/>), not by the walk itself.</summary>
            public Graph graph;

            /// <summary>Where that canvas lives, when the caller knows — the importer does and the
            /// graph itself does not (see <see cref="GraphRegistryScope.For(Graph, string)"/>).</summary>
            public string assetPath;

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

            /// <summary>An informational note on a node. Counted but never fatal, and dropped
            /// entirely by a sink that does not carry notes — see <see cref="IBakeNote"/>.</summary>
            public void Note(string message, INode node)
            {
                (m_Log as IBakeNote)?.Note(message, node);
                result.noteCount++;
            }

            /// <summary>
            /// Record that this graph returns <paramref name="name"/>. Called once per Set Output
            /// instruction, and DISTINCT BY NAME: setting the same output from both sides of a branch
            /// is the normal shape of "compute a result", not two return values, so the second write
            /// merges into the first row rather than adding one.
            ///
            /// The kinds have to agree, and when they do not the FIRST one stands. A name is one
            /// contract — the transition inspector offers one row per name and the route that binds to
            /// it writes one type into the blackboard — so a graph returning <c>result</c> as a number
            /// on one path and as text on another has no single answer to give. It still bakes (both
            /// instructions run and the runtime buffer carries whatever was written last, so nothing
            /// is silently dropped at execution time); the warning is that the DECLARATION the author
            /// sees in the inspector can only show one of the two.
            /// </summary>
            /// <param name="name">The output's name; already checked non-empty.</param>
            /// <param name="kind">The type this instruction returns it as.</param>
            /// <param name="node">The instruction, for the diagnostic.</param>
            public void DeclareOutput(string name, GraphTaskParameterKind kind, INode node)
            {
                if (m_OutputIndices.TryGetValue(name, out int existing))
                {
                    GraphTaskParameterKind first = declaredOutputs[existing].kind;
                    if (first != kind)
                    {
                        Warning($"'{Describe(node)}' returns '{name}' as a {kind}, but another Set "
                            + $"Output in this graph already returns it as a {first}. An output name "
                            + "is one contract: the transition that routes it is offered the first "
                            + "type only. Give one of them a different name.", node);
                    }
                    return;
                }

                m_OutputIndices[name] = declaredOutputs.Count;
                declaredOutputs.Add(new TaskOutputValue { name = name, kind = kind });
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
        /// order, so that pass two can resolve a wire to an index that already exists. Variable
        /// nodes join them as parameter pulls, which is why the variable TABLE is built first —
        /// a pull is only emitted for a name the table has.</summary>
        private static void Collect(BakeContext context, Graph graph)
        {
            CollectParameters(context, graph);

            foreach (INode node in graph.GetNodes())
            {
                if (node is ITaskGraphNode instruction)
                {
                    context.indexByNode[node] = context.program.Count;
                    context.sources.Add(node);
                    context.program.Add(NewInstruction(instruction.nodeKind));
                    continue;
                }

                // Entry nodes are not instructions (they name where a chain starts) and Graph
                // Toolkit's own constant nodes are read as literals wherever they are wired.
                // Anything else is something this graph cannot execute.
                if (node is OnEnterNode || node is OnTickNode || node is OnExitNode)
                    continue;
                if (node is IVariableNode variableNode)
                {
                    CollectVariableNode(context, variableNode);
                    continue;
                }
                if (node is IConstantNode)
                    continue;

                context.Warning($"'{Describe(node)}' is not a task-graph instruction, so the bake "
                    + "ignores it. Delete it, or replace it with a node from the task graph palette.",
                    node);
            }
        }

        /// <summary>
        /// Graph Toolkit's variable panel → <see cref="GraphTaskAsset.parameters"/>: the knobs the
        /// state that runs this task can override, each carrying the default typed into the panel.
        /// Float, string and bool are the whole set, because they are the whole set the program model
        /// can pull (<see cref="GraphTaskNodeKind.GetParamFloat"/> and its two siblings). A variable
        /// of any other type is named in a warning and left to the old constant-flattening path,
        /// which still works and is still what its wires get.
        /// </summary>
        private static void CollectParameters(BakeContext context, Graph graph)
        {
            IEnumerable<IVariable> variables = SafeVariables(graph);
            if (variables == null)
                return;

            foreach (IVariable variable in variables)
            {
                if (variable == null)
                    continue;

                string name = SafeVariableName(variable);
                if (string.IsNullOrEmpty(name))
                {
                    context.Warning("A graph variable has no name, so nothing could refer to it. It "
                        + "is not a parameter of this task; give it a name.", null);
                    continue;
                }

                Type type = SafeVariableType(variable);
                if (!TryParameterKind(type, out GraphTaskParameterKind kind))
                {
                    context.Warning($"Graph variable '{name}' is a "
                        + $"{(type != null ? type.Name : "unknown type")}, and a task parameter is a "
                        + "number, a piece of text or a checkbox. It is not offered on the state that "
                        + "runs this task, and its nodes bake as constants taken from its default.",
                        null);
                    continue;
                }

                // An OUTPUT variable is the RETURN half of the signature, Blueprint-style:
                // declared on the panel like the inputs, written by the Set Output node of
                // the same name, routed by the caller. It is not a parameter — nothing is
                // passed IN through it.
                if (SafeVariableKind(variable) == VariableKind.Output)
                {
                    context.DeclareOutput(name, kind, null);
                    continue;
                }

                if (context.parameterKinds.ContainsKey(name))
                {
                    context.Warning($"Two graph variables are called '{name}'. The first one is the "
                        + "parameter; the second is ignored, because an override names a parameter by "
                        + "its name and there would be no way to say which.", null);
                    continue;
                }

                context.parameterKinds[name] = kind;
                context.parameters.Add(ReadParameter(variable, name, kind));
            }
        }

        /// <summary>One parameter record: name, kind, and the variable's default in whichever of the
        /// two value slots its kind uses (bool rides in <c>floatValue</c> as non-zero, the same
        /// encoding <see cref="GraphTaskNodeKind.ConstBool"/> uses).</summary>
        private static GraphTaskParameter ReadParameter(IVariable variable, string name,
            GraphTaskParameterKind kind)
        {
            var parameter = new GraphTaskParameter
            {
                id = ReadParameterId(variable, name),
                name = name,
                kind = kind,
                stringValue = string.Empty
            };

            switch (kind)
            {
                case GraphTaskParameterKind.Float:
                    if (TryReadDefault(variable, out float number))
                        parameter.floatValue = number;
                    break;

                case GraphTaskParameterKind.String:
                    if (TryReadDefault(variable, out string text))
                        parameter.stringValue = text ?? string.Empty;
                    break;

                case GraphTaskParameterKind.Bool:
                    if (TryReadDefault(variable, out bool flag))
                        parameter.floatValue = flag ? 1f : 0f;
                    break;
            }

            return parameter;
        }

        /// <summary>
        /// A variable node on the canvas becomes a PARAMETER PULL — the instruction that reads the
        /// effective value (override, else default) every time it is pulled. Only for a variable that
        /// became a parameter: anything else is left unregistered, and <see cref="ResolveData"/> then
        /// takes the constant-flattening path for it, which is the pre-M7f behaviour and the only
        /// thing the program model can express for an unsupported type.
        /// </summary>
        private static void CollectVariableNode(BakeContext context, IVariableNode variableNode)
        {
            // The RAW name, not the diagnostic one: a table lookup must not match on the "(unnamed)"
            // placeholder a message would print.
            string name = SafeVariableName(SafeVariableOf(variableNode));
            if (name.Length == 0
                || !context.parameterKinds.TryGetValue(name, out GraphTaskParameterKind kind))
                return;

            GraphTaskNode pull = NewInstruction(ParameterNodeKind(kind));
            pull.stringValue = name;

            context.indexByNode[variableNode] = context.program.Count;
            context.sources.Add(variableNode);
            context.program.Add(pull);
        }

        /// <summary>An instruction with its wire slots allocated and every one of them UNWIRED. The
        /// -1 fill is load-bearing: a fresh <c>int[]</c> is full of zeros, and zero is a valid
        /// instruction index.</summary>
        /// <summary>The instruction shapes moved to the runtime with M30.6, because a second
        /// authoring surface made "how many pins has a Branch" a contract rather than a private
        /// habit of this file. Kept as one-line hops so every caller below reads unchanged.</summary>
        private static GraphTaskNode NewInstruction(GraphTaskNodeKind kind)
        {
            return GraphTaskProgram.NewInstruction(kind);
        }

        private static int[] Unwired(int count)
        {
            return GraphTaskProgram.Unwired(count);
        }

        private static int ExecPinCount(GraphTaskNodeKind kind)
        {
            return GraphTaskProgram.ExecPins(kind);
        }

        private static int DataPinCount(GraphTaskNodeKind kind)
        {
            return GraphTaskProgram.DataPins(kind);
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

                case GraphTaskNodeKind.SetOutputFloat:
                    node.stringValue = ReadOutputName(context, source,
                        SetOutputFloatNode.OutputNamePortName, GraphTaskParameterKind.Float);
                    node.data[0] = ResolveData(context, source, SetOutputFloatNode.ValuePortName,
                        typeof(float), LiteralSlot.Float, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.SetOutputString:
                    node.stringValue = ReadOutputName(context, source,
                        SetOutputStringNode.OutputNamePortName, GraphTaskParameterKind.String);
                    node.data[0] = ResolveData(context, source, SetOutputStringNode.ValuePortName,
                        typeof(string), LiteralSlot.SecondString, ref node);
                    node.exec[0] = ResolveExec(context, source, TaskGraphPorts.ExecOutPortName);
                    break;

                case GraphTaskNodeKind.SetOutputBool:
                    node.stringValue = ReadOutputName(context, source,
                        SetOutputBoolNode.OutputNamePortName, GraphTaskParameterKind.Bool);
                    // The bool literal goes in floatValue as 1/0 — the program model's only numeric
                    // slot, and the same encoding the captured value record uses for a bool.
                    node.data[0] = ResolveData(context, source, SetOutputBoolNode.ValuePortName,
                        typeof(bool), LiteralSlot.Float, ref node);
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
                    LowerReturnPins(context, source, node);
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

                case GraphTaskNodeKind.RegistryEntry:
                    ConfigureRegistryEntry(context, source, ref node);
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

                case GraphTaskNodeKind.GetParamFloat:
                case GraphTaskNodeKind.GetParamString:
                case GraphTaskNodeKind.GetParamBool:
                    // Fully built by CollectVariableNode: a variable node has no pins to resolve —
                    // its one output IS the pull, and the parameter name is its only payload.
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

                    // A CALL's RETURN PIN: lower the read into a GetTaskOutput pull bound to
                    // the call's instruction — the return flows inside the program, no
                    // blackboard in between. One pull per pin, shared by every reader.
                    if (producer is TaskCallNode call)
                    {
                        string pinName = port.FirstConnectedPort.Name;
                        System.Reflection.FieldInfo output =
                            TaskOutputPorts.Find(call.taskType, pinName);
                        if (output != null)
                        {
                            if (context.taskOutputPulls.TryGetValue((producer, pinName),
                                out int shared))
                                return shared;
                            GraphTaskNode pull = NewInstruction(TaskOutputPorts.PullKind(output));
                            pull.stringValue = pinName;
                            pull.data[0] = index;
                            int pullIndex = context.program.Count;
                            context.program.Add(pull);
                            context.sources.Add(producer);
                            context.taskOutputPulls[(producer, pinName)] = pullIndex;
                            return pullIndex;
                        }
                    }

                    context.Error($"The '{portName}' pin of '{Describe(owner)}' is wired to "
                        + $"'{Describe(producer)}', which produces no value — it is an instruction, "
                        + "not a value. Wire it into an exec pin instead.", owner);
                    return -1;
                }

                if (producer is IVariableNode variableNode)
                {
                    // A parameter pull, when the variable became a parameter. Graph Toolkit's typed
                    // pins already stop a bool reaching a float pin, so the type check below can only
                    // fire if that guarantee ever changes — and it costs one comparison to find out
                    // here rather than as a wrong number at runtime.
                    if (context.indexByNode.TryGetValue(producer, out int pull))
                    {
                        GraphTaskNodeKind pullKind = context.program[pull].kind;
                        if (TryParameterKind(valueType, out GraphTaskParameterKind expected)
                            && ParameterNodeKind(expected) == pullKind)
                            return pull;

                        context.Error($"Graph variable '{SafeVariableName(variableNode)}' does not "
                            + $"fit the '{portName}' pin of '{Describe(owner)}': the pin carries a "
                            + $"{(valueType != null ? valueType.Name : "?")} and the variable does "
                            + "not. Use a variable of the pin's type.", owner);
                        return -1;
                    }

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
        /// <summary>A registry's row names, capped, for the message that follows a mistyped one —
        /// the list an author would otherwise have to go and read. Listed because Entry is TYPED:
        /// there is no dropdown to fall back on, by design (see <see cref="RegistryEntryNode"/>),
        /// so the message has to carry the list itself.</summary>
        /// <param name="registry">The registry to describe.</param>
        /// <returns>"medkit, keycard, ration, relic", or "(no rows)".</returns>
        private static string RowNames(StateTreeRegistryAsset registry)
        {
            const int limit = 20;
            var names = new List<string>();
            for (int i = 0; i < registry.Count && names.Count < limit; i++)
            {
                StateTreeRegistryEntry row = registry.EntryAt(i);
                if (row != null && !string.IsNullOrEmpty(row.name))
                    names.Add(row.name);
            }
            if (names.Count == 0)
                return "(no rows)";
            return registry.Count > names.Count
                ? string.Join(", ", names) + ", … (" + registry.Count + " total)"
                : string.Join(", ", names);
        }

        /// <summary>
        /// A <see cref="RegistryEntryNode"/> into its instruction: the chosen row's NAME, and
        /// nothing else.
        ///
        /// NOTHING IS RESOLVED HERE, deliberately. The importer bakes with the AssetDatabase
        /// closed to queries — measured: the graph's registry scope comes back empty even given
        /// the right path — so a registry cannot be found, a row cannot be looked up, and any
        /// check written here would fail on the graphs that are correct. The name is what the
        /// consuming task resolves anyway, through its own service, exactly as it would a name
        /// typed into the pin.
        ///
        /// The CHECK lives where the AssetDatabase is open: EntryRefValidator, on every graph
        /// change, with the reachable rows listed.
        /// </summary>
        private static void ConfigureRegistryEntry(BakeContext context, INode source,
            ref GraphTaskNode node)
        {
            node.objectValue = null;
            node.stringValue2 = string.Empty;
            node.stringValue = ReadConstantString(context, source,
                RegistryEntryNode.EntryPortName);

            if (string.IsNullOrEmpty(node.stringValue))
            {
                context.Error("Registry Entry names no row, so everything it feeds would get an "
                    + "empty name. Choose one from its Entry list.", source);
            }
        }

        private static string ReadKey(BakeContext context, INode owner, string portName)
        {
            // A KEY PIN fed by a String PARAMETER binds LIVE: the constant baked below is the
            // parameter's default, and the recorded binding (field empty = the instruction's
            // own key) lets ApplyOverrides rewrite the instance's instruction when a state
            // overrides the parameter — the instruction twin of the library-call key binding.
            IPort port = owner.GetInputPortByName(portName);
            if (port != null && port.IsConnected
                && port.FirstConnectedPort?.GetNode() is IVariableNode variableNode)
            {
                string variableName = SafeVariableName(variableNode);
                if (context.parameterKinds.TryGetValue(variableName,
                        out GraphTaskParameterKind parameterKind)
                    && parameterKind == GraphTaskParameterKind.String)
                {
                    context.keyBindings.Add(new GraphTaskKeyBinding
                    {
                        node = context.indexByNode.TryGetValue(owner, out int ownerIndex)
                            ? ownerIndex : -1,
                        field = string.Empty,
                        parameter = variableName
                    });
                    context.consumedAtBake.Add(variableNode);
                }
            }

            string key = ReadConstantString(context, owner, portName);
            if (string.IsNullOrEmpty(key))
            {
                context.Error($"'{Describe(owner)}' has no blackboard key, so it would read or write "
                    + "an unnamed slot. Type the key into its Key field.", owner);
            }
            return key;
        }

        /// <summary>
        /// An OUTPUT NAME: a constant string, required, never a wire — a blackboard key's rules, for a
        /// stronger reason. An output name is the CONTRACT a transition's route binds to (name-keyed
        /// by design, like a function's return value and unlike the id-keyed input parameters), so a
        /// computed name would be a contract nothing could be written against, and an empty one would
        /// declare a return value no route could ever name.
        ///
        /// Also the one place that knows what this graph returns, so it is where the declaration list
        /// is built: <see cref="BakeContext.DeclareOutput"/> merges the name into
        /// <see cref="GraphTaskAsset.declaredOutputs"/>, which exists so the transition inspector can
        /// offer this task's outputs in a dropdown instead of asking the author to retype them.
        /// </summary>
        private static string ReadOutputName(BakeContext context, INode owner, string portName,
            GraphTaskParameterKind kind)
        {
            string name = ReadConstantString(context, owner, portName);
            if (string.IsNullOrEmpty(name))
            {
                context.Error($"'{Describe(owner)}' has no output name, so it would return a value "
                    + "under no name and no transition could route it. Type the name into its Output "
                    + "field.", owner);
                return string.Empty;
            }

            context.DeclareOutput(name, kind, owner);
            return name;
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
                // Consumed-at-bake means a KEY binding was recorded for this wire — the value
                // is NOT frozen (ApplyOverrides rewrites it per activation), so the warning
                // would be telling the author the opposite of the truth.
                if (producer is IVariableNode variableNode
                    && !context.consumedAtBake.Contains(producer))
                    WarnBakedConstantVariable(context, owner, portName, variableNode);
            }

            return LibraryParameterPorts.TryReadValue(port, valueType, out object value) ? value : null;
        }

        /// <summary>A variable on a field the program model has no pin for — a blackboard key, a cue
        /// name, a compare operator. Those are baked strings, so the variable's default is all there
        /// is and a per-state override can never reach them.</summary>
        private static void WarnBakedConstantVariable(BakeContext context, INode owner, string portName,
            IVariableNode variableNode)
        {
            context.Warning($"Graph variable '{SafeVariableName(variableNode)}' feeds the "
                + $"'{portName}' of '{Describe(owner)}', which is baked into the program as a fixed "
                + "value. The variable's default is what gets baked, and overriding the parameter on "
                + "a state changes nothing here. Type the value in instead.", owner);
        }

        /// <summary>A variable that did NOT become a parameter — an unsupported type, no name, or a
        /// duplicate of one already taken. <see cref="CollectParameters"/> has already said which, so
        /// this says only what happens instead: the pre-M7f flattening, unchanged.</summary>
        private static void WarnFlattenedVariable(BakeContext context, INode owner, string portName,
            IVariableNode variableNode)
        {
            context.Warning($"Graph variable '{SafeVariableName(variableNode)}' is not a parameter of "
                + "this task (see the variable's own warning for why), so on the "
                + $"'{portName}' of '{Describe(owner)}' it is baked as a CONSTANT taken from its "
                + "default value and no state can override it. Use the blackboard nodes for a value "
                + "that has to change while the task runs.", owner);
        }

        /// <summary>
        /// A variable on a library call's parameter port. This is LEGAL and it works — the embedded
        /// sub-asset's field gets the variable's default — but it is the BAKE-TIME route, so it is the
        /// one use of a variable a per-state override does not reach, which an author who overrode it
        /// elsewhere in the same graph would otherwise have to discover by experiment.
        /// </summary>
        private static void NoteBakedCallParameter(BakeContext context, INode owner, string portName,
            IVariableNode variableNode)
        {
            context.Note($"Graph variable '{SafeVariableName(variableNode)}' sets '{portName}' on "
                + $"'{Describe(owner)}' at BAKE TIME: a library call's parameters are baked into its "
                + "own copy, so this one keeps the variable's default even when a state overrides the "
                + "parameter. Wire the variable into a graph node's pin for the overridable route, or "
                + "put the value on the blackboard where the task reads one.", owner);
        }

        /// <summary>The name of the variable a variable node reads, for a diagnostic. Never empty:
        /// a message that names nothing is worse than one that says so.</summary>
        private static string SafeVariableName(IVariableNode variableNode)
        {
            string name = SafeVariableName(SafeVariableOf(variableNode));
            return name.Length > 0 ? name : "(unnamed)";
        }

        /// <summary>The variable a variable node reads, or null when it has none or refuses to
        /// answer.</summary>
        private static IVariable SafeVariableOf(IVariableNode variableNode)
        {
            try
            {
                return variableNode?.Variable;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A variable's name, or empty when it has none or refuses to answer. Graph Toolkit
        /// builds these models lazily, so a half-built one throws rather than returning null.</summary>
        private static string SafeVariableName(IVariable variable)
        {
            try
            {
                return variable != null ? variable.Name ?? string.Empty : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The Return node's WIRED output pins, lowered into Set Output instructions ahead of
        /// the return — the Unreal Return node without a new instruction kind. The return
        /// instruction itself is REPURPOSED in place as the first Set Output (so every exec
        /// pin already targeting it flows through the returns), the rest are appended and
        /// chained, and a fresh return of the original kind is appended as the tail. Unwired
        /// pins lower to nothing: not returned on this path.
        /// </summary>
        private static void LowerReturnPins(BakeContext context, INode source, GraphTaskNode node)
        {
            GraphTaskNodeKind returnKind = node.kind;
            GraphTaskNode current = null;

            foreach (IPort port in source.GetInputPorts())
            {
                if (port == null || !port.IsConnected
                    || string.Equals(port.Name, TaskGraphPorts.ExecInPortName,
                        StringComparison.Ordinal))
                    continue;

                Type dataType = port.DataType;
                GraphTaskNodeKind setKind;
                GraphTaskParameterKind outputKind;
                LiteralSlot literalSlot;
                Type valueType;
                if (dataType == typeof(float) || dataType == typeof(int))
                {
                    setKind = GraphTaskNodeKind.SetOutputFloat;
                    outputKind = GraphTaskParameterKind.Float;
                    literalSlot = LiteralSlot.Float;
                    valueType = typeof(float);
                }
                else if (dataType == typeof(string))
                {
                    setKind = GraphTaskNodeKind.SetOutputString;
                    outputKind = GraphTaskParameterKind.String;
                    literalSlot = LiteralSlot.SecondString;
                    valueType = typeof(string);
                }
                else if (dataType == typeof(bool))
                {
                    setKind = GraphTaskNodeKind.SetOutputBool;
                    outputKind = GraphTaskParameterKind.Bool;
                    literalSlot = LiteralSlot.Float;
                    valueType = typeof(bool);
                }
                else
                {
                    continue;
                }

                GraphTaskNode setNode;
                if (current == null)
                {
                    // First wired pin: the return instruction BECOMES this Set Output, with
                    // its slots re-sized for the new kind.
                    node.kind = setKind;
                    node.exec = new[] { -1 };
                    node.data = new[] { -1 };
                    setNode = node;
                }
                else
                {
                    setNode = NewInstruction(setKind);
                    int appendedIndex = context.program.Count;
                    context.program.Add(setNode);
                    context.sources.Add(source);
                    current.exec[0] = appendedIndex;
                }

                setNode.stringValue = port.Name;
                context.DeclareOutput(port.Name, outputKind, source);
                setNode.data[0] = ResolveData(context, source, port.Name, valueType,
                    literalSlot, ref setNode);
                current = setNode;
            }

            if (current != null)
            {
                GraphTaskNode tail = NewInstruction(returnKind);
                int tailIndex = context.program.Count;
                context.program.Add(tail);
                context.sources.Add(source);
                current.exec[0] = tailIndex;
            }
        }

        /// <summary>
        /// The stable identity a state's override binds to: the variable's own, when Graph Toolkit's
        /// model will give it up, and otherwise one derived from the name. See PARAMETER IDENTITY in
        /// the type doc for where each comes from and what the difference costs.
        /// </summary>
        /// <param name="variable">The graph variable being baked.</param>
        /// <param name="name">Its name, already validated as non-empty.</param>
        /// <returns>An id, never empty.</returns>
        internal static string ReadParameterId(IVariable variable, string name)
        {
            string identity = TryReadVariableIdentity(variable);
            return identity.Length > 0 ? identity : k_NameIdPrefix + name;
        }

        /// <summary>
        /// Graph Toolkit's per-variable guid, read off the concrete model by reflection because
        /// <see cref="IVariable"/> does not carry it and the class that does is internal.
        ///
        /// Wrapped like every other read of this surface: a variable mid-edit throws rather than
        /// answering, and one bad variable must not take down the bake. Cached per model TYPE, not per
        /// variable — this runs on every keystroke through <c>OnGraphChanged</c>, and the lookup is
        /// the expensive half.
        /// </summary>
        /// <param name="variable">The variable to identify.</param>
        /// <returns>The identity as text, or empty when the model has none to give.</returns>
        private static string TryReadVariableIdentity(IVariable variable)
        {
            if (variable == null)
                return string.Empty;

            try
            {
                Type type = variable.GetType();
                if (!s_IdentityProperties.TryGetValue(type, out PropertyInfo property))
                {
                    property = type.GetProperty(k_IdentityProperty,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (property != null
                        && (!property.CanRead || property.GetIndexParameters().Length > 0))
                        property = null;

                    s_IdentityProperties[type] = property;
                }

                if (property == null)
                    return string.Empty;

                object value = property.GetValue(variable);
                string text = value != null ? value.ToString() : string.Empty;
                return IsUsableIdentity(text) ? text : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Whether an identity read back is worth baking. A hash that was never assigned
        /// prints as all zeros, and an id every unassigned variable shares is worse than no id at all
        /// — every override in the project would bind to the same one.</summary>
        private static bool IsUsableIdentity(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '0')
                    return true;
            }

            return false;
        }

        /// <summary>A variable's declared type, or null when it has none or refuses to answer.</summary>
        private static Type SafeVariableType(IVariable variable)
        {
            try
            {
                return variable?.DataType;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The panel's Input/Local/Output choice, read under the same lazy-model
        /// guard. Local when it refuses to answer — the pre-output behavior.</summary>
        private static VariableKind SafeVariableKind(IVariable variable)
        {
            try
            {
                return variable != null ? variable.VariableKind : VariableKind.Local;
            }
            catch (Exception)
            {
                return VariableKind.Local;
            }
        }

        /// <summary>The graph's variables in creation order, or null when the graph has none or
        /// cannot answer.</summary>
        private static IEnumerable<IVariable> SafeVariables(Graph graph)
        {
            try
            {
                return graph.GetVariables();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A variable's default value as <typeparamref name="T"/>. Wrapped because
        /// <c>IVariable.TryGetDefaultValue</c> reaches into the variable's serialized model, which a
        /// variable mid-edit can throw from — and one bad variable must not take down the bake.
        /// </summary>
        /// <typeparam name="T">The value type to read.</typeparam>
        /// <param name="variable">The variable to read.</param>
        /// <param name="value">Receives the default on success, the type's own default otherwise.</param>
        /// <returns>True when a default of that type was read.</returns>
        private static bool TryReadDefault<T>(IVariable variable, out T value)
        {
            try
            {
                return variable.TryGetDefaultValue(out value);
            }
            catch (Exception)
            {
                value = default;
                return false;
            }
        }

        /// <summary>The parameter kind a value type maps to. The three are the whole set the program
        /// model can pull, so this doubles as "can this be a parameter at all".</summary>
        /// <param name="valueType">The variable's or pin's type.</param>
        /// <param name="kind">Receives the matching kind.</param>
        /// <returns>True for float, string and bool.</returns>
        private static bool TryParameterKind(Type valueType, out GraphTaskParameterKind kind)
        {
            if (valueType == typeof(float))
            {
                kind = GraphTaskParameterKind.Float;
                return true;
            }
            if (valueType == typeof(string))
            {
                kind = GraphTaskParameterKind.String;
                return true;
            }
            if (valueType == typeof(bool))
            {
                kind = GraphTaskParameterKind.Bool;
                return true;
            }

            kind = default;
            return false;
        }

        /// <summary>The pull instruction that reads a parameter of this kind.</summary>
        /// <param name="kind">The parameter kind.</param>
        /// <returns>The matching <see cref="GraphTaskNodeKind"/>.</returns>
        private static GraphTaskNodeKind ParameterNodeKind(GraphTaskParameterKind kind)
        {
            switch (kind)
            {
                case GraphTaskParameterKind.String:
                    return GraphTaskNodeKind.GetParamString;
                case GraphTaskParameterKind.Bool:
                    return GraphTaskNodeKind.GetParamBool;
                default:
                    return GraphTaskNodeKind.GetParamFloat;
            }
        }

        /// <summary>Whether an instruction reads a task parameter.</summary>
        private static bool IsParameterKind(GraphTaskNodeKind kind)
            => kind == GraphTaskNodeKind.GetParamFloat || kind == GraphTaskNodeKind.GetParamString
                || kind == GraphTaskNodeKind.GetParamBool;

        /// <summary>Whether an instruction is PULLED rather than stepped — the classification
        /// <see cref="Diagnose"/> needs. <see cref="TaskGraphPorts.IsDataKind"/> is the shared
        /// version and predates the parameter pulls; it is not this file's to extend, and a pull
        /// misread as an exec node would be reported as "nothing runs this" on every variable node
        /// in the graph.</summary>
        private static bool IsPullKind(GraphTaskNodeKind kind)
            => TaskGraphPorts.IsDataKind(kind) || IsParameterKind(kind);

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
            ApplyParameters(context, source, task, taskType, index);
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
            ApplyParameters(context, source, condition, conditionType, index);
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
            Type libraryType, int nodeIndex)
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
                        // A value pin wired into a TASK's plain field becomes an INPUT BINDING:
                        // lowered like any flow-node pin, pulled and written onto the call's
                        // copy at every enter — `damage` flows from a stats read into a damage
                        // dealer with no blackboard in between. Only plain value fields bind;
                        // key fields and objects keep the refusal.
                        Type portType = LibraryParameterPorts.PortDataType(field.FieldType);
                        bool bindable = target is StateTreeTaskAsset
                            && (portType == typeof(float) || portType == typeof(int)
                                || portType == typeof(bool) || portType == typeof(string));
                        if (!bindable)
                        {
                            context.Error($"'{field.Name}' on '{Describe(source)}' is wired to "
                                + $"'{Describe(producer)}', but only a task's float/int/bool/string "
                                + "fields accept a value wire — this one is baked as a value. Type "
                                + "the value in, or put it on the blackboard and turn on the task's "
                                + "blackboard option where it has one.", source);
                            continue;
                        }

                        GraphTaskNode instruction = context.program[nodeIndex];
                        int producerIndex = ResolveData(context, source, field.Name, portType,
                            LiteralSlot.None, ref instruction);
                        if (producerIndex >= 0)
                        {
                            int[] pins = instruction.data ?? Array.Empty<int>();
                            int pin = pins.Length;
                            Array.Resize(ref pins, pin + 1);
                            pins[pin] = producerIndex;
                            instruction.data = pins;
                            context.inputBindings.Add(new GraphTaskInputBinding
                            {
                                node = nodeIndex,
                                field = field.Name,
                                pin = pin
                            });
                        }
                        continue;
                    }
                    if (producer is IVariableNode variableNode)
                    {
                        context.consumedAtBake.Add(producer);
                        // A NAME field fed by a String parameter is the baked-call value that
                        // stays LIVE: the binding recorded here makes the interpreter re-apply the
                        // parameter's effective value to the embedded copy each activation, so a
                        // caller's override retargets it. Three shapes qualify, because all three
                        // are "a name the call resolves later" — a key wrapper, a typed registry
                        // reference (which the call looks up by entryName), and a plain string.
                        // That is what lets a registry row choose which item its conversation
                        // hands over without a second graph. Every other field keeps the
                        // bake-time rule and its note.
                        string variableName = SafeVariableName(variableNode);
                        bool nameField = field.FieldType == typeof(StateTreeKeyField)
                            || typeof(IStateTreeEntryRef).IsAssignableFrom(field.FieldType)
                            || field.FieldType == typeof(string);
                        if (nameField
                            && context.parameterKinds.TryGetValue(variableName,
                                out GraphTaskParameterKind parameterKind)
                            && parameterKind == GraphTaskParameterKind.String)
                        {
                            context.keyBindings.Add(new GraphTaskKeyBinding
                            {
                                node = nodeIndex,
                                field = field.Name,
                                parameter = variableName
                            });
                        }
                        else
                        {
                            NoteBakedCallParameter(context, source, field.Name, variableNode);
                        }
                    }
                }

                if (!LibraryParameterPorts.TryReadValue(port,
                    LibraryParameterPorts.PortDataType(field.FieldType), out object value))
                    continue;

                try
                {
                    LibraryParameterPorts.WriteFieldValue(field, target, value);
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

                if (IsPullKind(node.kind))
                {
                    if (!allPulled.Contains(i) && !context.consumedAtBake.Contains(source))
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
                else if (IsOutputKind(node.kind) && exitChain.Contains(i) && !enterChain.Contains(i))
                {
                    // A task's outputs are read the instant it finishes, and On Exit is teardown that
                    // runs after — the same "returned, then cleaned up" order a function has. A Set
                    // Output there therefore writes into a buffer nobody looks at again.
                    context.Warning($"'{Describe(source)}' only runs in the On Exit chain, and this "
                        + "task's outputs are captured the moment it finishes — before On Exit runs. "
                        + "Nothing can route this value. Set it in the On Enter or On Tick chain, "
                        + "before whatever returns.", source);
                }
            }
        }

        /// <summary>Whether an instruction writes one of this task's return values.</summary>
        /// <param name="kind">The instruction to classify.</param>
        /// <returns>True for the three Set Output instructions.</returns>
        private static bool IsOutputKind(GraphTaskNodeKind kind)
            => kind == GraphTaskNodeKind.SetOutputFloat || kind == GraphTaskNodeKind.SetOutputString
                || kind == GraphTaskNodeKind.SetOutputBool;

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
