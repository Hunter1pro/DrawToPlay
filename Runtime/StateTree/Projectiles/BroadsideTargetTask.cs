using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ABEAM, OR NOT AT ALL (M28) — the rule that makes a ship's gun a ship's gun.
    ///
    /// A cannon does not fire where the bow points. This finds the nearest body carrying a tag
    /// whose bearing from the owner lies inside a SIDE arc — port or starboard, both, nothing
    /// ahead and nothing astern — and publishes it, along with which side it lies on. Turning
    /// the ship is therefore aiming, which is the whole feel being asked for: you line a target
    /// up on your beam and let go.
    ///
    /// HT had this rule and walked away from it — its own detector says the four flank hexes
    /// "was too narrow" and now takes anything in range. This is the strict version, kept
    /// strict, with the arc as a number an upgrade can widen.
    ///
    /// UPGRADES REACH IT: range and arc are read from the owner's ATTRIBUTES when it has them
    /// (<see cref="rangeAttribute"/>, <see cref="arcAttribute"/>) and from the authored fields
    /// when it does not — the same "read it live so upgrades are felt immediately" HT wrote its
    /// cannon around. Failure when nothing is abeam is an ANSWER, not an error: it is what the
    /// ability's refusal and the indicator are both built on.
    /// </summary>
    [StateTreeCategory("Tasks/Combat", "Find a target abeam — the broadside rule")]
    public sealed class BroadsideTargetTask : StateTreeTaskAsset
    {
        [Tooltip("What counts as a target.")]
        [StateTreeKey(StateTreeKeyKind.Tag)]
        public StateTreeKeyField tag = new StateTreeKeyField("enemy");

        [Tooltip("Where the target is published.")]
        [StateTreeKey(StateTreeKeyKind.Object)]
        public StateTreeKeyField targetKey = new StateTreeKeyField("target");

        [Tooltip("Where the side is published — 'port' or 'starboard', for a skin to show.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField sideKey = new StateTreeKeyField();

        [Tooltip("How far the guns reach, in metres.")]
        public float range = 9f;

        [Tooltip("Half-width of each beam arc, in degrees from straight abeam. 30 means a "
            + "target counts from 60° to 120° off the bow, on either side.")]
        [Range(5f, 80f)] public float arcHalfAngle = 32f;

        [Tooltip("Optional: an attribute that overrides range when the owner has one — the "
            + "upgrade's door.")]
        public string rangeAttribute = "cannonRange";

        [Tooltip("Optional: an attribute that overrides the arc when the owner has one.")]
        public string arcAttribute = "cannonArc";

        [Tooltip("Publish onto a CONTEXT SCOPE as well as this tree's own board — how one "
            + "watcher answers for everybody. The gun reads it, the indicator reads it, and "
            + "there is exactly one opinion about what is abeam.")]
        public bool publishToScope;

        [Tooltip("Which scope, when publishing.")]
        public StateTreeContextKind scope = StateTreeContextKind.Player;

        [Tooltip("Never finish — the WATCHER shape. A task that answers Success or Failure is "
            + "retired the moment it does, which is right for a gate inside an ability and "
            + "useless for something that must keep looking while a ship sails. On means it "
            + "stays Running and republishes every tick; off means it answers once.")]
        public bool keepWatching;

        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(tag))
                return StateTreeStatus.Failure;

            WorldService world = StateTreeContextHost.FindService<WorldService>(context.owner);
            if (world == null)
                return StateTreeStatus.Failure;

            Transform ship = context.owner.transform;
            float reach = Attribute(context.owner, rangeAttribute, range);
            float half = Mathf.Clamp(Attribute(context.owner, arcAttribute, arcHalfAngle), 1f, 89f);

            m_Buffer.Clear();
            world.CollectByTag(tag, m_Buffer);

            WorldObjectBehaviour best = null;
            float bestDistance = float.MaxValue;
            string bestSide = "";
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                WorldObjectBehaviour candidate = m_Buffer[i];
                if (candidate == null || candidate.gameObject == context.owner)
                    continue;

                Vector3 offset = candidate.transform.position - ship.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > reach || distance < 0.001f)
                    continue;

                // THE ANGLE OFF THE BOW, signed: right of the bow is starboard, left is port,
                // and "abeam" is 90° from it either way. Everything here is flat — a gun does
                // not care that a target is a metre lower.
                Vector3 direction = offset / distance;
                float bearing = Vector3.SignedAngle(ship.forward, direction, Vector3.up);
                float offBeam = Mathf.Abs(Mathf.Abs(bearing) - 90f);
                if (offBeam > half)
                    continue;   // ahead or astern: the guns do not point there

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                    bestSide = bearing >= 0f ? "starboard" : "port";
                }
            }

            Publish(context.blackboard, best, bestSide);
            if (publishToScope)
            {
                StateTreeContextHost host =
                    StateTreeContextHost.Resolve(context.owner, scope);
                if (host != null && host.Context != null)
                    Publish(host.Context.blackboard, best, bestSide);
            }

            if (keepWatching)
                return StateTreeStatus.Running;
            return best != null ? StateTreeStatus.Success : StateTreeStatus.Failure;
        }

        /// <summary>Write the answer, or rub it out. A MISS CLEARS THE KEYS: a stale target is
        /// worse than none, and the indicator hanging over a ship that sailed out of the arc
        /// would be a lie the player acts on.</summary>
        private void Publish(System.Collections.Generic.Dictionary<string, object> board,
            WorldObjectBehaviour best, string side)
        {
            if (board == null)
                return;
            if (!string.IsNullOrEmpty(targetKey))
            {
                if (best != null)
                    board[(string)targetKey] = best.gameObject;
                else
                    board.Remove((string)targetKey);
            }
            if (string.IsNullOrEmpty(sideKey))
                return;
            if (best != null)
                board[(string)sideKey] = side;
            else
                board.Remove((string)sideKey);
        }

        /// <summary>An attribute's value when the actor carries one, else the authored
        /// number — how an upgrade changes a gun without touching a tree.</summary>
        private static float Attribute(GameObject owner, string attributeName, float fallback)
        {
            if (owner == null || string.IsNullOrEmpty(attributeName))
                return fallback;
            var attributes = owner.GetComponent<AttributeComponent>();
            if (attributes == null || !attributes.Has(attributeName))
                return fallback;
            // EFFECTIVE, not current: (base + Σ add) × Π mult is what an upgrade or a fitted
            // gun moves, and reading it here is what makes the change felt on the next shot.
            return attributes.Effective(attributeName);
        }
    }
}
