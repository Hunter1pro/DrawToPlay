using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Perception: acquires the nearest living <see cref="HealthComponent"/> of a different
    /// <see cref="CombatTeam"/> within <see cref="detectRange"/> and writes it to
    /// blackboard["target"], returning true when a target is held. Port of
    /// <c>ai_choose_target_task.gd</c>'s selection collapsed into
    /// <c>enemies_in_radius_condition.gd</c>'s shape — one component, because in this tree
    /// shape the same test both fires the idle→chase interrupt AND supplies the target that
    /// chase then consumes.
    ///
    /// THE ONE WRITE: this is the only condition in the library that mutates the blackboard,
    /// and it is deliberate — acquisition IS the side effect being tested. It is idempotent
    /// (re-evaluating with the same world state produces the same target), which is what makes
    /// it safe on a checkWhileRunning transition evaluated every tick.
    ///
    /// STICKINESS: <see cref="keepExistingTarget"/> mirrors Godot's
    /// <c>keep_existing_valid_target</c> — an already-held target that is still alive and
    /// still inside <see cref="loseRange"/> is kept, so a zombie does not re-aim at whichever
    /// victim happens to be a hair closer this frame. <see cref="loseRange"/> larger than
    /// <see cref="detectRange"/> gives the classic hysteresis band: hard to notice you, harder
    /// to shake off.
    ///
    /// SOURCE (M9): candidates come from the world registry —
    /// <see cref="WorldService.CollectByTag"/> on <see cref="WorldTags.Combatant"/>, the tag
    /// every <see cref="HealthComponent"/> enrolls itself under. Exact and immediate (a spawn
    /// is visible the same tick — the polled scan's quarter-second lag is gone with the poll),
    /// and every query lands in the world's deep log. The per-evaluation work is a linear pass
    /// over one tag bucket into a reused buffer. NO WorldService reachable through the spine
    /// is a wiring error, not an empty world: false, plus one warning per activation —
    /// perception REQUIRES the world now.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Target Detected",
        fileName = "TargetDetected")]
    [StateTreeCategory("Conditions/Perception", "Acquire nearest living enemy into blackboard target")]
    public sealed class TargetDetectedCondition : StateTreeConditionAsset
    {
        /// <summary>Acquisition radius, world units.</summary>
        public float detectRange = 6f;

        /// <summary>World units at which an already-held target is dropped. 0 = reuse
        /// <see cref="detectRange"/>. Larger than detectRange = hysteresis.</summary>
        public float loseRange;

        /// <summary>Read <see cref="StateTreeLibraryUtil.DetectRangeKey"/> instead of
        /// <see cref="detectRange"/>.</summary>
        public bool useBlackboardRange;

        /// <summary>Keep a still-valid target instead of re-picking the nearest every
        /// evaluation (Godot <c>keep_existing_valid_target</c>).</summary>
        public bool keepExistingTarget = true;

        /// <summary>Clear blackboard["target"] when nothing qualifies. On for the detector
        /// that drives an idle↔chase interrupt (a stale target would keep the chase alive);
        /// off when a second component owns target lifetime.</summary>
        public bool clearTargetWhenNone = true;

        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        private bool m_WarnedNoService;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null)
                return false;

            GameObject owner = context.owner;
            if (owner == null)
                return false;

            // The owner's own team is what "hostile" is measured against. No health on the
            // owner means no team, so nothing can be classified as hostile: false, per the
            // no-component rule.
            HealthComponent ownerHealth = StateTreeLibraryUtil.ResolveComponent<HealthComponent>(owner);
            if (ownerHealth == null)
                return false;

            float acquireRange = StateTreeLibraryUtil.ResolveFloat(context,
                StateTreeLibraryUtil.DetectRangeKey, detectRange, useBlackboardRange);
            float dropRange = loseRange > 0f ? loseRange : acquireRange;

            if (keepExistingTarget)
            {
                GameObject held = StateTreeLibraryUtil.GetValidTarget(context, owner);
                if (held != null
                    && StateTreeLibraryUtil.PlanarDistance(owner, held) <= dropRange)
                    return true;
            }

            GameObject nearest = FindNearestHostile(owner, ownerHealth.team, acquireRange);
            if (nearest == null)
            {
                if (clearTargetWhenNone)
                    StateTreeLibraryUtil.ClearTarget(context);
                return false;
            }

            StateTreeLibraryUtil.SetTarget(context, nearest);
            return true;
        }

        /// <summary>Nearest living opposite-team combatant inside <paramref name="range"/>,
        /// asked of the world registry's <see cref="WorldTags.Combatant"/> bucket and compared
        /// by squared distance so the pass never calls a square root. The health pool is
        /// resolved per candidate (<see cref="StateTreeLibraryUtil.ResolveComponent{T}"/>, so
        /// the health-as-child layout still answers); a citizen that lost its health somehow is
        /// simply not hostile.</summary>
        private GameObject FindNearestHostile(GameObject owner, CombatTeam ownerTeam, float range)
        {
            WorldService world = StateTreeContextHost.FindService<WorldService>(owner);
            if (world == null)
            {
                if (!m_WarnedNoService)
                {
                    m_WarnedNoService = true;
                    Debug.LogWarning("TargetDetectedCondition: no WorldService reachable from '"
                        + owner.name + "' — perception reads the world registry now (M9); add a "
                        + "Root context host with a WorldService to the scene.", owner);
                }
                return null;
            }

            m_Buffer.Clear();
            world.CollectByTag(WorldTags.Combatant, m_Buffer);

            float rangeSq = range > 0f ? range * range : float.PositiveInfinity;
            float bestSq = float.PositiveInfinity;
            GameObject best = null;

            for (int i = 0; i < m_Buffer.Count; i++)
            {
                WorldObjectBehaviour citizen = m_Buffer[i];
                if (citizen == null)
                    continue;

                GameObject candidate = citizen.gameObject;
                if (candidate == owner)
                    continue;

                HealthComponent health =
                    StateTreeLibraryUtil.ResolveComponent<HealthComponent>(candidate);
                if (health == null || health.team == ownerTeam || !health.isAlive)
                    continue;

                float distanceSq =
                    StateTreeLibraryUtil.PlanarOffset(owner, candidate).sqrMagnitude;
                if (distanceSq > rangeSq || distanceSq >= bestSq)
                    continue;

                bestSq = distanceSq;
                best = candidate;
            }
            return best;
        }
    }
}
