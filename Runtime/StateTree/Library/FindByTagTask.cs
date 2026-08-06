using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Asks the world registry for an object by TAG and puts it on the blackboard — the query
    /// atom of brief §3.3, and the registry-backed alternative to scanning: "find the nearest
    /// 'lever'", "find the 'player'". The default key is <c>"target"</c>, the M6 perception
    /// convention, so Chase/Face/Attack consume what this finds with no adapter.
    ///
    /// A MISS CLEARS THE KEY and Fails: a stale target is worse than no target (the
    /// TargetDetected precedent), and Failure is the branchable answer — "no lever in range"
    /// is a transition, not an error. No <see cref="WorldService"/> reachable through the
    /// spine IS an error (the wiring is broken, not the world empty): Failure plus one
    /// warning per activation.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Find By Tag", fileName = "FindByTag")]
    [StateTreeCategory("Tasks/World", "Find a world object by tag onto the blackboard")]
    public sealed class FindByTagTask : StateTreeTaskAsset
    {
        [StateTreeKey(StateTreeKeyKind.Tag)]
        public string tag = "";

        /// <summary>Blackboard key the found GameObject lands under. The perception convention
        /// key by default, so combat tasks read it unchanged.</summary>
        [StateTreeKey(StateTreeKeyKind.Object)]
        public string targetKey = "target";

        /// <summary>Nearest to the owner (default), or simply the first registered — the cheap
        /// form for tags that exist at most once.</summary>
        public bool nearest = true;

        /// <summary>Zero or less = unlimited. Measured from the owner's position.</summary>
        public float maxDistance;

        private bool m_WarnedNoService;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(targetKey))
                return StateTreeStatus.Failure;

            WorldService world = StateTreeContextHost.FindService<WorldService>(context.owner);
            if (world == null)
            {
                if (!m_WarnedNoService)
                {
                    m_WarnedNoService = true;
                    Debug.LogWarning("FindByTagTask: no WorldService reachable from '"
                        + (context.owner != null ? context.owner.name : "(null)")
                        + "' for tag '" + tag + "'.", context.owner);
                }
                return StateTreeStatus.Failure;
            }

            WorldObjectBehaviour found = nearest
                ? world.FindNearest(tag,
                    context.owner != null ? context.owner.transform.position : Vector3.zero,
                    maxDistance)
                : FirstRegistered(world);

            if (found == null)
            {
                context.blackboard.Remove(targetKey);
                return StateTreeStatus.Failure;
            }

            context.blackboard[targetKey] = found.gameObject;
            return StateTreeStatus.Success;
        }

        private WorldObjectBehaviour FirstRegistered(WorldService world)
        {
            var bucket = new System.Collections.Generic.List<WorldObjectBehaviour>();
            return world.CollectByTag(tag, bucket) > 0 ? bucket[0] : null;
        }
    }
}
