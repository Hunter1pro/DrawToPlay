using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THIS STRING IS A TAG (M31) — picked from the vocabulary its owner declares, not typed.
    ///
    /// A tag is the most-used reference in this toolset and was the only one with no declaration:
    /// objectives find their subject by tag, cutscenes cast by tag, the craft ability finds a
    /// bench by tag, and the world's whole spatial index is keyed by one. Every one of those was
    /// a word somebody typed twice and hoped about.
    ///
    /// Marked, the field offers exactly the rows the asking asset DECLARES — a manifest's tag
    /// vocabularies, a def's or a tree's declared catalogs — and nothing else in the project. A
    /// name no vocabulary holds stays typeable, because a tag being invented is not yet a
    /// contract; the moment a row exists for it, the field is a link and locks like every other
    /// link here.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class WorldTagAttribute : PropertyAttribute
    {
        /// <summary>Optional: offer only tags of this GROUP — "objective", "world". The row's own
        /// group, which is the category idea without a dotted-name hierarchy nobody can
        /// enumerate.</summary>
        public readonly string group;

        /// <summary>The sibling field holding the row's id, so a rename follows. Empty means the
        /// name is the whole reference.</summary>
        public readonly string idField;

        public WorldTagAttribute(string group = "", string idField = "")
        {
            this.group = group ?? "";
            this.idField = idField ?? "";
        }
    }
}
