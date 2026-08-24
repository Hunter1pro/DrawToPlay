using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True when a health pool sits below (or above) a fraction of its maximum — the
    /// "flee at 20%", "enrage under half", "target is dead" gate. Covers Godot's
    /// <c>ai_target_dead_condition.gd</c> as the degenerate case (source Target, Below,
    /// fraction 0).
    ///
    /// One component reads either pool because that is the whole difference between the two
    /// uses: <see cref="source"/> picks the owner's own bar or the blackboard target's.
    ///
    /// Fraction is <c>current / effective</c> read from the attribute store, and current is
    /// allowed to go negative on an overkill (Consume keeps it as information), so Below 0
    /// is a correct "is dead" test rather than an off-by-a-hair one. An effective of zero
    /// cannot be divided by: that pool is treated as empty, so Below is true, Above false.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Health Threshold",
        fileName = "HealthThreshold")]
    [StateTreeCategory("Conditions/Combat", "Owner or target HP fraction vs threshold")]
    public sealed class HealthThresholdCondition : StateTreeConditionAsset
    {
        public enum Source
        {
            Owner,
            Target
        }

        public enum Comparison
        {
            /// <summary>fraction &lt;= threshold — inclusive, so threshold 0 catches
            /// exactly-dead.</summary>
            AtOrBelow,

            /// <summary>fraction &gt; threshold.</summary>
            Above
        }

        /// <summary>Whose health pool to read.</summary>
        public Source source = Source.Owner;

        public Comparison comparison = Comparison.AtOrBelow;

        /// <summary>Normalised hit points, 0..1.</summary>
        [Range(0f, 1f)]
        public float fraction = 0.5f;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null)
                return false;

            GameObject subject = source == Source.Owner
                ? context.owner
                : StateTreeLibraryUtil.GetTarget(context);
            if (subject == null)
                return false;

            // The number lives on the attribute store — current over EFFECTIVE, so a
            // max-health modifier moves this gate's scale too.
            AttributeComponent vitals =
                StateTreeLibraryUtil.ResolveComponent<AttributeComponent>(subject);
            if (vitals == null || !vitals.Has(AttributeNames.Health))
                return false;
            float cap = vitals.Effective(AttributeNames.Health);
            float current = cap > 0f ? vitals.Value(AttributeNames.Health) / cap : 0f;
            return comparison == Comparison.AtOrBelow ? current <= fraction : current > fraction;
        }
    }
}
