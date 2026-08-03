namespace PowerOfFire.DrawToPlay.GraphEditor
{
    /// <summary>
    /// Implemented by every node of a <see cref="TaskGraph"/> that becomes an instruction in the
    /// baked program. The single member is the instruction it becomes; everything else about the
    /// mapping — which ports fill <c>exec[]</c>, which fill <c>data[]</c>, which are baked as
    /// constants — lives in <see cref="TaskGraphBaker"/>, in one switch, because that switch IS the
    /// byte-precise runtime contract and splitting it across forty node classes would make it
    /// unreadable and unreviewable.
    ///
    /// The entry nodes (<see cref="OnEnterNode"/>, <see cref="OnTickNode"/>,
    /// <see cref="OnExitNode"/>) deliberately do NOT implement this: they are not instructions, they
    /// name where a chain starts.
    /// </summary>
    public interface ITaskGraphNode
    {
        /// <summary>The program instruction this node bakes into.</summary>
        GraphTaskNodeKind nodeKind { get; }
    }
}
