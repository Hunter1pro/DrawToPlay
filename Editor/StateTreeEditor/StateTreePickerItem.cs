namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// ONE ROW OFFERED TO <see cref="StateTreeNodePicker"/> BY A CALLER — the seam that lets the
    /// picker show something other than the node library.
    ///
    /// WHY IT EXISTS. The picker is the good way to choose from a list in this toolset: search,
    /// categories, a description under the name, keyboard first, favourites. Everything ELSE that
    /// asks the author to choose from a list — a registry row, above all — had a
    /// <c>DropdownField</c>, which is a flat alphabetical strip of names that stops being usable
    /// somewhere around thirty entries and can never say what a row IS. The difference was never
    /// in the picker's rendering, it was that its item list was hard-wired to types and assets.
    ///
    /// So a caller can hand it rows instead. The picker's search, grouping, keyboard handling and
    /// layout are untouched and shared; only where the rows come from differs.
    ///
    /// <see cref="payload"/> is what comes back: the picker does not know or care what a row
    /// stands for, and hands the object straight back to the callback that supplied it.
    /// </summary>
    public sealed class StateTreePickerItem
    {
        /// <summary>The row's name — what is shown, searched, and sorted on.</summary>
        public string displayName;

        /// <summary>Slash-separated category path, or empty for an ungrouped row. This is what
        /// turns two hundred rows into a handful of collapsible groups.</summary>
        public string category;

        /// <summary>One line under the name: what this row IS, in the author's words.</summary>
        public string description;

        /// <summary>The tooltip's second line — the thing you would search the project for
        /// (which registry a row came from, which folder an asset is in).</summary>
        public string identity;

        /// <summary>Stable key for favourites and for keeping the selection across a rebuild.
        /// Must not change when the row is renamed, or a favourite is lost on rename.</summary>
        public string persistKey;

        /// <summary>Handed back to the caller's callback, untouched.</summary>
        public object payload;
    }
}
