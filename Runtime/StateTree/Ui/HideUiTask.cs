using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>Take a row OFF screen from anywhere — the cross-state verb for a piece some
    /// other state left showing (a fire-and-forget HUD a game-over state clears). The
    /// state-owned lifetime is <see cref="ShowUiTask"/>'s hide-on-exit; this is for the
    /// rest.</summary>
    [StateTreeCategory("Tasks/Ui", "Hide a UI row now")]
    public sealed class HideUiTask : StateTreeTaskAsset
    {
        [Tooltip("The row to hide — picked from the UI registry. Hiding what is not shown "
            + "succeeds quietly.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            if (service == null)
                return StateTreeStatus.Failure;
            service.Hide(ui.entryName);
            return StateTreeStatus.Success;
        }
    }
}
