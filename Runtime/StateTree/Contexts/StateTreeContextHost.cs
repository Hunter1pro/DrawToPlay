using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Which rung of the context spine a host is (brief §3.1). The three are a
    /// HIERARCHY, not three flavors of the same thing: Root state outlives levels, Level state
    /// outlives players, and resolution walks that ladder upward.</summary>
    public enum StateTreeContextKind
    {
        Root,
        Level,
        Player
    }

    /// <summary>
    /// One rung of the context spine (brief §3.1): Root → Level → Player/GameMode, the thing
    /// every tree and graph reaches through. A host is three ideas in one component, and each is
    /// deliberately thin:
    ///
    /// A SCOPE OF STATE. <see cref="Context"/> is a plain <see cref="StateTreeContext"/> whose
    /// blackboard IS the scope — Root = global, Level = per-level, Player = per-player. Level
    /// swap is therefore not a feature: destroying the old host and enabling a new one is a
    /// fresh dictionary, while Root, living on a persistent object, keeps its entries. Nothing
    /// here is a singleton and nothing survives by magic — persistence is the scene's business.
    ///
    /// A TREE MOUNT. The host runs its own optional <see cref="tree"/> IN its own context, so
    /// the states of that tree read and write the scope's blackboard directly. That tree is the
    /// context's orchestration — the game mode as a tree list, and the §3.7 service entries
    /// ("InventoryService state on PlayerContext") are its states; mounting several service
    /// trees is a RunSubTreeTask per service inside it, which is the wiring-by-trees rule
    /// rather than a service registry growing an API. C# service classes exist too, but only
    /// as atoms (<see cref="StateTreeServiceBehaviour"/>): looked up through the spine, never
    /// orchestrating it.
    ///
    /// AN ADDRESS. <see cref="Resolve"/> finds the host a piece of running behavior means by
    /// walking the transform hierarchy upward first — parenting IS the multiplayer split: two
    /// Player hosts side by side each own the units under them — and only then falling back to
    /// the registry, where a match must be UNIQUE to count: guessing between two levels would
    /// wire behavior to whichever loaded first, which is the kind of bug that ships.
    /// <see cref="contextId"/> exists for callers that must name a sibling from outside its
    /// branch ("p2"). All resolution is by kind + optional id — never by type, never by name.
    /// </summary>
    public sealed class StateTreeContextHost : MonoBehaviour
    {
        public StateTreeContextKind kind = StateTreeContextKind.Level;

        /// <summary>Optional disambiguator for siblings of one kind — "p1"/"p2". Empty matches
        /// any requested id; a non-empty id only matches itself.</summary>
        public string contextId = "";

        /// <summary>The context's own orchestration tree — game mode, service entries. Optional:
        /// a host with no tree is still a scope and an address.</summary>
        public StateTreeAsset tree;

        /// <summary>Overrides for the tree's declared parameters (the M7h id-bound rows) — how a
        /// scene parameterises the mount, exactly as <see cref="StateTreeRunner.parameterOverrides"/>
        /// does for a free-standing runner.</summary>
        public List<GraphTaskParameterOverride> parameterOverrides =
            new List<GraphTaskParameterOverride>();

        public bool autoStart = true;

        private StateTreeContext m_Context;
        private StateTreeExecutor m_Executor;
        private bool m_Started;

        private static readonly List<StateTreeContextHost> s_Registered =
            new List<StateTreeContextHost>();

        /// <summary>The scope's state. Created on first touch and kept for the component's
        /// lifetime, so an atom that resolves the host before its tree starts still writes the
        /// same dictionary the tree later reads.</summary>
        public StateTreeContext Context
        {
            get
            {
                if (m_Context == null)
                    m_Context = new StateTreeContext(gameObject);
                return m_Context;
            }
        }

        public bool isRunning => m_Executor != null && m_Executor.isRunning;

        public string activeNodeId => m_Executor != null ? m_Executor.activeNodeId : "";

        // --- lifecycle ------------------------------------------------------------------

        private void Start()
        {
            m_Started = true;
            if (autoStart && tree != null)
                StartTree();
        }

        private void OnEnable()
        {
            Register();
            // Re-enabling a host that had started brings its services back; the FIRST start
            // stays in Start so a scene's hosts finish registering before any tree runs.
            if (m_Started && autoStart && tree != null && !isRunning)
                StartTree();
        }

        /// <summary>A disabled scope takes its services down WITH it — the running tree's tasks
        /// get OnExit(Cancelled), which is the library's teardown contract. State (the
        /// blackboard) survives disable; only behavior stops.</summary>
        private void OnDisable()
        {
            Unregister();
            StopTree();
        }

        private void OnDestroy()
        {
            Unregister();
            StopTree();
        }

        public void StartTree()
        {
            if (tree == null)
                return;

            StateTreeExecutor executor = EnsureExecutor();
            executor.data = tree;
            executor.parameterOverrides = parameterOverrides;
            executor.context = Context;
            executor.owner = gameObject;
            executor.StartTree();
        }

        public void StopTree()
        {
            if (m_Executor != null)
                m_Executor.StopTree();
        }

        private void Update()
        {
            TickTree(Time.deltaTime);
        }

        /// <summary>One tick of the context's own tree — public for headless tests, like the
        /// runner's.</summary>
        public void TickTree(float deltaTime)
        {
            if (m_Executor != null)
                m_Executor.TickTree(deltaTime);
        }

        private StateTreeExecutor EnsureExecutor()
        {
            if (m_Executor != null)
                return m_Executor;

            m_Executor = new StateTreeExecutor
            {
                logLabel = "StateTreeContextHost",
                logContext = this
            };
            return m_Executor;
        }

        // --- registry + resolution ------------------------------------------------------

        /// <summary>Public rather than lifecycle-only so EditMode tests (where plain
        /// MonoBehaviour callbacks are not guaranteed) and manual wiring can do exactly what
        /// OnEnable does. Idempotent.</summary>
        public void Register()
        {
            if (!s_Registered.Contains(this))
                s_Registered.Add(this);
        }

        public void Unregister()
        {
            s_Registered.Remove(this);
        }

        /// <summary>
        /// The host a caller at <paramref name="from"/> means by "the <paramref name="kind"/>
        /// context". Hierarchy first — an ancestor host is unambiguous and is how two Player
        /// hosts coexist — then the registry, then a scene scan for the registry-cold case
        /// (EditMode tests); in both fallbacks a match only counts when it is UNIQUE, because
        /// guessing between siblings would bind behavior to load order. Ambiguity warns and
        /// returns null: parent the caller, or give the hosts ids.
        /// </summary>
        public static StateTreeContextHost Resolve(GameObject from, StateTreeContextKind kind,
            string contextId = null)
        {
            for (Transform walk = from != null ? from.transform : null; walk != null;
                walk = walk.parent)
            {
                StateTreeContextHost found = MatchOn(walk, kind, contextId);
                if (found != null)
                    return found;
            }

            StateTreeContextHost unique = UniqueMatch(s_Registered, kind, contextId, out int seen);
            if (unique != null)
                return unique;
            if (seen == 0)
            {
                StateTreeContextHost[] scene = UnityEngine.Object
                    .FindObjectsByType<StateTreeContextHost>(FindObjectsInactive.Exclude);
                unique = UniqueMatch(scene, kind, contextId, out seen);
                if (unique != null)
                    return unique;
            }

            // Once per address, not per call: conditions resolve every tick, and a hundred
            // copies of one wiring error would bury it.
            if (seen > 1 && s_WarnedAmbiguous.Add(kind + "|" + (contextId ?? "")))
            {
                Debug.LogWarning("StateTreeContextHost: '" + kind + "' is ambiguous from '"
                    + (from != null ? from.name : "(null)") + "' — " + seen + " hosts match. "
                    + "Parent the caller under the one it means, or set contextId.");
            }
            return null;
        }

        private static readonly HashSet<string> s_WarnedAmbiguous = new HashSet<string>();

        /// <summary>Nearest host of ANY kind at or above <paramref name="from"/> — the parent
        /// chain's step, hierarchy only: a fallback that jumped scopes here would make "my
        /// context" depend on what else the scene contains.</summary>
        public static StateTreeContextHost ResolveNearest(GameObject from)
        {
            for (Transform walk = from != null ? from.transform : null; walk != null;
                walk = walk.parent)
            {
                StateTreeContextHost[] hosts = walk.GetComponents<StateTreeContextHost>();
                for (int i = 0; i < hosts.Length; i++)
                {
                    if (hosts[i] != null && hosts[i].isActiveAndEnabled)
                        return hosts[i];
                }
            }
            return null;
        }

        /// <summary>The next rung up: the nearest host strictly above this one, or — when the
        /// hierarchy runs out below Root — the unique Root, so a detached Player still reaches
        /// global state. Root itself has no parent.</summary>
        public StateTreeContextHost ParentHost
        {
            get
            {
                StateTreeContextHost above = transform.parent != null
                    ? ResolveNearest(transform.parent.gameObject)
                    : null;
                if (above != null)
                    return above;
                if (kind == StateTreeContextKind.Root)
                    return null;
                StateTreeContextHost root = UniqueMatch(s_Registered, StateTreeContextKind.Root,
                    null, out _);
                return root != this ? root : null;
            }
        }

        private static StateTreeContextHost MatchOn(Transform at, StateTreeContextKind kind,
            string contextId)
        {
            StateTreeContextHost[] hosts = at.GetComponents<StateTreeContextHost>();
            for (int i = 0; i < hosts.Length; i++)
            {
                StateTreeContextHost host = hosts[i];
                if (host != null && host.isActiveAndEnabled && host.Matches(kind, contextId))
                    return host;
            }
            return null;
        }

        private static StateTreeContextHost UniqueMatch(IReadOnlyList<StateTreeContextHost> hosts,
            StateTreeContextKind kind, string contextId, out int seen)
        {
            StateTreeContextHost match = null;
            seen = 0;
            for (int i = 0; i < hosts.Count; i++)
            {
                StateTreeContextHost host = hosts[i];
                if (host == null || !host.isActiveAndEnabled || !host.Matches(kind, contextId))
                    continue;
                ++seen;
                match = host;
            }
            return seen == 1 ? match : null;
        }

        /// <summary>An empty <see cref="contextId"/> on the HOST matches any request; a host
        /// that names itself only answers to that name or to a caller that asked for no name in
        /// particular.</summary>
        private bool Matches(StateTreeContextKind requestedKind, string requestedId)
        {
            if (kind != requestedKind)
                return false;
            if (string.IsNullOrEmpty(requestedId))
                return true;
            return string.Equals(contextId, requestedId, StringComparison.Ordinal);
        }

        // --- services -------------------------------------------------------------------

        private readonly Dictionary<Type, Component> m_Services = new Dictionary<Type, Component>();

        /// <summary>Registered under its CONCRETE type; last registration of a type wins, which
        /// a re-enabled service relies on. Services are atoms (§3.7) — this dictionary is a
        /// phone book, not a container: no construction, no dependencies, no lifetime.</summary>
        public void RegisterService(Component service)
        {
            if (service != null)
                m_Services[service.GetType()] = service;
        }

        public void UnregisterService(Component service)
        {
            if (service == null)
                return;
            if (m_Services.TryGetValue(service.GetType(), out Component held) && held == service)
                m_Services.Remove(service.GetType());
        }

        /// <summary>
        /// A service visible from this scope: own registrations first (exact type, then
        /// assignable — so a lookup by base class or interface finds the concrete registration),
        /// then the parent chain, ending at Root. A Player asking for a global service simply
        /// asks; where it lives is the wiring's business, not the caller's.
        /// </summary>
        public T GetService<T>() where T : class
        {
            StateTreeContextHost walk = this;
            int guard = 0;
            while (walk != null && ++guard < 32)
            {
                if (walk.m_Services.TryGetValue(typeof(T), out Component exact) && exact != null)
                    return exact as T;
                foreach (KeyValuePair<Type, Component> entry in walk.m_Services)
                {
                    if (entry.Value is T assignable)
                        return assignable;
                }
                walk = walk.ParentHost;
            }
            return null;
        }

        /// <summary>The one-call form behavior uses: "the nearest scope's view of T". Nearest
        /// host from <paramref name="from"/> (any kind), else the unique Root — a caller under
        /// no context at all still sees global services.</summary>
        public static T FindService<T>(GameObject from) where T : class
        {
            StateTreeContextHost host = ResolveNearest(from);
            if (host == null)
                host = Resolve(from, StateTreeContextKind.Root);
            return host != null ? host.GetService<T>() : null;
        }
    }
}
