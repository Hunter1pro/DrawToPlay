using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// PUT A ROW ON SCREEN, FOR AS LONG AS THIS STATE LIVES — the UI pass's core verb: a
    /// popup IS a state, so showing on enter and hiding on exit means nothing can forget to
    /// close it, and an interrupt that pre-empts the state takes the popup with it.
    ///
    /// The row is ⛃-picked; <see cref="arguments"/> are the show-site's answers to the
    /// row's declared parameters — one ConfirmPopup row serves every question. For a
    /// fire-and-forget show (the session revealing its HUD), turn off
    /// <see cref="holdWhileShown"/> and <see cref="hideOnExit"/> — the task completes and
    /// the piece stays.
    /// </summary>
    [StateTreeCategory("Tasks/Ui", "Show a UI row while this state runs (hide on exit)")]
    public sealed class ShowUiTask : StateTreeTaskAsset
    {
        [Tooltip("The row to show — picked from the UI registry.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        [Tooltip("The show-site's arguments for the row's declared parameters — bound by id, "
            + "defaults where absent.")]
        public GraphTaskParameterSet arguments = new GraphTaskParameterSet();

        [Tooltip("On: the task RUNS while the piece is on screen — the state owns the "
            + "popup's lifetime. Off: show and complete (a HUD revealed for the session).")]
        public bool holdWhileShown = true;

        [Tooltip("Hide the row when this state exits. Off for a piece that outlives its "
            + "shower.")]
        public bool hideOnExit = true;

        private bool m_Shown;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            UiService service = ResolveService(context);
            if (service == null)
                return StateTreeStatus.Failure;
            UiDef row = service.Find(ui.entryName);
            if (row == null)
            {
                Debug.LogError("[ShowUi] no UI row named '" + ui.entryName + "' in the "
                    + "service's catalog.", context?.owner);
                return StateTreeStatus.Failure;
            }

            if (!m_Shown)
            {
                service.Show(row, arguments != null ? arguments.values : null);
                m_Shown = true;
            }
            return holdWhileShown ? StateTreeStatus.Running : StateTreeStatus.Success;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (m_Shown && hideOnExit)
            {
                UiService service = ResolveService(context);
                if (service != null)
                    service.Hide(ui.entryName);
            }
            m_Shown = false;
        }

        private static UiService ResolveService(StateTreeContext context)
        {
            if (context == null || context.owner == null)
                return null;
            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            if (service == null)
            {
                Debug.LogError("[ShowUi] no UiService reachable from '" + context.owner.name
                    + "' — mount one on the scope that owns the screen.", context.owner);
            }
            return service;
        }
    }
}
