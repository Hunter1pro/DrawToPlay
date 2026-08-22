using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A SERVICE THAT WANTS A BODY (M40.3c, meta-rule 3) — the bag wants the player, the bench
    /// wants the player. A body's lifetime IS its scope: the host that scopes it registers
    /// when the body is born and unregisters when it goes, and those two moments are the only
    /// ones that matter. So the host tells every reachable service that wants its kind of body
    /// — no component on the prefab, no injection, no Start — and the service holds the body
    /// from then until it is told it is gone. HT's <c>bind_player</c>, done by the scope.
    /// </summary>
    public interface IBindsBody
    {
        /// <summary>Which scope kind is "a body" to this service.</summary>
        StateTreeContextKind bodyKind { get; }

        void Bind(StateTreeContextHost body);

        void Unbind(StateTreeContextHost body);
    }
}
