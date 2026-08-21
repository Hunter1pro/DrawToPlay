using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE ROW A LIST SKIN SHOWS — the name for the wiring, the label and count for the human.
    ///
    /// Handed to a skin by whatever knows the domain (a bind task reading the bag), never
    /// looked up by the skin itself: a view that fetches its own content is a view with an
    /// opinion about where content lives, and that opinion is always wrong by the next
    /// milestone. Same rule the bag's cells follow, said for the plain-list case.
    /// </summary>
    [Serializable]
    public struct UiListEntry
    {
        public string itemId;
        public string label;
        public int count;
    }
}
