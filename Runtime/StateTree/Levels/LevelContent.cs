using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE LEVEL, in its own file: the scene that stores its arena, the entry params seeded
    /// onto the Level scope, the tag vocabulary the level mints, and the OBJECT MANIFEST —
    /// every placed thing as a row.
    ///
    /// The project's <see cref="LevelRegistry"/> holds only the catalog: one row per level,
    /// carrying identity and a reference to this asset. That split is the point — a level's
    /// content grows (dozens of objects, per-object config) without the project catalog
    /// growing with it, two people can edit two levels without touching the same asset, and
    /// a level can be loaded, versioned or shipped on its own.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level", fileName = "Level")]
    public sealed class LevelContent : ScriptableObject
    {
        /// <summary>Shown on the loading overlay and HUDs; empty falls back to the catalog
        /// row's name.</summary>
        public string displayName = "";

        /// <summary>Project path of the scene ("Assets/.../LevelA.unity"). The scene must be
        /// in Build Settings for additive loading to find it in play mode and players. Picked
        /// as the scene asset in the inspector; kept as the path the loader reads.</summary>
        [ScenePath]
        public string scenePath = "";

        /// <summary>Seeded onto the LEVEL host's context blackboard right after the scene
        /// loads, before anything ticks — the same row type and boxing rules every other
        /// parameter surface uses.</summary>
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        /// <summary>The level's OBJECT REGISTRY — its WORLD MANIFEST in its own asset: every
        /// placed thing is a searchable, groupable row (kind, definition row, position,
        /// placement tags, config). The scene itself holds only the arena; a game-side
        /// spawner turns these rows into VIEWS on <see cref="LevelService.levelLoaded"/> —
        /// which is why a view can be swapped, and why loading them async by position later
        /// is a spawner change, not a data change.</summary>
        public LevelObjectRegistry objects;

        /// <summary>Which tags this level's objects carry — DERIVED from
        /// <see cref="objects"/> (each row's kind plus its placement tags), never stored: a
        /// hand-kept summary of the manifest is a copy that drifts. This is the "what lives
        /// in this level?" answer a reader (or a future streamer) asks without loading the
        /// scene.</summary>
        public void CollectTags(List<string> into)
        {
            if (into == null || objects == null)
                return;
            for (int i = 0; i < objects.entries.Count; i++)
            {
                LevelObjectDef row = objects.entries[i];
                if (row == null)
                    continue;
                AddOnce(into, row.kind.entryName);
                for (int j = 0; j < row.tags.Count; j++)
                    AddOnce(into, row.tags[j]?.tag);
            }
        }

        private static void AddOnce(List<string> into, string tag)
        {
            if (string.IsNullOrEmpty(tag) || into.Contains(tag))
                return;
            into.Add(tag);
        }
    }
}
