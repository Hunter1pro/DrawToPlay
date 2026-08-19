using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    public sealed class WaterCheckTask : StateTreeTaskAsset
    {
        [Tooltip("The item that permits being afloat — a boat. Empty = this actor is not "
            + "gated by anything carried.")]
        public StateTreeEntryRef<ItemDef> requires = new StateTreeEntryRef<ItemDef>();

        [Tooltip("A world tag that permits being afloat, for an actor with no bag to carry an "
            + "artifact in — a raider that arrived in its own boat. Empty = no tag route.")]
        [WorldTag("State")]
        public string requiresTag = "";

        [Tooltip("The tag the level's water volumes carry.")]
        [WorldTag("World")]
        public string waterTag = "water";

        [StateTreeKey(StateTreeKeyKind.String)]
        public StateTreeKeyField aboardKey = new StateTreeKeyField(BoardingKeys.Aboard);

        /// <summary>
        /// Runs for as long as its state does — RUNNING, never Success.
        ///
        /// The rule cost an hour to relearn: this executor RETIRES a task the moment it
        /// returns a terminal status, so a sense that answered Success sensed exactly once
        /// and then sat silent while the actor walked into the sea. A repeating sense is
        /// Running plus <c>blocking = false</c>: alive every tick, holding nothing open.
        /// </summary>
        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;

            string key = aboardKey;
            bool aboard = context.blackboard.ContainsKey(key);
            WaterVolumeBehaviour water = WaterVolumeBehaviour.At(context.owner,
                context.owner.transform.position, waterTag);

            if (water == null)
            {
                if (aboard)
                    context.blackboard.Remove(key);
                // THE LAST DRY GROUND, recorded while dry — the only moment it is true.
                // Recorded at the first WET tick instead (the first version), it was a
                // point already in the water, so disembarking teleported the actor back
                // into the sea and it boarded again on the next tick: a loop that looked
                // exactly like "the mode never ends".
                context.blackboard[BoardingKeys.LastGround] = context.owner.transform.position;
                return StateTreeStatus.Running;
            }
            if (aboard)
                return StateTreeStatus.Running;

            string refusal = WhyNotFloat(context);
            if (refusal != null)
            {
                Refuse(context.owner, refusal);
                return StateTreeStatus.Running;
            }

            context.blackboard[key] = water.name;
            return StateTreeStatus.Running;
        }

        /// <summary>
        /// Why this actor may not float, or null when it may. A REASON rather than a bool,
        /// because "no boat" and "no inventory reachable from here" are different problems and
        /// only one of them is the player's fault.
        ///
        /// TWO ROUTES TO THE SAME RULE, because "nobody floats without a reason" should hold
        /// for everyone and only the player has a bag to keep an artifact in. A carried ITEM is
        /// the player's reason; a world TAG is a raider's, declared on the row that places it.
        /// Leaving both empty means an actor that floats unconditionally, which the review
        /// rightly read as the rule not applying to half the cast.
        /// </summary>
        private string WhyNotFloat(StateTreeContext context)
        {
            if (!string.IsNullOrEmpty(requiresTag))
            {
                var citizens = context.owner.GetComponents<WorldObjectBehaviour>();
                for (int i = 0; i < citizens.Length; i++)
                {
                    if (citizens[i] != null && citizens[i].HasTag(requiresTag))
                        return null;
                }
            }

            string itemName = requires.entryName;
            if (string.IsNullOrEmpty(itemName))
                return string.IsNullOrEmpty(requiresTag)
                    ? null
                    : "is no '" + requiresTag + "'";
            InventoryService inventory =
                StateTreeContextHost.FindService<InventoryService>(context.owner);
            if (inventory == null)
                return "no InventoryService is reachable from this actor";
            StateTreeContextHost carrier = StateTreeContextHost.Resolve(context.owner,
                StateTreeContextKind.Player);
            if (carrier == null || carrier.Context == null)
                return "no carrier scope to check for '" + itemName + "'";
            return inventory.Has(carrier.Context, itemName)
                ? null
                : "carries no '" + itemName + "'";
        }

        /// <summary>Said ONCE per actor PER REASON, not once per tick: standing at the shore
        /// without a boat is a normal thing to do and a log per frame would bury the level's
        /// real findings — but a NEW reason is new information and gets its own line.</summary>
        private static void Refuse(GameObject owner, string reason)
        {
            if (!s_Refused.Add(owner.name + "|" + reason))
                return;
            Debug.Log("[Boarding] " + owner.name + " stays ashore: " + reason + ".", owner);
        }

        private static readonly System.Collections.Generic.HashSet<string> s_Refused =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
    }
}
