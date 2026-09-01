using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What an objective ASKS FOR — the four verbs the demo speaks. Order
    /// serialized, append only.</summary>
    public enum ObjectiveKind
    {
        /// <summary>Reach a ZONE — a placed <see cref="ObjectiveZoneBehaviour"/> found by
        /// <see cref="ObjectiveDef.targetTag"/>. Zero, one or many zones may carry the tag;
        /// the NEAREST one is the target and arriving at ANY completes. Never nested.</summary>
        MoveTo = 0,

        /// <summary>Put down <see cref="ObjectiveDef.count"/> enemies. The game reports
        /// kills (<see cref="ObjectiveService.ReportKill"/>); <see cref="ObjectiveDef.targetTag"/>
        /// filters which count, empty counts every reported kill.</summary>
        EnemyKill = 1,

        /// <summary>Finish a conversation — the picked dialog row
        /// (<see cref="ObjectiveDef.target"/>), reported by the dialog runner.</summary>
        Dialog = 2,

        /// <summary>Carry <see cref="ObjectiveDef.count"/> of the picked item row —
        /// reported from the inventory's own counts, so a dropped item un-progresses.</summary>
        Pickup = 3
    }

    /// <summary>
    /// ONE OBJECTIVE, AS A REGISTRY ROW (M24, brief §10.4) — the HT pattern with its cost
    /// removed: HT's Unity side paid 132 code registrations and a class per objective kind;
    /// here an objective is a ROW picking its subject through dependsOn (the dialog row, the
    /// item row), and the four kinds are watchers on ONE service. The chain is a wire —
    /// <see cref="nextOnComplete"/>, the ability continuation's shape — so a quest line is
    /// rows pointing at rows, visible in the wire map like everything else.
    /// </summary>
    [Serializable]
    public sealed class ObjectiveDef : StateTreeRegistryEntry
    {
        public ObjectiveKind kind = ObjectiveKind.MoveTo;

        [Tooltip("What the player reads — the HUD's current-objective line.")]
        public string displayName = "";

        [Tooltip("The SUBJECT row, picked through this registry's dependsOn: the dialog to "
            + "finish, the item to carry. MoveTo and EnemyKill use the tag instead.")]
        public StateTreeEntryRef<StateTreeRegistryEntry> target =
            new StateTreeEntryRef<StateTreeRegistryEntry>();

        [Tooltip("EnemyKill: how many. Pickup: how many carried at once.")]
        public int count = 1;

        [Tooltip("A WORLD TAG: MoveTo's zone identity, EnemyKill's victim filter (empty = "
            + "any reported kill), and for every kind the thing the offscreen arrow points "
            + "at — the nearest citizen carrying it.")]
        [WorldTag]
        public string targetTag = "";

        [Tooltip("MoveTo: arrive distance when the zone itself does not say (a zone's own "
            + "radius wins).")]
        public float radius = 1.5f;

        [Tooltip("A FACT that completes this row: a key on the service's scope board, "
            + "compared as text. Empty = no fact watcher. The flow writes facts; the ledger "
            + "hears them - no completing task in any tree.")]
        public string factKey = "";

        [Tooltip("The value that counts as done - a stableId, a name. Empty means 'the key "
            + "exists'.")]
        public string factValue = "";

        [Tooltip("A GATE: this row applies only while the scope board agrees. Key unset = "
            + "the row is PENDING (current but inert - the ledger waits for the answer); set "
            + "and equal = the row runs; set and different = the row is passed over "
            + "silently. How a choice forks a linear stack.")]
        public string gateKey = "";

        [Tooltip("The value that lets the row run.")]
        public string gateValue = "";

        [Tooltip("A declared request written on the ROOT board when this row completes - "
            + "'video.play' with a film row's name plays a completion film. Empty = nothing "
            + "called.")]
        public string completeRequestKey = "";

        [Tooltip("The request's value.")]
        public string completeRequestValue = "";

        [Tooltip("The LINEAR line's chain: completing this activates that. Ignored while "
            + "a zone stack asks the row — there, the stack's ORDER is the chain.")]
        public StateTreeEntryRef<ObjectiveDef> nextOnComplete = new StateTreeEntryRef<ObjectiveDef>();

        [Tooltip("The arrow's glyph for this row — empty keeps the default pointer. The "
            + "HT per-objective icon override, as authored text.")]
        public string arrowGlyph = "";

        [Tooltip("The accent this row wears — the objective line, the arrow tint, and the "
            + "world marker's colour.")]
        public Color accentColor = new Color(0.95f, 0.92f, 0.75f);

        [Tooltip("Also stand a marker on the target IN THE WORLD, not only an arrow on the "
            + "screen edge. Off by default: a marker is a promise that the place is reachable "
            + "and worth looking at, and rows that ask for something abstract should not make "
            + "it.")]
        public bool worldMarker;

        // Zone membership lives on the ZONE now (ZoneDef.stack, the container row):
        // a row joins a zone by being picked into its ordered stack — one authored
        // source, no string agreeing with a list by spelling.

        public override string Describe()
        {
            var what = kind switch
            {
                ObjectiveKind.MoveTo => "reach '" + targetTag + "'",
                ObjectiveKind.EnemyKill => "kill " + Mathf.Max(1, count)
                    + (string.IsNullOrEmpty(targetTag) ? "" : " of '" + targetTag + "'"),
                ObjectiveKind.Dialog => "talk '" + target.entryName + "'",
                _ => "carry " + Mathf.Max(1, count) + " '" + target.entryName + "'"
            };
            return what + (string.IsNullOrEmpty(nextOnComplete.entryName)
                ? " · ends the chain"
                : " → '" + nextOnComplete.entryName + "'");
        }
    }
}
