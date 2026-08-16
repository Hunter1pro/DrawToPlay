using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE GENERIC CALLER (§4g): ask any declared subsystem for one of its requests, from
    /// ANY tree or graph on ANY context — an NPC's dialog, a level flow, a tutorial state.
    /// Requests live on the ROOT board (the scope every subsystem watches), so this atom
    /// writes there explicitly instead of to its own context; that is the whole reason it
    /// exists, and what makes "the keeper hands you a medkit and the bag opens on it" one
    /// node in a dialog graph. The value is the field, or a blackboard key's string (the
    /// key wins) — for handing forward something the surrounding flow just produced.
    /// </summary>
    [StateTreeCategory("Tasks/Services", "Write a subsystem request onto the root board")]
    public sealed class RequestTask : StateTreeTaskAsset
    {
        [Tooltip("The request key — a def's declared request (see the Subsystem APIs "
            + "window for what exists).")]
        [ServiceRequestKey]
        public string key = "";

        [Tooltip("The request's value — an item name for a typed request, '1' for a "
            + "plain ask.")]
        public string value = "1";

        [Tooltip("Optional: a blackboard key holding the value — wins over the field "
            + "when it resolves to a string.")]
        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField valueKey = new StateTreeKeyField();

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null || string.IsNullOrEmpty(key))
                return StateTreeStatus.Failure;

            StateTreeContextHost root = StateTreeContextHost.Resolve(context.owner,
                StateTreeContextKind.Root);
            if (root == null || root.Context == null)
                return StateTreeStatus.Failure;

            string resolved = value;
            string dynamicKey = valueKey;
            if (!string.IsNullOrEmpty(dynamicKey)
                && context.blackboard.TryGetValue(dynamicKey, out object held)
                && held is string text && !string.IsNullOrEmpty(text))
                resolved = text;

            root.Context.blackboard[key] = resolved ?? "";
            return StateTreeStatus.Success;
        }
    }
}
