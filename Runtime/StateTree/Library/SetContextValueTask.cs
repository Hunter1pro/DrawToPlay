using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Writes one entry on a CONTEXT scope's blackboard — <see cref="SetBlackboardTask"/> aimed
    /// up the spine (brief §3.6's global-state model: state lives on context blackboards, Root =
    /// global, Level = per-level, reached through scoped atoms exactly like this one). The local
    /// tree's own blackboard stays what it was: this task is how a behavior PUBLISHES — "the
    /// door is open", "alarm raised" — for every other tree under the same scope to read.
    ///
    /// The scope is named by kind + optional id and resolved from the running tree's OWNER, so
    /// the same preset tree writes p1's Player scope on p1 and p2's on p2 — parenting is the
    /// addressing (see <see cref="StateTreeContextHost.Resolve"/>). No host = Failure plus one
    /// warning per activation, because a publish that lands nowhere is a wiring error the
    /// author has to see once, not per tick.
    ///
    /// Stateless toward the value (the write happened or it did not) — Cancelled-safe with no
    /// teardown, like its local sibling.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Set Context Value",
        fileName = "SetContextValue")]
    [StateTreeCategory("Tasks/Context", "Write a value on a context scope (Root/Level/Player)")]
    public sealed class SetContextValueTask : StateTreeTaskAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        /// <summary>Disambiguates sibling scopes ("p2") when the owner is not parented under the
        /// one it means. Empty = nearest/unique of the kind.</summary>
        public string scopeId = "";

        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public string key = "";

        public SetBlackboardTask.ValueKind kind = SetBlackboardTask.ValueKind.Float;

        public float floatValue;

        public string stringValue = "";

        /// <summary>Seed a default without stomping what the scope already holds — the same
        /// contract as the local task's flag.</summary>
        public bool onlyIfMissing;

        private bool m_WarnedNoHost;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return StateTreeStatus.Failure;

            StateTreeContextHost host = StateTreeContextHost.Resolve(context.owner, scope, scopeId);
            if (host == null)
            {
                if (!m_WarnedNoHost)
                {
                    m_WarnedNoHost = true;
                    Debug.LogWarning("SetContextValueTask: no '" + scope + "' context reachable "
                        + "from '" + (context.owner != null ? context.owner.name : "(null)")
                        + "' for key '" + key + "'.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            var scoped = host.Context.blackboard;
            if (onlyIfMissing && scoped.ContainsKey(key))
                return StateTreeStatus.Success;

            switch (kind)
            {
                case SetBlackboardTask.ValueKind.Float:
                    scoped[key] = floatValue;
                    break;
                case SetBlackboardTask.ValueKind.String:
                    scoped[key] = stringValue;
                    break;
                default:
                    scoped.Remove(key);
                    break;
            }
            return StateTreeStatus.Success;
        }
    }
}
