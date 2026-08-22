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

            // THE SETTINGS LAND HERE (M36): every declared default first, then the def's
            // overrides, then this install's — and the derived constructor body has not run
            // yet, so it sees the final numbers when it does. This is the only place that is
            // true. Each layer that speaks is remembered, so the scope tree can say where a
            // number came from.
            ServiceSettings.Initialize(this);
            m_SettingSources = ServiceSettings.Sources(this);
            if (definition != null)
            {
                ServiceSettings.Apply(this, definition.settings, "def '" + definition.name + "'",
                    m_SettingSources, ServiceSettingSource.Def);
            }
            if (scope.tuning != null)
            {
                ServiceSettings.Apply(this, scope.tuning, "the install on '" + scope.name + "'",
                    m_SettingSources, ServiceSettingSource.Install);
            }
        }

        private readonly StateTreeContextHost m_Scope;
        private readonly ServiceDef m_Definition;
        private readonly Dictionary<string, ServiceSettingSource> m_SettingSources;

        /// <summary>Where each declared setting's value came from — the class, the def, or this
        /// install. What the scope tree shows beside the number.</summary>
        public IReadOnlyDictionary<string, ServiceSettingSource> settingSources => m_SettingSources;

        /// <summary>The host this subsystem lives on.</summary>
        public StateTreeContextHost scope => m_Scope;

        /// <summary>What it serves, or null.</summary>
        public ServiceDef definition => m_Definition;

        /// <summary>The screen's ledger, resolved through the scope chain. Null is a legal
        /// answer — a headless scope has no screen — and is asked for rather than cached,
        /// because the UI service is itself a subsystem a level may replace.</summary>
        protected UiService Ui => m_Scope.GetService<UiService>();

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
                // ITS OWN COLLABORATORS, FIRST (M36.1's find): the behaviour this class replaced
                // filled a service's [InjectService] fields in OnEnable, and M33 deleted it
                // without moving that here — so every such field has read null since, and the
                // bench never opened. The first tick is the right moment: every subsystem on
                // the scope is installed by now, in dependency order, so a miss is a real
                // wiring error and is said out loud.
                StateTreeServiceInjector.Inject(this, m_Scope.gameObject);
                ServiceDef def = m_Definition;
                if (def != null)
                    ShowSpawns(def);
                OnStarted();
            }

            ServePendingRequests();
            TickReactionGraphs(deltaTime);
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
            bool emptyAllowed = string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(row.emptyMeans);
            if (row.namesRowOf != null && !emptyAllowed && row.namesRowOf.FindByName(value) == null)
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
            if (board == null || string.IsNullOrEmpty(key))
                return;
            board[key] = payload;
            // EVERY ANNOUNCEMENT HAS A NUMBER (M38.1). The payload stays on the key for anyone
            // to read, and nothing consumes it — so a listener that wants "dawn happened" rather
            // than "dawn has happened at some point" needs a way to tell the second announcement
            // from the first even when the payload reads the same. The serial beside the key is
            // that: it only ever grows, and AnnouncementCondition fires once per step of it.
            string serialKey = AnnouncementSerialKey(key);
            board[serialKey] = board.TryGetValue(serialKey, out object held) && held is int serial
                ? serial + 1
                : 1;

            // A CONTRACT'S FIELDS ARE KEYS TOO (M38.2). A CraftResult is one object on one key,
            // which a skin binds whole — and a tree or a graph that wants "was it made" or "the
            // line" had no key to read. Every public field a payload carries that is a number, a
            // bool, a string or an enum lands beside the key as key.field, so the existing
            // readers (Get String, Compare, Has Key) reach into a contract without learning it.
            ServiceContracts.Flatten(board, key, payload);
        }

        /// <summary>Where an announcement's serial lives beside its payload: <c>key.announced</c>.</summary>
        public static string AnnouncementSerialKey(string key)
        {
            return key + ".announced";
        }

        /// <summary>Everything this subsystem holds open, released. The scope disposes what it
        /// owns, so a subclass overrides this and calls base rather than inventing a teardown.</summary>
        public virtual void Dispose()
        {
            for (int i = m_ReactionRuns != null ? m_ReactionRuns.Count - 1 : -1; i >= 0; i--)
            {
                m_ReactionRuns[i].program.OnExit(m_ReactionRuns[i].context, StateTreeStatus.Cancelled);
                UnityEngine.Object.Destroy(m_ReactionRuns[i].program);
            }
            m_ReactionRuns?.Clear();
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
                if (row != null && !string.IsNullOrEmpty(row.key) && board.ContainsKey(row.key))
                    ServeDirect(row, board);
            }
        }

        private void ServeDirect(ServiceRequest row, Dictionary<string, object> board)
        {
            string value = board.TryGetValue(row.key, out object held) && held != null
                ? held as string ?? held.ToString()
                : "";
            OnRequest(row, value);
            RunReactionGraph(row, value, board);
            board.Remove(row.key);
        }

        /// <summary>The declared UI beats of a served request: verb on the shown row's views,
        /// the request's value as argument, a named key's held object as the payload.</summary>
        // ---- reaction graphs (M38.2) ---------------------------------------------------------

        /// <summary>A reaction graph that has not finished — Say To is one tick, a Show that holds
        /// is many. Each run is a COPY of the program, so two crafts a frame apart do not share
        /// instance state.</summary>
        private sealed class ReactionRun
        {
            public GraphTaskAsset program;
            public StateTreeContext context;
        }

        private List<ReactionRun> m_ReactionRuns;

        /// <summary>
        /// THE WIRING WITH AN IF IN IT: a request that names a reaction graph runs it here, on
        /// this subsystem's scope, after the action and the row reactions — so what the action
        /// announced is already on its key, fields beside it, for the graph to branch on. The
        /// request's value waits under <c>key.asked</c>. The graph's board IS the scope's board:
        /// a reaction reads and writes what everything else on the scope reads and writes.
        /// </summary>
        private void RunReactionGraph(ServiceRequest row, string value, Dictionary<string, object> board)
        {
            if (row.reactionGraph == null)
                return;
            board[row.key + ".asked"] = value ?? "";
            var run = new ReactionRun
            {
                program = GraphTaskAsset.Copy(row.reactionGraph),
                context = m_Scope.Context
            };
            run.program.hideFlags = HideFlags.HideAndDontSave;
            run.program.OnEnter(run.context);
            if (Step(run, 0f))
                return;
            m_ReactionRuns ??= new List<ReactionRun>();
            m_ReactionRuns.Add(run);
        }

        private void TickReactionGraphs(float deltaTime)
        {
            for (int i = m_ReactionRuns != null ? m_ReactionRuns.Count - 1 : -1; i >= 0; i--)
            {
                if (Step(m_ReactionRuns[i], deltaTime))
                    m_ReactionRuns.RemoveAt(i);
            }
        }

        /// <summary>One tick of a reaction run; true when it is finished and torn down.</summary>
        private static bool Step(ReactionRun run, float deltaTime)
        {
            StateTreeStatus status = run.program.OnTick(run.context, deltaTime);
            if (status == StateTreeStatus.Running)
                return false;
            run.program.OnExit(run.context, status);
            // A finished copy goes at once; in the editor (a test) Destroy is refused.
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(run.program);
            else
                UnityEngine.Object.DestroyImmediate(run.program);
            return true;
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
                {
                    GameObject view = ui.Show(row);
                    if (view != null)
                        m_Spawned.Add(view);
                }
            }
        }

        private readonly List<GameObject> m_Spawned = new List<GameObject>();

        /// <summary>
        /// THE SCREEN THIS SERVICE SHOWED (M39.2b) — held as the return value of showing it,
        /// and called. HT's rule: a domain holds what it spawned and tells it what to draw;
        /// nothing subscribes to anything. Null until the first tick has shown the spawns, or
        /// when the def spawns no view of that type.
        /// </summary>
        protected T Spawned<T>() where T : Component
        {
            for (int i = m_Spawned.Count - 1; i >= 0; i--)
            {
                if (m_Spawned[i] == null)
                {
                    m_Spawned.RemoveAt(i);
                    continue;
                }
                T view = m_Spawned[i].GetComponentInChildren<T>(true);
                if (view != null)
                    return view;
            }
            return null;
        }

        private Dictionary<string, object> Board()
        {
            return m_Scope != null && m_Scope.Context != null
                ? m_Scope.Context.blackboard
                : null;
        }
        private bool m_Started;
    }
}
