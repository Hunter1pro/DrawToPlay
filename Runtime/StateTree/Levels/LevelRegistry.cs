using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One LEVEL as data (M16) — what a level IS, knowable before anything loads: the scene
    /// that stores it (scenes stay the storage; replacing them would cost more tooling than
    /// it buys), and the ENTRY PARAMS seeded onto the Level scope's blackboard the moment
    /// the scene is up — so one scene can be many levels (hard mode, test setups, expedition
    /// variants) by rows that differ only in parameters. The base carries identity: typed
    /// references (a portal's target, the session tree's level states) wire by id and follow
    /// renames like everything else.
    /// </summary>
    [Serializable]
    public sealed class LevelDef : StateTreeRegistryEntry
    {
        /// <summary>Shown on the loading overlay and HUDs; empty falls back to
        /// <c>name</c>.</summary>
        public string displayName = "";

        /// <summary>Project path of the scene ("Assets/.../LevelA.unity"). The scene must be
        /// in Build Settings for additive loading to find it in play mode and players.</summary>
        public string scenePath = "";

        /// <summary>Seeded onto the LEVEL host's context blackboard right after the scene
        /// loads, before anything ticks — the same row type and boxing rules every other
        /// parameter surface uses.</summary>
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        /// <summary>The level's OWN tag rows — tags UNIQUE to this level, in a registry the
        /// level owns. Root surfaces aggregate them: the tag picker under the root tree
        /// shows the union of the global registry and every level's, grouped by level, so a
        /// level can mint vocabulary without touching the shared list and the root still
        /// SEES all of it. Optional — most levels only use global tags.</summary>
        public WorldTagRegistry tags;

        /// <summary>The level's WORLD MANIFEST: which tag rows (global or from
        /// <see cref="tags"/>) its objects carry — id-wired references, not strings.
        /// Descriptive today (a reader can ask "what lives in this level?" without loading
        /// it); the seam a future async-load-objects-by-position reads.</summary>
        public List<StateTreeEntryRef<WorldTagDef>> usedTags =
            new List<StateTreeEntryRef<WorldTagDef>>();

        /// <summary>The level's OBJECTS, as data: every placed thing is a row here — its
        /// kind, its definition row, its position, its placement tags and config. The scene
        /// itself holds only the arena; a game-side spawner turns these rows into VIEWS on
        /// <see cref="LevelService.levelLoaded"/> — which is why a view can be swapped, and
        /// why loading them async by position later is a spawner change, not a data
        /// change.</summary>
        public List<LevelObjectDef> objects = new List<LevelObjectDef>();

        public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
    }

    /// <summary>One placed object of a level, as DATA: what it is (<see cref="kind"/> — the
    /// tag-vocabulary word a spawner maps to a view), which definition row it is an instance
    /// of, where it stands, which placement tags it carries and its per-placement config.
    /// The definition stays in the object's own registry; this row is the PLACEMENT.</summary>
    [Serializable]
    public sealed class LevelObjectDef
    {
        public string id = "";

        /// <summary>What to spawn — a tag-vocabulary word ("unit", "pickup", "door") the
        /// game's spawner maps to a view.</summary>
        public string kind = "";

        /// <summary>The definition row this object is an instance OF (unit row, item row) —
        /// id-wired; which registry it lives in is implied by <see cref="kind"/>.</summary>
        public string entryId = "";

        public string entryName = "";

        public Vector2 position;

        /// <summary>Placement-only tags this instance carries — where a level's own
        /// vocabulary (see <see cref="LevelDef.tags"/>) lands on an object.</summary>
        public List<string> tags = new List<string>();

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
    /// it; the dashboard edits it.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Registry", fileName = "LevelRegistry")]
    public sealed class LevelRegistry : StateTreeRegistry<LevelDef>
    {
    }
}
