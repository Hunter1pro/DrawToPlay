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
        }
    }
}
