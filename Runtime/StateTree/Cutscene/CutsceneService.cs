using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE DIRECTOR (M27) — the def-only subsystem that casts a scene, runs its script and
    /// gives the controls back.
    ///
    /// It owns three things and nothing else: WHO is in the scene (roles resolved by tag,
    /// once, at the start), WHERE the script runs (a director host it makes and destroys, so
    /// the beats are an ordinary tree with an ordinary blackboard), and WHETHER the game is
    /// under its control (a key on the root board plus a standing tag on the actors it took
    /// over). Everything the scene DOES is beats, and every beat is somebody else's ability —
    /// see DirectTask.
    ///
    /// HT's player did the same job with a PlayableDirector, and its shape survives the port
    /// intact: bind the parts by tag at play time, take the controls, play, put back what the
    /// scene moved, write the scene off so it never plays twice.
    /// </summary>
    [AddComponentMenu("Draw To Play/Services/Cutscene Service")]
    [ServiceActionContract(PlayAction, "value = cutscene row name")]
    [ServiceActionContract(SkipAction, "value ignored — ends whatever is playing")]
    public sealed class CutsceneService : StateTreeServiceBehaviour
    {
        public const string PlayAction = "cutscene";

        public const string SkipAction = "cutscene-skip";

        [Tooltip("The declaration this service runs: scope and the cutscene registry.")]
        public ServiceDef definition;

        protected override ServiceDef FlowSource => definition;

        public CutsceneRegistry cutscenes =>
            definition != null ? definition.registry as CutsceneRegistry : null;

        /// <summary>What is playing, or null. Diagnostics and the skip path — a scene is a
        /// mode, and a mode has exactly one holder.</summary>
        public CutsceneDef playing => m_Playing;

        private CutsceneDef m_Playing;
        private StateTreeContextHost m_Director;
        private string m_PlacementId = "";
        private readonly List<AbilityHost> m_Held = new List<AbilityHost>();

        protected override void OnRequest(ServiceRequest request, string value)
        {
            switch (request.action)
            {
                case PlayAction:
                    Play(value);
                    break;
                case SkipAction:
                    Finish(skipped: true);
                    break;
            }
        }

        /// <summary>
        /// Cast a scene and start it. The placement id is what gets written off when it ends,
        /// so the same row placed twice is spent twice — one-shot-ness belongs to the PLACE,
        /// not to the script.
        /// </summary>
        /// <param name="stageHint">Something standing IN the level the scene belongs to —
        /// the trigger that asked, usually. It matters because "the level" is not a thing this
        /// service can look up from where it lives: it sits on the ROOT, and Level-kind hosts
        /// are plentiful (every spawned exit and timber stand runs its own tree on one), so
        /// asking the spine for "the level" from up here is ambiguous and answers nobody.
        /// Walking UP from something inside it always answers.</param>
        public CutsceneResult Play(string cutsceneName, string placementId = "",
            GameObject stageHint = null)
        {
            var result = new CutsceneResult { cutsceneName = cutsceneName ?? "" };

            if (m_Playing != null)
                return Refuse(result, "'" + m_Playing.name + "' is already playing");

            CutsceneRegistry catalog = cutscenes;
            result.cutscene = catalog != null && !string.IsNullOrEmpty(cutsceneName)
                ? catalog.FindByName(cutsceneName) as CutsceneDef
                : null;
            if (result.cutscene == null)
                return Refuse(result, "no cutscene named '" + result.cutsceneName + "'");
            if (result.cutscene.beats == null)
                return Refuse(result, "'" + result.cutsceneName + "' has no script");

            StateTreeContextHost stage = Stage(stageHint);
            if (stage == null)
                return Refuse(result, "no level to play it in");

            // THE CAST, RESOLVED ONCE. A beat that searched every tick would find a different
            // actor halfway through a scene; the whole point of naming roles is that "the
            // keeper" means one body for the length of the moment.
            var cast = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
            WorldService world = StateTreeContextHost.FindService<WorldService>(stage.gameObject);
            for (int i = 0; i < result.cutscene.cast.Count; i++)
            {
                CutsceneRole role = result.cutscene.cast[i];
                if (role == null || string.IsNullOrEmpty(role.role))
                    continue;
                WorldObjectBehaviour actor = world != null && !string.IsNullOrEmpty(role.tag)
                    ? world.FindNearest(role.tag, stage.transform.position, 0f)
                    : null;
                if (actor == null)
                {
                    if (role.required)
                        return Refuse(result, "nobody here is '" + role.tag + "' for the part "
                            + "of " + role.role);
                    continue;
                }
                cast[role.role] = actor.gameObject;
            }

            m_Playing = result.cutscene;
            m_PlacementId = placementId ?? "";

            // THE STAGE IS A HOST, so the script is an ordinary tree: its beats read an
            // ordinary blackboard, the executor debugs it like any other, and the director
            // stays a thing that starts and stops rather than a second runtime.
            var stageObject = new GameObject("Cutscene · " + result.cutscene.name);
            stageObject.transform.SetParent(stage.transform, false);
            m_Director = stageObject.AddComponent<StateTreeContextHost>();
            m_Director.kind = StateTreeContextKind.Level;
            m_Director.autoStart = false;
            m_Director.tree = result.cutscene.beats;
            m_Director.treeFinished += OnScriptFinished;

            // THE CAST GOES ON THE BOARD BEFORE THE CURTAIN, and the order is the whole scene:
            // StartTree enters the first beat immediately, so a cast published afterwards
            // arrives one moment too late — every beat looked for its actor, found an empty
            // board, warned, and completed, which from the outside was a scene that played
            // perfectly in zero seconds with nobody in it. The same lesson as every other
            // "published after the reader ran" bug in this project, in its newest disguise.
            foreach (KeyValuePair<string, GameObject> part in cast)
                m_Director.Context.blackboard[part.Key] = part.Value;

            m_Director.StartTree();

            if (result.cutscene.takesControl)
                TakeControl(cast);

            Board()?.Add(CutsceneKeys.Playing, result.cutscene.name);
            result.line = "playing " + Title(result.cutscene);
            Announce(CutsceneResult.Key, result);
            return result;
        }

        /// <summary>
        /// End it — the same path whether the script ran out or the player pressed skip, which
        /// is the only way skip is ever correct: anything a scene must leave behind is left by
        /// the beats' own exits, and both roads run them.
        /// </summary>
        public void Finish(bool skipped)
        {
            if (m_Playing == null)
                return;

            var result = new CutsceneResult
            {
                cutscene = m_Playing,
                cutsceneName = m_Playing.name,
                played = !skipped,
                skipped = skipped,
                line = (skipped ? "skipped " : "") + Title(m_Playing)
            };

            if (m_Director != null)
            {
                m_Director.treeFinished -= OnScriptFinished;
                // StopTree first: exiting the beats is what cancels what they started on the
                // cast, and destroying the host without it would leave an actor mid-stride.
                m_Director.StopTree();
                DestroyStage(m_Director.gameObject);
            }
            m_Director = null;

            GiveControlBack();

            Dictionary<string, object> board = Board();
            board?.Remove(CutsceneKeys.Playing);

            // WRITTEN OFF, so a reload does not replay it — the same sentence the felled tree
            // and the taken pickup are written off with.
            if (m_Playing.playsOnce && !string.IsNullOrEmpty(m_PlacementId))
                spent?.Invoke(m_PlacementId);

            m_Playing = null;
            m_PlacementId = "";
            Announce(CutsceneResult.Key, result);
        }

        /// <summary>A placement's scene is over and should not come back. The SAVE lives in
        /// the game's own progress service, which is example-side, so the director says what
        /// happened and lets whoever owns the file write it down.</summary>
        public event System.Action<string> spent;

        private void OnScriptFinished()
        {
            Finish(skipped: false);
        }

        /// <summary>
        /// Hold the actors the scene is using — a standing tag, so their land verbs refuse
        /// without a single task learning that cutscenes exist (the M26 tag idiom, second
        /// home). The player's own tree reads the mode key and parks itself in its watching
        /// state; this is the half that stops an ability already in flight from firing.
        /// </summary>
        private void TakeControl(Dictionary<string, GameObject> cast)
        {
            m_Held.Clear();
            foreach (KeyValuePair<string, GameObject> part in cast)
            {
                var host = part.Value != null ? part.Value.GetComponent<AbilityHost>() : null;
                if (host == null)
                    continue;
                host.AddTag(CutsceneKeys.WatchingTag);
                m_Held.Add(host);
            }

            StateTreeContextHost player = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Player);
            var playerHost = player != null ? player.GetComponent<AbilityHost>() : null;
            if (playerHost != null && !m_Held.Contains(playerHost))
            {
                playerHost.AddTag(CutsceneKeys.WatchingTag);
                m_Held.Add(playerHost);
            }
        }

        private void GiveControlBack()
        {
            for (int i = 0; i < m_Held.Count; i++)
            {
                if (m_Held[i] != null)
                    m_Held[i].RemoveTag(CutsceneKeys.WatchingTag);
            }
            m_Held.Clear();
        }

        /// <summary>
        /// Where a scene is played: the LEVEL, because its cast, its camera and the services
        /// its beats reach for all live and die there.
        ///
        /// Found by walking up from something that is IN it — the asker first, the player
        /// second — rather than by asking for "the level" in the abstract, which this project
        /// cannot answer: a Level-kind host is what every spawned thing with a tree of its own
        /// runs on, so the kind is plentiful and the question is ambiguous.
        /// </summary>
        private StateTreeContextHost Stage(GameObject hint)
        {
            StateTreeContextHost stage = hint != null
                ? StateTreeContextHost.Resolve(hint, StateTreeContextKind.Level)
                : null;
            if (stage != null)
                return stage;

            StateTreeContextHost player = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Player);
            return player != null
                ? StateTreeContextHost.Resolve(player.gameObject, StateTreeContextKind.Level)
                : null;
        }

        private CutsceneResult Refuse(CutsceneResult result, string why)
        {
            result.refusal = why;
            result.line = why;
            Announce(CutsceneResult.Key, result);
            Debug.LogWarning("[Cutscene] " + why + ".", this);
            return result;
        }

        private static string Title(CutsceneDef row)
        {
            return row == null ? "" : string.IsNullOrEmpty(row.displayName)
                ? row.name : row.displayName;
        }

        /// <summary>The stage comes down the same way the tree's own runtime copies do —
        /// deferred in play, at once outside it, so a scene torn down in an editor preview or
        /// a headless test is not an error the runtime prints.</summary>
        private static void DestroyStage(GameObject stage)
        {
            if (stage == null)
                return;
            if (Application.isPlaying)
                Destroy(stage);
            else
                DestroyImmediate(stage);
        }

        private Dictionary<string, object> Board()
        {
            StateTreeContextHost root = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Root);
            return root != null && root.Context != null ? root.Context.blackboard : null;
        }
    }
}
