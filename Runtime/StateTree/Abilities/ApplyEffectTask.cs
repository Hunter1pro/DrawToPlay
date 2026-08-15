using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// APPLY AN EFFECT ROW to a target (M23, reworked on review) — the task that moved
    /// effects out of ability-row data and onto the ability's TREE, where the sequencing
    /// lives: swing, then land the effect on whoever the swing struck; or breathe, and land
    /// the recovery on yourself. The effect is a PICKED row (list the effect registry in the
    /// tree's Data), the target is declared, and the same row works both ways — being struck
    /// and self-recovery are one call on the target's <see cref="AbilityHost"/>.
    ///
    /// A missing target is a MISS, not a failure: the swing that connected with nobody flows
    /// on, and the tree does not have to branch on it. A target without an AbilityHost still
    /// takes Instant health (through its HealthComponent); statuses need a host to live on
    /// and are skipped with one warning.
    /// </summary>
    [StateTreeCategory("Tasks/Abilities", "Apply a picked effect row to self or a struck target")]
    public sealed class ApplyEffectTask : StateTreeTaskAsset
    {
        public enum Target
        {
            /// <summary>The owner applies it to itself — recovery, a self-buff.</summary>
            Self = 0,

            /// <summary>Whoever a blackboard key names — the swing's published victim.</summary>
            FromBlackboard = 1
        }

        [Tooltip("The effect row — picked from the registry the tree lists in Data.")]
        public StateTreeEntryRef<EffectDef> effect = new StateTreeEntryRef<EffectDef>();

        public Target target = Target.Self;

        [Tooltip("FromBlackboard: the key holding the target (a GameObject or a component on "
            + "it) — what SwingTask publishes as its struck victim. Absent or null = a miss: "
            + "the task succeeds having applied nothing.")]
        [StateTreeKey(StateTreeKeyKind.String, any: true)]
        public StateTreeKeyField targetKey = new StateTreeKeyField("struck");

        [InjectOwner] private AbilityHost m_Owner;

        private bool m_WarnedNoHost;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Owner == null)
                return StateTreeStatus.Failure;

            AbilityService service = m_Owner.service;
            EffectDef row = service != null ? service.FindEffect(effect.entryName) : null;
            if (row == null)
            {
                Debug.LogError("[ApplyEffect] no effect row named '" + effect.entryName
                    + "' in the service's registries.", m_Owner);
                return StateTreeStatus.Failure;
            }

            GameObject victim = ResolveTarget(context);
            if (victim == null)
                return StateTreeStatus.Success;   // a miss is a normal outcome

            var host = victim.GetComponent<AbilityHost>();
            if (host != null)
            {
                // The owner is the SOURCE of this application — what a Source-aspect cue on
                // the row shows at (the caster's flash on a landed hit).
                host.ApplyEffect(row, m_Owner.gameObject);
                return StateTreeStatus.Success;
            }

            // No host: an Instant health delta still lands — a crate can be hurt — but a
            // status has nowhere to live.
            if (row.duration == AbilityEffectDuration.Instant
                && row.operation == EffectOperation.Delta
                && string.Equals(row.attribute.entryName, HealthComponent.AttributeName,
                    System.StringComparison.Ordinal))
            {
                var health = victim.GetComponent<HealthComponent>();
                if (health != null)
                {
                    float magnitude = AbilityHost.ScaledMagnitude(row, m_Owner.gameObject,
                        service);
                    if (magnitude > 0f)
                        health.Heal(magnitude);
                    else
                        health.TakeDamage(-magnitude);
                    return StateTreeStatus.Success;
                }
            }
            if (!m_WarnedNoHost)
            {
                m_WarnedNoHost = true;
                Debug.LogWarning("[ApplyEffect] target '" + victim.name + "' has no "
                    + "AbilityHost — effect '" + row.name + "' was skipped.", m_Owner);
            }
            return StateTreeStatus.Success;
        }

        private GameObject ResolveTarget(StateTreeContext context)
        {
            if (target == Target.Self)
                return m_Owner.gameObject;
            if (context == null
                || !context.blackboard.TryGetValue((string)targetKey, out object held))
                return null;
            switch (held)
            {
                case GameObject go:
                    return go;
                case Component component when component != null:
                    return component.gameObject;
                default:
                    return null;
            }
        }
    }
}
