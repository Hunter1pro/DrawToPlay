using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Shoves the blackboard target's physics body away from the owner — brief §7.2's
    /// "Knockback (impulse on the target's PhysicsBody)", and the M6 stand-in for the whole
    /// Effect layer alongside the damage in <see cref="AttackTask"/>. Port of the intent
    /// behind Godot's <c>knockback_task.gd</c>, which integrates a decaying velocity by hand
    /// because a CharacterBody3D has no impulse; here the physics engine owns the decay
    /// (friction, damping), so the task is a single impulse and done.
    ///
    /// VERIFIED API (unity-physicscore2d-bodies-api):
    /// <c>PhysicsBody.ApplyLinearImpulseToCenter(Vector2 impulse, bool wake)</c> and
    /// <c>PhysicsBody.mass</c>. ToCenter rather than the at-a-point overload on purpose: a
    /// point impulse also spins the victim, and a drawn character sent tumbling by every hit
    /// reads as a physics bug rather than as a hit.
    ///
    /// SPEED → IMPULSE. <see cref="WeaponDefAsset.knockback"/> is a SPEED in world units per
    /// second (Godot's 90 px/s ÷ 32), but an impulse is measured in kg·m/s. Multiplying by
    /// <c>body.mass</c> converts one into the other, so the resulting velocity CHANGE equals
    /// the authored speed no matter how heavy the victim's drawn body turned out — which is
    /// the property a designer tuning "knockback: 3" actually expects. <see cref="scale"/>
    /// then tunes per-task without editing the shared weapon asset. Set
    /// <see cref="massIndependent"/> off to apply the value as a raw impulse instead, where
    /// heavier things move less.
    ///
    /// FAILURE IS THE ANSWER TO EVERY MISSING PIECE (library rule): no target, no
    /// <see cref="EntityBody"/> on it, an invalid body (the target is a static prop, or play
    /// mode has not created bodies yet), or the two entities standing at exactly the same
    /// point so there is no direction to push along. Failure rather than a silent Success
    /// keeps the mistake visible in the tree.
    ///
    /// Stateless and instantaneous — nothing to undo when an interrupt lands.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Knockback Effect",
        fileName = "KnockbackEffect")]
    [StateTreeCategory("Tasks/Combat", "Impulse on the target body away from owner")]
    public sealed class KnockbackEffectTask : StateTreeTaskAsset
    {
        /// <summary>Knockback source. Null = use <see cref="knockback"/>.</summary>
        public WeaponDefAsset weapon;

        /// <summary>Knockback speed in world units/second when no weapon is assigned.</summary>
        public float knockback = 90f / 32f;

        /// <summary>Multiplier on the resolved knockback — a heavy attack reusing a light
        /// weapon's def.</summary>
        public float scale = 1f;

        /// <summary>Extra upward push in world units/second, applied on top of the
        /// owner→target direction. A pure horizontal shove slides a victim along the ground;
        /// a little lift is what makes a hit read.</summary>
        public float lift;

        /// <summary>Multiply by the victim's mass so the velocity change matches the authored
        /// speed regardless of body mass. Off = the value is a raw impulse.</summary>
        public bool massIndependent = true;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null)
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return StateTreeStatus.Failure;

            EntityBody entityBody = StateTreeLibraryUtil.ResolveComponent<EntityBody>(target);
            if (entityBody == null)
                return StateTreeStatus.Failure;

            PhysicsBody body = entityBody.body;
            if (!body.isValid)
                return StateTreeStatus.Failure;

            Vector2 offset = StateTreeLibraryUtil.PlanarOffset(owner, target);
            float distance = offset.magnitude;
            if (distance <= 0f)
                return StateTreeStatus.Failure;

            float speed = (weapon != null ? weapon.knockback : knockback) * scale;
            Vector2 impulse = offset / distance * speed + new Vector2(0f, lift);

            if (massIndependent)
            {
                float mass = body.mass;
                if (mass <= 0f)
                    return StateTreeStatus.Failure;   // Static/Kinematic: infinite mass
                impulse *= mass;
            }

            // wake: true — a sleeping victim must react, and a body asleep on the ground is
            // exactly the case knockback exists for.
            body.ApplyLinearImpulseToCenter(impulse, true);
            return StateTreeStatus.Success;
        }
    }
}
