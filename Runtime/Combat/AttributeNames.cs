namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The attribute names domain code speaks WITHOUT a component to hang them on — the
    /// health facade is gone (M23's retirement, completed): the number lives on
    /// <see cref="AttributeComponent"/>, damage and healing are effect rows, the guard
    /// window is the 'guarded' status tag, and death is the value crossing zero. What
    /// remains of "health" as code is its SPELLING, which must match the AttributeDef row
    /// the registries declare — so it is written once, here.
    /// </summary>
    public static class AttributeNames
    {
        /// <summary>The survival pool.</summary>
        public const string Health = "health";
    }
}
