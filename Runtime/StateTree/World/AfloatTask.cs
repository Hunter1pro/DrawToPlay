using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    public sealed class AfloatTask : StateTreeTaskAsset
    {
        // HOLDING A MODE IS NOT HOLDING A STATE OPEN — set blocking = false wherever this is
        // authored. It runs for as long as its state does and never finishes, which is what a
        // standing swap means; but blocking is a COMPLETION gate, and a state whose gate never
        // opens can never take an on-completion transition. The firing state proved it: one
        // cannon shot and the ship was stuck in it for good, unable to steer, because the edge
        // back to sailing waited for a completion this task was quietly preventing. (A
        // constructor cannot fix it for you: Unity restores the serialized default afterwards,
        // and the assets a builder already wrote keep whatever they were written with.)

        [Tooltip("Speed while afloat — a boat is not a fast pair of legs. Applied as the "
            + "actor's moveSpeed attribute and reverted on exit.")]
        public float speed = 7f;

        [Tooltip("The visual to enable while afloat — a child of the actor, by name. "
            + "Empty = no visual change.")]
        public string visualChild = "Boat";

        [Tooltip("The visual to HIDE while afloat — the body the ship replaces. Empty = the "
            + "actor stays visible and rides in its boat.")]
        public string hiddenChild = "";

        [Tooltip("The tag granted while afloat, which land abilities are blocked by.")]
        public string tag = BoardingKeys.AboardTag;

        [System.NonSerialized] private AttributeComponent.ModifierHandle m_Speed;
        [System.NonSerialized] private GameObject m_Visual;
        [System.NonSerialized] private GameObject m_Hidden;
        [System.NonSerialized] private AbilityHost m_Host;

        public override void OnEnter(StateTreeContext context)
        {
            if (context == null || context.owner == null)
                return;

            var attributes = context.owner.GetComponent<AttributeComponent>();
            // The land speed stays exactly where it was: this is a MODIFIER, so disembarking
            // is a revert rather than a remembered number written back.
            //
            // NOTHING IS INVENTED when the attribute is absent. Ensuring it at zero (the first
            // version) meant the revert restored zero, and the player walked out of the water
            // unable to walk. Whoever owns the number seeds it — the mover does, on the first
            // step any actor takes — so absent here means an actor that has never moved and
            // has no land speed to swap.
            if (attributes != null && attributes.Has(AttributeNames.MoveSpeed))
            {
                float ashore = attributes.Effective(AttributeNames.MoveSpeed);
                m_Speed = attributes.AddModifier(AttributeNames.MoveSpeed,
                    speed - ashore, 1f);
            }

            m_Host = context.owner.GetComponent<AbilityHost>();
            if (m_Host != null && !string.IsNullOrEmpty(tag))
                m_Host.AddTag(tag);

            m_Visual = FindChild(context.owner.transform, visualChild);
            if (m_Visual != null)
                m_Visual.SetActive(true);

            // THE BODY GOES AWAY, and that is what makes this a mode rather than a prop: while
            // you are afloat you ARE the ship — HT's CharacterView.ActiveShip, which swaps the
            // visual rather than parenting one to the other. The walker comes back on exit.
            m_Hidden = FindChild(context.owner.transform, hiddenChild);
            if (m_Hidden != null)
                m_Hidden.SetActive(false);

            // Sit ON the water rather than in it.
            WaterVolumeBehaviour water = WaterVolumeBehaviour.At(context.owner,
                context.owner.transform.position);
            if (water != null)
            {
                Vector3 position = context.owner.transform.position;
                position.y = water.SurfaceY;
                Teleport(context.owner, position);
            }
        }

        /// <summary>
        /// HOLDS THE SURFACE, every tick — afloat for as long as the state is.
        ///
        /// The entry snap alone is not floating: the mover applies gravity each frame, so
        /// a boat that was only placed on the water sank to the seabed within a second and
        /// sailed along the bottom with its hull showing. Corrected AFTER the mover in the
        /// state's task list, because the last write of a frame is the one you see.
        /// </summary>
        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context != null && context.owner != null)
            {
                WaterVolumeBehaviour water = WaterVolumeBehaviour.At(context.owner,
                    context.owner.transform.position);
                if (water != null)
                {
                    Vector3 position = context.owner.transform.position;
                    if (!Mathf.Approximately(position.y, water.SurfaceY))
                    {
                        position.y = water.SurfaceY;
                        Teleport(context.owner, position);
                    }
                }
            }
            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (context == null || context.owner == null)
                return;

            var attributes = context.owner.GetComponent<AttributeComponent>();
            if (attributes != null && m_Speed != null)
                attributes.RemoveModifier(m_Speed);
            m_Speed = null;

            if (m_Host != null && !string.IsNullOrEmpty(tag))
                m_Host.RemoveTag(tag);
            m_Host = null;

            if (m_Visual != null)
                m_Visual.SetActive(false);
            m_Visual = null;

            if (m_Hidden != null)
                m_Hidden.SetActive(true);
            m_Hidden = null;

            // ONLY WHEN THE MODE IS OVER, and only if still wet.
            //
            // The mode is the KEY, not this state: an aboard branch with more than one state
            // in it (sailing, firing) hands over by exiting one and entering the next, and a
            // rescue that fired on any exit would haul the player ashore every time they used
            // the cannon. Walking out of the water is its own disembark and needs no rescue
            // either — the actor is already where it wants to be. What is left is the exit
            // that strands: the mode ending while the body is still on the waves.
            bool stillAboard = context.blackboard.ContainsKey(BoardingKeys.Aboard);
            if (!stillAboard
                && WaterVolumeBehaviour.At(context.owner, context.owner.transform.position) != null
                && context.blackboard.TryGetValue(BoardingKeys.LastGround, out object held)
                && held is Vector3 ground)
                Teleport(context.owner, ground);
        }

        /// <summary>A CharacterController fights transform writes; it has to be off for the
        /// instant of the move, exactly as the demo's other teleports do it.</summary>
        private static void Teleport(GameObject actor, Vector3 position)
        {
            var controller = actor.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            actor.transform.position = position;
            if (controller != null)
                controller.enabled = true;
        }

        private static GameObject FindChild(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name)
                    return all[i].gameObject;
            }
            return null;
        }
    }
}
