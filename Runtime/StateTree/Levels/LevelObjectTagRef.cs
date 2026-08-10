using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One placement tag, PICKED from the tag vocabulary rather than typed — the project's
    /// global <see cref="WorldTagRegistry"/> rows plus the level's own
    /// (<see cref="LevelContent.tags"/>), which is exactly the set a placement in that level
    /// may carry.
    ///
    /// The NAME is the whole reference (no id): tags are matched by exact text at runtime, so
    /// the same rule the Tag-kind key picker follows applies here — a known-list choice, not
    /// an id wire.
    /// </summary>
    [Serializable]
    public sealed class LevelObjectTagRef
    {
        public string tag = "";
    }
}
