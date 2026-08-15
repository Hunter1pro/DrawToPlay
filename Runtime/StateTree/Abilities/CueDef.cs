using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE CUE, AS A REGISTRY ROW — the particles, not a string that hopes to mean them. A cue
    /// OBSERVES: when an effect applies, its picked cue's <see cref="prefab"/> is spawned at
    /// the target and destroyed after <see cref="secondsAlive"/>; nothing about it may mutate
    /// combat state (the HT rule — that is an effect's job). Listener-style cues subscribe to
    /// <see cref="AbilityHost.cueFired"/> instead and need no row at all.
    /// </summary>
    [Serializable]
    public sealed class CueDef : StateTreeRegistryEntry
    {
        [Tooltip("What appears — particles, a flash, a decal. Spawned at the target's "
            + "position when the owning effect applies.")]
        public GameObject prefab;

        [Tooltip("Seconds before the spawned instance is destroyed. Zero or less falls back "
            + "to two seconds — a cue that never leaves is a leak wearing a costume.")]
        public float secondsAlive = 2f;

        [Tooltip("On: the instance parents to the target and rides along. Off: it stays "
            + "where the hit happened.")]
        public bool attachToTarget;
    }
}
