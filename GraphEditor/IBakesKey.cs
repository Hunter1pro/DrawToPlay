namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>A board-reading node whose KEY is composed from its dropdowns rather than typed
    /// into a port — "craft.last" + "line". The baker asks it for the key instead of reading a
    /// port (M38.2).</summary>
    public interface IBakesKey
    {
        string BakedKey();
    }
}
