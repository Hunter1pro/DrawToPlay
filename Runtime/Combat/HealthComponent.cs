using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Which side a health pool or strike volume belongs to — port of the
    /// <c>@export_enum("player", "enemy")</c> pair shared by health.gd and hitbox.gd. Hitboxes
    /// only damage the OPPOSITE team, which is the whole reason the tag lives on the health
    /// pool rather than on the body.</summary>
    public enum CombatTeam
    {
        Player,
        Enemy
    }

    /// <summary>
    /// Hit points for any drawn entity — port of hunter's <c>health.gd</c> (draw-tool-port-brief.md
    /// §6.2 "Health (max HP, i-frames)"). Damage is gated by an invulnerability window so a swept
    /// hitbox cannot land twice in the same strike, damage-over-time bypasses that window (§ the
    /// Godot <c>tick_damage</c> path), and death is a one-shot event.
    ///
    /// Death also closes the §6.3 destruction seam: "HP threshold wired to the same Health
    /// component". When the entity carries a <see cref="DestructibleShape"/>, dying calls
    /// <see cref="DestructibleShape.Fragment"/>, so a drawn prop that runs out of HP breaks into
    /// physical debris through exactly the same code path an impact takes.
    ///
    /// Godot's health.gd also drove a hit-flash ShaderMaterial (flash.gdshader) on the parent
    /// CanvasItem. That is a VFX concern with no port in this toolset yet, so the flash fields are
    /// deliberately absent — <see cref="damaged"/> is the hook a future flash/cue component
    /// subscribes to. The <c>EffectStack.incoming_mult()</c> multiplier is likewise absent: status
    /// effects are data-only in M5 (<see cref="EffectDefAsset"/>), with no runtime stack yet.
    /// </summary>
    public sealed class HealthComponent : MonoBehaviour
    {
        /// <summary>health.gd <c>max_hp</c>. HP is a float because damage-over-time drains
        /// fractional amounts every tick.</summary>
        public float maxHP = 3f;

        /// <summary>health.gd <c>team</c>. Strikes only damage the opposite team.</summary>
        public CombatTeam team = CombatTeam.Enemy;

        /// <summary>Seconds of invulnerability after every hit — health.gd <c>hit_grace</c>,
        /// renamed to the brief's vocabulary ("i-frames", §6.2).</summary>
        public float iFrames = 0.25f;

        /// <summary>health.gd <c>poise</c>: super armour — hits never knock back or stagger. Data
        /// only here; the striker reads it (hitbox.gd checks <c>child.poise</c> before applying
        /// knockback).</summary>
        public bool poise;

        /// <summary>Run the §6.3 destruction seam on death. Off = the entity just dies and leaves
        /// its GameObject alone (an enemy that plays a death animation, say).</summary>
        public bool fragmentOnDeath = true;

        /// <summary>Optional explicit break target. Left empty it resolves to the first
        /// <see cref="DestructibleShape"/> on this GameObject or a parent — which covers both the
        /// Unity layout (health beside the shape) and Godot's (Health as a CHILD of the body).</summary>
        public DestructibleShape destructible;

        /// <summary>Damage landed: (amount, world position of the source). health.gd's
        /// <c>damaged(amount, source_pos)</c> signal — a flash/knockback/VFX cue subscribes here.
        /// Not raised by <see cref="TickDamage"/>, matching the Godot split ("effects drain
        /// quietly").</summary>
        public event System.Action<float, Vector2> damaged;

        /// <summary>HP reached zero. Raised exactly once — re-entrant damage during a
        /// <see cref="damaged"/> handler cannot double-kill.</summary>
        public event System.Action died;

        /// <summary>Current hit points. Allowed to go negative on an overkill, exactly as health.gd
        /// leaves it: overkill amount is information a damage-number cue wants.</summary>
        public float hp { get; private set; }

        public bool isAlive => hp > 0f;

        /// <summary>True while the i-frame window from the last hit is still open.</summary>
        public bool isInvulnerable => Time.time < m_GraceUntil;

        /// <summary>Absolute time the i-frame window closes. A deadline rather than health.gd's
        /// per-tick <c>_grace</c> countdown: identical behaviour, no Update, and exact at any frame
        /// rate. <c>maxf(_grace, t)</c> becomes a max over deadlines the same way.</summary>
        private float m_GraceUntil;

        private bool m_Died;

        private void Awake()
        {
            hp = maxHP;
        }

        /// <summary>Extra invulnerability window on top of the current one — health.gd
        /// <c>grant_invuln</c> (dash i-frames and friends). Never shortens an open window.</summary>
        public void GrantInvulnerability(float seconds)
        {
            m_GraceUntil = Mathf.Max(m_GraceUntil, Time.time + seconds);
        }

        /// <summary>Take a hit from <paramref name="sourcePosition"/> (world space, used by
        /// knockback and VFX). Returns true when the damage LANDED — false while invulnerable or
        /// already dead, in which case health.gd's contract says the striker skips knockback and
        /// retries next frame rather than consuming its once-per-strike hit record.</summary>
        public bool TakeDamage(float amount, Vector2 sourcePosition)
        {
            if (isInvulnerable || hp <= 0f)
                return false;

            m_GraceUntil = Time.time + iFrames;
            hp -= amount;
            damaged?.Invoke(amount, sourcePosition);

            if (hp <= 0f)
                Die();
            return true;
        }

        /// <summary>Self-sourced damage (falling, hazards) — the source is this entity's own
        /// position, so a knockback cue resolves to no direction.</summary>
        public bool TakeDamage(float amount)
        {
            return TakeDamage(amount, transform.position);
        }

        /// <summary>Damage-over-time tick — health.gd <c>tick_damage</c>: no i-frame gate, no
        /// <see cref="damaged"/> event, because a poison tick must not eat the window that protects
        /// against the next real hit.</summary>
        public void TickDamage(float amount)
        {
            if (hp <= 0f)
                return;

            hp -= amount;
            if (hp <= 0f)
                Die();
        }

        /// <summary>health.gd <c>heal</c>: the dead stay dead, and HP never exceeds
        /// <see cref="maxHP"/>.</summary>
        public void Heal(float amount)
        {
            if (hp <= 0f)
                return;

            hp = Mathf.Min(hp + amount, maxHP);
        }

        /// <summary>Restore to full and clear the death latch — for pooled/respawned entities.
        /// Not in health.gd (Godot frees the node instead), added because a Unity entity that gets
        /// re-enabled would otherwise be permanently un-killable.</summary>
        public void ResetHealth()
        {
            hp = maxHP;
            m_Died = false;
            m_GraceUntil = 0f;
        }

        /// <summary>Fires <see cref="died"/> once, then runs the destruction seam. Order matters:
        /// subscribers (loot drops, cues) run BEFORE the fragment call, which may destroy this
        /// GameObject.</summary>
        private void Die()
        {
            if (m_Died)
                return;
            m_Died = true;

            died?.Invoke();

            if (!fragmentOnDeath)
                return;

            var target = ResolveDestructible();
            if (target != null)
                target.Fragment(transform.position);
        }

        /// <summary>The break target, cached after the first successful lookup.
        /// GetComponentInParent includes this GameObject, so the same call covers both layouts.</summary>
        private DestructibleShape ResolveDestructible()
        {
            if (destructible == null)
                destructible = GetComponentInParent<DestructibleShape>();
            return destructible;
        }
    }
}
