using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Restore every tagged combatant to full health — the cheapest possible "next wave".
    /// Reviving in place instead of spawning keeps the minimal game circle inside the
    /// registry: the same citizens flip back to alive, perception re-acquires them on its
    /// next evaluation, and no prefab machinery enters the picture. (A real spawner is a
    /// later atom; this one is still honestly reusable — checkpoint resets, practice arenas.)
    /// Succeeds even when the tag matches nobody: reviving an empty room is a no-op, not an
    /// error — gate the wave on <see cref="AnyAliveWithTagCondition"/> instead.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Revive By Tag", fileName = "ReviveByTag")]
    [StateTreeCategory("Tasks/World", "Restore every tagged combatant to full health")]
    public sealed class ReviveByTagTask : StateTreeTaskAsset
    {
        public string tag = "";

        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        private bool m_WarnedNoService;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(tag))
                return StateTreeStatus.Failure;

            WorldService world = StateTreeContextHost.FindService<WorldService>(context.owner);
            if (world == null)
            {
                if (!m_WarnedNoService)
                {
                    m_WarnedNoService = true;
                    Debug.LogWarning("ReviveByTagTask: no WorldService reachable from '"
                        + (context.owner != null ? context.owner.name : "(null)")
                        + "' for tag '" + tag + "'.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            m_Buffer.Clear();
            world.CollectByTag(tag, m_Buffer);
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                if (m_Buffer[i] == null)
                    continue;
                HealthComponent health = StateTreeLibraryUtil
                    .ResolveComponent<HealthComponent>(m_Buffer[i].gameObject);
                if (health != null)
                    health.ResetHealth();
            }
            return StateTreeStatus.Success;
        }
    }
}
