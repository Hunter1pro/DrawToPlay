using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The main editor assembly's gateway to the M7 graph frontend
    /// (<c>PowerOfFire.DrawToPlay.GraphEditor</c>) and to Unity's Graph Toolkit. Every call is
    /// reflection; this assembly names neither.
    ///
    /// WHY REFLECTION AND NOT AN ASSEMBLY REFERENCE. draw-tool-port-brief.md §7.3 makes the
    /// frontend/model split a hard boundary because Graph Toolkit is experimental and "API churn
    /// between versions is expected" (§8 Risks). The GraphEditor assembly honours that boundary
    /// for the RUNTIME — the runner never sees a graph type. This class honours it for the
    /// TOOLS: PowerOfFire.DrawToPlay.Editor does not reference the frontend, so a frontend that
    /// fails to compile against a newer experimental release reads as "the Behavior tab says the
    /// frontend is unavailable" instead of taking every M0-M6 tool down with it. That is the same
    /// reasoning that put the frontend in its own assembly, applied one level up — and it is not
    /// hypothetical: this project moved from the 0.4.0-exp.2 package to the 0.5.0-exp.1 builtin
    /// module DURING M7, and the two do not agree on the case of a single member name.
    ///
    /// WHICH GRAPH TOOLKIT THIS TARGETS. 6000.5.3f1 resolves <c>com.unity.graphtoolkit</c>
    /// 0.5.0-exp.1 as <c>source: builtin</c> (Packages/packages-lock.json) — the code is in
    /// <c>UnityEditor.GraphToolkitModule.dll</c>, and its IntelliSense XML beside it is the
    /// authority for every signature below. That release has a full PUBLIC authoring API
    /// (<c>Graph.AddNode</c>, <c>Graph.Connect</c>, <c>ContextNode.CreateBlockNode</c>,
    /// <c>IPort.TrySetValue</c>), so nothing here needs an internal member.
    ///
    /// The 0.4.0-exp.2 spellings are still accepted as fallbacks — every lookup takes a candidate
    /// list, and 0.4's read API is camelCase (<c>nodeCount</c>, <c>isConnected</c>) where 0.5's
    /// is Pascal (<c>NodeCount</c>, <c>IsConnected</c>). 0.4 had no public way to add a node at
    /// all, so <see cref="Authoring"/> also carries a last-resort path through its internal model
    /// (each such member cited to the file it was read from). Cheap insurance, and a working
    /// record of what changed between the two.
    /// </summary>
    internal static class StateTreeGraphBridge
    {
        /// <summary>Namespace of the M7 frontend assembly (m7 conventions: file ownership).</summary>
        internal const string FrontendNamespace = "PowerOfFire.DrawToPlay.GraphEditor";

        /// <summary>The graph class carrying the <c>[Graph]</c> attribute. Resolved by name
        /// because this assembly cannot reference the frontend.</summary>
        internal const string GraphTypeName = FrontendNamespace + ".StateTreeGraph";

        /// <summary>The bake step (§7.3): graph → <see cref="StateTreeAsset"/>.</summary>
        internal const string BakerTypeName = FrontendNamespace + ".StateTreeGraphBaker";

        private const string k_GraphDatabaseTypeName = "Unity.GraphToolkit.Editor.GraphDatabase";
        private const string k_GraphAttributeTypeName = "Unity.GraphToolkit.Editor.GraphAttribute";

        /// <summary>Where graph assets authored from the Flow window land. One folder rather than
        /// "next to the entity" because a graph is a project asset shared by every instance of an
        /// archetype, exactly like the M6 preset trees it replaces.</summary>
        internal const string GraphFolder = "Assets/DrawToPlay/Graphs";

        // Member-name candidates: 0.5.0-exp.1 first, 0.4.0-exp.2 second.
        private static readonly string[] k_NodeCountNames = { "NodeCount", "nodeCount" };
        private static readonly string[] k_NameNames = { "Name", "name" };
        private static readonly string[] k_NodeOptionsNames = { "NodeOptions", "nodeOptions" };
        private static readonly string[] k_ExtensionNames = { "Extension", "extension" };
        private static readonly string[] k_SaveNames = { "SaveGraph", "SaveGraphIfDirty" };
        private static readonly string[] k_PositionNames = { "Position", "position" };

        private static Type s_GraphType;
        private static Type s_GraphDatabaseType;
        private static string s_Extension;
        private static string s_Error;
        private static bool s_Resolved;

        /// <summary>Forget every cached lookup. Called by the Flow window's Refresh so a user who
        /// has just recompiled the frontend does not have to reopen the window.</summary>
        internal static void InvalidateCache()
        {
            s_Resolved = false;
            s_GraphType = null;
            s_GraphDatabaseType = null;
            s_Extension = null;
            s_Error = null;
        }

        /// <summary>True when the frontend and Graph Toolkit are both loaded and the graph type
        /// declares a usable asset extension. <paramref name="error"/> is a user-facing sentence
        /// when false — never empty.</summary>
        internal static bool TryResolve(out string error)
        {
            if (!s_Resolved)
            {
                s_Resolved = true;
                s_Error = Resolve();
            }

            error = s_Error;
            return error == null;
        }

        /// <summary>The <c>[Graph]</c> class, or null when <see cref="TryResolve"/> fails.</summary>
        internal static Type graphType
        {
            get
            {
                TryResolve(out _);
                return s_GraphType;
            }
        }

        /// <summary>Asset extension WITHOUT the leading dot, as <c>GraphAttribute.Extension</c>
        /// declares it. Graph Toolkit registers both the bare and the dotted spelling, so either
        /// form in the frontend works and this normalises.</summary>
        internal static string extension
        {
            get
            {
                TryResolve(out _);
                return s_Extension;
            }
        }

        /// <summary>Project path for an entity's graph asset. Named after the entity so a scene
        /// with a zombie and an archer keeps two graphs apart.</summary>
        internal static string GraphAssetPath(string entityName)
        {
            var safe = SanitizeFileName(entityName);
            var ext = extension;
            if (string.IsNullOrEmpty(ext))
                ext = "statetree";
            return $"{GraphFolder}/{safe}.{ext}";
        }

        // --- create / open -------------------------------------------------------------------

        /// <summary>Create an EMPTY graph asset — <c>GraphDatabase.CreateGraph&lt;T&gt;(string)</c>
        /// ("Creates a new graph asset of type T at the specified file path"). Returns the graph
        /// as <c>object</c>: its type lives in an assembly this one cannot name.
        ///
        /// Overwrites an existing file at the path, so every caller here checks first.</summary>
        internal static object CreateGraph(string assetPath, out string error)
        {
            if (!TryResolve(out error))
                return null;

            EnsureFolder(DirectoryOf(assetPath));

            var method = RequireMethod(s_GraphDatabaseType, new[] { "CreateGraph" }, 1, ref error);
            if (method == null)
                return null;

            try
            {
                return method.MakeGenericMethod(s_GraphType).Invoke(null, new object[] { assetPath });
            }
            catch (Exception exception)
            {
                error = Describe("GraphDatabase.CreateGraph", exception);
                return null;
            }
        }

        /// <summary>Load the graph instance for an existing asset —
        /// <c>GraphDatabase.LoadGraph&lt;T&gt;(string)</c>. Returns the IN-MEMORY graph, which is
        /// what authoring wants; the import pipeline uses LoadGraphForImporter instead.</summary>
        internal static object LoadGraph(string assetPath, out string error)
        {
            if (!TryResolve(out error))
                return null;

            var method = RequireMethod(s_GraphDatabaseType, new[] { "LoadGraph" }, 1, ref error);
            if (method == null)
                return null;

            try
            {
                return method.MakeGenericMethod(s_GraphType).Invoke(null, new object[] { assetPath });
            }
            catch (Exception exception)
            {
                error = Describe("GraphDatabase.LoadGraph", exception);
                return null;
            }
        }

        /// <summary>Create the asset and put the Project Browser in rename mode —
        /// <c>GraphDatabase.PromptInProjectBrowserToCreateNewAsset&lt;T&gt;(string)</c>. Naming a
        /// graph is half of deciding what it is for, so this is the right creation gesture from
        /// the Flow window even though the asset lands wherever the Project window is pointed.</summary>
        internal static bool PromptCreate(string defaultName, out string error)
        {
            if (!TryResolve(out error))
                return false;

            var method = RequireMethod(s_GraphDatabaseType,
                new[] { "PromptInProjectBrowserToCreateNewAsset" }, 1, ref error);
            if (method == null)
                return false;

            try
            {
                method.MakeGenericMethod(s_GraphType).Invoke(null, new object[] { defaultName });
                return true;
            }
            catch (Exception exception)
            {
                error = Describe("GraphDatabase.PromptInProjectBrowserToCreateNewAsset", exception);
                return false;
            }
        }

        /// <summary>Persist pending edits — <c>GraphDatabase.SaveGraph(Graph)</c> (0.5) or
        /// <c>SaveGraphIfDirty</c> (0.4). Both end in AssetDatabase.SaveAssetIfDirty on the
        /// backing object, which does nothing for an object Unity does not think has changed, so
        /// the object is marked dirty first.</summary>
        internal static void SaveGraph(object graph)
        {
            if (graph == null || !TryResolve(out _))
                return;

            var graphObject = Authoring.GetGraphObject(graph) as UnityEngine.Object;
            if (graphObject != null)
                EditorUtility.SetDirty(graphObject);

            var method = FindMethod(s_GraphDatabaseType, k_SaveNames, 1);
            if (method == null)
                return;

            try
            {
                method.Invoke(null, new[] { graph });
            }
            catch (Exception)
            {
                // Best-effort: the caller has already reported what it built and the asset is on
                // disk from CreateGraph. A throw here must not lose that report.
            }
        }

        /// <summary>Node count — <c>Graph.NodeCount</c>. -1 when the graph could not be read at
        /// all, which callers treat as "unknown", never as "empty".</summary>
        internal static int NodeCount(object graph)
        {
            if (graph == null)
                return -1;

            var property = FindProperty(graph.GetType(), k_NodeCountNames);
            if (property == null)
                return -1;

            try
            {
                return (int)property.GetValue(graph);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>Every node in the graph — <c>Graph.GetNodes()</c>. Block nodes are excluded
        /// by design: they are reached through their parent context node.</summary>
        internal static IEnumerable GetNodes(object graph)
        {
            if (graph == null)
                return Array.Empty<object>();

            var method = FindMethod(graph.GetType(), new[] { "GetNodes" }, 0);
            if (method == null)
                return Array.Empty<object>();

            try
            {
                return method.Invoke(graph, Array.Empty<object>()) as IEnumerable
                       ?? Array.Empty<object>();
            }
            catch (Exception)
            {
                return Array.Empty<object>();
            }
        }

        /// <summary>Open the graph asset in its editor window. Plain AssetDatabase — a graph asset
        /// is an asset, and its importer registers the window.</summary>
        internal static bool OpenGraphAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return false;

            AssetDatabase.OpenAsset(asset);
            EditorGUIUtility.PingObject(asset);
            return true;
        }

        // --- bake ----------------------------------------------------------------------------

        /// <summary>Run an explicit bake method on the frontend, if it has one. Matched by SHAPE —
        /// any public static "Bake" whose first parameter accepts the graph and which returns a
        /// <see cref="StateTreeAsset"/> — so the frontend's exact signature is not pinned here.
        ///
        /// A frontend that bakes on IMPORT instead (the pattern both Graph Toolkit samples use,
        /// and the one this project ships) has no such method: its bake returns a richer result
        /// and the tree reaches the project as the graph file's main asset. Callers try the
        /// import route first for that reason; this is the secondary path.</summary>
        internal static StateTreeAsset Bake(object graph, string bakedAssetPath, out string error)
        {
            error = null;
            if (graph == null)
            {
                error = "No graph to bake.";
                return null;
            }

            var bakerType = FindType(BakerTypeName);
            if (bakerType == null)
            {
                error = $"'{BakerTypeName}' was not found.";
                return null;
            }

            var methods = bakerType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            MethodInfo best = null;
            var bestParameters = 0;
            for (var i = 0; i < methods.Length; ++i)
            {
                if (!string.Equals(methods[i].Name, "Bake", StringComparison.Ordinal))
                    continue;

                var parameters = methods[i].GetParameters();
                if (parameters.Length < 1 || parameters.Length > 2)
                    continue;
                if (!parameters[0].ParameterType.IsInstanceOfType(graph))
                    continue;
                if (parameters.Length == 2 && parameters[1].ParameterType != typeof(string))
                    continue;
                if (!typeof(StateTreeAsset).IsAssignableFrom(methods[i].ReturnType))
                    continue;

                // Prefer the explicit-path overload: it says where the asset goes instead of
                // letting the baker guess from the graph's own location.
                if (best == null || parameters.Length > bestParameters)
                {
                    best = methods[i];
                    bestParameters = parameters.Length;
                }
            }

            if (best == null)
            {
                error = $"'{BakerTypeName}' has no public static Bake(graph[, string path]) " +
                        $"returning a {nameof(StateTreeAsset)} (it may bake on import instead). " +
                        $"It declares: {DescribeMethods(bakerType, "Bake")}";
                return null;
            }

            try
            {
                var arguments = bestParameters == 2
                    ? new object[] { graph, bakedAssetPath }
                    : new object[] { graph };
                return best.Invoke(null, arguments) as StateTreeAsset;
            }
            catch (Exception exception)
            {
                error = Describe($"{BakerTypeName}.Bake", exception);
                return null;
            }
        }

        // --- resolution ----------------------------------------------------------------------

        private static string Resolve()
        {
            s_GraphType = FindType(GraphTypeName);
            if (s_GraphType == null)
            {
                return $"The M7 graph frontend is not loaded: '{GraphTypeName}' was not found. " +
                       "It ships in the PowerOfFire.DrawToPlay.GraphEditor assembly; if that " +
                       "assembly failed to compile, the Console will say why.";
            }

            s_GraphDatabaseType = FindType(k_GraphDatabaseTypeName);
            if (s_GraphDatabaseType == null)
            {
                return $"Graph Toolkit is not loaded: '{k_GraphDatabaseTypeName}' was not found, " +
                       "even though the frontend assembly is present.";
            }

            var attributeType = FindType(k_GraphAttributeTypeName);
            if (attributeType == null)
                return $"'{k_GraphAttributeTypeName}' was not found.";

            var attribute = Attribute.GetCustomAttribute(s_GraphType, attributeType, false);
            if (attribute == null)
            {
                return $"'{GraphTypeName}' carries no [Graph] attribute, so it has no asset " +
                       "extension and no importer.";
            }

            var extensionProperty = FindProperty(attributeType, k_ExtensionNames);
            var declared = extensionProperty != null
                ? extensionProperty.GetValue(attribute) as string
                : null;

            if (string.IsNullOrEmpty(declared))
                return $"'{GraphTypeName}' declares an empty [Graph] extension.";

            s_Extension = declared.TrimStart('.');
            return null;
        }

        // --- authoring -------------------------------------------------------------------------

        /// <summary>
        /// The write half of the bridge, over Graph Toolkit's public authoring API:
        /// <c>Graph.AddNode(Node)</c>, <c>Graph.Connect(IPort output, IPort input)</c>,
        /// <c>ContextNode.CreateBlockNode(Type, int index = -1)</c>, <c>IPort.TrySetValue&lt;T&gt;</c>,
        /// <c>Node.DefineNode()</c>, <c>Node.Position</c> — every one of them read out of
        /// UnityEditor.GraphToolkitModule.xml for 0.5.0-exp.1.
        ///
        /// Where 0.4.0-exp.2 had no public equivalent (it had no authoring API at all) a fallback
        /// through its internal model follows, cited to the source file it was read from. Callers
        /// must treat a false return as "ask the user to author it by hand", never as a silent
        /// no-op: on the canvas an unwired node looks like a decision someone made.
        ///
        /// These calls bypass the graph editor's own undo. That is acceptable for a menu command
        /// that builds an asset from scratch — the sole caller — and would be wrong inside an
        /// interactive tool, which should bracket edits with Graph.UndoBeginRecordGraph /
        /// UndoEndRecordGraph instead.
        /// </summary>
        internal static class Authoring
        {
            /// <summary>Preflight: can this graph be written to at all? Callers use it so a
            /// failure is one clear message before anything is built, rather than a half-authored
            /// graph the user then has to clean up.</summary>
            internal static bool IsAvailable(object graph, out string error)
            {
                error = null;
                if (graph == null)
                {
                    error = "No graph.";
                    return false;
                }

                var hasAdd = FindMethod(graph.GetType(), new[] { "AddNode" }, 1) != null;
                var hasConnect = FindMethod(graph.GetType(), new[] { "Connect" }, 2) != null;
                if (hasAdd && hasConnect)
                    return true;

                var model = GetGraphModel(graph);
                if (model != null &&
                    FindMethod(model.GetType(), new[] { "CreateNodeModel" }, 2) != null &&
                    FindCreateWire(model.GetType()) != null)
                    return true;

                error = "This Graph Toolkit has no authoring API this command knows how to use: " +
                        "neither Graph.AddNode/Graph.Connect (0.5) nor the 0.4 internal model. " +
                        "Author the graph by hand instead.";
                return false;
            }

            /// <summary>Add a node and place it. Returns the <c>Node</c> instance — the object the
            /// public API talks to, and the one the caller keeps to wire and configure.</summary>
            internal static object AddNode(object graph, Type nodeType, Vector2 position,
                out string error)
            {
                error = null;

                object node;
                try
                {
                    node = Activator.CreateInstance(nodeType, true);
                }
                catch (Exception exception)
                {
                    error = Describe($"new {nodeType.Name}()", exception);
                    return null;
                }

                var add = FindMethod(graph.GetType(), new[] { "AddNode" }, 1);
                if (add != null)
                {
                    try
                    {
                        add.Invoke(graph, new[] { node });
                        SetPosition(node, position);
                        // Ports and options exist only after a definition pass, and callers wire
                        // and configure the returned node immediately.
                        DefineNode(node);
                        return node;
                    }
                    catch (Exception exception)
                    {
                        error = Describe($"Graph.AddNode({nodeType.Name})", exception);
                        return null;
                    }
                }

                // 0.4.0-exp.2: GraphModelImp.CreateNodeModel(Node, Vector2)
                // (GraphToolkitPublic/Implementation/GraphModelImp.cs:96), which dispatches on the
                // instance type and calls InitCustomNode to bind instance ↔ model.
                var model = GetGraphModel(graph);
                var create = model != null
                    ? FindMethod(model.GetType(), new[] { "CreateNodeModel" }, 2)
                    : null;
                if (create == null)
                {
                    error = "Neither Graph.AddNode(Node) nor GraphModelImp.CreateNodeModel was found.";
                    return null;
                }

                try
                {
                    create.Invoke(model, new[] { node, (object)position });
                    return node;
                }
                catch (Exception exception)
                {
                    error = Describe($"CreateNodeModel({nodeType.Name})", exception);
                    return null;
                }
            }

            /// <summary>Add a block inside a context node —
            /// <c>ContextNode.CreateBlockNode(Type, int index = -1)</c> ("Creates and inserts a
            /// new BlockNode into this context node… -1 appends the block at the bottom"). The
            /// framework rejects a block the context does not accept, which is §7.3's "invalid
            /// wiring is impossible" enforced by the toolkit rather than by us.</summary>
            internal static object AddBlock(object graph, object contextNode, Type blockType,
                out string error)
            {
                error = null;
                if (contextNode == null)
                {
                    error = "No context node to add a block to.";
                    return null;
                }

                var create = FindMethod(contextNode.GetType(), new[] { "CreateBlockNode" }, 2);
                if (create != null)
                {
                    try
                    {
                        // Returns void (verified against UnityEditor.GraphToolkitModule), so the
                        // block is read back off the context: index -1 appended it, and
                        // BlockCount/GetBlock are the public way to reach it.
                        create.Invoke(contextNode, new object[] { blockType, -1 });
                        var created = LastBlock(contextNode);
                        if (created == null)
                        {
                            error = $"CreateBlockNode({blockType.Name}) added nothing to " +
                                    $"{contextNode.GetType().Name}.";
                            return null;
                        }

                        DefineNode(created);
                        return created;
                    }
                    catch (Exception exception)
                    {
                        error = Describe($"CreateBlockNode({blockType.Name})", exception);
                        return null;
                    }
                }

                var add = FindMethod(contextNode.GetType(), new[] { "AddBlockNode" }, 1);
                if (add != null)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(blockType, true);
                        add.Invoke(contextNode, new[] { instance });
                        return instance;
                    }
                    catch (Exception exception)
                    {
                        error = Describe($"AddBlockNode({blockType.Name})", exception);
                        return null;
                    }
                }

                // 0.4.0-exp.2: CreateNodeModel makes the block model but does not parent it
                // (GraphModelImp.cs:104); ContextNodeModel.InsertBlock does
                // (Model/BasicModel/ContextNodeModel.cs:66).
                var model = GetGraphModel(graph);
                var contextModel = GetNodeModel(contextNode);
                var createModel = model != null
                    ? FindMethod(model.GetType(), new[] { "CreateNodeModel" }, 2)
                    : null;
                var insert = contextModel != null
                    ? FindMethod(contextModel.GetType(), new[] { "InsertBlock" }, 3)
                    : null;
                if (createModel == null || insert == null)
                {
                    error = "No way to add a block was found: neither ContextNode.CreateBlockNode / " +
                            "AddBlockNode (0.5) nor CreateNodeModel + InsertBlock (0.4).";
                    return null;
                }

                try
                {
                    var block = Activator.CreateInstance(blockType, true);
                    var blockModel = createModel.Invoke(model, new[] { block, (object)Vector2.zero });
                    // SpawnFlags is internal; Default == None == 0 (GraphModel.cs:17-29).
                    var spawnFlags = insert.GetParameters()[2].ParameterType;
                    insert.Invoke(contextModel,
                        new[] { blockModel, (object)(-1), Enum.ToObject(spawnFlags, 0) });
                    return block;
                }
                catch (Exception exception)
                {
                    error = Describe($"InsertBlock({blockType.Name})", exception);
                    return null;
                }
            }

            /// <summary>Wire an output port to an input port — <c>Graph.Connect(IPort output,
            /// IPort input)</c>, which returns false only when the wire already existed. Ports
            /// come from the public <c>GetOutputPortByName</c> / <c>GetInputPortByName</c>.</summary>
            internal static bool Connect(object graph, object fromNode, string fromPort,
                object toNode, string toPort, out string error)
            {
                error = null;

                var output = GetPort(fromNode, "GetOutputPortByName", fromPort, ref error);
                var input = GetPort(toNode, "GetInputPortByName", toPort, ref error);
                if (output == null || input == null)
                    return false;

                var connect = FindMethod(graph.GetType(), new[] { "Connect" }, 2);
                if (connect != null)
                {
                    try
                    {
                        connect.Invoke(graph, new[] { output, input });
                        return true;
                    }
                    catch (Exception exception)
                    {
                        error = Describe($"Graph.Connect({fromPort} → {toPort})", exception);
                        return false;
                    }
                }

                // 0.4.0-exp.2: GraphModel.CreateWire(PortModel toPort, PortModel fromPort,
                // Hash128 guid) (Model/BasicModel/GraphModel.cs:2188). Destination FIRST.
                var model = GetGraphModel(graph);
                var createWire = model != null ? FindCreateWire(model.GetType()) : null;
                if (createWire == null)
                {
                    error = "Neither Graph.Connect nor GraphModel.CreateWire was found.";
                    return false;
                }

                try
                {
                    createWire.Invoke(model, new[] { input, output, (object)new Hash128() });
                    return true;
                }
                catch (Exception exception)
                {
                    error = Describe($"CreateWire({fromPort} → {toPort})", exception);
                    return false;
                }
            }

            /// <summary>
            /// Give a node's parameter a value, by whichever route the node class exposes it. The
            /// baker accepts all three and looks them up in this order too ("pulls each one from
            /// the node option, input port or node field of that name"), so writing them in the
            /// same order cannot produce a graph that bakes differently from how it reads:
            /// <list type="bullet">
            /// <item>a serialized FIELD on the node class — set directly;</item>
            /// <item>an input PORT of that name — <c>IPort.TrySetValue&lt;T&gt;</c>, "sets a new
            /// value to the port's UI field";</item>
            /// <item>a node OPTION of that name — no public setter exists in either release, so
            /// this reaches the option's backing port (0.5) or its embedded constant (0.4).</item>
            /// </list>
            /// </summary>
            internal static bool SetOption(object node, string optionName, object value,
                out string error)
            {
                error = null;
                if (node == null)
                {
                    error = "No node to configure.";
                    return false;
                }

                if (TrySetField(node, optionName, value, ref error))
                    return true;

                var port = GetPortOrNull(node, "GetInputPortByName", optionName);
                if (port != null && TrySetPortValue(port, value, ref error))
                    return true;

                var option = GetOptionOrNull(node, optionName);
                if (option != null && TrySetOptionValue(option, value, ref error))
                    return true;

                if (string.IsNullOrEmpty(error))
                {
                    error = $"{node.GetType().Name} exposes no field, input port or node option " +
                            $"named \"{optionName}\". It declares options " +
                            $"[{DescribeOptions(node)}] and ports {DescribePorts(node)}.";
                }

                return false;
            }

            /// <summary>Re-run the node's own port/option definition after its parameters changed
            /// — <c>Node.DefineNode()</c>. Needed because options are allowed to change a node's
            /// topology, so a port count that depends on one is only correct after this.</summary>
            internal static void DefineNode(object node)
            {
                var method = FindMethod(node.GetType(), new[] { "DefineNode" }, 0);
                if (method == null)
                    return;

                try
                {
                    method.Invoke(node, Array.Empty<object>());
                }
                catch (Exception)
                {
                    // A refresh, not a mutation: a throw leaves the node as it was and the
                    // caller's own wiring pass reports anything actually missing.
                }
            }

            /// <summary>The ScriptableObject backing the graph asset, used only to mark it dirty
            /// before a save. Null on a release that does not expose it — <see cref="SaveGraph"/>
            /// still runs, it just relies on the toolkit's own dirty tracking.</summary>
            internal static object GetGraphObject(object graph)
            {
                var model = GetGraphModel(graph);
                if (model == null)
                    return null;

                var property = FindProperty(model.GetType(), new[] { "GraphObject" });
                try
                {
                    return property != null ? property.GetValue(model) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static bool TrySetField(object node, string name, object value, ref string error)
            {
                var field = FindField(node.GetType(), name);
                if (field == null)
                    return false;

                try
                {
                    field.SetValue(node, Coerce(value, field.FieldType));
                    return true;
                }
                catch (Exception exception)
                {
                    error = Describe($"{node.GetType().Name}.{name} = {value}", exception);
                    return false;
                }
            }

            /// <summary><c>IPort.TrySetValue&lt;T&gt;(T)</c>, closed over the value's own type
            /// (or the port's declared type when they differ, which is what a float option fed an
            /// int literal would hit).</summary>
            private static bool TrySetPortValue(object port, object value, ref string error)
            {
                var method = FindMethod(port.GetType(), new[] { "TrySetValue" }, 1);
                if (method == null || !method.IsGenericMethodDefinition)
                    return false;

                var declared = ReadTypeProperty(port, new[] { "DataType", "dataType" });
                var target = declared ?? (value != null ? value.GetType() : typeof(object));

                try
                {
                    var closed = method.MakeGenericMethod(target);
                    return closed.Invoke(port, new[] { Coerce(value, target) }) is bool ok && ok;
                }
                catch (Exception exception)
                {
                    error = Describe("IPort.TrySetValue", exception);
                    return false;
                }
            }

            /// <summary>Node options have no public setter in either release. 0.5's option is
            /// backed by a port, so the port's own TrySetValue does the job; 0.4's is backed by a
            /// Constant reached through NodeOption.PortModel → PortModel.EmbeddedValue →
            /// Constant.ObjectValue (NodeOption.cs:22, PortModel.cs:644, Constant.cs:31) — the
            /// same chain its TryGetValue reads back through.</summary>
            private static bool TrySetOptionValue(object option, object value, ref string error)
            {
                if (TrySetPortValue(option, value, ref error))
                    return true;

                var portProperty = FindProperty(option.GetType(), new[] { "PortModel", "Port", "port" });
                object port = null;
                try
                {
                    port = portProperty != null ? portProperty.GetValue(option) : null;
                }
                catch (Exception)
                {
                    // fall through to the failure message
                }

                if (port == null)
                    return false;

                if (TrySetPortValue(port, value, ref error))
                    return true;

                var constantProperty = FindProperty(port.GetType(), new[] { "EmbeddedValue" });
                try
                {
                    var constant = constantProperty != null ? constantProperty.GetValue(port) : null;
                    if (constant == null)
                        return false;

                    var objectValue = FindProperty(constant.GetType(), new[] { "ObjectValue" });
                    if (objectValue == null || !objectValue.CanWrite)
                        return false;

                    objectValue.SetValue(constant, value);
                    return true;
                }
                catch (Exception exception)
                {
                    error = Describe("Constant.ObjectValue", exception);
                    return false;
                }
            }

            /// <summary>The last block of a context node — <c>ContextNode.BlockCount</c> +
            /// <c>GetBlock(int)</c> — which is the one an append just added.</summary>
            private static object LastBlock(object contextNode)
            {
                var countProperty = FindProperty(contextNode.GetType(),
                    new[] { "BlockCount", "blockCount" });
                var getBlock = FindMethod(contextNode.GetType(), new[] { "GetBlock" }, 1);
                if (countProperty == null || getBlock == null)
                    return null;

                try
                {
                    var count = (int)countProperty.GetValue(contextNode);
                    return count > 0 ? getBlock.Invoke(contextNode, new object[] { count - 1 }) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static void SetPosition(object node, Vector2 position)
            {
                var property = FindProperty(node.GetType(), k_PositionNames);
                if (property == null || !property.CanWrite)
                    return;

                try
                {
                    property.SetValue(node, position);
                }
                catch (Exception)
                {
                    // Layout only. A node in the wrong place still bakes correctly.
                }
            }

            /// <summary>Widen the literals this file writes to whatever the target actually is —
            /// an int written into a float field, mostly. Never a lossy or surprising conversion:
            /// anything that is not already assignable and not convertible is passed through for
            /// the setter to reject with a real message.</summary>
            private static object Coerce(object value, Type target)
            {
                if (value == null || target == null || target.IsInstanceOfType(value))
                    return value;

                try
                {
                    if (target.IsEnum && value is IConvertible)
                        return Enum.ToObject(target, value);
                    if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
                        return Convert.ChangeType(value, target);
                }
                catch (Exception)
                {
                    return value;
                }

                return value;
            }

            private static object GetGraphModel(object graph)
            {
                if (graph == null)
                    return null;

                var field = FindField(graph.GetType(), "m_Implementation");
                try
                {
                    return field != null ? field.GetValue(graph) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static object GetNodeModel(object node)
            {
                if (node == null)
                    return null;

                var field = FindField(node.GetType(), "m_Implementation");
                try
                {
                    return field != null ? field.GetValue(node) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static object GetPort(object node, string accessor, string portName,
                ref string error)
            {
                if (node == null)
                {
                    error = "Cannot connect a null node.";
                    return null;
                }

                var port = GetPortOrNull(node, accessor, portName);
                if (port == null)
                {
                    error = $"{node.GetType().Name} has no port named \"{portName}\" " +
                            $"({accessor}). It declares: {DescribePorts(node)}";
                }

                return port;
            }

            private static object GetPortOrNull(object node, string accessor, string portName)
            {
                var method = FindMethod(node.GetType(), new[] { accessor }, 1);
                if (method == null)
                    return null;

                try
                {
                    return method.Invoke(node, new object[] { portName });
                }
                catch (Exception)
                {
                    // 0.4 indexes ports by name into a dictionary and throws on a miss; 0.5
                    // returns null. Both mean "no such port".
                    return null;
                }
            }

            private static object GetOptionOrNull(object node, string optionName)
            {
                var method = FindMethod(node.GetType(), new[] { "GetNodeOptionByName" }, 1);
                if (method == null)
                    return null;

                try
                {
                    return method.Invoke(node, new object[] { optionName });
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private static Type ReadTypeProperty(object instance, string[] names)
            {
                var property = FindProperty(instance.GetType(), names);
                try
                {
                    return property != null ? property.GetValue(instance) as Type : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            /// <summary>0.4's three-parameter CreateWire (destination, source, guid) rather than
            /// its five-parameter typed overload (GraphModel.cs:2188 vs :2202).</summary>
            private static MethodInfo FindCreateWire(Type modelType)
            {
                while (modelType != null)
                {
                    var methods = modelType.GetMethods(BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly);
                    for (var i = 0; i < methods.Length; ++i)
                    {
                        if (!string.Equals(methods[i].Name, "CreateWire", StringComparison.Ordinal))
                            continue;

                        var parameters = methods[i].GetParameters();
                        if (parameters.Length == 3 && parameters[2].ParameterType == typeof(Hash128))
                            return methods[i];
                    }

                    modelType = modelType.BaseType;
                }

                return null;
            }
        }

        // --- diagnostics -----------------------------------------------------------------------

        /// <summary>Port names on an instance, in display order (<c>GetInputPorts</c> /
        /// <c>GetOutputPorts</c>, then <c>IPort.Name</c>). Callers use it to pick a port without
        /// having guessed its name: a node with exactly one port in a direction is unambiguous.</summary>
        internal static List<string> PortNames(object node, bool output)
        {
            var names = new List<string>();
            if (node == null)
                return names;

            var method = FindMethod(node.GetType(),
                new[] { output ? "GetOutputPorts" : "GetInputPorts" }, 0);
            if (method == null)
                return names;

            try
            {
                var ports = method.Invoke(node, Array.Empty<object>()) as IEnumerable;
                if (ports == null)
                    return names;

                foreach (var port in ports)
                {
                    if (port == null)
                        continue;

                    var property = FindProperty(port.GetType(), k_NameNames);
                    var value = property != null ? property.GetValue(port) as string : null;
                    if (!string.IsNullOrEmpty(value))
                        names.Add(value);
                }
            }
            catch (Exception)
            {
                names.Clear();
            }

            return names;
        }

        /// <summary>Comma-separated node option names on an instance (<c>Node.NodeOptions</c>),
        /// for "you asked for X, it has Y" errors.</summary>
        internal static string DescribeOptions(object node)
        {
            var property = FindProperty(node.GetType(), k_NodeOptionsNames);
            if (property == null)
                return "(unknown)";

            try
            {
                return JoinNames(property.GetValue(node) as IEnumerable);
            }
            catch (Exception)
            {
                return "(unknown)";
            }
        }

        /// <summary>Comma-separated input and output port names on an instance.</summary>
        internal static string DescribePorts(object node)
        {
            return $"in [{string.Join(", ", PortNames(node, false))}], " +
                   $"out [{string.Join(", ", PortNames(node, true))}]";
        }

        /// <summary>Every non-abstract type in the frontend assembly, so a name mismatch between a
        /// caller's pins and what the frontend actually shipped is a one-line fix rather than a
        /// hunt.</summary>
        internal static string DescribeFrontendTypes()
        {
            var graph = graphType;
            if (graph == null)
                return "(the frontend assembly is not loaded)";

            var builder = new StringBuilder();
            Type[] types;
            try
            {
                types = graph.Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException loadException)
            {
                types = loadException.Types ?? Array.Empty<Type>();
            }

            for (var i = 0; i < types.Length; ++i)
            {
                if (types[i] == null || types[i].IsAbstract || !types[i].IsClass)
                    continue;

                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(types[i].Name);
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string DescribeMethods(Type type, string name)
        {
            var builder = new StringBuilder();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (var i = 0; i < methods.Length; ++i)
            {
                if (!string.Equals(methods[i].Name, name, StringComparison.Ordinal))
                    continue;

                if (builder.Length > 0)
                    builder.Append("; ");
                builder.Append(methods[i].ToString());
            }

            return builder.Length > 0 ? builder.ToString() : "(no method of that name)";
        }

        private static string JoinNames(IEnumerable items)
        {
            if (items == null)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var info = FindProperty(item.GetType(), k_NameNames);
                var value = info != null ? info.GetValue(item) as string : null;
                if (string.IsNullOrEmpty(value))
                    continue;

                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(value);
            }

            return builder.ToString();
        }

        /// <summary>Unwrap the TargetInvocationException reflection wraps everything in — the
        /// inner message is the one worth reading.</summary>
        private static string Describe(string call, Exception exception)
        {
            var inner = exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException
                : exception;
            return $"{call} failed: {inner.GetType().Name}: {inner.Message}";
        }

        // --- reflection plumbing -----------------------------------------------------------------

        /// <summary>Type lookup across every loaded assembly. Type.GetType alone only finds types
        /// in this assembly or in mscorlib unless the name is assembly-qualified, and neither the
        /// frontend nor Graph Toolkit is referenced here.</summary>
        private static Type FindType(string fullName)
        {
            var direct = Type.GetType(fullName, false);
            if (direct != null)
                return direct;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; ++i)
            {
                Type candidate;
                try
                {
                    candidate = assemblies[i].GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private static MethodInfo RequireMethod(Type type, string[] names, int parameterCount,
            ref string error)
        {
            var method = FindMethod(type, names, parameterCount);
            if (method == null)
            {
                error = $"'{type.FullName}.{names[0]}' with {parameterCount} parameter(s) was not " +
                        "found — Graph Toolkit's API has changed.";
            }

            return method;
        }

        /// <summary>Walk the hierarchy explicitly: reflection does not return a base type's
        /// private members from a derived type, and the members this bridge needs are spread
        /// across a node subclass, Node, and the graph's model.</summary>
        private static MethodInfo FindMethod(Type type, string[] names, int parameterCount)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            for (var n = 0; n < names.Length; ++n)
            {
                var cursor = type;
                while (cursor != null)
                {
                    var methods = cursor.GetMethods(flags);
                    for (var i = 0; i < methods.Length; ++i)
                    {
                        if (string.Equals(methods[i].Name, names[n], StringComparison.Ordinal) &&
                            methods[i].GetParameters().Length == parameterCount)
                            return methods[i];
                    }

                    cursor = cursor.BaseType;
                }
            }

            // Interface members: an instance handed back as IPort/INodeOption may implement them
            // explicitly, in which case they are not on the class's own member list.
            var interfaces = type.GetInterfaces();
            for (var n = 0; n < names.Length; ++n)
            {
                for (var i = 0; i < interfaces.Length; ++i)
                {
                    var method = interfaces[i].GetMethod(names[n],
                        BindingFlags.Public | BindingFlags.Instance);
                    if (method != null && method.GetParameters().Length == parameterCount)
                        return method;
                }
            }

            return null;
        }

        private static PropertyInfo FindProperty(Type type, string[] names)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            for (var n = 0; n < names.Length; ++n)
            {
                var cursor = type;
                while (cursor != null)
                {
                    var property = cursor.GetProperty(names[n], flags);
                    if (property != null)
                        return property;

                    cursor = cursor.BaseType;
                }
            }

            var interfaces = type.GetInterfaces();
            for (var n = 0; n < names.Length; ++n)
            {
                for (var i = 0; i < interfaces.Length; ++i)
                {
                    var property = interfaces[i].GetProperty(names[n],
                        BindingFlags.Public | BindingFlags.Instance);
                    if (property != null)
                        return property;
                }
            }

            return null;
        }

        /// <summary>A settable, serialized field on a node class — public, or private with
        /// [SerializeField], because both are what Unity persists and what the baker reads.</summary>
        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly;

            while (type != null)
            {
                var field = type.GetField(name, flags);
                if (field != null &&
                    (field.IsPublic || field.IsDefined(typeof(SerializeField), false)))
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        // --- assets -----------------------------------------------------------------------------

        /// <summary>Create every missing folder along an "Assets/a/b" path (the
        /// <see cref="CharacterFlowAsset"/> helper, kept local so the two do not depend on each
        /// other's private members).</summary>
        internal static string EnsureFolder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
                return string.Empty;

            var segments = assetFolderPath.Split('/');
            var path = segments[0];
            for (var i = 1; i < segments.Length; ++i)
            {
                var next = $"{path}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    return path;
                path = next;
            }

            return path;
        }

        private static string DirectoryOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return string.Empty;

            var directory = System.IO.Path.GetDirectoryName(assetPath);
            return directory != null ? directory.Replace('\\', '/') : string.Empty;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Entity";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; ++i)
            {
                var c = name[i];
                var bad = c == '.';
                for (var j = 0; j < invalid.Length && !bad; ++j)
                    bad = invalid[j] == c;

                builder.Append(bad ? '_' : c);
            }

            var result = builder.ToString().Trim();
            return result.Length > 0 ? result : "Entity";
        }
    }
}
