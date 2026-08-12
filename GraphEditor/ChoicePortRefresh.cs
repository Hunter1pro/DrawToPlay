using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// KEEPS THE DROPDOWNS HONEST — the pass that gives a port its choices once the graph knows
    /// which registries it reaches, and updates them when that answer changes.
    ///
    /// WHY IT IS NEEDED AT ALL. A port's choices come from data (the rows a registry holds, the
    /// fields a row class declares) but are attached when the port is DEFINED — and ports are
    /// defined while the graph model is still being assembled, when a node cannot yet say which
    /// graph it belongs to. So the first definition of a freshly loaded node usually has no
    /// choices, and a node defined before a registry gained a row has stale ones. Both look the
    /// same to an author: a text box, or a list missing the thing they just added.
    ///
    /// SO IT REDEFINES, BUT ONLY WHAT CHANGED. Every node whose choice set differs from what its
    /// ports already offer is redefined; everything else is untouched. That is the same rule
    /// <c>TaskGraph.RefreshReturnPins</c> follows for Return pins and for the same reason: this
    /// runs inside <see cref="Graph.OnGraphChanged"/>, and redefining unconditionally would churn
    /// ports on every keystroke and could loop through the hook.
    ///
    /// REDEFINING A NODE PRESERVES ITS WIRES AND VALUES — it is the same call the toolkit makes
    /// when a node's shape depends on its own options — so a graph that gains dropdowns keeps
    /// everything the author already put in it.
    /// </summary>
    public static class ChoicePortRefresh
    {
        /// <summary>
        /// Bring every choice-bearing port on the canvas up to date.
        /// </summary>
        /// <param name="nodes">The graph's nodes, already collected by the caller.</param>
        public static void Refresh(IReadOnlyList<INode> nodes)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!(nodes[i] is Node node))
                    continue;

                try
                {
                    DropUnofferedValue(node);
                    if (IsStale(node))
                    {
                        // A Registry Entry's list is built from a field, not from its pin, because
                        // OnDefinePorts cannot read pins. Point that field at the chosen registry
                        // BEFORE redefining, or the new definition rebuilds the same stale list
                        // and the node is judged stale again on every pass — the loop.
                        (node as RegistryEntryNode)?.AdoptChoiceSource(
                            RegistryEntryNode.ResolveRegistry(node));
                        node.DefineNode();
                        // The model is right after DefineNode, but the VIEW still shows the
                        // widgets it built last time — see PortChoices.RequestRebuild, and
                        // ChoiceDropdownSync for the half that registering a change cannot do.
                        PortChoices.RequestRebuild(node);
                        DropUnofferedValue(node);
                    }
                }
                catch (Exception)
                {
                    // A half-built node answers by throwing; it will be asked again on the next
                    // change, and a dropdown that arrives one edit late is not worth a console
                    // line on every graph load.
                }
            }
        }

        /// <summary>
        /// Clear a choice pin holding a row the current list does not offer.
        ///
        /// A row from the registry the author just switched AWAY from is not a value, it is a
        /// leftover — and left in place it is actively misleading, because a
        /// <see cref="UnityEngine.UIElements.DropdownField"/> given a value outside its list
        /// re-clamps to some other row and shows THAT. The screen then names a row the graph does
        /// not hold. Dropped, the pin reads unset, the widget shows unset, and the bake asks for a
        /// row instead of complaining about one nobody chose.
        ///
        /// Run both before and after the redefine: before, so a switch is cleaned even when the
        /// list itself did not change; after, so it is cleaned against the NEW list.
        /// </summary>
        /// <param name="node">The node being refreshed.</param>
        private static void DropUnofferedValue(Node node)
        {
            if (!(node is RegistryEntryNode entryNode))
                return;

            IPort port = entryNode.GetInputPortByName(RegistryEntryNode.EntryPortName);
            if (port == null)
                return;

            IReadOnlyList<string> rows = RegistryEntryNode.WantedRows(entryNode);
            if (rows.Count == 0)
                return;

            if (!LibraryParameterPorts.TryReadValue(port, typeof(string), out object current)
                || !(current is string name) || string.IsNullOrEmpty(name))
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i], name, StringComparison.Ordinal))
                    return;
            }
            LibraryParameterPorts.TryWriteValue(port, typeof(string), string.Empty);
        }

        /// <summary>Whether any of this node's ports is offering something other than what the
        /// data now says.</summary>
        /// <param name="node">The node to test.</param>
        /// <returns>True when it should be redefined.</returns>
        private static bool IsStale(Node node)
        {
            if (node is RegistryEntryNode entryNode)
            {
                return !PortChoices.Matches(
                    entryNode.GetInputPortByName(RegistryEntryNode.EntryPortName),
                    RegistryEntryNode.WantedRows(entryNode));
            }

            Type libraryType = LibraryParameterPorts.LibraryTypeOf(node);
            if (libraryType == null)
                return false;

            IReadOnlyList<FieldInfo> fields = LibraryParameterPorts.GetParameterFields(libraryType);
            for (int i = 0; i < fields.Count; i++)
            {
                if (LibraryParameterPorts.EntryTypeOf(fields[i].FieldType) == null)
                    continue;
                if (!PortChoices.Matches(node.GetInputPortByName(fields[i].Name),
                        LibraryParameterPorts.EntryChoicesFor(node, fields[i])))
                    return true;
            }
            return false;
        }
    }
}
