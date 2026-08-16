using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ABILITY, AS A REGISTRY ROW (M23) — what the HT projects wrote as a C# subclass per
    /// ability becomes data: identity, the four tag channels, a TREE that does the work
    /// (called with arguments through the same parameter surface every other tree call uses),
    /// the payload parts (effects holding cues, nesting declared by the service's rules), and
    /// the continuation. New ability = new row; a new C# task is the exception, for a
    /// genuinely novel mechanic.
    ///
    /// THE FOUR TAG CHANNELS, verbatim from the Godot HT skill because they are the rules that
    /// made design fast there: <see cref="abilityTags"/> is what this IS; <see cref="blockTags"/>
    /// is what it suppresses while active; <see cref="cancelTags"/> is what it kills on
    /// activation; <see cref="activationTags"/> is what the owner temporarily HAS while it
    /// runs (read back through <c>AbilityHost.HasTag</c>).
    ///
    /// THE CONTINUATION (<see cref="nextOnFinish"/>) is the ability-tree-becomes-FSM
    /// experiment, shipped: when this ability's tree finishes — M22's treeFinished, which is
    /// why that milestone came first — the host activates the named row. A combo, a recovery,
    /// a stagger chain is a wire between two rows, zero code. The Unity HT port listed "No
    /// combos" as a limitation; this field is its fix.
    /// </summary>
    [Serializable]
    public sealed class AbilityDef : StateTreeRegistryEntry
    {
        /// <summary>The root kind ability rows carry in the service's nesting rules.</summary>
        public const string RootKind = "ability";

        [Tooltip("Shown in UI; the row's name stays the runtime key.")]
        public string displayName = "";

        [Tooltip("What this ability IS — the identity other channels match against.")]
        public List<string> abilityTags = new List<string>();

        [Tooltip("While this runs, abilities carrying any of these tags cannot start.")]
        public List<string> blockTags = new List<string>();

        [Tooltip("Starting this cancels any active ability carrying one of these tags.")]
        public List<string> cancelTags = new List<string>();

        [Tooltip("The OWNER holding any of these refuses the activation — a state of the actor, "
            + "not of another ability. 'No chopping while afloat', 'no casting while silenced'.")]
        public List<string> blockedByTags = new List<string>();

        [Tooltip("Held by the owner while this runs — queryable via AbilityHost.HasTag.")]
        public List<string> activationTags = new List<string>();

        [Tooltip("What the ability DOES — a tree, run by the owner's AbilityHost. Null is "
            + "legal: an ability that is only its effects applies them and finishes at once.")]
        [StateTreePick]
        public StateTreeAsset tree;

        /// <summary>This row's arguments for the tree's declared parameters — the same set a
        /// manifest placement or a dialog row uses, so one authored tree serves many
        /// abilities at different numbers.</summary>
        [GraphTaskParameters(nameof(tree))]
        public GraphTaskParameterSet parameters = new GraphTaskParameterSet();

        [Tooltip("Seconds before this ability may start again, counted from when it finishes.")]
        public float cooldownSeconds;

        // The payload lives in the TREE, as tasks — ApplyEffectTask picking effect rows with
        // their targets — not as data nested here. The first cut nested string-bag "parts" on
        // this row and was reviewed out: it wired by loose strings, could not name a target,
        // and duplicated what a tree already is.

        /// <summary>The continuation — the ability activated when this one's tree finishes
        /// (not when it is cancelled: a cancelled call returns nothing). Empty = fall back to
        /// the host's default ability, the idle floor.</summary>
        public StateTreeEntryRef<AbilityDef> nextOnFinish = new StateTreeEntryRef<AbilityDef>();

        /// <summary>
        /// Where an activation's TARGET payload lands on this ability's tree — a key the tree
        /// declares, wired by id like every key use. The caller's mind did the searching (its
        /// perception published a quarry); the activation hands the result over, and the
        /// ability attacks WHO IT WAS GIVEN instead of re-finding somebody on its own.
        /// Empty = the ability targets for itself (the player's push, swung at whatever is
        /// in front).
        /// </summary>
        public StateTreeKeyField targetKey = new StateTreeKeyField();
    }
}
