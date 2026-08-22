namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE FILE, AS A CAPABILITY (M39.2b) — what a domain calls after it changed something
    /// worth keeping. HT's rule applied: the bag does not raise "changed" for a save to hear;
    /// its write calls the save, in the same method, where the dependency can be read. The
    /// save coalesces and writes on its own clock; this is only the knock on its door.
    /// </summary>
    public interface IAutosave
    {
        void MarkDirty();
    }
}
