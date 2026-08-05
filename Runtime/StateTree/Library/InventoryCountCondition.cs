using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True while the Player scope holds at least <see cref="atLeast"/> of an item — "can
    /// afford", "has the key", "quiver not empty" as a transition guard. Quiet like every
    /// per-tick condition; the write-side atoms carry the wiring warnings.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Items/Inventory Count", fileName = "InventoryCount")]
    [StateTreeCategory("Conditions/Items", "Player-scope inventory holds at least N of an item")]
    public sealed class InventoryCountCondition : StateTreeConditionAsset
    {
        public string itemId = "";

        public int atLeast = 1;

        public string scopeId = "";

        public bool invert;

        public override bool Evaluate(StateTreeContext context)
        {
            bool enough = false;
            if (context != null && !string.IsNullOrEmpty(itemId))
            {
                StateTreeContextHost player = StateTreeContextHost.Resolve(context.owner,
                    StateTreeContextKind.Player, scopeId);
                enough = player != null
                    && StateTreeInventoryUtil.Count(player.Context, itemId) >= atLeast;
            }
            return invert ? !enough : enough;
        }
    }
}
