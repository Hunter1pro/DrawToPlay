using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Show or hide one screen and Succeed — the imperative half of persistent panels. A
    /// <see cref="ShowScreenTask"/> with <c>closeOnExit</c> off deliberately leaves its screen
    /// up when its state exits; THIS is how the state that owns "everything is closed now"
    /// (or "the detail panel appears now") says so explicitly. One job, stateless,
    /// Cancelled-safe: the visibility write happened or it did not.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/UI/Set Screen Visible", fileName = "SetScreenVisible")]
    [StateTreeCategory("Tasks/UI", "Show or hide a screen, then succeed")]
    public sealed class SetScreenVisibleTask : StateTreeTaskAsset
    {
        [StateTreeKey(StateTreeKeyKind.Screen)]
        public string screenId = "";

        public bool visible = true;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || string.IsNullOrEmpty(screenId))
                return StateTreeStatus.Failure;

            UIService service = StateTreeContextHost.FindService<UIService>(context.owner);
            UIScreenBehaviour screen = service != null ? service.Find(screenId) : null;
            if (screen == null)
                return StateTreeStatus.Failure;

            if (visible)
                screen.Show();
            else
                screen.Hide();
            return StateTreeStatus.Success;
        }
    }
}
