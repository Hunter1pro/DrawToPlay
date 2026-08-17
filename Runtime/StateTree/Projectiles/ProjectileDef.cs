using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A THING IN FLIGHT, as a row (M28) — everything about a cannonball except what it does to
    /// whoever it lands on, which belongs to the effect the ability applies.
    ///
    /// The numbers are the ones HT's projectile actually reads: a speed, a gravity, a drag, the
    /// radius it sweeps, what it may hit, and two hard lifetimes. The model is a PREFAB on the
    /// row rather than an address, because this project has no addressables and a row that
    /// names its own prefab is one less lookup that can fail at the worst moment.
    /// </summary>
    [Serializable]
    public sealed class ProjectileDef : StateTreeRegistryEntry
    {
        [Tooltip("What to call it on screen or in a log.")]
        public string displayName = "";

        [Tooltip("The model that flies. Its colliders and rigidbodies are switched off on the "
            + "clone — the sweep below is the truth, the model is scenery.")]
        public GameObject prefab;

        [Tooltip("Metres per second at the muzzle.")]
        public float speed = 22f;

        [Tooltip("Downward pull. Zero is a flat shot; negative is the arc a cannon has.")]
        public float gravity = -12f;

        [Tooltip("Air, as a fraction of speed lost per second. Zero is a vacuum.")]
        public float drag;

        [Tooltip("The ball's own radius: what it sweeps with, and what the sub-steps are "
            + "sized against so a fast shot cannot pass through a hull.")]
        public float radius = 0.18f;

        [Tooltip("How far it may travel before it is simply gone.")]
        public float maxDistance = 40f;

        [Tooltip("How long it may live, whatever it is doing.")]
        public float maxSeconds = 4f;

        [Tooltip("Most sub-steps in one frame — the cost ceiling of not tunnelling.")]
        [Range(1, 16)] public int maxSubSteps = 8;

        [Tooltip("The model turns to face where it is going.")]
        public bool faceVelocity = true;

        [Tooltip("Which layers stop it. Nothing selected means everything, which is the "
            + "honest default for a demo and the wrong one for a game.")]
        public LayerMask hitMask = ~0;

        public override string Describe()
        {
            return (string.IsNullOrEmpty(displayName) ? name : displayName)
                + " — " + speed.ToString("0.#") + " m/s, "
                + (Mathf.Approximately(gravity, 0f) ? "flat" : "arcing")
                + ", " + maxSeconds.ToString("0.#") + "s";
        }
    }
}
