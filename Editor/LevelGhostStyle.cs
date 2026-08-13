namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// What a placement's ghost is DRAWN AS in the Level Manifest overlay — a view preference, so
    /// it lives in EditorPrefs and never in the level's data.
    ///
    /// Two modes because the two questions an author asks about a level are different. "Does this
    /// read?" — is the guard behind the crate, can the player see the door — wants the real
    /// silhouette. "What is where?" — which of these is an NPC, which is loot, are there three
    /// pickups or four — wants shapes that are instantly told apart, and a field of accurate grey
    /// meshes is worse at that than a field of coloured capsules.
    /// </summary>
    public enum LevelGhostStyle
    {
        /// <summary>The prefab's real geometry at 1:1, rigged characters included — see
        /// <see cref="LevelGhostMeshes"/> for how a skinned mesh is posed without putting anything
        /// in the scene.</summary>
        Mesh = 0,

        /// <summary>A capsule per placement, coloured by kind. Reads at any zoom, tells kinds apart
        /// at a glance, and can never be mistaken for the finished object.</summary>
        Capsule = 1
    }
}
