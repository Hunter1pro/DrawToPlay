using System;
using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Marks a public field of a <see cref="StateTreeTaskAsset"/> as one of that task's OUTPUTS —
    /// a value the task RETURNS when it finishes, which the transition leaving the state may route
    /// wherever it wants (M7j).
    ///
    /// WHY THIS IS NOT JUST "WRITE THE BLACKBOARD". A task that writes a blackboard key has decided,
    /// at authoring time, both the value and where it lands, for every state that will ever use it.
    /// An output decides only the value; the CALLER decides where it goes, and may decide differently
    /// per transition — which is what turns a task into something composable. It is the same
    /// difference as between a function that returns a number and a function that assigns to a global.
    ///
    /// OUTPUTS ARE NAME-KEYED, deliberately unlike the id-keyed input parameters
    /// (<see cref="GraphTaskParameter.id"/>). An input parameter is a knob an author tunes and then
    /// renames without meaning anything by it, so its identity has to survive the rename. An output is
    /// a CONTRACT other authored data is written against: renaming <c>lastDamageDealt</c> is a
    /// breaking change to every route that reads it, exactly as renaming a method is to its callers,
    /// and quietly re-binding those routes would hide the break rather than fix it. The FIELD NAME is
    /// the contract name — there is no name argument here, so the two can never drift apart.
    ///
    /// SUPPORTED FIELD TYPES are <c>float</c>, <c>int</c>, <c>bool</c> and <c>string</c>: the four the
    /// blackboard has an unambiguous boxed form for (see <see cref="StateTreeExecutor"/>'s boxing
    /// rules). A field of any other type carrying this attribute is ignored — silently, because the
    /// attribute is authored in C# where a compiler-visible mistake is the author's own to see, and a
    /// per-frame log for a decorated <c>Vector2</c> would help nobody.
    ///
    /// CONDITIONS HAVE NO OUTPUTS IN V1. A condition is a predicate evaluated as part of deciding a
    /// transition — it has no completion moment for a value to be captured AT, and the transition that
    /// would route it is the very thing it is being asked about. Nothing about the model forbids it
    /// later; there is simply no coherent capture point today.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TaskOutputAttribute : Attribute
    {
        /// <summary>What this output means, for the inspector's route picker. Optional: a well-named
        /// output usually says it already, and an empty description is better than a restated
        /// one.</summary>
        public string description { get; }

        public TaskOutputAttribute(string description = "")
        {
            this.description = description;
        }
    }

    /// <summary>
    /// ONE captured output: the name it was returned under, its kind, and the value. The three
    /// producers — a <c>[TaskOutput]</c> field read by reflection, a graph's <c>Set Output</c>
    /// instruction, and a wrapper forwarding an inner task's — all flatten into this, so the routing
    /// side has exactly one shape to handle and cannot come to treat them differently.
    ///
    /// A STRUCT, and flat, for the same reason <see cref="GraphTaskNode"/> is: it is serialized data
    /// (<see cref="GraphTaskAsset.declaredOutputs"/> is a baked list of these), it is copied by value
    /// into and out of the capture buffers on the hot path, and it has no identity worth sharing.
    ///
    /// A BOOL RIDES IN <see cref="floatValue"/> (!= 0), matching
    /// <see cref="GraphTaskParameter"/> — one numeric slot for both kinds means an output retyped
    /// between Float and Bool keeps working, and it is the encoding the baker already emits for a bool
    /// literal.
    /// </summary>
    [Serializable]
    public struct TaskOutputValue
    {
        /// <summary>The contract name a <see cref="TransitionOutputRoute"/> binds to. Compared
        /// ORDINALLY (case included) — it names a field or a typed-in graph constant, and "damage"
        /// and "Damage" are two different contracts in both.</summary>
        public string name;

        public GraphTaskParameterKind kind;

        /// <summary>Value for Float, and for Bool (!= 0).</summary>
        public float floatValue;

        /// <summary>Value for String.</summary>
        public string stringValue;

        /// <summary>
        /// A CONTRACT PAYLOAD (§4d): a whole typed object — an ItemUseResult, a struck
        /// citizen — published as ONE output and landed under one key, so a growing
        /// contract grows the class, never the number of keys or the downstream wiring.
        /// When set it wins over the scalar slots at the landing site; the scalars stay
        /// filled as the degraded view for readers that only speak Float/String. Runtime
        /// only — a payload is produced by a run, never serialized with the asset.
        /// </summary>
        [NonSerialized] public object objectValue;
    }

    /// <summary>
    /// ONE unconditional return connection, carried by the TASK (every task —
    /// <see cref="StateTreeTaskAsset.returns"/>): when the task finishes, the output named
    /// here is published to a blackboard key, whatever way the state then leaves. This is the
    /// "out parameter" flavor of returning; the "assignment" flavor stays on the TRANSITION
    /// (<see cref="TransitionOutputRoute"/>), which can route the same output differently per
    /// exit wire. One concept, two binding sites — a C# task's <c>[TaskOutput]</c> fields and
    /// a program's outputs route identically through both.
    /// </summary>
    [Serializable]
    public sealed class TaskReturnRoute
    {
        /// <summary>The output's contract name (a <c>[TaskOutput]</c> field's name, or a
        /// program's declared output).</summary>
        public string output = "";

        /// <summary>Where it lands on the running tree's blackboard — ⚑-wireable to a
        /// declared key, free-typed text otherwise.</summary>
        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public StateTreeKeyField key = new StateTreeKeyField();
    }

    /// <summary>
    /// Implemented by a task that produces its outputs from somewhere OTHER than its own
    /// <c>[TaskOutput]</c> fields — today <see cref="GraphTaskAsset"/> (which buffers what its
    /// <c>Set Output</c> instructions wrote) and <see cref="RunGraphTask"/> (which forwards the
    /// per-activation instance it owns). The executor asks this first and falls back to reflection, so
    /// a task is either a source or a plain field-carrier and never half of each.
    ///
    /// THE TIMING IS THE CONTRACT: the executor collects at the moment the task RETURNS a terminal
    /// status, before it calls <c>OnExit</c> on it. An implementor may therefore hold its outputs in
    /// state that <c>OnExit</c> tears down — which is exactly what <see cref="RunGraphTask"/> does,
    /// since its OnExit destroys the instance the values live on.
    /// </summary>
    public interface IStateTreeOutputSource
    {
        /// <summary>Appends this task's outputs to <paramref name="into"/>.</summary>
        /// <returns>False when there was nothing to add, so a caller can skip the work of storing an
        /// empty result without inspecting the list.</returns>
        bool TryCollectOutputs(List<TaskOutputValue> into);
    }
}
