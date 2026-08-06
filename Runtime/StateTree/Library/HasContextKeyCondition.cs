using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while a context scope's blackboard holds a key — the spine-scoped twin of a local
    /// has-key check, and the piece that makes cross-tree coordination a CONDITION rather than
    /// code: "alarm raised on the Level" is an interrupt transition guarded by this, which is
    /// the no-event-bus rule doing its job (an interrupt IS the subscription; the published
    /// key is the payload's address).
    ///
    /// Evaluated per tick when used on an interrupt, so failure to resolve a host stays QUIET
    /// here (just false, inverted or not): the write side (<see cref="SetContextValueTask"/>)
    /// already warns once about broken wiring, and a condition that logged per tick would bury
    /// that one useful line.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Has Context Key",
        fileName = "HasContextKey")]
    [StateTreeCategory("Conditions/Context", "A context scope holds (or lacks) a key")]
    public sealed class HasContextKeyCondition : StateTreeConditionAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string scopeId = "";

        [StateTreeKey(StateTreeKeyKind.Event)]
        public string key = "";

        /// <summary>True while the key is ABSENT instead — "until the alarm is raised".</summary>
        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool present = false;
            if (context != null && !string.IsNullOrEmpty(key))
            {
                StateTreeContextHost host =
                    StateTreeContextHost.Resolve(context.owner, scope, scopeId);
                present = host != null && host.Context.blackboard.ContainsKey(key);
            }
            return invert ? !present : present;
        }
    }
}
