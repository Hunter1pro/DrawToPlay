using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE LEVEL'S OBJECTS, as a registry in its own file — the same asset kind, dashboard and
    /// discipline every other entry list gets: search across every field, group sections
    /// ("enemies", "props", "doors"), ids minted on add and never edited, renames safe because
    /// references are id-wired.
    ///
    /// A level's <see cref="LevelContent.objects"/> points at one of these. Separate file
    /// because a level's object list is the part that GROWS: hundreds of rows belong in their
    /// own asset, not inline in the level header, and two people can edit two levels'
    /// placements without meeting in the same file.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Levels/Level Objects",
        fileName = "LevelObjects")]
    public sealed class LevelObjectRegistry : StateTreeRegistry<LevelObjectDef>
    {
    }
}
