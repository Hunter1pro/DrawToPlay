namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A SUBSYSTEM WITH NO CLASS (M41.3) — what a def becomes when it declares asks and a body
    /// but names no service type: a shrine, a lamp, a door. Every one of its asks is served by
    /// its reaction graph, which the base already runs when a request lands; this class adds
    /// nothing, and that is the point. "Spawning, a tag, and a task graph is enough" — the
    /// installer builds one of these for any def with requests and no class, and the API
    /// window lists it beside the bench.
    /// </summary>
    public sealed class GraphServedService : StateTreeService
    {
        public GraphServedService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
        }
    }
}
