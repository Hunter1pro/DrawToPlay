using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// LOOSE ONE (M28) — the beat that puts a ball in the air and holds the state open until it
    /// lands.
    ///
    /// It aims at a BODY named on the blackboard, leads nothing and guarantees nothing: a shot
    /// is a shot, and a target that moves may be missed, which is the entire point of a weapon
    /// that has to be aimed by turning the ship. What it hit lands under
    /// <see cref="struckKey"/>, so the effect the ability applies is the ordinary
    /// <c>ApplyEffectTask</c> reading the ordinary key — the projectile carries no damage, in
    /// the same division HT keeps between its ProjectileComponent and its CannonAbility.
    ///
    /// Missing is a SUCCESS, not a failure: the ball flew, nothing was there, and the ability
    /// should finish and go on cooldown exactly as if it had hit. A tree that wants to know
    /// branches on the struck key.
    /// </summary>
    [StateTreeCategory("Tasks/Combat", "Fire a projectile at a body on the blackboard")]
    public sealed class FireProjectileTask : StateTreeTaskAsset
    {
        [Tooltip("Which ball — a row from the projectile catalog.")]
        public StateTreeEntryRef<ProjectileDef> projectile = new StateTreeEntryRef<ProjectileDef>();

        /// <summary>The catalog itself, bound at StartTree from the tree's listed registries —
        /// no asset slot on the task to mis-wire.</summary>
        private StateTreeRegistryRef<ProjectileDef> m_Projectiles =
            new StateTreeRegistryRef<ProjectileDef>();

        [Tooltip("Who it is aimed at: a blackboard key holding the body.")]
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField targetKey = new StateTreeKeyField("target");

        [Tooltip("Where what it hit is published — the key the effect beat then reads.")]
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField struckKey = new StateTreeKeyField("struck");

        [Tooltip("Muzzle height above the owner's feet, so a gun does not fire from the "
            + "waterline.")]
        public float muzzleHeight = 1.1f;

        [Tooltip("Aim this far up the target's body rather than at its feet.")]
        public float aimHeight = 0.9f;

        [System.NonSerialized] private ProjectileFlight m_Flight;

        public override void OnEnter(StateTreeContext context)
        {
            m_Flight = null;
            if (context == null || context.owner == null)
                return;

            ProjectileDef row = Row(context);
            if (row == null)
            {
                Debug.LogWarning("FireProjectileTask: no projectile row named '"
                    + projectile.entryName + "' — nothing was fired.", context.owner);
                return;
            }

            GameObject target = Body(context, targetKey);
            Vector3 muzzle = context.owner.transform.position + Vector3.up * muzzleHeight;
            Vector3 aim = target != null
                ? target.transform.position + Vector3.up * aimHeight
                : muzzle + context.owner.transform.forward * 10f;

            // BALLISTICS, HONESTLY: aim at where the target IS, with the row's speed, and let
            // gravity do what it does. A shot that must not miss is a different feature (and a
            // worse one) than a shot that can.
            Vector3 direction = aim - muzzle;
            Vector3 velocity = direction.sqrMagnitude > 0.0001f
                ? direction.normalized * row.speed
                : context.owner.transform.forward * row.speed;

            // A LOB, when the row arcs: without a little lift, an arcing ball aimed straight at
            // a body always lands short of it, and every player reads that as a broken gun.
            if (row.gravity < 0f)
            {
                float flight = direction.magnitude / Mathf.Max(0.01f, row.speed);
                velocity += Vector3.up * (-row.gravity * 0.5f * flight);
            }

            m_Flight = new ProjectileFlight(row, muzzle, velocity, context.owner.transform);
            if (!string.IsNullOrEmpty(struckKey))
                context.blackboard.Remove((string)struckKey);
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Flight == null)
                return StateTreeStatus.Success;
            if (m_Flight.Tick(deltaTime))
                return StateTreeStatus.Running;

            if (!string.IsNullOrEmpty(struckKey) && m_Flight.struck != null)
                context.blackboard[(string)struckKey] = m_Flight.struck;
            m_Flight = null;
            return StateTreeStatus.Success;
        }

        /// <summary>A cancelled ability takes its ball with it — a level torn down mid-flight
        /// must not leave a mesh sailing over an empty scene.</summary>
        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            m_Flight?.Cancel();
            m_Flight = null;
        }

        /// <summary>The row, from the bound catalog first and the host chain's own trees
        /// second — the same two roads LoadLevelTask walks, for the same reason: an ability
        /// hosted from a graph has no registry injected into it.</summary>
        private ProjectileDef Row(StateTreeContext context)
        {
            string wanted = projectile.entryName;
            if (string.IsNullOrEmpty(wanted))
                return null;
            if (m_Projectiles.TryGet(wanted, out ProjectileDef bound))
                return bound;

            StateTreeContextHost host = StateTreeContextHost.ResolveNearest(
                context != null ? context.owner : null);
            int guard = 0;
            while (host != null && ++guard < 32)
            {
                var registries = host.tree != null ? host.tree.registries : null;
                for (int i = 0; registries != null && i < registries.Count; i++)
                {
                    if (registries[i] != null
                        && registries[i].FindByName(wanted) is ProjectileDef found)
                        return found;
                }
                host = host.ParentHost;
            }
            return null;
        }

        private static GameObject Body(StateTreeContext context, StateTreeKeyField key)
        {
            string name = key;
            if (context == null || string.IsNullOrEmpty(name)
                || !context.blackboard.TryGetValue(name, out object held))
                return null;
            return held as GameObject ?? (held as Component)?.gameObject;
        }
    }
}
