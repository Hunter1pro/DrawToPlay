namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// A NODE WHOSE DROPDOWNS DEPEND ON EACH OTHER (M38.1) — subsystem → request → value. The
    /// Registry Entry node's shape, as a seam: <see cref="ChoicePortRefresh"/> asks whether the
    /// lists a node is offering still match what its first pins say, and redefines it when not.
    /// Port definition cannot read pins, so a node remembers its sources in fields that
    /// <see cref="AdoptChoiceSources"/> writes from the pins and <c>OnDefinePorts</c> reads.
    /// </summary>
    public interface IDeclaredApiNode
    {
        /// <summary>Read the pins the lists depend on into the fields the definition reads.
        /// True when something changed and the node needs redefining.</summary>
        bool AdoptChoiceSources();

        /// <summary>Whether any dependent port offers a list other than what the pins now say.</summary>
        bool IsStale();

        /// <summary>Clear a dependent pin holding a value its list no longer offers.</summary>
        void DropUnoffered();
    }
}
