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

        /// <summary>QUIET first attempt. During the scene-load enable queue a host later in the
        /// queue still reports <c>isActiveAndEnabled == false</c>, so resolution here can
        /// legitimately find nothing — proven live in the M10 demo, where BOTH services loaded
        /// unconnected. <see cref="Start"/> is the attempt that gets to complain: it runs after
        /// every OnEnable in the scene, so a miss there is a real wiring error.</summary>
        protected virtual void OnEnable()
        {
            TryConnect(false);
        }

        /// <summary>The order-free retry — the adoption principle applied to the service
        /// itself. Also where this service's own [InjectService] fields are filled — in TWO
        /// passes, because sibling registration order is nobody's to rely on (the quiet
        /// OnEnable connect misses during the scene-load queue, so a sibling may only
        /// register in ITS Start, and Start order is arbitrary — observed live, not
        /// theorized). The quiet pass here fills what already resolves; the loud pass one
        /// frame later fills the rest or names the wiring error. Injected fields are
        /// therefore valid from the first Update on — a service must not use them inside
        /// Start itself.</summary>
        protected virtual void Start()
        {
            TryConnect(true);
            StateTreeServiceInjector.Inject(this, gameObject, true);
            StartCoroutine(CompleteInjection());
        }

        private System.Collections.IEnumerator CompleteInjection()
        {
            yield return null;
            StateTreeServiceInjector.Inject(this, gameObject);
        }

        protected virtual void OnDisable()
        {
            StopFlows();
            Disconnect();
        }

        public void Connect()
        {
            TryConnect(true);
        }

        private void TryConnect(bool warnWhenMissing)
        {
            StateTreeContextHost host = explicitContext != null
                ? explicitContext
                : StateTreeContextHost.ResolveNearest(gameObject);
            if (host == null)
                host = StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Root);

            if (host == null)
            {
                if (warnWhenMissing)
                {
                    Debug.LogWarning("StateTreeService '" + GetType().Name + "' on '" + name
                        + "' found no context host — it stays unconnected. Parent it under a "
                        + "host, assign explicitContext, or add a Root host to the scene.", this);
                }
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

        // ---- the subsystem's OWN flows (the UI wiring brief §4b) -----------------------

        /// <summary>The declaration whose <see cref="ServiceDef.flows"/> tree this service
        /// runs, or null. A declared service overrides this returning its def — one line —
        /// and the base does the rest: the tree starts on the first Update (when services
        /// are valid), ticks with the service, and is Cancelled down with it.</summary>
        protected virtual ServiceDef FlowSource => null;

        private StateTreeExecutor m_Flows;
        private bool m_FlowsStarted;

        /// <summary>Virtual so a subclass with its own Update stays honest: override, call
        /// base, then do your work — a hidden (non-override) Update would silently stop the
        /// subsystem's flows.</summary>
        protected virtual void Update()
        {
            TickFlows(Time.deltaTime);
        }

        /// <summary>One frame of the flow tree — public so EditMode tests own the clock
        /// (the AbilityHost contract). The first call starts the tree.</summary>
        public void TickFlows(float deltaTime)
        {
            if (!m_FlowsStarted)
            {
                m_FlowsStarted = true;
                StartFlows();
            }
            m_Flows?.TickTree(deltaTime);
        }

        /// <summary>Whether the flow tree is up — a wired def and a started run.</summary>
        public bool flowsRunning => m_Flows != null && m_Flows.isRunning;

        private void StartFlows()
        {
            ServiceDef def = FlowSource;
            if (def == null || def.flows == null)
                return;
            StateTreeContextHost host = m_ConnectedTo;
            if (host == null)
            {
                Debug.LogWarning("StateTreeService '" + GetType().Name + "' on '" + name
                    + "' has flows but no connected host — they will not run.", this);
                return;
            }
            m_Flows = new StateTreeExecutor
            {
                data = def.flows,
                owner = host.gameObject,
                // THE SHARED BOARD, explicitly: the executor would otherwise mint its own
                // context, and a flow tree reading a different blackboard than the one the
                // skins' requests land on would simply never fire.
                context = host.Context,
                logLabel = "Service '" + (string.IsNullOrEmpty(def.serviceName)
                    ? GetType().Name : def.serviceName) + "' flows",
                logContext = this
            };
            m_Flows.StartTree();
            if (!m_Flows.isRunning)
                m_Flows = null;
        }

        private void StopFlows()
        {
            if (m_Flows != null)
                m_Flows.StopTree();
            m_Flows = null;
            m_FlowsStarted = false;
        }
    }
}
