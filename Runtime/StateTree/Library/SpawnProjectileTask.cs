using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Fires <see cref="WeaponDefAsset.projectile"/> at the blackboard target — brief §7.2's
    /// "SpawnProjectile (socket + weapon def)", and what makes the archer preset an archer
    /// rather than a slow zombie.
    ///
    /// THE VELOCITY MUST BE SET BEFORE THE BODY EXISTS. <see cref="EntityBody"/> creates its
    /// PhysicsBody in OnEnable from the serialized <see cref="EntityBody.bodyDefinition"/>, at
    /// the Transform's pose — and the bodies skill is explicit that a body should be created
    /// at its final state rather than corrected afterwards. A plain
    /// <c>Instantiate(activePrefab)</c> runs OnEnable during the call, so the arrow would be
    /// born at rest and have to be shoved. The fix is the standard Unity idiom, chosen after
    /// weighing it against mutating the prefab asset's active flag (never — that dirties the
    /// asset) and against post-hoc <c>body.linearVelocity</c> writes (works, but bypasses the
    /// definition so a pooled or re-enabled projectile would resurrect at rest):
    /// <list type="number">
    /// <item>create a DEACTIVATED holder GameObject;</item>
    /// <item>instantiate the prefab as its child — inactive in hierarchy, so no Awake/OnEnable
    /// runs and no body is created;</item>
    /// <item>write the pose and <c>bodyDefinition.linearVelocity</c>;</item>
    /// <item>reparent to the scene root with worldPositionStays, which activates the instance
    /// and lets EntityBody build the body already moving;</item>
    /// <item>destroy the holder.</item>
    /// </list>
    /// A projectile prefab with no EntityBody is spawned all the same (steps 3–5 minus the
    /// definition write), so a simple Transform-driven projectile still works.
    ///
    /// LIFETIME comes from the weapon: <c>projectileRange / projectileSpeed</c> seconds, the
    /// port of Godot's "expires after N pixels of travel". 0 range = no auto-destroy, and the
    /// projectile prefab owns its own cleanup.
    ///
    /// Result semantics match <see cref="AttackTask"/>: no target or out of
    /// <see cref="range"/> = Failure (leave the firing state), cooling down = Running (hold
    /// it), one projectile per entry = Success. No weapon or no projectile prefab = Failure —
    /// a shooter with nothing to shoot is a configuration error, and failing loudly through
    /// the tree beats silently doing nothing forever.
    ///
    /// Cancelled-safety: the spawn is atomic within a single OnTick — either the projectile
    /// exists as an independent scene object or it was never created. Nothing is retained, so
    /// an interrupt has nothing to tear down, and the holder is destroyed in the same call
    /// that creates it rather than being tracked across ticks.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Spawn Projectile",
        fileName = "SpawnProjectile")]
    [StateTreeCategory("Tasks/Combat", "Spawn the weapon projectile toward the target")]
    public sealed class SpawnProjectileTask : StateTreeTaskAsset
    {
        /// <summary>Supplies the prefab, the speed and the range.</summary>
        public WeaponDefAsset weapon;

        /// <summary>Muzzle offset in the owner's local axes (world units); X is mirrored by
        /// the owner's facing. The "socket" of brief §7.2, without a socket system.</summary>
        public Vector2 muzzleOffset = new Vector2(0.5f, 0f);

        /// <summary>Maximum firing distance, world units. 0 = unlimited.</summary>
        public float range;

        /// <summary>Read <see cref="StateTreeLibraryUtil.AttackRangeKey"/> instead of
        /// <see cref="range"/>.</summary>
        public bool useBlackboardRange;

        /// <summary>Minimum firing distance, world units — the archer's "too close to shoot"
        /// bound. 0 = none.</summary>
        public float minRange;

        /// <summary>Seconds between shots; <see cref="WeaponDefAsset.cooldown"/> is used
        /// instead when <see cref="useWeaponCooldown"/> is set.</summary>
        public float cooldown = 1f;

        public bool useWeaponCooldown = true;

        /// <summary>Optional blackboard key mirroring the next-ready time for a
        /// <see cref="CooldownReadyCondition"/> on a transition.</summary>
        public string cooldownKey = "";

        /// <summary>Face the target before firing.</summary>
        public bool faceTarget = true;

        private float m_NextShotTime;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || weapon == null || weapon.projectile == null)
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return StateTreeStatus.Failure;

            Vector2 offset = StateTreeLibraryUtil.PlanarOffset(owner, target);
            float distance = offset.magnitude;
            float maxRange = StateTreeLibraryUtil.ResolveFloat(context,
                StateTreeLibraryUtil.AttackRangeKey, range, useBlackboardRange);
            if (maxRange > 0f && distance > maxRange)
                return StateTreeStatus.Failure;
            if (minRange > 0f && distance < minRange)
                return StateTreeStatus.Failure;

            Transform ownerTransform = owner.transform;
            if (faceTarget)
                FaceTargetTask.ApplyFacing(ownerTransform, offset.x);

            if (Time.time < m_NextShotTime)
                return StateTreeStatus.Running;

            // A target sitting exactly on the muzzle gives no aim direction; hold rather than
            // fire a projectile with zero velocity that would sit on the floor forever.
            if (distance <= 0f)
                return StateTreeStatus.Running;

            float facing = ownerTransform.localScale.x < 0f ? -1f : 1f;
            Vector3 ownerPosition = ownerTransform.position;
            Vector3 spawnPosition = new Vector3(
                ownerPosition.x + muzzleOffset.x * facing,
                ownerPosition.y + muzzleOffset.y,
                ownerPosition.z);

            Vector2 velocity = offset / distance * weapon.projectileSpeed;
            Spawn(spawnPosition, velocity);

            ArmCooldown(context);
            return StateTreeStatus.Success;
        }

        /// <summary>The deactivated-holder spawn described in the class comment.</summary>
        private void Spawn(Vector3 position, Vector2 velocity)
        {
            GameObject holder = new GameObject("StateTreeProjectileSpawn");
            holder.SetActive(false);

            GameObject instance = Object.Instantiate(weapon.projectile, holder.transform);
            Transform instanceTransform = instance.transform;
            instanceTransform.position = position;

            // Aim the visual along the shot as well as the body; a drawn arrow authored
            // pointing +X then reads correctly at any angle.
            instanceTransform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);

            if (instance.TryGetComponent(out EntityBody entityBody))
            {
                // Struct field on a component field: copy, edit, write back — the pattern the
                // umbrella skill prescribes for PhysicsCore2D definitions.
                var definition = entityBody.bodyDefinition;
                definition.linearVelocity = velocity;
                entityBody.bodyDefinition = definition;
            }

            // Reparenting to the scene root activates the instance (worldPositionStays keeps
            // the pose written above), which is the moment EntityBody.OnEnable creates the
            // body — already carrying the velocity.
            instanceTransform.SetParent(null, true);

            float lifetime = weapon.projectileSpeed > 0f
                ? weapon.projectileRange / weapon.projectileSpeed
                : 0f;

            if (Application.isPlaying)
            {
                Object.Destroy(holder);
                if (lifetime > 0f)
                    Object.Destroy(instance, lifetime);
            }
            else
            {
                // Edit-mode evaluation (an inspector-driven tick) must not leak the holder.
                Object.DestroyImmediate(holder);
            }
        }

        private void ArmCooldown(StateTreeContext context)
        {
            float seconds = useWeaponCooldown ? weapon.cooldown : cooldown;
            m_NextShotTime = Time.time + Mathf.Max(seconds, 0f);
            if (!string.IsNullOrEmpty(cooldownKey))
                StateTreeLibraryUtil.SetFloat(context, cooldownKey, m_NextShotTime);
        }
    }
}
