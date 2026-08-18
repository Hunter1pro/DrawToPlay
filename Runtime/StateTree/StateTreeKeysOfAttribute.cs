using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS STRING IS A KEY OF THE TREE THAT ROW NAMES — the registry-row half of
    /// <see cref="StateTreeKeyAttribute"/>.
    ///
    /// A key field on a task can find its declarations by looking UP the scene: the tree that
    /// owns the task is right there. A registry ROW has no such tree — but it often NAMES one,
    /// the way a cutscene row names the beats it runs. This attribute says which sibling field
    /// holds that reference, so the editor can offer the tree's own declared keys instead of
    /// leaving the author to retype a name and hope it matches.
    ///
    /// It stays a plain string: a role is free text by design (a script may speak of a part no
    /// tree has declared yet), and the picker is an offer rather than a cage — the same
    /// relationship the registry pickers have with a row's dependsOn.
    /// </summary>
    public sealed class StateTreeKeysOfAttribute : PropertyAttribute
    {
        /// <summary>The sibling (or ancestor) field naming the tree whose keys to offer —
        /// "beats" on a cutscene row.</summary>
        public readonly string treeField;

        /// <summary>Which kind of key this field means. Ignored when <see cref="any"/>.</summary>
        public readonly StateTreeKeyKind kind;

        /// <summary>Offer every declared key regardless of kind.</summary>
        public readonly bool any;

        // (idField is declared below — assigned in the constructor like the rest.)

        /// <summary>The sibling string field holding the picked declaration's id, when the
        /// link is meant to SURVIVE a rename. Empty makes the picker a plain offer.</summary>
        public string idField = "";

        public StateTreeKeysOfAttribute(string treeField,
            StateTreeKeyKind kind = StateTreeKeyKind.Object, bool any = false,
            string idField = "")
        {
            this.treeField = treeField;
            this.kind = kind;
            this.any = any;
            this.idField = idField;
        }
    }
}
