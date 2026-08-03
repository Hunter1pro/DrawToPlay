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
    /// COST: the candidate list comes from <see cref="HealthScanCache"/> (a polled
    /// FindObjectsByType, refreshed at most every <see cref="rescanInterval"/> seconds and
    /// shared by every detector in the scene) — read its class comment before tuning. The
    /// per-evaluation work here is a linear pass over the cached list, no allocation.
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

        /// <summary>Seconds between scene scans — a hint to the shared
        /// <see cref="HealthScanCache"/>, not a per-component guarantee.</summary>
        public float rescanInterval = HealthScanCache.DefaultInterval;

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

        /// <summary>Nearest living opposite-team health pool inside <paramref name="range"/>,
        /// compared by squared distance so the scan never calls a square root.</summary>
        private GameObject FindNearestHostile(GameObject owner, CombatTeam ownerTeam, float range)
        {
            List<HealthComponent> candidates = HealthScanCache.Scan(rescanInterval);
            float rangeSq = range > 0f ? range * range : float.PositiveInfinity;
            float bestSq = float.PositiveInfinity;
            GameObject best = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                HealthComponent health = candidates[i];
                // The cache is allowed to go stale between scans — see HealthScanCache.
                if (health == null || health.team == ownerTeam || !health.isAlive)
                    continue;

                GameObject candidate = health.gameObject;
                if (candidate == owner)
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
