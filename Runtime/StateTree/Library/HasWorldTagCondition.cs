using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while ANY live world object carries the tag — existence as a condition, which is
    /// how "a beacon is lit somewhere" or "no enemies remain" guards a transition without an
    /// event bus: the interrupt is the subscription, the registry is the source of truth.
    ///
    /// Quiet on every failure path (no service, empty tag), for the same reason
    /// <see cref="HasContextKeyCondition"/> is: this evaluates per tick on interrupts, and the
    /// wiring warning belongs to the task-side atoms that run once.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Has World Tag",
        fileName = "HasWorldTag")]
    [StateTreeCategory("Conditions/World", "A world object with this tag exists (or none does)")]
    public sealed class HasWorldTagCondition : StateTreeConditionAsset
    {
        [StateTreeKey(StateTreeKeyKind.Tag)]
        public string tag = "";

        /// <summary>True while NO object carries the tag instead — "all enemies cleared".</summary>
        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool present = false;
            if (context != null && !string.IsNullOrEmpty(tag))
            {
                WorldService world =
                    StateTreeContextHost.FindService<WorldService>(context.owner);
                present = world != null && world.HasTag(tag);
            }
            return invert ? !present : present;
        }
    }
}
