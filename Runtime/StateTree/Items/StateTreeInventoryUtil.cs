using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The one place the inventory's blackboard encoding lives: an item's count on a context
    /// scope is the key <c>item:&lt;id&gt;</c> holding a boxed float (the M7g rule — the same
    /// shape graph reads, conditions, and saves already understand). Everything inventory is
    /// state on the SPINE, which is what makes it per-player for free, readable from any
    /// tree, and picked up by the future per-state save without a dedicated inventory store.
    /// </summary>
    public static class StateTreeInventoryUtil
    {
        public const string KeyPrefix = "item:";

        public static string Key(string itemId) => KeyPrefix + itemId;

        public static int Count(StateTreeContext scope, string itemId)
        {
            if (scope == null || string.IsNullOrEmpty(itemId))
                return 0;
            if (!scope.blackboard.TryGetValue(Key(itemId), out object held) || !(held is float f))
                return 0;
            return (int)f;
        }

        /// <summary>
        /// Write a count; zero-or-less REMOVES the key, so "none left" and "never had one" are
        /// the same absent state every condition already handles.
        ///
        /// INTERNAL SINCE M32: four callers used to write this — the service, two graph atoms
        /// and the save restore — so nothing owned the encoding and no change to a bag could be
        /// announced, validated or drawn. <see cref="InventoryService"/> is the writer now, and
        /// everyone else asks it.
        /// </summary>
        internal static void SetCount(StateTreeContext scope, string itemId, int count)
        {
            if (scope == null || string.IsNullOrEmpty(itemId))
                return;
            if (count <= 0)
                scope.blackboard.Remove(Key(itemId));
            else
                scope.blackboard[Key(itemId)] = (float)count;
        }
    }
}
