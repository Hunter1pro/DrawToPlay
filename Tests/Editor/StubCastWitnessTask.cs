using UnityEngine;

namespace PowerOfFire.DrawToPlay.Tests
{
    /// <summary>
    /// A beat that only WITNESSES: it writes down which body stood under its role key at the
    /// moment its state opened.
    ///
    /// This exists for one bug. A cutscene's cast is published to the director's blackboard
    /// and the script is started, and if those two happen in the wrong order every beat opens
    /// against an empty board — which looks, from outside, like a scene that played perfectly
    /// in no time with nobody in it. OnEnter is the only moment that can tell.
    ///
    /// It reports through <see cref="StateTreeTestLog"/> rather than its own fields for the
    /// usual reason: the executor deep-copies the tree, so the authored instance is never the
    /// one that runs, and the context is the one channel that survives the copy.
    /// </summary>
    internal sealed class StubCastWitnessTask : StateTreeTaskAsset
    {
        /// <summary>The role this beat is addressed to — the key the cast is published under.</summary>
        public string roleKey = "hero";

        /// <summary>Ticks to stay Running. Zero or less never finishes, which is how a test
        /// watches a scene while it is still playing.</summary>
        public int finishOnTick;

        private int m_Ticks;

        public override void OnEnter(StateTreeContext context)
        {
            m_Ticks = 0;
            GameObject found = context != null
                && context.blackboard.TryGetValue(roleKey, out object held)
                ? held as GameObject
                : null;
            StateTreeTestLog.Record(context,
                roleKey + ":enter:" + (found != null ? found.name : "(nobody)"));
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            m_Ticks++;
            return finishOnTick > 0 && m_Ticks >= finishOnTick
                ? StateTreeStatus.Success
                : StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            StateTreeTestLog.Record(context, roleKey + ":exit:" + status);
        }
    }
}
