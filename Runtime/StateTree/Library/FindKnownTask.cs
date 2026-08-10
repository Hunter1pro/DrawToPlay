using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Put THE object carrying a tag on the blackboard — the lookup for things a level has
    /// exactly one of (the player, the exit). Where <see cref="FindByTagTask"/> asks "which
    /// of these is nearest?" and sweeps the bucket to answer, this asks the registry for the
    /// one it already knows and only then checks whether it is close enough to matter.
    ///
    /// That difference is the point: perception should be a question to the world's registry,
    /// not a scan of the level by everything that might care.
    /// </summary>
    [StateTreeCategory("Tasks/World", "Put the single object carrying a tag on the blackboard")]
    public sealed class FindKnownTask : StateTreeTaskAsset
    {
        [StateTreeKey(StateTreeKeyKind.Tag)]
        public StateTreeKeyField tag = new StateTreeKeyField();

        /// <summary>Blackboard key the found GameObject lands under — the perception
        /// convention key by default, so the combat tasks read it unchanged.</summary>
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField targetKey = new StateTreeKeyField("target");

        /// <summary>World units; 0 or less = any distance. Beyond it the target is CLEARED,
        /// so "lost sight" is the same fact as "never saw".</summary>
        public float maxDistance;

        [InjectService] private WorldService m_World;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;

            WorldObjectBehaviour known = m_World.FindKnown((string)tag);
            var key = (string)targetKey;
            if (known == null)
            {
                context.blackboard.Remove(key);
                return StateTreeStatus.Failure;
            }

            if (maxDistance > 0f)
            {
                float distance = StateTreeLibraryUtil.PlanarDistance(context.owner,
                    known.gameObject);
                if (distance > maxDistance)
                {
                    context.blackboard.Remove(key);
                    return StateTreeStatus.Failure;
                }
            }

            context.blackboard[key] = known.gameObject;
            return StateTreeStatus.Success;
        }
    }
}
