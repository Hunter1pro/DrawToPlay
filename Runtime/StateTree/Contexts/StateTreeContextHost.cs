using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// as atoms (<see cref="StateTreeService"/>): looked up through the spine, never
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

        public event Action treeStarted;
        public event Action treeStopped;

        /// <summary>The tree ran off its end (M22) — re-raised from
        /// <see cref="StateTreeExecutor.treeFinished"/>, before the stop that follows it.</summary>
        public event Action treeFinished;

        public event Action<string> nodeEntered;
        public event Action<string> nodeLeft;
        public event Action<string, string> activeNodeChanged;

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

        /// <summary>
        /// EVERY SCOPE THAT IS UP (M34) — for anything that wants to SEE the spine rather than
        /// resolve one thing on it: the scope-tree window, a diagnostic, a test that asserts
        /// what a level installed. Registration already existed; only the reading was private.
        /// </summary>
        public static IReadOnlyList<StateTreeContextHost> registered => s_Registered;

        /// <summary>The subsystems this scope owns, in install order.</summary>
        public IReadOnlyList<StateTreeService> subsystems => m_Subsystems;

        /// <summary>What this scope answers for, by capability — provided instances and
        /// anything a recipe has constructed so far.</summary>
        public IEnumerable<KeyValuePair<Type, object>> provided => m_Provided;

        /// <summary>The tree this scope runs, or null.</summary>
        public StateTreeAsset runningTree => m_Executor != null ? tree : null;

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
            DisposeOwnedServices();
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
            TickSubsystems(Time.deltaTime);
            TickTree(Time.deltaTime);
        }

        /// <summary>
        /// One frame of every subsystem this scope owns, BEFORE its own tree (M33).
        ///
        /// The order is deliberate and it used to be luck: services were components, so whether
        /// a request was served before or after the tree that reads its result depended on
        /// Unity's script order. Serving first means a tree ticked in the same frame sees what
        /// the subsystems just did.
        ///
        /// Public for the same reason TickTree is: an EditMode test owns the clock.
        /// </summary>
        public void TickSubsystems(float deltaTime)
        {
            for (int i = 0; i < m_Subsystems.Count; i++)
                m_Subsystems[i]?.Tick(deltaTime);
        }

        /// <summary>One tick of the context's own tree — public for headless tests, like the
        /// runner's.</summary>
        public void TickTree(float deltaTime)
        {
            if (m_Executor != null)
                m_Executor.TickTree(deltaTime);
        }

        /// <summary>Assert this context's current state over again — what a body needs after
        /// something outside it (a cutscene beat) took its animation over, since the state that
        /// would put the idle back is the one it never left. See
        /// <see cref="StateTreeExecutor.ReenterActiveNode"/>.</summary>
        public void ReenterActiveState()
        {
            if (m_Executor != null)
                m_Executor.ReenterActiveNode();
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
            // The runner's event surface, verbatim: a tree mounted on a context is watched by
            // the same tooling (the State Tree window's play highlight above all) as a tree on
            // a runner, and an event the mount flavor lacked would make host mounts the
            // second-class option the M8 design says they are not.
            m_Executor.treeStarted += RaiseTreeStarted;
            m_Executor.treeStopped += RaiseTreeStopped;
            m_Executor.treeFinished += RaiseTreeFinished;
            m_Executor.nodeEntered += RaiseNodeEntered;
            m_Executor.nodeLeft += RaiseNodeLeft;
            m_Executor.activeNodeChanged += RaiseActiveNodeChanged;
            return m_Executor;
        }

        private void RaiseTreeStarted() => treeStarted?.Invoke();

        private void RaiseTreeStopped() => treeStopped?.Invoke();

        private void RaiseTreeFinished() => treeFinished?.Invoke();

        private void RaiseNodeEntered(string nodeId) => nodeEntered?.Invoke(nodeId);

        private void RaiseNodeLeft(string nodeId) => nodeLeft?.Invoke(nodeId);

        private void RaiseActiveNodeChanged(string from, string to)
            => activeNodeChanged?.Invoke(from, to);

        // --- registry + resolution ------------------------------------------------------

        /// <summary>Public rather than lifecycle-only so EditMode tests (where plain
        /// MonoBehaviour callbacks are not guaranteed) and manual wiring can do exactly what
        /// OnEnable does. Idempotent.</summary>
        public void Register()
        {
            if (s_Registered.Contains(this))
                return;
            s_Registered.Add(this);
            TellBodyBinders(bind: true);
        }

        public void Unregister()
        {
            if (!s_Registered.Remove(this))
                return;
            TellBodyBinders(bind: false);
        }

        /// <summary>
        /// THE BODY'S BIRTH AND DEATH, told to the services that want one (M40.3c): every
        /// <see cref="IBindsBody"/> reachable above this host whose <c>bodyKind</c> is this
        /// host's kind is bound on register and unbound on unregister. The scope is the
        /// lifetime; nothing else has to watch for a body to appear or go.
        /// </summary>
        private void TellBodyBinders(bool bind)
        {
            // A host unregistered AFTER it was destroyed (a test's teardown) has no parent to
            // walk; its services were told on the destroy itself.
            if (this == null)
                return;
            var binders = new List<IBindsBody>();
            for (StateTreeContextHost walk = ParentHost; walk != null; walk = walk.ParentHost)
                walk.CollectOwn(binders);
            for (int i = 0; i < binders.Count; i++)
            {
                if (binders[i].bodyKind != kind)
                    continue;
                if (bind)
                    binders[i].Bind(this);
                else
                    binders[i].Unbind(this);
            }
        }

        /// <summary>Every distinct <typeparamref name="T"/> provided on this host or any above
        /// it — what a service calls when it has something to tell everyone who asked (the
        /// world telling its watchers a citizen arrived). A call, not a subscription.</summary>
        public void CollectServices<T>(List<T> into) where T : class
        {
            for (StateTreeContextHost walk = this; walk != null; walk = walk.ParentHost)
                walk.CollectOwn(into);
        }

        /// <summary>Every distinct service this host provides that is a <typeparamref name="T"/>
        /// — a service provided under several names is listed once.</summary>
        private void CollectOwn<T>(List<T> into) where T : class
        {
            foreach (KeyValuePair<Type, object> entry in m_Provided)
            {
                if (entry.Value is T match && !into.Contains(match))
                    into.Add(match);
            }
            foreach (KeyValuePair<Type, Component> entry in m_Services)
            {
                if (entry.Value is T match && !into.Contains(match))
                    into.Add(match);
            }
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

        /// <summary>Installer-provided and lazily constructed instances, keyed by the
        /// CAPABILITY they were registered under — one instance may sit under several keys
        /// (register it once per interface it serves).</summary>
        private readonly Dictionary<Type, object> m_Provided = new Dictionary<Type, object>();

        /// <summary>
        /// THE INSTALL LAYER IN FLIGHT (M36.3): set by the installer for the length of one
        /// subsystem's construction, taken by <see cref="StateTreeService"/>'s constructor, and
        /// null at every other moment. It is the one piece of per-install data the (scope, def)
        /// contract has no parameter for, carried by the scope because the scope is what both
        /// sides already hold.
        /// </summary>
        internal ServiceSettingSet tuning;

        /// <summary>Type-only registrations (M15): capability → implementation to construct
        /// ON FIRST ASK, constructor-injected from this scope's view of the spine. This is
        /// what keeps installers order-free at MissionSystem scale — registration declares,
        /// the first resolve builds the graph.</summary>
        private readonly Dictionary<Type, Type> m_Recipes = new Dictionary<Type, Type>();

        /// <summary>Instances THIS host constructed that asked for disposal — swept with the
        /// host. Installer-provided instances are the installer's to dispose.</summary>
        private List<IDisposable> m_Owned;

        /// <summary>Implementations mid-construction — the cycle guard. Static is safe: Unity
        /// resolves on the main thread, and a cycle is a cycle whichever scope started it.</summary>
        private static readonly HashSet<Type> s_Constructing = new HashSet<Type>();

        /// <summary>Registered under its CONCRETE type; last registration of a type wins, which
        /// a re-enabled service relies on. Behaviour services stay a phone book — the scene
        /// constructs them; only <see cref="Provide{TInterface, TImpl}"/> recipes get the
        /// container treatment.</summary>
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

        /// <summary>Register a ready instance under a capability — the installer's verb for
        /// hand-made and plain-C# services. Call once per interface the instance serves.</summary>
        public void Provide<T>(T instance) where T : class
        {
            Provide(typeof(T), instance);
        }

        /// <summary>
        /// The same, for an installer holding a runtime Type (M33) — a def names its service by
        /// name, so the thing building it has no generic parameter to give.
        ///
        /// A SUBSYSTEM REGISTERED HERE IS OWNED HERE: it is ticked with this scope and disposed
        /// with it, which is the whole of its lifetime and the reason it needs no Unity messages
        /// of its own.
        /// </summary>
        public void Provide(Type capability, object instance)
        {
            if (capability == null || instance == null)
                return;
            m_Provided[capability] = instance;

            if (instance is StateTreeService subsystem && !m_Subsystems.Contains(subsystem))
                m_Subsystems.Add(subsystem);
            if (instance is IDisposable disposable)
            {
                m_Owned ??= new List<IDisposable>();
                if (!m_Owned.Contains(disposable))
                    m_Owned.Add(disposable);
            }
        }

        /// <summary>
        /// Let a subsystem go — it stops being ticked, stops being resolvable, and is no longer
        /// this scope's to dispose. The counterpart of Provide, for a test or a scope that is
        /// swapping one implementation for another.
        /// </summary>
        public void Forget(object instance)
        {
            if (instance == null)
                return;
            var keys = new List<Type>();
            foreach (KeyValuePair<Type, object> entry in m_Provided)
            {
                if (ReferenceEquals(entry.Value, instance))
                    keys.Add(entry.Key);
            }
            for (int i = 0; i < keys.Count; i++)
                m_Provided.Remove(keys[i]);

            if (instance is StateTreeService subsystem)
                m_Subsystems.Remove(subsystem);
            if (instance is IDisposable disposable)
                m_Owned?.Remove(disposable);
        }

        /// <summary>The subsystems this scope runs, in install order — which is dependency
        /// order, because that is the order an installer builds them in.</summary>
        private readonly List<StateTreeService> m_Subsystems = new List<StateTreeService>();

        /// <summary>Register a RECIPE: the first <see cref="GetService{T}"/> for the capability
        /// constructs <typeparamref name="TImpl"/> through its greediest public constructor,
        /// resolving every parameter from this scope's view of the spine (own scope first,
        /// then parents), and caches the instance here. Registration order never matters —
        /// the graph resolves itself on demand, cycle-guarded.</summary>
        public void Provide<TInterface, TImpl>()
            where TInterface : class
            where TImpl : class, TInterface
        {
            m_Recipes[typeof(TInterface)] = typeof(TImpl);
        }

        /// <summary>
        /// A service visible from this scope: own registrations first (provided instances,
        /// then recipes, then behaviours — exact type before assignable, so a lookup by
        /// interface finds the concrete registration), then the parent chain, ending at Root.
        /// A Player asking for a global service simply asks; where it lives is the wiring's
        /// business, not the caller's.
        /// </summary>
        public T GetService<T>() where T : class
        {
            return GetService(typeof(T)) as T;
        }

        /// <summary>The non-generic core — what the executor's ServiceRef injection and the
        /// recipe constructor share with the generic face.</summary>
        public object GetService(Type type)
        {
            if (type == null)
                return null;

            StateTreeContextHost walk = this;
            int guard = 0;
            while (walk != null && ++guard < 32)
            {
                object hit = walk.ResolveOwn(type);
                if (hit != null)
                    return hit;
                walk = walk.ParentHost;
            }
            return null;
        }

        private object ResolveOwn(Type type)
        {
            if (m_Provided.TryGetValue(type, out object provided) && provided != null)
                return provided;
            if (m_Recipes.TryGetValue(type, out Type impl))
                return Construct(type, impl);
            if (m_Services.TryGetValue(type, out Component exact) && exact != null)
                return exact;
            foreach (KeyValuePair<Type, Component> entry in m_Services)
            {
                if (entry.Value != null && type.IsInstanceOfType(entry.Value))
                    return entry.Value;
            }
            return null;
        }

        /// <summary>
        /// The whole container, in one method: greediest public constructor, each parameter
        /// resolved through THIS host's chain (the registering scope — a Level recipe may
        /// depend on Root services, never sideways), instance cached under the capability and
        /// owned for disposal when it asks. Every failure is ONE error naming the service and
        /// what it hungered for, and resolves to null — a boot tree can route that to an
        /// error state; a hidden throw mid-scene-load cannot.
        /// </summary>
        private object Construct(Type key, Type impl)
        {
            if (!s_Constructing.Add(impl))
            {
                Debug.LogError($"[{name}] service recipe cycle: {impl.Name} is already being "
                    + "constructed further up this resolve — breaking the loop with null.", this);
                return null;
            }

            try
            {
                var constructors = impl.GetConstructors();
                if (constructors.Length == 0)
                {
                    Debug.LogError($"[{name}] service recipe {impl.Name} has no public "
                        + "constructor.", this);
                    return null;
                }
                ConstructorInfo constructor = constructors[0];
                for (int i = 1; i < constructors.Length; i++)
                {
                    if (constructors[i].GetParameters().Length
                        > constructor.GetParameters().Length)
                        constructor = constructors[i];
                }

                ParameterInfo[] parameters = constructor.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    arguments[i] = GetService(parameters[i].ParameterType);
                    if (arguments[i] == null)
                    {
                        Debug.LogError($"[{name}] constructing {impl.Name} for "
                            + $"{key.Name}: nothing on the spine provides "
                            + $"{parameters[i].ParameterType.Name} (parameter "
                            + $"'{parameters[i].Name}').", this);
                        return null;
                    }
                }

                object instance = constructor.Invoke(arguments);
                m_Provided[key] = instance;
                if (instance is IDisposable disposable)
                    (m_Owned ?? (m_Owned = new List<IDisposable>())).Add(disposable);
                return instance;
            }
            finally
            {
                s_Constructing.Remove(impl);
            }
        }

        /// <summary>Constructed-and-owned services die with their scope. Called from the
        /// lifecycle OnDestroy rather than OnDisable: a disabled host keeps its instances
        /// for re-enable; destruction is the end of the scope's life. Public and idempotent
        /// for the reason Register is — EditMode tests do what the lifecycle would.</summary>
        public void DisposeOwnedServices()
        {
            if (m_Owned == null)
                return;
            for (int i = 0; i < m_Owned.Count; i++)
            {
                try
                {
                    m_Owned[i]?.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{name}] service Dispose threw: {e.Message}", this);
                }
            }
            m_Owned.Clear();
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

        /// <summary>The non-generic twin, for reflection-driven callers (the field
        /// injector).</summary>
        public static object FindService(Type type, GameObject from)
        {
            StateTreeContextHost host = ResolveNearest(from);
            if (host == null)
                host = Resolve(from, StateTreeContextKind.Root);
            return host != null ? host.GetService(type) : null;
        }
    }
}
