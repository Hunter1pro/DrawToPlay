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
        public string targetTag = "";

        [Tooltip("MoveTo: arrive distance when the zone itself does not say (a zone's own "
            + "radius wins).")]
        public float radius = 1.5f;

        [Tooltip("The chain: completing this activates that — a quest line as wires "
            + "between rows, the nextOnFinish pattern.")]
        public StateTreeEntryRef<ObjectiveDef> nextOnComplete = new StateTreeEntryRef<ObjectiveDef>();

        [Tooltip("The arrow's glyph for this row — empty keeps the default pointer. The "
            + "HT per-objective icon override, as authored text.")]
        public string arrowGlyph = "";

        [Tooltip("The accent this row wears — the objective line and the arrow tint.")]
        public Color accentColor = new Color(0.95f, 0.92f, 0.75f);

        [Tooltip("The ZONE whose stack this row belongs to — a world tag carried by a "
            + "placed zone volume. Rows sharing it form that zone's stack (entered at the "
            + "row no other row in the zone chains TO); the service activates the NEAREST "
            + "zone that still has work, so walking changes what is asked (the HT "
            + "distance-zone switch). Empty = the linear line, active when no zone "
            + "competes.")]
        public string zone = "";

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
