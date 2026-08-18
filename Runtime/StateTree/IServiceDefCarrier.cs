namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A ROW THAT IS BACKED BY A DEF (M30.2, and the hinge of M30.3).
    ///
    /// Contracts are claimed by <see cref="ServiceDef"/>s, but the things an author picks are
    /// ROWS — an object row, a kind row, an ability row. This is how a row says "the def behind
    /// me is that one", so a picker can filter rows by the promises their defs keep without
    /// knowing what kind of row it is looking at.
    ///
    /// It is deliberately one property. When M30.3 makes the def the object itself, the rows that
    /// place objects will implement this and nothing else about them has to change.
    /// </summary>
    public interface IServiceDefCarrier
    {
        ServiceDef ServiceDef { get; }
    }
}
