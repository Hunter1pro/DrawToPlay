using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The additive level machinery (M16), placed on the persistent ROOT: only Root's scene
    /// never unloads; a level is an additively loaded scene carrying its own Level host and
    /// WorldService, so the registry of what exists dies with the level — no cleanup code,
    /// and no GameObject-lifecycle coupling between the rungs (the Level host chains to Root
    /// through the registry, not through parenting).
    ///
    /// The transition is the Unreal sequence under a veil: raise <see cref="LoadingKey"/> on
    /// the Root scope (the loading overlay is a Root overlay watching that key — no
    /// transition scene), unload the current level, load the next, seed the entry's
    /// parameters onto the fresh Level scope, announce <see cref="CurrentKey"/>, drop the
    /// veil. Async is UniTask — the project standard — cancelled by this component's
    /// destruction, so a torn-down Root never leaves a transition running into a dead scene.
    ///
    /// THE LAYERING (the re-brief, stated where it is implemented): this service owns the
    /// session's travel POLICY as C# verbs — <see cref="RequestLevel"/>,
    /// <see cref="EnterExpedition"/> (which remembers the way back — <see cref="returnLevel"/>
    /// is SERVICE state, not blackboard choreography), <see cref="ReturnFromExpedition"/>.
    /// Scene furniture and UI call the verbs; the verbs write ONE key
    /// (<see cref="GotoKey"/>, the service's inbox); the SESSION TREE stays pure state
    /// handling — it only decides WHEN a pending request is served (its travel state runs the
    /// load program) and WHERE the session is afterwards. Trees and task graphs reach the same
    /// verbs through thin atoms (<see cref="EnterExpeditionTask"/>,
    /// <see cref="ReturnFromExpeditionTask"/>, <see cref="LoadLevelTask"/>).
    /// </summary>
    [ServiceActionContract(GotoAction, "value = level name — the session travels there")]
    [ServiceActionContract(ExpeditionAction,
        "value = level name — travel there and remember the way back")]
    [ServiceActionContract(ReturnAction, "no value — go back where the expedition started")]
    public sealed class LevelService : StateTreeService
    {

        /// <summary>Built by its scope's installer (M33): which level is up, and the inbox
        /// everything that wants to travel writes to.</summary>
        public LevelService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
        }

        /// <summary>Root-scope key holding the incoming level's label while a transition
        /// runs; absent otherwise. Views watch it; trees may too.</summary>
        public const string LoadingKey = "level:loading";

        /// <summary>Root-scope key naming the level currently up (the entry's name — the
        /// wiring-facing string, as everywhere).</summary>
        public const string CurrentKey = "level:current";

        /// <summary>Root-scope key holding a REQUESTED level's name — this service's inbox.
        /// Everything that wants to travel writes it (portals and UI through the verbs
        /// below, the dev picker directly); the session tree's travel state serves it and
        /// consumes it. One key, one mechanism.</summary>
        public const string GotoKey = "level:goto";

        /// <summary>The declared travel action — <see cref="RequestLevel"/> as something a
        /// caller can ASK for by name (M33's §4g request table) rather than only by holding
        /// this class. A def that declares it names the level catalog beside it, so a request
        /// for a level nobody has is refused at the door with the name in the message.</summary>
        public const string GotoAction = "level-goto";

        /// <summary>The declared form of <see cref="EnterExpedition"/>.</summary>
        public const string ExpeditionAction = "level-expedition";

        /// <summary>The declared form of <see cref="ReturnFromExpedition"/>.</summary>
        public const string ReturnAction = "level-return";

        public LevelDef current { get; private set; }

        public bool isLoading { get; private set; }

        /// <summary>Fired once a level's scene is up and its parameters are seeded — BEFORE
        /// <see cref="CurrentKey"/> announces the arrival to the tree, so everything a
        /// spawner builds from <see cref="LevelDef.objects"/> exists by the time states
        /// react. The seam manifest spawners (and future async streamers) hang from.</summary>
        public event Action<LevelDef, Scene> levelLoaded;

        /// <summary>Where <see cref="ReturnFromExpedition"/> goes — captured by
        /// <see cref="EnterExpedition"/>, spent by the return. Session policy lives HERE, in
        /// the service, so the tree never shuffles it between keys.</summary>
        public string returnLevel { get; private set; }

        private Scene m_LoadedScene;

        // ---- the session verbs -----------------------------------------------------------

        /// <summary>Ask the session to travel: writes the level's name into
        /// <see cref="GotoKey"/>. The tree decides when it is served — a request made
        /// mid-transition simply waits for the next in-state to notice it.</summary>
        public void RequestLevel(string levelName)
        {
            if (string.IsNullOrEmpty(levelName))
            {
                Debug.LogWarning("[LevelService] RequestLevel with no name — ignored.");
                return;
            }
            WriteRootKey(GotoKey, levelName);
        }

        /// <summary>Travel to an expedition level, remembering where we left from so
        /// <see cref="ReturnFromExpedition"/> can take us back. Entering an expedition FROM
        /// the expedition (or from nowhere) is refused — there would be nothing true to
        /// remember.</summary>
        public void EnterExpedition(string expeditionLevel)
        {
            string from = current != null ? current.name : null;
            if (string.IsNullOrEmpty(from)
                || string.Equals(from, expeditionLevel, StringComparison.Ordinal))
            {
                Debug.LogWarning("[LevelService] EnterExpedition refused: no current level to "
                    + "return to (or already there).");
                return;
            }
            returnLevel = from;
            RequestLevel(expeditionLevel);
        }

        /// <summary>Travel back to wherever <see cref="EnterExpedition"/> left from, spending
        /// the memory. False when there is nothing to return to.</summary>
        public bool ReturnFromExpedition()
        {
            if (string.IsNullOrEmpty(returnLevel))
            {
                Debug.LogWarning("[LevelService] ReturnFromExpedition with nothing to return "
                    + "to — ignored.");
                return false;
            }
            RequestLevel(returnLevel);
            returnLevel = null;
            return true;
        }

        /// <summary>
        /// THE VERBS, ASKED FOR (M33 §4g) — the same three the portals and the picker call
        /// directly, reachable by a declared key so a tree or a UI row can travel without a
        /// reference to this class. Nothing new happens here: each case is the verb.
        /// </summary>
        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case GotoAction:
                    RequestLevel(value);
                    break;

                case ExpeditionAction:
                    EnterExpedition(value);
                    break;

                case ReturnAction:
                    ReturnFromExpedition();
                    break;
            }
        }

        /// <summary>
        /// Run one transition; true when the level landed. One at a time: a second call
        /// while loading fails fast — the session tree's states make overlapping requests
        /// unrepresentable, and a queue here would hide a wiring mistake.
        /// </summary>
        public async UniTask<bool> LoadAsync(LevelDef level)
        {
            if (level == null || string.IsNullOrEmpty(level.scenePath))
            {
                Debug.LogError(level != null && level.content == null
                    ? $"[LevelService] catalog row '{level.name}' has no Level asset — the "
                        + "row names a level whose content file was never set."
                    : "[LevelService] LoadAsync called with no level / empty scenePath.");
                return false;
            }
            if (isLoading)
            {
                Debug.LogError($"[LevelService] already loading — '{level.name}' refused. One "
                    + "transition at a time; the session tree's states should make this "
                    + "unrepresentable.");
                return false;
            }

            // ALREADY OPEN? ADOPT IT. Asking for the level you are standing in is a real request
            // with an obvious right answer, and unloading it to load it again would throw away
            // everything that has happened in it — including, when an author presses play on a
            // level scene directly, the very scene they were working on.
            //
            // Adopting means doing everything the load does EXCEPT the loading: take ownership of
            // the scene, seed the entry parameters, announce it, and become current. Anything
            // waiting on levelLoaded then behaves identically whichever way the level arrived.
            Scene open = SceneManager.GetSceneByPath(level.scenePath);
            if (open.IsValid() && open.isLoaded && !ReferenceEquals(current, level))
            {
                m_LoadedScene = open;
                SceneManager.SetActiveScene(open);
                SeedLevelParameters(level);
                levelLoaded?.Invoke(level, open);
                current = level;
                WriteRootKey(CurrentKey, level.name);
                return true;
            }

            // THE SCOPE'S OWN TOKEN (M33): a load must stop when the scope this service
            // belongs to goes, and the scope is the object that outlives or dies with it —
            // which is what the component's destroyCancellationToken used to mean.
            CancellationToken token = scope.destroyCancellationToken;
            isLoading = true;
            WriteRootKey(LoadingKey, level.Label);
            try
            {
                if (m_LoadedScene.IsValid() && m_LoadedScene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(m_LoadedScene);
                    if (unload != null)
                        await unload.ToUniTask(cancellationToken: token);
                }
                current = null;
                ClearRootKey(CurrentKey);

                AsyncOperation load = SceneManager.LoadSceneAsync(level.scenePath,
                    LoadSceneMode.Additive);
                if (load == null)
                {
                    Debug.LogError($"[LevelService] scene '{level.scenePath}' cannot load — "
                        + "is it in Build Settings?");
                    return false;
                }
                await load.ToUniTask(cancellationToken: token);

                m_LoadedScene = SceneManager.GetSceneByPath(level.scenePath);
                if (m_LoadedScene.IsValid())
                    SceneManager.SetActiveScene(m_LoadedScene);

                SeedLevelParameters(level);
                levelLoaded?.Invoke(level, m_LoadedScene);

                current = level;
                WriteRootKey(CurrentKey, level.name);
                return true;
            }
            catch (OperationCanceledException)
            {
                // The Root died mid-transition — everything this would clean up is going
                // down with it.
                return false;
            }
            finally
            {
                ClearRootKey(LoadingKey);
                isLoading = false;
            }
        }

        /// <summary>The entry's parameters land on the LEVEL host's blackboard before
        /// anything ticks — same boxing rules as every parameter surface (String as string,
        /// Bool as 1/0 float, the rest as float), so the library atoms read them unchanged.
        /// A level scene without a Level host is a wiring error worth saying once.</summary>
        private void SeedLevelParameters(LevelDef level)
        {
            StateTreeContextHost levelHost = FindLevelHost(m_LoadedScene);
            if (levelHost == null)
            {
                Debug.LogWarning($"[LevelService] scene '{level.scenePath}' has no Level "
                    + "context host — entry parameters have nowhere to land and nothing "
                    + "scopes this level.");
                return;
            }

            var parameters = level.parameters;
            for (int i = 0; parameters != null && i < parameters.Count; i++)
            {
                GraphTaskParameter row = parameters[i];
                if (row == null || string.IsNullOrEmpty(row.name))
                    continue;
                object boxed;
                switch (row.kind)
                {
                    case GraphTaskParameterKind.String:
                        boxed = row.stringValue ?? string.Empty;
                        break;
                    case GraphTaskParameterKind.Bool:
                        boxed = row.floatValue != 0f ? 1f : 0f;
                        break;
                    default:
                        boxed = row.floatValue;
                        break;
                }
                levelHost.Context.blackboard[row.name] = boxed;
            }
        }

        private static StateTreeContextHost FindLevelHost(Scene scene)
        {
            if (!scene.IsValid())
                return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var host = roots[i].GetComponentInChildren<StateTreeContextHost>(true);
                if (host != null && host.kind == StateTreeContextKind.Level)
                    return host;
            }
            return null;
        }

        private void WriteRootKey(string key, string value)
        {
            if (scope != null && scope.Context != null)
                scope.Context.blackboard[key] = value ?? string.Empty;
        }

        private void ClearRootKey(string key)
        {
            if (scope != null && scope.Context != null)
                scope.Context.blackboard.Remove(key);
        }
    }
}
