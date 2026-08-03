using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True when the blackboard target is within <see cref="range"/> of the owner — port of
    /// <c>ai_target_within_range_condition.gd</c>, the gate that turns chase into attack.
    ///
    /// Godot's version also writes <c>target_distance</c> / <c>target_last_position</c> back
    /// into the blackboard. M6 locks the blackboard to four keys, so those side-effect writes
    /// are dropped: a condition that mutates state is a condition that behaves differently
    /// depending on how often the runner happens to evaluate it, and interrupts are evaluated
    /// every single tick.
    ///
    /// <see cref="minRange"/> keeps the archer honest: 0 means "no inner bound", anything
    /// larger makes this an annulus test so a keep-your-distance tree can ask "am I in my
    /// firing band?" with one component.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Target In Range",
        fileName = "TargetInRange")]
    [StateTreeCategory("Conditions/Perception", "Target within range (optional min annulus)")]
    public sealed class TargetInRangeCondition : StateTreeConditionAsset
    {
        /// <summary>World units. The upper bound; 0 or less means "no upper bound".</summary>
        public float range = 1f;

        /// <summary>World units. Lower bound, 0 = none — an annulus for ranged archetypes
        /// that must not be standing on top of their target.</summary>
        public float minRange;

        /// <summary>Read <see cref="StateTreeLibraryUtil.AttackRangeKey"/> instead of
        /// <see cref="range"/>. Off by default: M6 conventions prefer serialized params, the
        /// blackboard is for cross-task state.</summary>
        public bool useBlackboardRange;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null)
                return false;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return false;

            float distance = StateTreeLibraryUtil.PlanarDistance(owner, target);
            float maxRange = StateTreeLibraryUtil.ResolveFloat(context,
                StateTreeLibraryUtil.AttackRangeKey, range, useBlackboardRange);

            if (minRange > 0f && distance < minRange)
                return false;
            return maxRange <= 0f || distance <= maxRange;
        }
    }
}
