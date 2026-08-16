namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The names crafting answers to. One place, because a request key is written by whoever
    /// asks (an ability's task, a dialog node, a test) and read by the def that declares it —
    /// and a key that agrees only by spelling is the thing this toolset exists to end.
    /// </summary>
    public static class CraftKeys
    {
        /// <summary>Make one of a recipe. Value = the recipe row's name.</summary>
        [ServiceRequestKey]
        public const string Begin = "craft.begin";
    }
}
