using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// "YOU — DO THAT" (M27): the one task a script needs that a mind never does.
    ///
    /// Every acting task in this toolset acts on the body its tree runs on —
    /// <see cref="ActivateAbilityTask"/> says so in its own summary, and the movers, the
    /// clips and the swings are all [InjectOwner]. That is exactly right for a MIND and
    /// exactly wrong for a SCRIPT, which commands several bodies at once. Three ways out were
    /// available: give every task an actor field (touching the whole library, and fighting an
    /// injection that resolves once at bind time), re-host each beat, or make beats ABILITIES
    /// — which actors already run on themselves, with cues, tags, cancellation and a finish
    /// signal included.
    ///
    /// This is the third, implemented as the second: the beat names a ROLE, the director's
    /// board says which body that is, and the ability runs on THAT actor's own host. So a new
    /// beat is a new ability ROW, not a new class, and anything an actor can do in the game it
    /// can do in a scene — with no cinematic-only copy of a verb anywhere.
    ///
    /// A STATE OF THESE IS A TIMELINE COLUMN. Several in one state are several actors acting
    /// at once; <c>blocking</c> decides which of them the beat waits for, exactly as it does
    /// everywhere else.
    /// </summary>
    [StateTreeCategory("Tasks/Cutscene", "Have a cast member perform an ability")]
    public sealed class DirectTask : StateTreeTaskAsset
    {
        [Tooltip("Whose beat this is — a role from the cutscene's cast, resolved to a body "
            + "before the script started.")]
        [StateTreeKey(StateTreeKeyKind.Object, any: true)]
        public StateTreeKeyField role = new StateTreeKeyField("hero");

        [Tooltip("What they do. Any ability row — the same catalog the game plays from.")]
        public StateTreeEntryRef<AbilityDef> ability = new StateTreeEntryRef<AbilityDef>();

        [Tooltip("Hold the beat open until they finish. Off starts them and moves on, which "
            + "is how two actors overlap.")]
        public bool waitForFinish = true;

        [Tooltip("Optional: a role to hand the ability as its target — 'strike the raider', "
            + "'look at the hero'.")]
        [StateTreeKey(StateTreeKeyKind.Object, any: true)]
        public StateTreeKeyField targetRole = new StateTreeKeyField();

        [System.NonSerialized] private AbilityHost m_Actor;
        [System.NonSerialized] private bool m_Finished;
        [System.NonSerialized] private bool m_Started;

        public override void OnEnter(StateTreeContext context)
        {
            m_Actor = null;
            m_Finished = false;
            m_Started = false;

            GameObject body = Body(context, role);
            if (body == null)
            {
                // A missing part is a LEGIBLE refusal, never a null: a scene whose keeper died
                // three quests ago should say so once and carry on with the rest of its beats.
                Debug.LogWarning("[Cutscene] no actor is cast as '" + (string)role
                    + "' — that beat is skipped.");
                return;
            }

            m_Actor = body.GetComponent<AbilityHost>();
            if (m_Actor == null)
            {
                Debug.LogWarning("[Cutscene] '" + body.name + "' has no AbilityHost, so it "
                    + "cannot perform '" + ability.entryName + "'.", body);
                return;
            }

            m_Actor.abilityFinished += OnAbilityFinished;
            AbilityDef row = m_Actor.service != null
                ? m_Actor.service.Find(ability.entryName)
                : null;
            if (row == null)
            {
                Debug.LogWarning("[Cutscene] no ability row named '" + ability.entryName
                    + "' for " + body.name + ".", body);
                return;
            }

            GameObject target = Body(context, targetRole);
            m_Started = m_Actor.Activate(row, target);
            if (!m_Started)
            {
                Debug.LogWarning("[Cutscene] " + body.name + " refused '" + row.name
                    + "' — something it is doing, or a tag on the row, says not now.", body);
            }
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Actor == null || !m_Started)
                return StateTreeStatus.Success;   // nothing to wait for; the beat moves on
            if (!waitForFinish)
                return StateTreeStatus.Success;
            return m_Finished ? StateTreeStatus.Success : StateTreeStatus.Running;
        }

        /// <summary>
        /// A CANCELLED SCENE CANCELS ITS ACTORS. Skipping, or a level torn down mid-beat, must
        /// not leave a keeper walking to a well nobody is watching any more — the symmetry
        /// every mode task in this project has had to learn.
        /// </summary>
        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Actor != null)
            {
                m_Actor.abilityFinished -= OnAbilityFinished;
                if (status == StateTreeStatus.Cancelled && m_Started && !m_Finished)
                    m_Actor.Cancel();
                if (m_Started)
                    GiveThemBackToThemselves();
            }
            m_Actor = null;
            m_Started = false;
            m_Finished = false;
        }

        /// <summary>
        /// THE BEAT IS OVER, SO THE ACTOR IS THEIRS AGAIN. A directed body is the one case where
        /// an actor's own tree is not the thing that moved it: the keeper stood in 'waiting' for
        /// the whole scene, so the idle its state plays ON THE WAY IN was never played again, and
        /// a one-shot that has run out with nothing behind it leaves a character in no pose at
        /// all — the heap on the ground this fixed.
        ///
        /// Re-entering the state the actor is already in is the smallest true statement of "carry
        /// on being yourself": whatever that state asserts about the body — its idle, its speed —
        /// is asserted again, and no transition is faked to do it.
        /// </summary>
        private void GiveThemBackToThemselves()
        {
            var host = m_Actor.GetComponent<StateTreeContextHost>();
            if (host != null)
                host.ReenterActiveState();
        }

        /// <summary>However it ended — run out, cancelled by another ability, the actor
        /// downed — the beat stops waiting. A scene that hung because its actor was
        /// interrupted would be worse than one that moves on.</summary>
        private void OnAbilityFinished(AbilityDef finished, StateTreeStatus status)
        {
            m_Finished = true;
        }

        /// <summary>The body behind a role — an object on the director's board, put there when
        /// the scene was cast.</summary>
        private static GameObject Body(StateTreeContext context, StateTreeKeyField key)
        {
            string name = key;
            if (context == null || string.IsNullOrEmpty(name)
                || !context.blackboard.TryGetValue(name, out object held))
                return null;
            return held as GameObject ?? (held as Component)?.gameObject;
        }
    }
}
