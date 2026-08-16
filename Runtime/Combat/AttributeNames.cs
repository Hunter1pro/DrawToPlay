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

        /// <summary>How fast this actor moves, in metres per second — the number a MODE
        /// changes (M26: a boat is not a fast pair of legs, it is a different speed on the
        /// same body). An actor without it moves at its locomotion service's default, so
        /// the attribute is an override, not a requirement.</summary>
        public const string MoveSpeed = "moveSpeed";
    }
}
