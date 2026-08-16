using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The flow's redraw step (the UI wiring brief): read what is carried and worn NOW and
    /// HAND it to the bag skin. The one place domain and skin meet — the skin never asks,
    /// and everything else in a flow can assume the bag shows the truth after this task.
    /// A bag that is not on screen is a Success with nothing to do, not an error.
    /// </summary>
    [StateTreeCategory("Tasks/UI", "Redraw the bag from what is carried now")]
    public sealed class RedrawBagTask : StateTreeTaskAsset
    {
        [Tooltip("The bag's UI row — picked from the UI registry.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;

            InventoryWidgetView widget = FindBag(context, ui.entryName);
            if (widget == null)
                return StateTreeStatus.Success;

            InventoryService inventory =
                StateTreeContextHost.FindService<InventoryService>(context.owner);
            if (inventory == null)
                return StateTreeStatus.Failure;

            inventory.RedrawInto(widget);
            return StateTreeStatus.Success;
        }

        /// <summary>The shown bag skin, through the UI service's ledger — the hub holds the
        /// references so tasks can reach the systems they drive.</summary>
        internal static InventoryWidgetView FindBag(StateTreeContext context, string rowName)
        {
            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            GameObject view = service != null ? service.ShownView(rowName) : null;
            return view != null
                ? view.GetComponentInChildren<InventoryWidgetView>(true)
                : null;
        }
    }
}
