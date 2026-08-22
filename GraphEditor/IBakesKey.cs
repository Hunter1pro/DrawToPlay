namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>A board-reading node whose KEY is composed from its dropdowns rather than typed
    /// into a port — "craft.last" + "line". The baker asks it for the key instead of reading a
    /// port (M38.2).</summary>
    public interface IBakesKey
    {
        string BakedKey();

        /// <summary>The scope kind the answering subsystem writes on ("Root", "Level", …) —
        /// baked into the node so the read lands where the answer is, whatever context the
        /// graph runs on (M40.1). Empty means the graph's own board.</summary>
        string BakedScope();
    }
}
