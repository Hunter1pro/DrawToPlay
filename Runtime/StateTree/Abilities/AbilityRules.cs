using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The ability service's rulebook, applied — validation of rows against their
    /// <see cref="ServiceDef"/>. The structure that used to need a nesting walk went TYPED on
    /// review (effect rows referencing cue rows: an illegal child is unrepresentable, which
    /// beats refused), so what remains to check is what types cannot say:
    ///
    /// ONE ABILITY IS ONE TREE — a row's tree must carry the service's declared
    /// <see cref="ServiceDef.treeKind"/>, so the catalog cannot quietly point an ability at
    /// an NPC's mind or a level flow. The kind is stamped where the tree is authored; this is
    /// the read side.
    /// </summary>
    public static class AbilityRules
    {
        /// <summary>Validate one ability row. Problems are appended, one line each, naming
        /// the row — a report nobody can trace is not worth writing.</summary>
        public static void Validate(ServiceDef service, AbilityDef row, List<string> problems)
        {
            if (service == null || row == null || problems == null)
                return;

            if (row.tree != null && !string.IsNullOrEmpty(service.treeKind)
                && !string.Equals(row.tree.treeKind, service.treeKind,
                    System.StringComparison.Ordinal))
            {
                problems.Add("ability '" + row.name + "': its tree '" + row.tree.name
                    + "' is kind '" + row.tree.treeKind + "', not '" + service.treeKind
                    + "' — one ability is one ability tree, and this row points somewhere "
                    + "else.");
            }

            if (row.tree != null)
                ValidateTree(service, row.tree, problems);
        }

        /// <summary>
        /// The nesting rules applied to the TREE (the HT ability editor's law, on states):
        /// the root IS the service's root kind; a rule-typed state must be allowed under the
        /// nearest typed ancestor; a plain state is transparent grouping; a state whose kind
        /// allows nothing beneath it is a leaf, and anything under it — typed or not — is a
        /// finding.
        /// </summary>
        public static void ValidateTree(ServiceDef service, StateTreeAsset tree,
            List<string> problems)
        {
            if (service == null || tree == null || tree.root == null || problems == null)
                return;
            ValidateChildren(service, ServiceDef.TreeRootKind, tree.root,
                "tree '" + tree.name + "'", problems, 0);

            // ONE TREE HAS ONE ABILITY — the review's standing decision. The rules allow an
            // 'ability' state under the root like HT's root held many; this project's cut is
            // one per tree, so a second one is a finding, not a silent catalog.
            int abilityStates = CountRole(tree.root, AbilityDef.RootKind, 0);
            if (abilityStates > 1)
            {
                problems.Add("tree '" + tree.name + "': " + abilityStates + " '"
                    + AbilityDef.RootKind + "' states — one tree has ONE ability; split the "
                    + "others into their own trees.");
            }
        }

        private static int CountRole(StateTreeNodeAsset node, string kind, int depth)
        {
            if (node == null || depth > 64)
                return 0;
            int count = string.Equals(node.roleKind, kind, System.StringComparison.Ordinal)
                ? 1 : 0;
            for (int i = 0; i < node.children.Count; i++)
                count += CountRole(node.children[i], kind, depth + 1);
            return count;
        }

        private static void ValidateChildren(ServiceDef service, string effectiveKind,
            StateTreeNodeAsset node, string path, List<string> problems, int depth)
        {
            if (node == null || depth > 64)
                return;

            bool leaf = !string.IsNullOrEmpty(effectiveKind)
                && service.AllowedUnder(effectiveKind).Count == 0;

            for (int i = 0; i < node.children.Count; i++)
            {
                StateTreeNodeAsset child = node.children[i];
                if (child == null)
                    continue;
                string label = path + " → '" + child.nodeId + "'";

                if (leaf)
                {
                    problems.Add(label + ": a '" + effectiveKind + "' state is a leaf — "
                        + "nothing may nest under it.");
                    continue;
                }

                string childKind = child.roleKind;
                if (!string.IsNullOrEmpty(childKind)
                    && !service.Allows(effectiveKind, childKind))
                {
                    problems.Add(label + ": a '" + childKind + "' cannot sit under '"
                        + effectiveKind + "' — the rules allow ["
                        + string.Join(", ", service.AllowedUnder(effectiveKind)) + "].");
                }

                // A plain state is transparent: its children answer to the same kind it did.
                ValidateChildren(service,
                    string.IsNullOrEmpty(childKind) ? effectiveKind : childKind,
                    child, label, problems, depth + 1);
            }
        }
    }
}
