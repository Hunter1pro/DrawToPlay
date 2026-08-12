using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using PowerOfFire.DrawToPlay.Editor;

namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// A PIN'S CHOICES, DESCRIBED — turns the bare row names a canvas pin offers into picker rows
    /// with a category, a description and the catalog they came from.
    ///
    /// A pin can only ever hold strings, so its choice list is names and nothing else. That is
    /// enough for the native dropdown and not enough for a person: "medkit" says nothing about
    /// what it is, and three area manifests each holding a "grunt 1" are indistinguishable. Every
    /// one of those names, though, IS a row of a registry the graph can reach — so the row is
    /// found and its own group, description and registry come with it.
    ///
    /// A name that matches no reachable row still gets a plain item rather than being dropped: it
    /// is a legal thing to have on a pin (a registry outside the graph's declared reach, a row
    /// renamed since), and a picker that silently omitted it would make the pin unfixable.
    ///
    /// ON THE ASSEMBLY DIRECTION. This is the first thing here to reference
    /// <c>PowerOfFire.DrawToPlay.Editor</c>, for its picker. That does not breach the GraphEditor
    /// firewall, which is about the OTHER direction: the main editor assembly must not reference
    /// this one, because Graph Toolkit is 0.5.0-exp and its API may move without the tools
    /// noticing (hence <c>StateTreeGraphBridge</c>'s reflection). Depending the other way is
    /// acyclic — the editor assembly references only the runtime — and it is what stops the canvas
    /// growing a second, worse picker of its own.
    /// </summary>
    internal static class RegistryPickerItems
    {
        /// <summary>
        /// Describe each choice against the rows the graph can reach.
        /// </summary>
        /// <param name="choices">The pin's values, in the order it offers them.</param>
        /// <param name="graph">The canvas the pin is on; null yields plain rows.</param>
        /// <returns>One item per non-empty choice.</returns>
        public static List<StateTreePickerItem> For(IReadOnlyList<string> choices, Graph graph)
        {
            var items = new List<StateTreePickerItem>();
            if (choices == null)
                return items;

            var reachable = new List<StateTreeRegistryAsset>();
            if (graph != null)
            {
                GraphRegistryScope scope = GraphRegistryScope.For(graph);
                for (int i = 0; i < scope.registries.Count; i++)
                    reachable.Add(scope.registries[i]);
            }

            for (int i = 0; i < choices.Count; i++)
            {
                string choice = choices[i];
                if (string.IsNullOrEmpty(choice))
                    continue;   // the "unset" row is reached by clearing, not by picking

                items.Add(Describe(choice, reachable));
            }
            return items;
        }

        /// <summary>One choice, against the reachable registries.</summary>
        private static StateTreePickerItem Describe(string choice,
            List<StateTreeRegistryAsset> reachable)
        {
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryEntry row = reachable[i].FindByName(choice);
                if (row == null)
                    continue;

                return new StateTreePickerItem
                {
                    displayName = row.name,
                    category = row.group ?? string.Empty,
                    description = DescriptionOf(row),
                    identity = reachable[i].name,
                    persistKey = row.id,
                    payload = choice
                };
            }

            return new StateTreePickerItem
            {
                displayName = choice,
                category = string.Empty,
                description = string.Empty,
                identity = string.Empty,
                persistKey = choice,
                payload = choice
            };
        }

        /// <summary>The row's human text: the first string field its class adds beyond the base
        /// three. Found by SHAPE rather than by a required name, so a new registry kind still
        /// costs one entry class and nothing else — the same rule the Inspector's picker follows.
        /// </summary>
        private static string DescriptionOf(StateTreeRegistryEntry row)
        {
            foreach (FieldInfo field in row.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(string))
                    continue;
                if (field.Name == "id" || field.Name == "name" || field.Name == "group")
                    continue;
                return field.GetValue(row) as string ?? string.Empty;
            }
            return string.Empty;
        }
    }
}
