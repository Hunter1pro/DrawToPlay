using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>How long an effect part lives — the HT vocabulary kept verbatim: Instant is a
    /// one-shot delta, Duration ticks and expires, Infinite lasts until the ability domain
    /// removes it.</summary>
    public enum AbilityEffectDuration
    {
        Instant = 0,
        Duration = 1,
        Infinite = 2
    }

    /// <summary>What a re-applied Duration effect does to the copy already running — the four
    /// HT stacking modes, order serialized, append only.</summary>
    public enum AbilityStacking
    {
        Replace = 0,
        RefreshDuration = 1,
        AddStacksRefreshDuration = 2,
        AddStacksKeepDuration = 3
    }

    /// <summary>
    /// ONE NODE of an ability's authored payload — the HT one-class model on purpose: a single
    /// serializable row with a KIND discriminator instead of a class per kind, because the
    /// Unity HT port paid a twin-class tax (every effect and cue needing a runtime class AND a
    /// serialized node class) and the Godot side collapsed it back to one. Which fields mean
    /// anything depends on <see cref="kind"/>; what may nest under what is NOT this class's
    /// business — the service's <see cref="ServiceDef.nestingRules"/> declare it, and the
    /// editor refuses the rest at author time.
    ///
    /// Recursive on purpose (an effect holds its cues); Unity serializes self-nested lists to
    /// a fixed depth far past the two levels the ability rules allow.
    /// </summary>
    [Serializable]
    public sealed class AbilityPartDef
    {
        /// <summary>The ability service's part kinds. Constants, not an enum: a SERVICE
        /// declares its kinds in its nesting rules, and a different service is free to declare
        /// different ones — an enum here would make the ability vocabulary the only one.</summary>
        public const string EffectKind = "effect";
        public const string CueKind = "cue";

        [Tooltip("What this part IS — validated against the service's nesting rules.")]
        public string kind = EffectKind;

        [Tooltip("Diagnostic name; for a Duration effect also the identity re-applications "
            + "stack under.")]
        public string name = "";

        // ---- effect ------------------------------------------------------------------------

        [Tooltip("Effect: which attribute the magnitude lands on. 'health' reaches the "
            + "owner's HealthComponent; other names are the game's business via "
            + "AbilityHost.attributeApplied.")]
        public string attribute = "health";

        [Tooltip("Effect: the signed delta. Negative is damage, positive is healing — the "
            + "HT convention, kept so no effect needs a 'kind of delta' field.")]
        public float magnitude;

        public AbilityEffectDuration duration = AbilityEffectDuration.Instant;

        [Tooltip("Duration effect: how long it lives, in seconds.")]
        public float seconds = 3f;

        [Tooltip("Duration effect: seconds between periodic applications of the magnitude. "
            + "Zero means the magnitude lands once, on application, and the effect only "
            + "carries its tags for the rest of its life.")]
        public float tickInterval;

        [Tooltip("Duration effect: how many copies may stack.")]
        public int maxStacks = 1;

        public AbilityStacking stacking = AbilityStacking.AddStacksRefreshDuration;

        [Tooltip("Duration effect: tags held on the owner while it is active — the "
            + "'poisoned', 'burning' facts conditions gate on.")]
        public List<string> grantedTags = new List<string>();

        // ---- cue ---------------------------------------------------------------------------

        [Tooltip("Cue: the name fired through AbilityHost.cueFired when the parent effect "
            + "applies. A cue OBSERVES — it never mutates combat state; that is an effect's "
            + "job (the HT rule).")]
        public string cueName = "";

        // ---- nesting -----------------------------------------------------------------------

        public List<AbilityPartDef> children = new List<AbilityPartDef>();
    }
}
