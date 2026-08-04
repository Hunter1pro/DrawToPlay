using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Base for C# service ATOMS (brief §3.2/§3.7): derive, put it in the scene, and it connects
    /// itself to its context — nearest host up the hierarchy, or the one named by
    /// <see cref="explicitContext"/> when the service lives away from its scope. Scoping is
    /// therefore PLACEMENT: under the Root object the service is global, under a Level host it
    /// dies and returns with the level. Trees and graphs reach it through
    /// <see cref="StateTreeContextHost.FindService{T}"/> and the spine's parent chain.
    ///
    /// What belongs in a subclass is the §3.7 boundary, stated once here so every service
    /// inherits the sentence: heavy data invariants, an engine/serialization boundary, or
    /// hot-path compute — an atom exposed through nodes. Orchestration ("when does the shop
    /// open") is a TREE on the context, and an `if` about game rules in a subclass probably
    /// wants to be a graph.
    ///
    /// <see cref="Connect"/>/<see cref="Disconnect"/> are public and idempotent for the same
    /// reason the host's Register is: EditMode tests and manual wiring do exactly what the
    /// lifecycle does, with no second path.
    /// </summary>
    public abstract class StateTreeServiceBehaviour : MonoBehaviour
    {
        /// <summary>Connect here instead of the nearest host — for a service object that cannot
        /// live under its scope in the hierarchy. Null = resolve by placement.</summary>
        public StateTreeContextHost explicitContext;

        private StateTreeContextHost m_ConnectedTo;

        /// <summary>The host this service is registered on, null while unconnected.</summary>
        public StateTreeContextHost connectedTo => m_ConnectedTo;

        protected virtual void OnEnable()
        {
            Connect();
        }

        protected virtual void OnDisable()
        {
            Disconnect();
        }

        public void Connect()
        {
            StateTreeContextHost host = explicitContext != null
                ? explicitContext
                : StateTreeContextHost.ResolveNearest(gameObject);
            if (host == null)
                host = StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Root);

            if (host == null)
            {
                Debug.LogWarning("StateTreeService '" + GetType().Name + "' on '" + name
                    + "' found no context host — it stays unconnected. Parent it under a host, "
                    + "assign explicitContext, or add a Root host to the scene.", this);
                return;
            }

            if (m_ConnectedTo == host)
                return;

            Disconnect();
            host.RegisterService(this);
            m_ConnectedTo = host;
        }

        public void Disconnect()
        {
            if (m_ConnectedTo == null)
                return;
            m_ConnectedTo.UnregisterService(this);
            m_ConnectedTo = null;
        }
    }
}
