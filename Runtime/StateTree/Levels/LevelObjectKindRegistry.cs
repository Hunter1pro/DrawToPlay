using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>One spawnable KIND as a row: the name is the word a level's object rows point
    /// at and the game's spawner maps to a view; the description is what the spawner makes of
    /// it.</summary>
    [Serializable]
    public sealed class LevelObjectKindDef : StateTreeRegistryEntry
    {
        [TextArea]
        public string description = "";

        /// <summary>The registry instances of this kind name a row OF — units come from the
        /// unit table, pickups from the item table. The one place that link is stated, which
        /// is what lets a placement's entry be a DROPDOWN of the right rows instead of a
        /// typed name. Empty means the kind has no definition table (a door is only its
        /// config), and its placements name nothing.</summary>
        public StateTreeRegistryAsset definitions;

        /// <summary>
        /// What a placement of this kind LOOKS LIKE, for the scene view — the manifest overlay
        /// draws this prefab's meshes at each placement, translucent, so an author laying out a
        /// level from data can see the level rather than a field of identical dots.
        ///
        /// EDITOR AFFORDANCE ONLY. The spawner builds its own view and never reads this: what a
        /// kind BECOMES at run time is the spawner's business, and tying the preview to it would
        /// make the two drift the moment either changed. Leave it empty and placements draw as
        /// dots, which is what they did before.
        /// </summary>
        public GameObject preview;
    }

    /// <summary>
    /// The PROJECT's spawnable kinds — the known list a level object row picks from, instead
    /// of a free-typed word. Hangs off <see cref="LevelRegistry.kinds"/>, because which kinds
    /// exist is a project-wide definition, not a per-level one: every level's manifest speaks
    /// the same vocabulary, and a kind the spawner does not implement is visible as a row
    /// nobody builds rather than a typo nobody notices.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Object Kind Registry",
        fileName = "LevelObjectKindRegistry")]
    public sealed class LevelObjectKindRegistry : StateTreeRegistry<LevelObjectKindDef>
    {
    }
}
