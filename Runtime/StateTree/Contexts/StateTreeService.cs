using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A SUBSYSTEM, AS A CLASS (M33) — the def-served machinery with no Unity messages in it.
    ///
    /// The behaviour version answered "when am I wired?" in five places: OnEnable to connect,
    /// Start to inject, a coroutine to inject again, Update to tick and to notice a collaborator
    /// that arrived late, and a connect/disconnect pair on top to make the sequence legible. All
    /// five exist because a MonoBehaviour cannot take its dependencies in a constructor.
    ///
    /// This can. It is built when its scope is built, by whoever installs it, with the host it
    /// belongs to and the def it runs; anything else it needs it asks the scope for AT
    /// CONSTRUCTION, so a missing collaborator is a loud failure at install time rather than a
    /// null three frames into play. It is ticked by its scope and disposed with it.
    ///
    /// What it keeps, unchanged, is the def contract (§4g): requests served by action and
    /// reactions, an optional flow tree for the ones that WAIT, declared spawns shown on the
    /// first tick, announcements landed on the scope's board. The rows do not move; only the
    /// thing reading them does.
    /// </summary>
    public abstract class StateTreeService : IDisposable
    {
        /// <param name="scope">The host this subsystem belongs to — its board, its lifetime,
        /// and the chain it resolves collaborators through.</param>
        /// <param name="definition">What it serves. Null is legal for a service with no
        /// declared surface, which is a real thing (the world index, the clock).</param>
        protected StateTreeService(StateTreeContextHost scope, ServiceDef definition)
        {
            m_Scope = scope != null
                ? scope
                : throw new ArgumentNullException(nameof(scope),
                    "A service belongs to a scope: it reads that scope's board and dies with it.");
            m_Definition = definition;
        }

        private readonly StateTreeContextHost m_Scope;
        private readonly ServiceDef m_Definition;

        /// <summary>The host this subsystem lives on.</summary>
        public StateTreeContextHost scope => m_Scope;

        /// <summary>What it serves, or null.</summary>
        public ServiceDef definition => m_Definition;

        /// <summary>The screen's ledger, resolved through the scope chain. Null is a legal
        /// answer — a headless scope has no screen — and is asked for rather than cached,
        /// because the UI service is itself a subsystem a level may replace.</summary>
        protected UiService Ui => m_Scope.GetService<UiService>();

        /// <summary>Whether the flow tree is up — a wired def and a started run.</summary>
        public bool flowsRunning => m_Flows != null && m_Flows.isRunning;

        /// <summary>
        /// One frame of the subsystem: start what has to start, serve what has been asked, tick
        /// the flow tree. Called by the scope — public so a test owns the clock, exactly as the
        /// behaviour version was.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!m_Started)
            {
                m_Started = true;
                ServiceDef def = m_Definition;
                if (def != null)
                {
                    FlowRules.Validate(def, m_Scope);
                    StartFlows(def);
                    ShowSpawns(def);
                }
                OnStarted();
            }

            ServePendingRequests();
            m_Flows?.TickTree(deltaTime);
            OnTick(deltaTime);
        }

        /// <summary>
        /// The subsystem's own frame, after its requests are served and its flows have ticked.
        ///
        /// For the ones that genuinely watch something — what the player is standing at, whether
        /// an objective's zone has been reached. Most services have none, and a service that
        /// wants one is saying "I poll", which is worth being explicit about.
        /// </summary>
        protected virtual void OnTick(float deltaTime)
        {
        }

        /// <summary>
        /// The first tick, after the def is validated, the flows are running and the screens are
        /// up. For the work that needs the world assembled rather than the constructor's moment
        /// — adopting a screen, seeding from a save. Most services need nothing here.
        /// </summary>
        protected virtual void OnStarted()
        {
        }

        /// <summary>
        /// Ask this subsystem for one of its DECLARED requests — the typed door for C# callers,
        /// validated against the def's rows so a typo is a loud finding rather than a key nobody
        /// reads. Data-side callers (skins, tree tasks) write the same key on the board; both
        /// roads meet in the same serving pass.
        /// </summary>
        public void Request(string key, string value = "1")
        {
            ServiceDef def = m_Definition;
            ServiceRequest row = def != null ? def.RequestFor(key) : null;
            if (row == null)
            {
                Debug.LogError("Service '" + GetType().Name + "': '" + key + "' is not a "
                    + "declared request of '" + (def != null ? def.serviceName : "(no def)")
                    + "' — see the def's requests list.");
                return;
            }
            if (row.internalOnly)
            {
                Debug.LogError("Service '" + GetType().Name + "': '" + key + "' is this "
                    + "subsystem's own button, not part of its API.");
                return;
            }
            if (row.namesRowOf != null && row.namesRowOf.FindByName(value) == null)
            {
                Debug.LogError("Service '" + GetType().Name + "': request '" + key
                    + "' takes a row of '" + row.namesRowOf.name + "', and '" + value
                    + "' names none of them.");
                return;
            }
            Dictionary<string, object> board = Board();
            if (board != null)
                board[key] = value ?? "";
        }

        /// <summary>What a request's action MEANS — rules stay code, the mapping stays rows.</summary>
        protected virtual void OnRequest(ServiceRequest request, string value)
        {
        }

        /// <summary>Land an announcement on the scope's board — the outbound half of the API.</summary>
        protected void Announce(string key, object payload)
        {
            Dictionary<string, object> board = Board();
            if (board != null && !string.IsNullOrEmpty(key))
                board[key] = payload;
        }

        /// <summary>Everything this subsystem holds open, released. The scope disposes what it
        /// owns, so a subclass overrides this and calls base rather than inventing a teardown.</summary>
        public virtual void Dispose()
        {
            StopFlows();
            m_Started = false;
        }

        // ---- serving ---------------------------------------------------------------------

        private void ServePendingRequests()
        {
            ServiceDef def = m_Definition;
            Dictionary<string, object> board = Board();
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
                    Debug.LogError("Service '" + GetType().Name + "': request '" + row.key
                        + "' declares state '" + row.stateId + "', which the flows tree does "
                        + "not have.");
                    board.Remove(row.key);   // do not retry a broken row every frame
                }
                return;   // one at a time, declaration order = priority
            }
        }

        private void ServeDirect(ServiceRequest row, Dictionary<string, object> board)
        {
            string value = board.TryGetValue(row.key, out object held) && held != null
                ? held as string ?? held.ToString()
                : "";
            OnRequest(row, value);
            RunReactions(row, value);
            board.Remove(row.key);
        }

        /// <summary>The declared UI beats of a served request: verb on the shown row's views,
        /// the request's value as argument, a named key's held object as the payload.</summary>
        private void RunReactions(ServiceRequest row, string value)
        {
            if (row.reactions == null || row.reactions.Count == 0)
                return;
            UiService ui = Ui;
            if (ui == null)
                return;
            Dictionary<string, object> board = Board();
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

        private void ShowSpawns(ServiceDef def)
        {
            if (def.spawns == null || def.spawns.Count == 0)
                return;
            UiService ui = Ui;
            if (ui == null)
            {
                Debug.LogWarning("Service '" + GetType().Name + "' declares spawns but no "
                    + "UiService is reachable — its screen stays unshown.");
                return;
            }
            for (int i = 0; i < def.spawns.Count; i++)
            {
                var spawn = def.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.entryName))
                    continue;
                UiDef row = ui.Find(spawn.entryName);
                if (row == null)
                    Debug.LogError("Service '" + GetType().Name + "' spawns UI row '"
                        + spawn.entryName + "', which the UI registry does not have.");
                else
                    ui.Show(row);
            }
        }

        // ---- the subsystem's own flow tree ------------------------------------------------

        private void StartFlows(ServiceDef def)
        {
            if (def.flows == null)
                return;
            m_Flows = new StateTreeExecutor
            {
                data = def.flows,
                owner = m_Scope.gameObject,
                // THE SHARED BOARD, explicitly: a flow tree reading a different blackboard from
                // the one the skins' requests land on would simply never fire.
                context = m_Scope.Context,
                logLabel = "Service '" + (string.IsNullOrEmpty(def.serviceName)
                    ? GetType().Name : def.serviceName) + "' flows",
                logContext = m_Scope
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
        }

        /// <summary>THE DERIVED CONSUME: leaving a request state clears its key.</summary>
        private void OnFlowNodeChanged(string previousId, string currentId)
        {
            ServiceDef def = m_Definition;
            if (def == null || string.IsNullOrEmpty(previousId))
                return;
            for (int i = 0; i < def.requests.Count; i++)
            {
                ServiceRequest row = def.requests[i];
                if (row != null && string.Equals(row.stateId, previousId, StringComparison.Ordinal))
                {
                    m_Flows?.context?.blackboard.Remove(row.key);
                    return;
                }
            }
        }

        private Dictionary<string, object> Board()
        {
            return m_Scope != null && m_Scope.Context != null
                ? m_Scope.Context.blackboard
                : null;
        }

        private StateTreeExecutor m_Flows;
        private bool m_Started;
    }
}
