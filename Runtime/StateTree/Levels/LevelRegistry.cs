using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One level's place in the PROJECT CATALOG (M16): identity — the id typed references
    /// wire to (a portal's target, the session tree's level states, a save file) — and ONE
    /// field, the <see cref="LevelContent"/> asset that IS the level. Everything a level
    /// consists of (scene, entry params, tag vocabulary, object manifest) lives in that file,
    /// so the catalog stays a list of names no matter how big the levels get.
    ///
    /// The properties below read through to the content: callers say <c>level.scenePath</c>
    /// and never care which asset stores it.
    /// </summary>
    [Serializable]
    public sealed class LevelDef : StateTreeRegistryEntry
    {
        /// <summary>THE level — see <see cref="LevelContent"/>. A row without one is an
        /// unfinished catalog entry: it names a level that has no content yet, and the load
        /// that tries it says so.</summary>
        public LevelContent content;

        public string scenePath => content != null ? content.scenePath : "";

        public List<GraphTaskParameter> parameters =>
            content != null ? content.parameters : null;

        public LevelObjectRegistry objects => content != null ? content.objects : null;

        /// <summary>The level's own tag vocabulary, or null — see
        /// <see cref="LevelContent.tags"/>.</summary>
        public WorldTagRegistry tags => content != null ? content.tags : null;

        public string Label => content != null && !string.IsNullOrEmpty(content.displayName)
            ? content.displayName
            : name;

        /// <summary>What lives in this level, derived from its manifest — see
        /// <see cref="LevelContent.CollectTags"/>.</summary>
        public void CollectTags(List<string> into)
        {
            if (content != null)
                content.CollectTags(into);
        }
    }

    /// <summary>One placed object of a level, as a REGISTRY ROW (it lives in the level's
    /// <see cref="LevelObjectRegistry"/>, so it is searchable and groupable like every other
    /// entry): the base carries its id, its own name — the placement's handle, which is what
    /// the spawned view is called — and its group. Then what it is (<see cref="kind"/> — a row
    /// of the project's <see cref="LevelObjectKindRegistry"/>, picked not typed), which
    /// definition row it is an instance of, where it stands, which placement tags it carries
    /// and its per-placement config. The definition stays in the object's own registry; this
    /// row is the PLACEMENT.</summary>
    [Serializable]
    public sealed class LevelObjectDef : StateTreeRegistryEntry
    {
        /// <summary>What to spawn — a kind row of the project's
        /// <see cref="LevelObjectKindRegistry"/> (see <see cref="LevelRegistry.kinds"/>),
        /// id-wired like every other typed reference. The game's spawner maps the kind to a
        /// view.</summary>
        public StateTreeEntryRef<LevelObjectKindDef> kind =
            new StateTreeEntryRef<LevelObjectKindDef>();

        /// <summary>The definition row this object is an instance OF (unit row, item row) —
        /// id-wired, and picked from the registry the <see cref="kind"/>'s row names in
        /// <see cref="LevelObjectKindDef.definitions"/>.</summary>
        public LevelObjectEntryRef entry = new LevelObjectEntryRef();

        /// <summary>The definition row's name — the spawner's question, unchanged by the
        /// reference moving into <see cref="entry"/>.</summary>
        public string entryName => entry != null ? entry.entryName : "";

        public Vector2 position;

        /// <summary>Placement-only tags this instance carries — where a level's own
        /// vocabulary (see <see cref="LevelContent.tags"/>) lands on an object. Picked from
        /// that vocabulary, never typed: see <see cref="LevelObjectTagRef"/>.</summary>
        public List<LevelObjectTagRef> tags = new List<LevelObjectTagRef>();

        /// <summary>Per-placement config (a door's key and target) — the standard parameter
        /// rows every other config surface uses.</summary>
        public List<GraphTaskParameter> config = new List<GraphTaskParameter>();

        /// <summary>The named String config row's value, or empty — the spawner's one
        /// question.</summary>
        public string ConfigValue(string configName)
        {
            for (int i = 0; i < config.Count; i++)
            {
                GraphTaskParameter row = config[i];
                if (row != null && string.Equals(row.name, configName, StringComparison.Ordinal))
                    return row.stringValue ?? "";
            }
            return "";
        }
    }

    /// <summary>The catalog of levels — a registry kind like any other: list it in a tree's
    /// Data section and level states pick their level with ⛃; the dev picker overlay lists
    /// it; the dashboard edits it. It also carries the project's spawnable
    /// <see cref="kinds"/>, so every level's manifest picks from one known list.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Registry", fileName = "LevelRegistry")]
    public sealed class LevelRegistry : StateTreeRegistry<LevelDef>
    {
        /// <summary>The known list of object kinds a level manifest may use — project-wide,
        /// because a kind is a project definition (what the spawner can build), not a
        /// per-level one.</summary>
        public LevelObjectKindRegistry kinds;
    }
}
