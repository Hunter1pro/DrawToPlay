using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// "A SUBSYSTEM JUST ANNOUNCED THIS" (M38.1) — true once per announcement, for the state or
    /// graph that listens.
    ///
    /// An announcement's payload stays on its key, and nothing consumes it: a listener that
    /// checked the key would be true forever after the first dawn, and a listener that removed
    /// the key would be the only one allowed to hear it. So a service stamps every announcement
    /// with a serial (<see cref="StateTreeService.AnnouncementSerialKey"/>), and this fires when
    /// the serial moves past the one it last saw — once per announcement, for any number of
    /// listeners, with the payload still on the key for whoever wants it.
    ///
    /// Each baked copy keeps its own "last seen", which is why a condition is a per-node asset.
    /// </summary>
    [StateTreeCategory("Conditions/Services", "True once each time a subsystem announces a key")]
    public sealed class AnnouncementCondition : StateTreeConditionAsset
    {
        [Tooltip("The announcement — a key a subsystem's def declares it says.")]
        [ServiceAnnouncementKey]
        public string key = "";

        [Tooltip("Where the announcing subsystem's board is. Root for a root subsystem, which is "
            + "most of them; the owner's own scope when unsure, since the walk goes up.")]
        public StateTreeContextKind scope = StateTreeContextKind.Root;

        private int m_LastSeen;
        private bool m_Primed;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null || string.IsNullOrEmpty(key))
                return false;
            StateTreeContextHost host = StateTreeContextHost.Resolve(context.owner, scope);
            var board = host != null && host.Context != null ? host.Context.blackboard : context.blackboard;
            int serial = board.TryGetValue(StateTreeService.AnnouncementSerialKey(key), out object held)
                && held is int count ? count : 0;

            // The first evaluation ADOPTS whatever has already been announced rather than firing
            // on it: a listener that starts after three dawns has not just heard one. A listener
            // that starts BEFORE the first dawn adopts zero — and hears the first.
            if (!m_Primed)
            {
                m_Primed = true;
                m_LastSeen = serial;
                return false;
            }
            if (serial == m_LastSeen)
                return false;
            m_LastSeen = serial;
            return true;
        }
    }
}
