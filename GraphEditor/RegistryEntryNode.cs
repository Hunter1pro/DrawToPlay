using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// ANY REGISTRY, ANY ROW — choose the catalog, then choose the row in it. One node for every
    /// kind of data, instead of one node per registry.
    ///
    /// WHAT IT IS FOR. Every library field that takes a registry row takes it BY NAME
    /// (<see cref="StateTreeEntryRef{TEntry}"/> ports as <c>entryName</c>, resolved by the task
    /// through its own service). The pins offer their own list already; this node earns its place
    /// when ONE chosen row feeds SEVERAL pins, and when the row is not an item — a level, a tag, a
    /// door — without anyone writing a node per catalog.
    ///
    /// ===================================================================================
    /// HOW THE TWO PINS STAY IN STEP — the part that took three attempts to get right
    /// ===================================================================================
    /// A pin's choices are attached when the pin is DEFINED, so an Entry list that follows the
    /// Registry slot means redefining the node whenever that slot changes. The trap is that
    /// <see cref="OnDefinePorts"/> CANNOT READ THE REGISTRY PIN: it runs while the node's ports are
    /// being rebuilt, so the read comes back empty, the list comes out empty, and the node is
    /// judged stale again — forever. That loop is what makes an author's edits look like they do
    /// nothing.
    ///
    /// So the list is built from <see cref="m_ChoiceSource"/>, a plain field, never from the pin.
    /// <see cref="ChoicePortRefresh"/> — which runs in <c>OnGraphChanged</c>, where pins ARE
    /// readable — copies the pin into that field and only then redefines the node. Define-time
    /// reads a value that cannot change underneath it, so the next comparison matches and the node
    /// settles.
    ///
    /// The field is deliberately NOT serialized: it is a cache of something the graph already
    /// stores (the pin), and on load the first definition simply has no list until the first
    /// refresh, which happens immediately.
    ///
    /// IT BAKES TO THE NAME and carries no registry reference, because it cannot: the importer
    /// resolves a graph with the AssetDatabase closed to queries — measured, not assumed — so
    /// nothing can be looked up at bake time. "Is this a real row?" is answered by
    /// <see cref="EntryRefValidator"/> on every graph change, where the AssetDatabase is open.
    ///
    /// Bakes to <see cref="GraphTaskNodeKind.RegistryEntry"/>: <c>stringValue</c> the row name.
    /// </summary>
    [Serializable]
    [UseWithGraph(typeof(TaskGraph))]
    [Node("Values", null, "Registry Entry")]
    public class RegistryEntryNode : Node, ITaskGraphNode
    {
        /// <summary>Name of the port holding the catalog to pick from.</summary>
        public const string RegistryPortName = "registry";

        /// <summary>Name of the port holding the chosen row.</summary>
        public const string EntryPortName = "entry";

        /// <summary>The registry the CURRENT Entry list was built from — see the class remarks.
        /// Written only by <see cref="AdoptChoiceSource"/>, read only by
        /// <see cref="OnDefinePorts"/>.</summary>
        [NonSerialized] private StateTreeRegistryAsset m_ChoiceSource;

        /// <inheritdoc />
        public GraphTaskNodeKind nodeKind => GraphTaskNodeKind.RegistryEntry;

        /// <inheritdoc />
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            TaskGraphPorts.AddData<StateTreeRegistryAsset>(context, RegistryPortName, "Registry",
                "The catalog to pick from. Any registry — items, levels, tags, doors.");
            TaskGraphPorts.AddChoiceData(context, EntryPortName, "Entry",
                "The row. Pick the registry first; this list is that registry's rows.",
                RowsOf(m_ChoiceSource));
            TaskGraphPorts.AddResult<string>(context,
                "The row's name, as every field that takes a registry row expects it.");
        }

        /// <summary>
        /// Point the Entry list at a registry, for the next definition.
        /// </summary>
        /// <param name="registry">The registry the list should hold, or null for no list.</param>
        /// <returns>True when this changed anything, i.e. the node needs redefining.</returns>
        public bool AdoptChoiceSource(StateTreeRegistryAsset registry)
        {
            if (ReferenceEquals(m_ChoiceSource, registry))
                return false;
            m_ChoiceSource = registry;
            return true;
        }

        /// <summary>What the Entry pin SHOULD offer, given the registry currently in its slot —
        /// what <see cref="ChoicePortRefresh"/> compares against to decide whether the node is out
        /// of date.</summary>
        /// <param name="node">The node to read. Its pins must be readable, so call this from
        /// <c>OnGraphChanged</c> and never from port definition.</param>
        /// <returns>The rows of the registry in the slot; empty when there is none.</returns>
        public static List<string> WantedRows(RegistryEntryNode node)
        {
            return RowsOf(ResolveRegistry(node));
        }

        /// <summary>One registry's rows, as the Entry pin offers them.</summary>
        /// <param name="registry">The catalog, or null.</param>
        /// <returns>Never null; empty leaves the pin a plain text field.</returns>
        private static List<string> RowsOf(StateTreeRegistryAsset registry)
        {
            var choices = new List<string>();
            if (registry == null || registry.Count == 0)
                return choices;

            // Empty first: a node whose registry is chosen but whose row is not yet is a real
            // state, and a list with no way back to it would force a row on the author at once.
            choices.Add(string.Empty);
            for (int i = 0; i < registry.Count; i++)
            {
                StateTreeRegistryEntry row = registry.EntryAt(i);
                if (row != null && !string.IsNullOrEmpty(row.name) && !choices.Contains(row.name))
                    choices.Add(row.name);
            }
            return choices;
        }

        /// <summary>The registry in the slot, or null — guarded because a caller may reach this
        /// while the graph model is still being built.</summary>
        /// <param name="node">The node to read.</param>
        /// <returns>The registry asset, or null.</returns>
        public static StateTreeRegistryAsset ResolveRegistry(Node node)
        {
            if (node == null)
                return null;
            try
            {
                return LibraryParameterPorts.TryReadValue(node.GetInputPortByName(RegistryPortName),
                    typeof(StateTreeRegistryAsset), out object value)
                    ? value as StateTreeRegistryAsset
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
