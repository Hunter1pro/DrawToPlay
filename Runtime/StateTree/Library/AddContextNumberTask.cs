using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Add to a number on a context scope — score, wave counter, kills, currency. The
    /// arithmetic sibling of <see cref="SetContextValueTask"/>: SET writes what you know, ADD
    /// moves what is already there (an absent key counts from zero, so seeding is optional).
    /// The M7g boxed-float rule throughout, which is what every scoped condition and graph
    /// read already speaks.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Add Context Number",
        fileName = "AddContextNumber")]
    [StateTreeCategory("Tasks/Context", "Add to a number on a context scope (score, wave, kills)")]
    public sealed class AddContextNumberTask : StateTreeTaskAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        public string scopeId = "";

        [StateTreeKey(StateTreeKeyKind.Float)]
        public StateTreeKeyField key = new StateTreeKeyField();

        public float delta = 1f;

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
                    Debug.LogWarning("AddContextNumberTask: no '" + scope + "' context reachable "
                        + "from '" + (context.owner != null ? context.owner.name : "(null)")
                        + "' for key '" + key + "'.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            var scoped = host.Context.blackboard;
            float current = scoped.TryGetValue(key, out object held) && held is float f ? f : 0f;
            scoped[key] = current + delta;
            return StateTreeStatus.Success;
        }
    }
}
