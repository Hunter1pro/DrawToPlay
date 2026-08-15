using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>How long an effect lives — the HT vocabulary kept verbatim: Instant is a
    /// one-shot delta, Duration ticks and expires, Infinite lasts until removed.</summary>
    public enum AbilityEffectDuration
    {
        Instant = 0,
        Duration = 1,
        Infinite = 2
    }

    /// <summary>What a re-applied Duration effect does to the copy already running — the four
    /// HT stacking modes. Order serialized, append only.</summary>
    public enum AbilityStacking
    {
        Replace = 0,
        RefreshDuration = 1,
        AddStacksRefreshDuration = 2,
        AddStacksKeepDuration = 3
    }

    /// <summary>
    /// ONE EFFECT, AS A REGISTRY ROW (M23, reworked on review) — "apply this delta / status to
    /// a target", picked wherever it is used instead of described in place. The first cut
    /// carried effects as string-bag parts nested inside the ability row; that was the wrong
    /// pattern in this toolset twice over — wired by loose strings where everything else is a
    /// picked reference, and unable to say WHO it lands on. A row fixes both: an
    /// <c>ApplyEffectTask</c> in the ability's tree picks the row and names the target
    /// (self, or whoever the swing struck), and the row's cue is a picked
    /// <see cref="CueDef"/> reference — this registry lists the cue registry in dependsOn,
    /// the same provenance chain every other cross-registry reference uses.
    /// </summary>
    [Serializable]
    public sealed class EffectDef : StateTreeRegistryEntry
    {
        [Tooltip("Which attribute the magnitude lands on. 'health' reaches the target's "
            + "HealthComponent natively; other names are the game's business via "
            + "AbilityHost.attributeApplied.")]
        public string attribute = "health";

        [Tooltip("The signed delta. Negative is damage, positive is healing — the HT "
            + "convention, so no effect needs a 'kind of delta' field.")]
        public float magnitude;

        public AbilityEffectDuration duration = AbilityEffectDuration.Instant;

        [Tooltip("Duration: how long it lives, in seconds.")]
        public float seconds = 3f;

        [Tooltip("Duration: seconds between periodic applications of the magnitude. Zero "
            + "means the magnitude lands once, on application, and the effect only carries "
            + "its tags for the rest of its life.")]
        public float tickInterval;

        [Tooltip("Duration: how many copies may stack.")]
        public int maxStacks = 1;

        public AbilityStacking stacking = AbilityStacking.AddStacksRefreshDuration;

        [Tooltip("Duration: tags held on the target while it is active — the 'poisoned', "
            + "'burning' facts conditions gate on (AbilityHost.HasTag).")]
        public List<string> grantedTags = new List<string>();

        /// <summary>The cue shown when this effect APPLIES — a picked row of the cue registry
        /// this registry depends on, never a typed name. Empty = a silent effect.</summary>
        public StateTreeEntryRef<CueDef> cue = new StateTreeEntryRef<CueDef>();
    }
}
