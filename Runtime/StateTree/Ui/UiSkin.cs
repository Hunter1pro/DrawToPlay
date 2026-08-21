using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE ONE WAY A TASK REACHES A SKIN (M35.7) — through the UI service's ledger, by the row's
    /// name, or not at all.
    ///
    /// Three tasks had written this walk out longhand (find the service, ask for the shown view,
    /// dig out the component); a fourth would have written it again slightly differently. It is
    /// the hub's whole job — hold the references, forward the reach — so it is said once here.
    ///
    /// A row that is not on screen answers null, which is a task's cue to succeed with nothing
    /// to do rather than to fail: a bag nobody is looking at is not an error.
    /// </summary>
    public static class UiSkin
    {
        /// <summary>The shown row's skin, or null when it is not up.</summary>
        public static UiViewBehaviour Shown(StateTreeContext context, string rowName)
        {
            return Shown<UiViewBehaviour>(context, rowName);
        }

        /// <summary>The shown row's skin of a particular kind — how a task that drives ONE
        /// view (the bag's, the craft panel's) asks for it.</summary>
        public static T Shown<T>(StateTreeContext context, string rowName) where T : Component
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(rowName))
                return null;

            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            GameObject view = service != null ? service.ShownView(rowName) : null;
            return view != null ? view.GetComponentInChildren<T>(true) : null;
        }
    }
}
