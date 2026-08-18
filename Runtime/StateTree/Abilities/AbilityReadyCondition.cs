using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// IS IT LOADED? (M28) — true when the owner could actually use this ability row right now:
    /// off cooldown, not blocked by a standing tag, holding whatever the row requires.
    ///
    /// It exists for the weapon that fires ITSELF. A gun with no button needs its edge gated by
    /// something, and the tempting shortcut — a cooldown key on the blackboard — puts the
    /// reload in two places: one number for the transition and another on the row, free to
    /// disagree the first time an upgrade moves one of them. This asks the ROW, through the
    /// host that owns the timer, so the strip at the bottom, the gate and the ability itself
    /// are all reading the same clock.
    ///
    /// A transition that fired while the ability was still reloading would enter the firing
    /// state, be refused there, complete, and come back — every frame, forever. That is what
    /// this prevents, and why it belongs on the edge rather than inside the ability.
    /// </summary>
    [StateTreeCategory("Conditions/Abilities", "True when an ability row is ready to use")]
    public sealed class AbilityReadyCondition : StateTreeConditionAsset
    {
        [Tooltip("The row being asked about.")]
        public StateTreeEntryRef<AbilityDef> ability = new StateTreeEntryRef<AbilityDef>();

        [Tooltip("True while it is NOT ready instead — 'while the gun is reloading'.")]
        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool ready = Ready(context);
            return invert ? !ready : ready;
        }

        private bool Ready(StateTreeContext context)
        {
            if (context == null || context.owner == null
                || string.IsNullOrEmpty(ability.entryName))
                return false;

            var host = context.owner.GetComponent<AbilityHost>();
            if (host == null || host.service == null)
                return false;

            AbilityDef row = host.service.Find(ability.entryName);
            if (row == null)
                return false;

            // ONE AT A TIME, still: an actor already swinging is not ready for anything, and
            // the gate has to agree with the host or the state thrash comes back.
            return host.active == null && host.CooldownReady(row);
        }
    }
}
