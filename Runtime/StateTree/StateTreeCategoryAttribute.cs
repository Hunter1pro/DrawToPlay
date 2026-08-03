using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Palette placement for a state-tree task or condition type: a slash-separated
    /// category path ("Conditions/Perception") and an optional short description shown
    /// in the picker. Untagged types still appear (under "Uncategorized"), so a
    /// freshly-written task is usable before anyone thinks about taxonomy — the
    /// attribute makes it FINDABLE once the library grows.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class StateTreeCategoryAttribute : Attribute
    {
        public string path { get; }
        public string description { get; }

        public StateTreeCategoryAttribute(string path, string description = "")
        {
            this.path = path;
            this.description = description;
        }
    }
}
