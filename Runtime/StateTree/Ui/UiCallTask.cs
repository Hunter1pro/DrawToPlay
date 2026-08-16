using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// ONE task for every UI beat (§4c): call a VERB on a shown row's views — "toggle" on
    /// the bag, "flash" with an item name, "pulse" on the HUD. The verb vocabulary is the
    /// view's (<see cref="UiViewBehaviour.Call"/>); this task only carries the word and
    /// its argument, so the next skin and the next subsystem need no new task types. The
    /// argument comes from the field, or dynamically from a blackboard key (the request's
    /// value) — the key wins. A hidden row or an unanswered verb is a quiet Success: a
    /// beat with nobody to hear it is not an error.
    /// </summary>
    [StateTreeCategory("Tasks/UI", "Call a verb on a shown UI row's views")]
    public sealed class UiCallTask : StateTreeTaskAsset
    {
        [Tooltip("The UI row whose views are called — picked from the UI registry.")]
        public StateTreeEntryRef<UiDef> ui = new StateTreeEntryRef<UiDef>();

        [Tooltip("The verb, in the view's vocabulary — 'toggle', 'flash', 'pulse', …")]
        public string verb = "";

        [Tooltip("The verb's argument, when it takes one.")]
        public string argument = "";

        [Tooltip("Optional: a blackboard key holding the argument — the request's value. "
            + "Wins over the field when it resolves.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField argumentKey = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(verb))
                return StateTreeStatus.Failure;

            UiService service = StateTreeContextHost.FindService<UiService>(context.owner);
            GameObject view = service != null ? service.ShownView(ui.entryName) : null;
            if (view == null)
                return StateTreeStatus.Success;

            string value = argument;
            object payload = null;
            string key = argumentKey;
            if (!string.IsNullOrEmpty(key)
                && context.blackboard.TryGetValue(key, out object held) && held != null)
            {
                // A string on the key is the scalar argument; anything richer is a CONTRACT
                // PAYLOAD (§4e) handed to the skin whole.
                if (held is string dynamic)
                {
                    if (!string.IsNullOrEmpty(dynamic))
                        value = dynamic;
                }
                else
                {
                    payload = held;
                }
            }

            UiViewBehaviour[] views = view.GetComponentsInChildren<UiViewBehaviour>(true);
            for (int i = 0; i < views.Length; i++)
                views[i].Call(verb, value, payload);
            return StateTreeStatus.Success;
        }
    }
}
