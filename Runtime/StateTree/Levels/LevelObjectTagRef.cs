using System;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One placement tag, PICKED from the tag vocabulary rather than typed — the project's
    /// global <see cref="WorldTagRegistry"/> rows plus the level's own
    /// (<see cref="LevelContent.tags"/>), which is exactly the set a placement in that level
    /// may carry.
    ///
    /// THE NAME IS WHAT THE RUNTIME READS — tags are matched by exact text, and nothing about
    /// that changed. The ID rides beside it so a vocabulary can be RENAMED: the picker re-reads
    /// the row's current name through the id, which is the difference between a rename that
    /// travels and a rename that breaks every placement carrying the old spelling.
    /// </summary>
    [Serializable]
    public sealed class LevelObjectTagRef
    {
        [WorldTag(idField: "tagId")]
        public string tag = "";

        /// <summary>The row this was picked from, when it was picked from one. Hidden: it is the
        /// wire, and the name above is the thing to read.</summary>
        [HideInInspector]
        public string tagId = "";
    }
}
