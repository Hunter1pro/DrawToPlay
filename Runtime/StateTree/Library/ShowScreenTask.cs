using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A screen, open exactly as long as its STATE is active — the load-bearing task of
    /// UI-as-trees (brief §3.5). OnEnter shows the named screen and listens; it stays Running
    /// while the human looks at it; the two things a human can do to a screen are the two ways
    /// a task can finish: a row CLICK completes with Success and the item id as a
    /// <see cref="TaskOutputAttribute"/> output (the transition routes it to the next state —
    /// the M7j return flow carrying UI intent), a CLOSE completes with Failure (the branchable
    /// "dismissed" answer). OnExit hides the screen on EVERY path — Success, Failure, and
    /// Cancelled alike — which is what makes an interrupt anywhere above this state tear the
    /// whole UI down correctly for free.
    ///
    /// No screen (or no <see cref="UIService"/> on the spine) is a wiring error: Failure plus
    /// one warning per activation, so a mistyped id cannot strand a state Running forever.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/UI/Show Screen", fileName = "ShowScreen")]
    [StateTreeCategory("Tasks/UI", "Open a screen while this state runs; click = Success + item id, close = Failure")]
    public sealed class ShowScreenTask : StateTreeTaskAsset
    {
        [StateTreeKey(StateTreeKeyKind.Screen)]
        public StateTreeKeyField screenId = new StateTreeKeyField();

        /// <summary>Local blackboard key the click result is PUBLISHED under (the
        /// TargetDetected idiom: a click writes the id here, a close removes it) — what the
        /// completion transitions branch on with <see cref="HasBlackboardKeyCondition"/>,
        /// since transition selection runs before output routing can deliver anything.</summary>
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField resultKey = new StateTreeKeyField("clickedItem");

        /// <summary>The clicked row's item id, ALSO published as a routable output for
        /// transitions that want it under a different key on the way somewhere else.</summary>
        [TaskOutput("Item id of the clicked row")]
        public string clickedItemId = "";

        /// <summary>Hide the screen when this state exits (the modal default). FALSE makes the
        /// screen a PERSISTENT panel — it stays up across the states a click flows through
        /// (master-detail: the list never blinks), and hiding becomes some other state's
        /// explicit job (<see cref="SetScreenVisibleTask"/> in the closed state). That
        /// includes Cancelled: a tree that can be interrupted above this state should keep
        /// the default, or its teardown state must hide what it left showing.</summary>
        public bool closeOnExit = true;

        private UIScreenBehaviour m_Screen;
        private bool m_ClickPending;
        private bool m_ClosePending;
        private string m_PendingItemId;
        private bool m_WarnedMissing;

        public override void OnEnter(StateTreeContext context)
        {
            m_ClickPending = false;
            m_ClosePending = false;
            m_PendingItemId = "";
            clickedItemId = "";

            UIService service = context != null
                ? StateTreeContextHost.FindService<UIService>(context.owner)
                : null;
            m_Screen = service != null ? service.Find(screenId) : null;
            if (m_Screen == null)
            {
                if (!m_WarnedMissing)
                {
                    m_WarnedMissing = true;
                    Debug.LogWarning("ShowScreenTask: no screen '" + screenId
                        + "' reachable — is a UIService on the spine and the screen registered?",
                        context != null ? context.owner : null);
                }
                return;
            }

            m_Screen.itemClicked += OnItemClicked;
            m_Screen.closeRequested += OnCloseRequested;
            m_Screen.Show();
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (m_Screen == null)
                return StateTreeStatus.Failure;

            if (m_ClickPending)
            {
                // Written BEFORE returning Success: the executor captures outputs at
                // completion, and an output assigned after the fact would route stale text.
                clickedItemId = m_PendingItemId;
                if (context != null && !string.IsNullOrEmpty(resultKey))
                    context.blackboard[resultKey] = m_PendingItemId;
                return StateTreeStatus.Success;
            }
            if (m_ClosePending)
            {
                if (context != null && !string.IsNullOrEmpty(resultKey))
                    context.blackboard.Remove(resultKey);
                return StateTreeStatus.Failure;
            }

            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Screen == null)
                return;
            m_Screen.itemClicked -= OnItemClicked;
            m_Screen.closeRequested -= OnCloseRequested;
            if (closeOnExit)
                m_Screen.Hide();
            m_Screen = null;
        }

        private void OnItemClicked(string itemId)
        {
            m_ClickPending = true;
            m_PendingItemId = itemId ?? "";
        }

        private void OnCloseRequested()
        {
            m_ClosePending = true;
        }
    }
}
