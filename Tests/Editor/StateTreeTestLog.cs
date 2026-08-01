using System.Collections.Generic;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// Per-context recording sink for the state-tree stubs.
    /// <para>
    /// The runner deep-copies the whole tree on StartTree (<see cref="StateTreeAsset.DeepCopy"/>
    /// → Object.Instantiate on every node/task/condition), so the instances the tests author are
    /// NOT the instances that run: a plain field on a stub asset can never be read back, and a
    /// delegate field would be dropped by Instantiate (non-serialized). The
    /// <see cref="StateTreeContext"/> however is created per runner and passed by reference to
    /// every task, so its blackboard is the one channel that survives the copy — one log list per
    /// runner, no statics, no cross-test bleed.
    /// </para>
    /// </summary>
    internal static class StateTreeTestLog
    {
        /// <summary>Blackboard key the log list lives under. Underscore-prefixed so it can never
        /// collide with the locked gameplay keys ("target", "moveSpeed", ...).</summary>
        public const string logKey = "__testLog";

        /// <summary>The calling context's log list, created on first use.</summary>
        public static List<string> Get(StateTreeContext context)
        {
            if (context == null)
                return new List<string>();
            List<string> log = null;
            object existing;
            if (context.blackboard.TryGetValue(logKey, out existing))
                log = existing as List<string>;
            if (log == null)
            {
                log = new List<string>();
                context.blackboard[logKey] = log;
            }
            return log;
        }

        /// <summary>Append one lifecycle entry to the calling context's log.</summary>
        public static void Record(StateTreeContext context, string entry)
        {
            Get(context).Add(entry);
        }
    }
}
