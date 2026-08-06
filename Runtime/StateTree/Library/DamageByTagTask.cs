using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Deal damage to the nearest LIVING tagged combatant (or every one of them) — an attack
    /// addressed by TAG through the registry instead of by perception: a smite command, a
    /// trap's bite, a scripted execution. Fails when nothing living carries the tag, which is
    /// the branchable "nothing to hit".
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Damage By Tag", fileName = "DamageByTag")]
    [StateTreeCategory("Tasks/World", "Damage the nearest (or every) living tagged combatant")]
    public sealed class DamageByTagTask : StateTreeTaskAsset
    {
        public string tag = "";

        public float amount = 1f;

        /// <summary>Nearest to the owner only (default), or everyone carrying the tag.</summary>
        public bool nearestOnly = true;

        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(tag) || amount <= 0f)
                return StateTreeStatus.Failure;

            WorldService world = StateTreeContextHost.FindService<WorldService>(context.owner);
            if (world == null)
                return StateTreeStatus.Failure;

            m_Buffer.Clear();
            world.CollectByTag(tag, m_Buffer);

            HealthComponent nearest = null;
            float nearestSq = float.PositiveInfinity;
            bool hitAny = false;
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                if (m_Buffer[i] == null)
                    continue;
                HealthComponent health = StateTreeLibraryUtil
                    .ResolveComponent<HealthComponent>(m_Buffer[i].gameObject);
                if (health == null || !health.isAlive)
                    continue;

                if (!nearestOnly)
                {
                    health.TakeDamage(amount, OwnerPosition(context));
                    hitAny = true;
                    continue;
                }

                float sq = StateTreeLibraryUtil
                    .PlanarOffset(context.owner, m_Buffer[i].gameObject).sqrMagnitude;
                if (sq < nearestSq)
                {
                    nearestSq = sq;
                    nearest = health;
                }
            }

            if (nearestOnly && nearest != null)
                hitAny = nearest.TakeDamage(amount, OwnerPosition(context));

            return hitAny ? StateTreeStatus.Success : StateTreeStatus.Failure;
        }

        private static Vector2 OwnerPosition(StateTreeContext context)
        {
            return context.owner != null
                ? (Vector2)context.owner.transform.position
                : Vector2.zero;
        }
    }
}
