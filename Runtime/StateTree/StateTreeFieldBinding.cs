using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One wire from a tree PARAMETER to a serialized FIELD of a task or a transition condition
    /// inside that tree (M7i) — the connection that was missing between "this tree declares
    /// speed" and "this state's ChaseTargetTask moves at speed". Without it a declared parameter
    /// could only be read through the blackboard by a hand-typed key, which every C# task with a
    /// plain <c>public float</c> field is unable to do.
    ///
    /// LIVES ON THE NODE, not on the task: the row has to name a task that only exists as an
    /// element of <see cref="StateTreeNodeAsset.tasks"/>, and a task sub-asset is deep-copied per
    /// activation while the node's authored data is the stable thing an editor can write to and
    /// undo. It also means one state's bindings are one list — which is what makes the inspector's
    /// "unlink" and the reorder/remove remapping tractable.
    ///
    /// THE TARGET IS AN INDEX, not a reference, for the same reason the graph program wires by
    /// index: a reference into a list the author reorders is a reference that silently changes
    /// meaning on serialization, while an index is a value the Ops layer can remap deliberately
    /// when it moves a row (and, when it fails to, produces a loud out-of-range error instead of a
    /// binding that quietly moved to a different task).
    ///
    /// THE PARAMETER IS AN ID, never a name (the M7h rule): a parameter renamed in the declaration
    /// keeps every field it drives. The name remains the blackboard key the tree seeds under, and
    /// that is a separate channel — a bound field is fed by
    /// <see cref="StateTreeExecutor.StartTree"/> writing it directly, not by the task reading the
    /// blackboard.
    ///
    /// Bindings are applied ONCE per tree start, onto the deep copy the executor owns, because a
    /// parameter's effective value is fixed for the duration of a call: a sub-tree activation
    /// re-copies and re-binds, which is exactly how a re-entered state picks up a changed override.
    /// </summary>
    [Serializable]
    public sealed class StateTreeFieldBinding
    {
        /// <summary>Which list of the owning node <see cref="targetIndex"/> indexes. Serialized as
        /// an int — append only.</summary>
        public enum TargetKind
        {
            /// <summary><see cref="StateTreeNodeAsset.tasks"/>.</summary>
            Task = 0,

            /// <summary>The condition of <see cref="StateTreeNodeAsset.transitions"/>[index].</summary>
            TransitionCondition = 1
        }

        public TargetKind targetKind;

        /// <summary>Index into the owning node's <c>tasks</c> or <c>transitions</c> list. Out of
        /// range = one error at start and the row is skipped; nothing throws, because an authored
        /// tree that lost a task must still run the states that did not.</summary>
        public int targetIndex;

        /// <summary>Name of a PUBLIC INSTANCE field on the target sub-asset — the serialized
        /// surface an author sees in the inspector, which is the only surface it makes sense to
        /// bind. Bindable field types are float/int (from a Float parameter), bool (Bool) and
        /// string (String); anything else is a kind mismatch and is reported.</summary>
        public string fieldName;

        /// <summary><see cref="GraphTaskParameter.id"/> of the tree parameter that supplies the
        /// value. Empty or unmatched = one error, row skipped: a field silently left at its
        /// authored value would look exactly like a binding that worked.</summary>
        public string parameterId;
    }
}
