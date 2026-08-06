using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Copies one entry from a context scope's blackboard into the LOCAL tree's blackboard —
    /// the read half of the spine's state model, shaped as a copy-down rather than a live view
    /// on purpose: the local tree then works on a value that cannot change under it mid-state,
    /// and every existing consumer (conditions, graph Get Blackboard nodes, M7k field bindings
    /// via entry-time keys) reads it with no new machinery. A direct context-value graph node is
    /// the planned v2; this task is the v1 that makes the spine usable everywhere today.
    ///
    /// <see cref="failIfMissing"/> defaults to true so a transition can branch on "the scope has
    /// no such value yet" — the same absence-is-information rule the local blackboard ops
    /// follow. With it off, a missing key is a quiet no-op and the local key keeps whatever it
    /// held.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Get Context Value",
        fileName = "GetContextValue")]
    [StateTreeCategory("Tasks/Context", "Copy a context-scope value into this tree's blackboard")]
    public sealed class GetContextValueTask : StateTreeTaskAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string scopeId = "";

        /// <summary>Key on the SCOPE's blackboard.</summary>
        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public string key = "";

        /// <summary>Key to write locally. Empty = same as <see cref="key"/>.</summary>
        [StateTreeKey(StateTreeKeyKind.Float, any: true)]
        public string localKey = "";

        public bool failIfMissing = true;

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
                    Debug.LogWarning("GetContextValueTask: no '" + scope + "' context reachable "
                        + "from '" + (context.owner != null ? context.owner.name : "(null)")
                        + "' for key '" + key + "'.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            if (!host.Context.blackboard.TryGetValue(key, out object value))
                return failIfMissing ? StateTreeStatus.Failure : StateTreeStatus.Success;

            context.blackboard[string.IsNullOrEmpty(localKey) ? key : localKey] = value;
            return StateTreeStatus.Success;
        }
    }
}
