using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// MAKES "IT MUST BE A ROW THAT EXISTS" TRUE on a canvas, for every typed reference
    /// (<see cref="StateTreeEntryRef{TEntry}"/>) a task or condition declares.
    ///
    /// THE PROBLEM THIS SOLVES. In the regular Inspector a typed reference is a DROPDOWN
    /// (<c>StateTreeEntryRefDrawer</c>) — the author picks a row and cannot type a name that is
    /// not one. On a canvas the same field is a bare text box, because a Graph Toolkit port's
    /// editor is chosen from a fixed type→widget table
    /// (<c>CustomizableModelPropertyField.CreateDefaultFieldForType</c>) and a dynamic dropdown
    /// needs its <c>EnumAttribute</c>, which is internal and <c>[UnityRestricted]</c>. There is no
    /// public seam to put a picker in the node. So the guarantee is moved from the widget to the
    /// graph: a name that is not a row is a graph ERROR — a red badge on the node and a line in
    /// the Error Notifications toolbar — and the message names every row that WOULD have been
    /// legal, so the list is in front of the author at the moment they need it.
    ///
    /// WHAT IS CHECKED, AND WHAT IS DELIBERATELY NOT.
    /// <list type="bullet">
    /// <item>A non-empty name that matches no row in scope → ERROR. This is the whole point.</item>
    /// <item>An EMPTY name → nothing. Unset is the task's own business: <c>Give Item</c> fails on
    /// it, a nullable reference does not, and this class does not know which.</item>
    /// <item>A port wired to a variable or constant → the wired value is checked, because that is
    /// the value the bake will read (<see cref="LibraryParameterPorts.TryReadValue"/>). A port
    /// wired to something that carries no value is skipped rather than guessed at.</item>
    /// <item>An entry class NO in-scope registry answers for → WARNING, not an error, and worded
    /// as the wiring gap it is. "I cannot check this" and "this is wrong" must not look the
    /// same.</item>
    /// </list>
    /// </summary>
    public static class EntryRefValidator
    {
        /// <summary>
        /// Report every typed reference on the canvas that does not name a real row.
        /// </summary>
        /// <param name="graph">The canvas being validated — also what the registry scope is
        /// resolved from.</param>
        /// <param name="nodes">The graph's nodes, already collected by the caller (both canvases
        /// walk them for their own checks and there is no reason to walk twice).</param>
        /// <param name="graphLogger">Sink for the errors and warnings shown on the graph.</param>
        public static void Validate(Graph graph, IReadOnlyList<INode> nodes,
            GraphLogger graphLogger)
        {
            if (graph == null || nodes == null || graphLogger == null)
                return;

            GraphRegistryScope scope = null;
            var unanswered = new List<Type>();

            for (int i = 0; i < nodes.Count; i++)
            {
                INode node = nodes[i];

                if (node is RegistryEntryNode)
                {
                    scope ??= GraphRegistryScope.For(graph);
                    CheckRegistryEntryNode(node, scope, graphLogger);
                    continue;
                }

                Type libraryType = LibraryParameterPorts.LibraryTypeOf(node);
                if (libraryType == null)
                    continue;

                IReadOnlyList<FieldInfo> fields =
                    LibraryParameterPorts.GetParameterFields(libraryType);
                for (int f = 0; f < fields.Count; f++)
                {
                    Type entryType = LibraryParameterPorts.EntryTypeOf(fields[f].FieldType);
                    if (entryType == null)
                        continue;

                    // Resolved on first need, so a graph with no typed references anywhere never
                    // touches the AssetDatabase.
                    scope ??= GraphRegistryScope.For(graph);
                    CheckPort(node, fields[f], entryType, scope, graphLogger, unanswered);
                }
            }
        }

        /// <summary>
        /// A <see cref="RegistryEntryNode"/>'s chosen row.
        ///
        /// TWO DIFFERENT PROBLEMS, TOLD APART. The node's dropdown offers the rows of whatever
        /// registry its slot points at — any registry in the project — while the graph may only
        /// REACH the ones its owning registry declares (<see cref="GraphRegistryScope"/>). So a
        /// row can be perfectly real and still not be data this graph is allowed to name, and the
        /// two cases need opposite fixes:
        /// <list type="bullet">
        /// <item>The row is not in the chosen registry at all → the pick is stale (renamed, or
        /// left over from another catalog). Fix: pick again, and the rows are listed.</item>
        /// <item>The row is real but its registry is not reachable → the DECLARATION is missing.
        /// Fix: add that registry to the Depends On of the registry that owns this graph. Saying
        /// "not a row of any registry this graph can reach" here would be true and useless — the
        /// author is looking at the row.</item>
        /// </list>
        ///
        /// THIS CANNOT LIVE IN THE BAKE. The importer runs with the AssetDatabase closed, so the
        /// scope comes back empty there — measured — and every correct graph would be reported.
        /// Here it is open, and this runs on every graph change.
        /// </summary>
        /// <param name="node">The Registry Entry node.</param>
        /// <param name="scope">The registries this graph may name.</param>
        /// <param name="graphLogger">Sink for the report.</param>
        private static void CheckRegistryEntryNode(INode node, GraphRegistryScope scope,
            GraphLogger graphLogger)
        {
            IPort port = node.GetInputPortByName(RegistryEntryNode.EntryPortName);
            if (!LibraryParameterPorts.TryReadValue(port, typeof(string), out object read)
                || !(read is string name) || string.IsNullOrEmpty(name))
                return;   // the bake already reports an empty choice

            StateTreeRegistryAsset chosen = RegistryEntryNode.ResolveRegistry(node as Node);
            if (chosen == null)
                return;   // the bake already reports a missing registry

            if (chosen.FindByName(name) == null)
            {
                graphLogger.LogError($"Registry Entry names '{name}', which '{chosen.name}' has no "
                    + "row for — it was renamed or removed, or left over from another registry. "
                    + "Its rows are: " + RowNames(chosen) + ".", node);
                return;
            }

            for (int i = 0; i < scope.registries.Count; i++)
            {
                if (ReferenceEquals(scope.registries[i], chosen))
                    return;
            }

            graphLogger.LogError($"Registry Entry names '{name}' from '{chosen.name}', which this "
                + "graph does not reach — so the row exists but this graph is not declared to use "
                + $"that data. Add '{chosen.name}' to the Depends On list of the registry that owns "
                + "this graph"
                + (scope.isEmpty
                    ? ", once a registry row points at this graph."
                    : " (" + OwningRegistries(scope) + ")."), node);
        }

        /// <summary>A registry's row names, capped, for the message that follows a stale pick.</summary>
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

        /// <summary>The registries a Depends On entry would go on — the ones that reach this
        /// graph, so the message names the asset to open rather than the idea of one.</summary>
        private static string OwningRegistries(GraphRegistryScope scope)
        {
            var names = new List<string>();
            for (int i = 0; i < scope.registries.Count; i++)
            {
                if (scope.IsRoot(scope.registries[i]))
                    names.Add(scope.registries[i].name);
            }
            return names.Count == 0 ? "the registry that lists it" : string.Join(" or ", names);
        }

        /// <summary>One typed-reference port of one node.</summary>
        /// <param name="node">The node carrying the port — what an error is pinned to.</param>
        /// <param name="field">The library field the port mirrors.</param>
        /// <param name="entryType">The entry class the reference wants.</param>
        /// <param name="scope">The registries this graph may name.</param>
        /// <param name="graphLogger">Sink for the report.</param>
        /// <param name="unanswered">Entry classes already reported as unreachable, so a graph
        /// with eight Give Item nodes gets one wiring warning rather than eight.</param>
        private static void CheckPort(INode node, FieldInfo field, Type entryType,
            GraphRegistryScope scope, GraphLogger graphLogger, List<Type> unanswered)
        {
            IPort port = node.GetInputPortByName(field.Name);
            if (port == null)
                return;

            // The port carries the entry NAME (LibraryParameterPorts.PortDataType), so a string
            // is what there is to check — wired or typed.
            if (!LibraryParameterPorts.TryReadValue(port, typeof(string), out object read))
                return;
            if (!(read is string name) || string.IsNullOrEmpty(name))
                return;

            if (!scope.Answers(entryType))
            {
                if (unanswered.Contains(entryType))
                    return;
                unanswered.Add(entryType);

                graphLogger.LogWarning(Label(port, field) + " names a " + entryType.Name
                    + " row, but no registry in this graph's reach holds those. "
                    + (scope.isEmpty
                        ? "Nothing points at this graph yet — put it in a registry row (an NPC's "
                            + "dialog program, a level's script) and the row's registry becomes "
                            + "its data."
                        : "Add the " + entryType.Name + " registry to the Depends On list of "
                            + "the registry that owns this graph.")
                    + " Until then '" + name + "' cannot be checked.", node);
                return;
            }

            if (scope.Find(entryType, name) != null)
                return;

            graphLogger.LogError(Label(port, field) + " is '" + name
                + "', which is not a row of any " + entryType.Name
                + " registry this graph can reach. Rows available: "
                + scope.DescribeEntries(entryType)
                + ". Use one of those, or add the row to the registry.", node);
        }

        /// <summary>The port as the author sees it in the node — its display name when it has
        /// one, else the field name the port is built from.</summary>
        private static string Label(IPort port, FieldInfo field)
        {
            string display = null;
            try
            {
                display = port.DisplayName;
            }
            catch (Exception)
            {
                // Graph Toolkit builds port models lazily and a half-built one throws rather
                // than answering; the field name is always right and always available.
            }
            return string.IsNullOrEmpty(display) ? field.Name : display;
        }
    }
}
