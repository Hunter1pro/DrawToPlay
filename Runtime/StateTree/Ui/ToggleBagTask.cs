using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The flow's answer to the bag button: open a closed bag, close an open one.
    /// Purely visual — pair it with <see cref="RedrawBagTask"/> earlier in the same state
    /// so what appears is fresh.</summary>
    [StateTreeCategory("Tasks/UI", "Toggle the bag panel open or closed")]
    public sealed class ToggleBagTask : StateTreeTaskAsset
    {
        [Tooltip("The bag's UI row — picked from the UI registry.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;
            InventoryWidgetView widget = RedrawBagTask.FindBag(context, ui.entryName);
            if (widget != null)
                widget.ToggleOpen();
            return StateTreeStatus.Success;
        }
    }
}
