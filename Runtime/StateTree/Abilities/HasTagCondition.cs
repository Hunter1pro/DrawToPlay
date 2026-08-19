using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE OWNER HOLDS A TAG — the read side of the status system as a transition edge. A
    /// hit that applies a 'struck' row (Duration, tag Struck) makes "I was just hit" a fact
    /// with a lifetime; this condition is how a flinch edge reads it — replacing the bespoke
    /// damage-event latch with the same tags everything else already gates on
    /// (<see cref="AbilityHost.HasTag"/>: the active ability's activation tags plus every
    /// running status's granted tags).
    /// </summary>
    [StateTreeCategory("Conditions/Abilities", "The owner holds an ability or status tag")]
    public sealed class HasTagCondition : StateTreeConditionAsset
    {
        [Tooltip("The tag to look for — an activation tag of the running ability, or a "
            + "granted tag of an active status ('Struck', 'Poisoned', 'Guarded').")]
        [WorldTag]
        public string tag = "";

        [Tooltip("Invert: pass while the owner does NOT hold the tag.")]
        public bool absent;

        [InjectOwner] private AbilityHost m_Owner;

        public override bool Evaluate(StateTreeContext context)
        {
            if (m_Owner == null || string.IsNullOrEmpty(tag))
                return false;
            return m_Owner.HasTag(tag) != absent;
        }
    }
}
