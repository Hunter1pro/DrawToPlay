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

        /// <summary>The level's WORLD MANIFEST: which tag rows (see
        /// <see cref="WorldTagRegistry"/>) its objects carry — id-wired references, not
        /// strings. Descriptive today (a reader can ask "what lives in this level?" without
        /// loading it); the seam a future async-load-objects-by-position reads.</summary>
        public List<StateTreeEntryRef<WorldTagDef>> usedTags =
            new List<StateTreeEntryRef<WorldTagDef>>();

        public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
    }

    /// <summary>The catalog of levels — a registry kind like any other: list it in a tree's
    /// Data section and level states pick their level with ⛃; the dev picker overlay lists
    /// it; the dashboard edits it.</summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Registry", fileName = "LevelRegistry")]
    public sealed class LevelRegistry : StateTreeRegistry<LevelDef>
    {
    }
}
