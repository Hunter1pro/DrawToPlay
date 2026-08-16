using System;
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
        private ServiceDef m_FlowsDef;
        private bool m_FlowsStarted;

        /// <summary>Virtual so a subclass with its own Update stays honest: override, call
        /// base, then do your work — a hidden (non-override) Update would silently stop the
        /// subsystem's flows.</summary>
        protected virtual void Update()
        {
            TickFlows(Time.deltaTime);
        }

        /// <summary>One frame of the subsystem's declared behavior — public so EditMode
        /// tests own the clock (the AbilityHost contract). The first call validates the
        /// def, starts the flow tree if one is declared, and shows the declared spawns;
        /// every call serves pending requests (def-level and tree alike).</summary>
        public void TickFlows(float deltaTime)
        {
            if (!m_FlowsStarted)
            {
                m_FlowsStarted = true;
                ServiceDef def = FlowSource;
                if (def != null)
                {
                    m_FlowsDef = def;
                    FlowRules.Validate(def, this);
                    StartFlows();
                    ShowSpawns(def);
                }
            }
            ServePendingRequests();
            m_Flows?.TickTree(deltaTime);
        }

        /// <summary>
        /// Ask this subsystem for one of its DECLARED requests (§4c) — the typed door for
        /// C# callers, validated against the def's rows so a typo is a loud finding, not a
        /// key nobody ever reads. Data-side callers (skins, tree tasks) write the same key
        /// on the scope's blackboard directly; both roads meet in the same flow state.
        /// </summary>
        public void Request(string key, string value = "1")
        {
            ServiceDef def = FlowSource;
            ServiceRequest row = def != null ? def.RequestFor(key) : null;
            if (row == null)
            {
                Debug.LogError("StateTreeService '" + GetType().Name + "': '" + key
                    + "' is not a declared request of '"
                    + (def != null ? def.serviceName : "(no def)")
                    + "' — see the def's requests list.", this);
                return;
            }
            // The TYPED value (§4d): a request whose row names a registry refuses a value
            // that names no row — the button that would do nothing forever, refused at the
            // door with a name in the message.
            if (row.namesRowOf != null && row.namesRowOf.FindByName(value) == null)
            {
                Debug.LogError("StateTreeService '" + GetType().Name + "': request '" + key
                    + "' takes a row of '" + row.namesRowOf.name + "', and '" + value
                    + "' names none of them.", this);
                return;
            }
            StateTreeContextHost host = m_ConnectedTo;
            if (host != null && host.Context != null)
                host.Context.blackboard[key] = value ?? "";
        }

        /// <summary>
        /// THE DERIVED ENTRY (§4c/§4g), declaration order = priority. A row WITHOUT a
        /// stateId is def-served (§4g): single-frame, independent — every pending one runs
        /// now (domain hook, reactions, consume). A row WITH one enters its flow state —
        /// one per tick, none while a request state is active, exactly as the interrupt
        /// wiring behaved.
        /// </summary>
        private void ServePendingRequests()
        {
            ServiceDef def = m_FlowsDef;
            var board = Board();
            if (def == null || board == null)
                return;

            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row != null && string.IsNullOrEmpty(row.stateId)
                    && !string.IsNullOrEmpty(row.key) && board.ContainsKey(row.key))
                    ServeDirect(row, board);
            }

            if (m_Flows == null || !m_Flows.isRunning)
                return;
            string active = m_Flows.activeNodeId;
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row != null && !string.IsNullOrEmpty(row.stateId)
                    && string.Equals(row.stateId, active, StringComparison.Ordinal))
                    return;   // mid-flow: tree rows wait for the return to idle
            }
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row == null || string.IsNullOrEmpty(row.stateId)
                    || string.IsNullOrEmpty(row.key) || !board.ContainsKey(row.key))
                    continue;
                if (!m_Flows.RequestTransition(row.stateId))
                {
                    Debug.LogError("StateTreeService '" + GetType().Name + "': request '"
                        + row.key + "' declares state '" + row.stateId
                        + "', which the flows tree does not have.", this);
                    board.Remove(row.key);   // do not retry a broken row every frame
                }
                return;
            }
        }

        /// <summary>A def-served request (§4g): the domain hook, then the declared UI
        /// beats, then the consume — what a flow state's frame did, without a tree.</summary>
        private void ServeDirect(ServiceRequest row,
            System.Collections.Generic.Dictionary<string, object> board)
        {
            string value = board.TryGetValue(row.key, out object held) && held is string text
                ? text
                : "";
            OnRequest(row, value);
            RunReactions(row, value);
            board.Remove(row.key);
        }

        /// <summary>
        /// THE DOMAIN HOOK (§4g): what a request's <see cref="ServiceRequest.action"/>
        /// MEANS is the service's to say — rules stay code, the mapping stays rows. Land
        /// announcements from here via <see cref="Announce"/>. Base does nothing (a pure
        /// UI request has no domain half).
        /// </summary>
        protected virtual void OnRequest(ServiceRequest request, string value)
        {
        }

        /// <summary>The declared UI beats of a def-served request — the UiCallTask, read
        /// from rows: verb on the shown row's views, the request's value as argument, a
        /// named key's held object as the PAYLOAD (an announcement travels whole).</summary>
        private void RunReactions(ServiceRequest row, string value)
        {
            if (row.reactions == null || row.reactions.Count == 0)
                return;
            UiService ui = StateTreeContextHost.FindService<UiService>(gameObject);
            if (ui == null)
                return;
            var board = Board();
            for (int i = 0; i < row.reactions.Count; i++)
            {
                UiReaction reaction = row.reactions[i];
                if (reaction == null || string.IsNullOrEmpty(reaction.verb))
                    continue;
                GameObject view = ui.ShownView(reaction.ui.entryName);
                if (view == null)
                    continue;
                string argument = reaction.valueArgument ? value : "";
                object payload = null;
                if (!string.IsNullOrEmpty(reaction.argumentKey) && board != null
                    && board.TryGetValue(reaction.argumentKey, out object heldArg)
                    && heldArg != null)
                {
                    if (heldArg is string textArg)
                        argument = textArg;
                    else
                        payload = heldArg;
                }
                UiViewBehaviour[] views = view.GetComponentsInChildren<UiViewBehaviour>(true);
                for (int j = 0; j < views.Length; j++)
                    views[j].Call(reaction.verb, argument, payload);
            }
        }

        /// <summary>The declared spawns (§4g), shown once at first Update: mounting the
        /// service with its def IS the whole subsystem, screen included.</summary>
        private void ShowSpawns(ServiceDef def)
        {
            if (def.spawns == null || def.spawns.Count == 0)
                return;
            UiService ui = StateTreeContextHost.FindService<UiService>(gameObject);
            if (ui == null)
            {
                Debug.LogWarning("StateTreeService '" + GetType().Name + "' declares spawns "
                    + "but no UiService is reachable — its screen stays unshown.", this);
                return;
            }
            for (int i = 0; i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.entryName))
                    continue;
                UiDef row = ui.Find(spawn.entryName);
                if (row == null)
                    Debug.LogError("StateTreeService '" + GetType().Name + "' spawns UI row '"
                        + spawn.entryName + "', which the UI registry does not have.", this);
                else
                    ui.Show(row);
            }
        }

        /// <summary>Land an announcement (§4g) on the scope's board — the outbound half
        /// of the API, written by the domain hook.</summary>
        protected void Announce(string key, object payload)
        {
            var board = Board();
            if (board != null && !string.IsNullOrEmpty(key))
                board[key] = payload;
        }

        private System.Collections.Generic.Dictionary<string, object> Board()
        {
            return m_ConnectedTo != null && m_ConnectedTo.Context != null
                ? m_ConnectedTo.Context.blackboard
                : null;
        }

        /// <summary>THE DERIVED CONSUME (§4c): leaving a request state clears its key —
        /// what a Clear task at the end of every flow used to say.</summary>
        private void OnFlowNodeChanged(string previousId, string currentId)
        {
            ServiceDef def = m_FlowsDef;
            if (def == null || string.IsNullOrEmpty(previousId))
                return;
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row != null && string.Equals(row.stateId, previousId,
                    StringComparison.Ordinal))
                {
                    m_Flows?.context?.blackboard.Remove(row.key);
                    return;
                }
            }
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
            {
                m_Flows = null;
                return;
            }
            m_Flows.activeNodeChanged += OnFlowNodeChanged;
        }

        private void StopFlows()
        {
            if (m_Flows != null)
            {
                m_Flows.activeNodeChanged -= OnFlowNodeChanged;
                m_Flows.StopTree();
            }
            m_Flows = null;
            m_FlowsDef = null;
            m_FlowsStarted = false;
        }
    }
}
