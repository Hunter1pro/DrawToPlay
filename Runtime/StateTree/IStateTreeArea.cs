namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A state's context that has a PLACE in its level: a centre (a placement id of the level's
    /// manifest) and a radius. A schema's group node (a zone) carries one as its context task; the
    /// editor's Scene view draws it as a circle and runs a line from it to each step it holds.
    /// </summary>
    public interface IStateTreeArea : IStateTreeNodeContext
    {
        string areaCentre { get; }

        float areaRadius { get; }
    }

    /// <summary>
    /// A GROUP'S DATA, not a task: what a group node carries about itself (its level, its place).
    /// The executor never runs it, and a state holding nothing else is entered through its first
    /// child — so a group runs its children in order, and its data stays readable by the editor.
    /// </summary>
    public interface IStateTreeNodeContext
    {
    }

    /// <summary>
    /// A group that IS one level of an episode tree: its nodes run only while that level is up
    /// (<see cref="StateTreeContextHost.JoinEpisode"/>), and where they stood is kept per level.
    /// </summary>
    public interface IStateTreeLevel : IStateTreeNodeContext
    {
        /// <summary>The level row's name, as <see cref="LevelService.current"/> carries it.</summary>
        string levelName { get; }
    }
}
