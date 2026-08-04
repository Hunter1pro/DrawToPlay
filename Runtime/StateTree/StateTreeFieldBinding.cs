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
    /// A row has TWO POSSIBLE SOURCES since M7k, and the difference between them is WHEN the value
    /// exists rather than where it comes from. A parameter is an argument of the call: fixed for the
    /// whole run, so its rows are applied ONCE per tree start onto the deep copy the executor owns
    /// (a sub-tree activation re-copies and re-binds, which is exactly how a re-entered state picks
    /// up a changed override). A blackboard key is a value the run PRODUCES — most often a task
    /// output a transition routed on the way in — so its rows are applied on every ENTRY of the
    /// state that owns them, immediately before the tasks are entered, and re-read on every
    /// re-entry.
    ///
    /// THAT SECOND SOURCE IS WHAT CLOSES THE LOOP M7j OPENED. A routed output lands on the
    /// blackboard, where a graph program and a condition can already read it by key — but a plain C#
    /// task with a <c>public float damage</c> cannot, which left "the state before me computed this"
    /// unable to reach the one surface most tasks are written against. route → key → field is that
    /// path, and the key is the joint: the producing transition and the consuming state name it
    /// independently, so neither has to know the other exists.
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
        /// value when <see cref="sourceKind"/> is <see cref="SourceKind.Parameter"/>. Empty or
        /// unmatched = one error, row skipped: a field silently left at its authored value would
        /// look exactly like a binding that worked.</summary>
        public string parameterId;

        /// <summary>Where a row takes its value from. Serialized as an int — append only, and
        /// <see cref="Parameter"/> is 0 so every row authored before M7k keeps meaning exactly what
        /// it meant.</summary>
        public enum SourceKind
        {
            /// <summary>A parameter of the tree, named by <see cref="parameterId"/>. Written ONCE,
            /// when the tree starts.</summary>
            Parameter = 0,

            /// <summary>A blackboard entry, named by <see cref="blackboardKey"/>. Written on every
            /// ENTRY of the owning state, before its tasks are entered.</summary>
            BlackboardKey = 1
        }

        /// <summary>Which of the two sources feeds this row. Default <see cref="SourceKind.Parameter"/>
        /// — the M7i behaviour, unchanged.</summary>
        public SourceKind sourceKind;

        /// <summary>
        /// Blackboard entry read when <see cref="sourceKind"/> is
        /// <see cref="SourceKind.BlackboardKey"/>; ignored otherwise. A NAME, not an id, and
        /// deliberately so: it is the same kind of name a transition's output route writes and a
        /// <c>Get Blackboard</c> node reads, and those cannot be identity-bound to anything — the
        /// blackboard is a dictionary shared by every producer on the entity, which is what makes it
        /// usable as a meeting point in the first place (the M7j note on name-keyed contracts).
        ///
        /// A key that is NOT PRESENT when the state is entered leaves the field alone, silently. That
        /// is not leniency: several transitions normally lead into one state, only some of them
        /// routing anything, and a state entered through an unrouted edge is the ordinary case rather
        /// than a fault. The field then holds whatever it last held — its authored value on the first
        /// entry, the previous entry's value afterwards.
        ///
        /// Empty here (with this source kind) is an INERT row: nothing to read, nothing written,
        /// nothing said at runtime. The inspector is where an unfinished row is visible and where it
        /// can be finished.
        /// </summary>
        public string blackboardKey;
    }
}
