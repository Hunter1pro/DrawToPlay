using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// START AN ABILITY from a state tree (M23) — the bridge between an actor's MIND (its
    /// tree: when to act) and the ability service (what acting is). The row is PICKED, not
    /// typed: list the ability registry in the tree's Data section and ⛃ offers its rows.
    ///
    /// With <see cref="waitForFinish"/> the task is latent for the ability's whole run —
    /// Success when it finished, Failure when it was cancelled out from under us or refused —
    /// so a state can put "recover" after "swing" and mean it. Without it, the task fires and
    /// forgets: Success the moment the activation is accepted.
    /// </summary>
    [StateTreeCategory("Tasks/Abilities", "Activate an ability on the owner's AbilityHost")]
    public sealed class ActivateAbilityTask : StateTreeTaskAsset
    {
        [Tooltip("The ability row — picked from the registry the tree lists in Data.")]
        public StateTreeEntryRef<AbilityDef> ability = new StateTreeEntryRef<AbilityDef>();

        [Tooltip("On: Running until the ability ends — Success if it finished, Failure if it "
            + "was cancelled. Off: Success as soon as the activation is accepted.")]
        public bool waitForFinish = true;

        [Tooltip("A REFUSED activation (cooldown, blocked, already active) fails the task by "
            + "default, so the state can route it. On: keep retrying every tick instead — "
            + "for a state that queues its swing until the gate opens.")]
        public bool retryWhileRefused;

        [InjectOwner] private AbilityHost m_Host;

        private AbilityDef m_Started;
        private bool m_Ended;
        private StateTreeStatus m_EndStatus;

        public override void OnEnter(StateTreeContext context)
        {
            m_Started = null;
            m_Ended = false;
            TryActivate();
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Host == null)
                return StateTreeStatus.Failure;

            if (m_Started == null)
            {
                if (!retryWhileRefused)
                    return StateTreeStatus.Failure;
                TryActivate();
                return m_Started == null ? StateTreeStatus.Running
                    : waitForFinish ? StateTreeStatus.Running : StateTreeStatus.Success;
            }

            if (!waitForFinish)
                return StateTreeStatus.Success;
            if (!m_Ended)
                return StateTreeStatus.Running;
            return m_EndStatus == StateTreeStatus.Success
                ? StateTreeStatus.Success
                : StateTreeStatus.Failure;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Host != null)
                m_Host.abilityFinished -= OnAbilityFinished;
            // The state leaving does NOT cancel the ability: an ability outliving the state
            // that started it is the fire-and-forget case working, and a state that wants
            // the tie has waitForFinish.
        }

        private void TryActivate()
        {
            if (m_Host == null)
                return;
            AbilityService service = m_Host.service;
            AbilityDef def = service != null ? service.Find(ability.entryName) : null;
            if (def == null)
            {
                if (service != null && !string.IsNullOrEmpty(ability.entryName))
                    Debug.LogError("[ActivateAbility] no ability row named '"
                        + ability.entryName + "'.", m_Host);
                return;
            }

            // Subscribe BEFORE activating: an ability with no tree finishes inside Activate,
            // and a listener attached afterwards would wait forever for a finish that
            // already happened.
            m_Host.abilityFinished += OnAbilityFinished;
            if (m_Host.Activate(def))
            {
                m_Started = def;
            }
            else
            {
                m_Host.abilityFinished -= OnAbilityFinished;
            }
        }

        private void OnAbilityFinished(AbilityDef ended, StateTreeStatus status)
        {
            if (m_Started == null || ended != m_Started)
                return;
            m_Ended = true;
            m_EndStatus = status;
        }
    }
}
