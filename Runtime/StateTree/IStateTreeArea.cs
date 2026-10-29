namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A state's context that has a PLACE in its level: a centre (a placement id of the level's
    /// manifest) and a radius. A schema's group node (a zone) carries one as its context task; the
    /// editor's Scene view draws it as a circle and runs a line from it to each step it holds.
    /// </summary>
    public interface IStateTreeArea
    {
        string areaCentre { get; }

        float areaRadius { get; }
    }
}
