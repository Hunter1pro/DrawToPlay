using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One row of a spawn / loot table — port of hunter's <c>spawn_entry.gd</c>
    /// (draw-tool-port-brief.md §6.2 "loot table (spawn_entry port)", §6.5 "drop table"). A biome
    /// table is a list of these; a director spends a difficulty budget on <see cref="cost"/> and
    /// places each pick on a spawn marker whose tag matches <see cref="markerTag"/>.
    ///
    /// The Godot fields are ported verbatim (prefab, cost, min difficulty, marker tag, elite). The
    /// weight/count fields below are NOT in spawn_entry.gd: the brief's M5 line asks this same type
    /// to double as a loot-table entry ("scene/prefab ref, weight, count range"), whose Godot
    /// precedent is enemy_base.gd's <c>heart_drop_chance</c>/<c>weapon_drop_chance</c> rolls rather
    /// than the spawn table. They are inert for the budget path — a director that only spends cost
    /// ignores them — so one asset type serves both tables instead of two near-identical ones.
    ///
    /// No unit conversion applies: cost, difficulty, weight and counts are all unitless.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Spawn Entry", fileName = "SpawnEntry")]
    public sealed class SpawnEntryAsset : ScriptableObject
    {
        /// <summary>Godot PackedScene → a Unity prefab. What gets instantiated.</summary>
        public GameObject prefab;

        /// <summary>Budget cost. The director spends difficulty * 10 per node, so a cost of 1 is
        /// "cheap trash" and heavier units cost more.</summary>
        public float cost = 1f;

        /// <summary>Heavier units appear only in harder nodes: skipped while the node's difficulty
        /// is below this.</summary>
        public float minDifficulty = 0f;

        /// <summary>Spawns only on markers whose tag matches EXACTLY. Empty = untagged markers
        /// only — tagged spots are reserved for the entries that name them.</summary>
        public string markerTag = "";

        /// <summary>Spawn as an elite: crown, x3 HP, +1 level, poise, guaranteed drops
        /// (enemy_base.gd <c>make_elite</c>).</summary>
        public bool elite;

        [Header("Loot table")]

        /// <summary>Relative pick weight when this table is rolled as a DROP table rather than
        /// spent as a budget. Not part of spawn_entry.gd — see the type summary.</summary>
        public float weight = 1f;

        /// <summary>Inclusive lower bound of the drop count. Not part of spawn_entry.gd.</summary>
        public int countMin = 1;

        /// <summary>Inclusive upper bound of the drop count. Not part of spawn_entry.gd.</summary>
        public int countMax = 1;

        /// <summary>Godot's exact-match marker rule, with null treated as untagged so a caller can
        /// pass a missing tag straight through.</summary>
        public bool MatchesMarker(string tag)
        {
            return markerTag == (tag ?? string.Empty);
        }

        /// <summary>Usable in a node of this difficulty.</summary>
        public bool IsAvailableAt(float difficulty)
        {
            return difficulty >= minDifficulty;
        }

        /// <summary>Inclusive roll over the count range. Bounds are ordered here rather than
        /// validated, so a swapped min/max is a shrug instead of an exception — the same tolerance
        /// terrain_paint.gd's scatter scale range applies to its own min/max pair.</summary>
        public int RollCount()
        {
            int low = Mathf.Min(countMin, countMax);
            int high = Mathf.Max(countMin, countMax);
            return Random.Range(low, high + 1);
        }
    }
}
