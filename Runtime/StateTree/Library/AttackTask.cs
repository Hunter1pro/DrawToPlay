using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Lands one hit on the blackboard target's <see cref="HealthComponent"/> and fires
    /// <c>context.EmitDamageDealt</c> — the M6 exit criterion's business end ("the zombie
    /// preset tree chases and ATTACKS a target"). Brief §7.2 lists Damage as an Effect; in
    /// M6 there is no separate effect-graph layer yet, so the effect is applied by the task
    /// that owns the swing.
    ///
    /// DAMAGE COMES FROM THE WEAPON DEF (M5 data, consumed not re-authored):
    /// <c>weapon.DamageAt(weaponLevel)</c>, which applies the tier ladder. With no weapon
    /// assigned it falls back to <see cref="fallbackDamage"/>, so a preset can be built and
    /// run before any weapon assets exist.
    ///
    /// RESULT SEMANTICS, and why they are what they are:
    /// <list type="bullet">
    /// <item>no target / target out of <see cref="range"/> — Failure. The attack state is over
    /// and the tree's completion transition sends the entity back to chasing. Attempting to
    /// close the distance from inside the attack state is what turns a two-state AI into a
    /// tangle.</item>
    /// <item>cooldown not expired — Running. The state holds, the entity stands in the target's
    /// face waiting to swing again, which is what the animation and the tree both expect.</item>
    /// <item>hit landed — Success, exactly one hit per entry. </item>
    /// <item>hit rejected by i-frames — Running, NOT Success. HealthComponent.TakeDamage
    /// returning false is documented as "the striker skips knockback and retries next frame
    /// rather than consuming its once-per-strike hit record", so consuming the cooldown here
    /// would let an i-frame window silently eat an entire attack.</item>
    /// </list>
    ///
    /// THE COOLDOWN CLOCK IS <see cref="Time.time"/>, absolute, and deliberately NOT reset in
    /// OnEnter — a cooldown that only counted down while the attack state was active would
    /// recharge for free during the chase back into range, and the entity would attack at
    /// whatever rate it could close distance. The deadline also survives into the blackboard
    /// under <see cref="cooldownKey"/> when one is set, so a
    /// <see cref="CooldownReadyCondition"/> on a transition can gate the same clock.
    ///
    /// CANCELLED-SAFE: the only state is the cooldown deadline, and a cancelled swing must NOT
    /// clear it (that would be a free attack for anyone who interrupts). Nothing is spawned,
    /// nothing is subscribed, so OnExit has nothing to release.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Attack", fileName = "Attack")]
    [StateTreeCategory("Tasks/Combat", "Deal weapon damage to target in range, with cooldown")]
    public sealed class AttackTask : StateTreeTaskAsset
    {
        /// <summary>Damage source. Null = use <see cref="fallbackDamage"/>.</summary>
        public WeaponDefAsset weapon;

        /// <summary>1-based weapon level for the tier multiplier
        /// (<see cref="WeaponDefAsset.DamageAt"/>).</summary>
        public int weaponLevel = 1;

        /// <summary>Damage when no weapon is assigned.</summary>
        public float fallbackDamage = 1f;

        /// <summary>Reach in world units, measured owner→target in the XY plane.</summary>
        public float range = 1f;

        /// <summary>Read <see cref="StateTreeLibraryUtil.AttackRangeKey"/> instead of
        /// <see cref="range"/>.</summary>
        public bool useBlackboardRange;

        /// <summary>Seconds between hits. 0 = every tick. A weapon's own
        /// <see cref="WeaponDefAsset.cooldown"/> is used instead when
        /// <see cref="useWeaponCooldown"/> is set.</summary>
        public float cooldown = 1f;

        /// <summary>Take the cooldown from the weapon def rather than
        /// <see cref="cooldown"/>.</summary>
        public bool useWeaponCooldown = true;

        /// <summary>Optional blackboard key mirroring the next-ready time, so a transition can
        /// gate on the same clock with <see cref="CooldownReadyCondition"/>. Empty = keep the
        /// cooldown private to this task.</summary>
        public string cooldownKey = "";

        /// <summary>Face the target before swinging.</summary>
        public bool faceTarget = true;

        private float m_NextAttackTime;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null)
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return StateTreeStatus.Failure;

            Vector2 offset = StateTreeLibraryUtil.PlanarOffset(owner, target);
            float reach = StateTreeLibraryUtil.ResolveFloat(context,
                StateTreeLibraryUtil.AttackRangeKey, range, useBlackboardRange);
            if (reach > 0f && offset.magnitude > reach)
                return StateTreeStatus.Failure;

            if (faceTarget)
                FaceTargetTask.ApplyFacing(owner.transform, offset.x);

            if (Time.time < m_NextAttackTime)
                return StateTreeStatus.Running;

            HealthComponent health =
                StateTreeLibraryUtil.ResolveComponent<HealthComponent>(target);
            if (health == null)
                return StateTreeStatus.Failure;

            float damage = weapon != null ? weapon.DamageAt(weaponLevel) : fallbackDamage;

            // sourcePosition is the striker's own position: knockback and hit-flash cues
            // derive their direction from it (HealthComponent.damaged).
            if (!health.TakeDamage(damage, owner.transform.position))
                return StateTreeStatus.Running;

            ArmCooldown(context);
            context.EmitDamageDealt(owner, target, damage);
            return StateTreeStatus.Success;
        }

        private void ArmCooldown(StateTreeContext context)
        {
            float seconds = useWeaponCooldown && weapon != null ? weapon.cooldown : cooldown;
            m_NextAttackTime = Time.time + Mathf.Max(seconds, 0f);
            if (!string.IsNullOrEmpty(cooldownKey))
                StateTreeLibraryUtil.SetFloat(context, cooldownKey, m_NextAttackTime);
        }
    }
}
