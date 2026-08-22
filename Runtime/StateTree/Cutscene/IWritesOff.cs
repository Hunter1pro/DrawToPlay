namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE LEDGER OF WHAT IS GONE (M40.4, meta-rule 5) — a felled tree, a taken pickup, a
    /// cutscene that plays once. The game keeps it (and saves it); the toolset's services
    /// write to it by this capability when something is spent.
    /// </summary>
    public interface IWritesOff
    {
        void MarkGone(string placementId);

        bool IsGone(string placementId);
    }
}
