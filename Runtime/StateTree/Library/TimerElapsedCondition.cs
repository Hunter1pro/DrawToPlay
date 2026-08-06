using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True once <see cref="seconds"/> have passed since a named timer started — "have I been
    /// idling long enough to wander?", "has the stagger lasted long enough to recover?". The
    /// counterpart to <see cref="CooldownReadyCondition"/>: a cooldown asks whether a deadline
    /// has passed, a timer measures elapsed time since a start stamp, and the difference
    /// matters because a timer can be RESTARTED by whoever owns the state.
    ///
    /// STATE LIVES IN THE BLACKBOARD: <see cref="timerKey"/> holds the absolute
    /// <see cref="Time.time"/> the clock started. Any task can reset it (write
    /// <c>Time.time</c>) or clear it to make the condition restart on its next evaluation —
    /// which is exactly how a state re-entry gets a fresh timer without this component
    /// carrying per-entry state it would have to reset on Cancelled.
    ///
    /// AN ABSENT KEY STARTS THE CLOCK AND RETURNS FALSE (when
    /// <see cref="startWhenMissing"/> is set, the default): no time has elapsed yet, so false
    /// is both correct and self-arming. With it off, an unset timer is simply false forever
    /// and some task must start it.
    ///
    /// CLOCK: <see cref="Time.time"/> — timeScale-aware, does not advance in EditMode tests.
    /// A task that must be testable headless should accumulate its own deltaTime instead; that
    /// is why <see cref="WaitTask"/> does not use this component.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Timer Elapsed",
        fileName = "TimerElapsed")]
    [StateTreeCategory("Conditions/Timing", "Blackboard-keyed timer elapsed")]
    public sealed class TimerElapsedCondition : StateTreeConditionAsset
    {
        /// <summary>Blackboard key holding the absolute time the timer started.</summary>
        [StateTreeKey(StateTreeKeyKind.Float)]
        public string timerKey = "stateTimer";

        /// <summary>Seconds that must elapse. 0 or less is always elapsed.</summary>
        public float seconds = 1f;

        /// <summary>Write <see cref="Time.time"/> into the key when it is missing, so the
        /// first evaluation arms the clock.</summary>
        public bool startWhenMissing = true;

        /// <summary>Clear the key once the timer fires, so the NEXT evaluation re-arms it.
        /// Turns the component into a repeating heartbeat instead of a one-shot latch.</summary>
        public bool restartOnElapsed;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null || string.IsNullOrEmpty(timerKey))
                return false;

            float now = Time.time;
            if (!StateTreeLibraryUtil.TryGetFloat(context, timerKey, out float startedAt))
            {
                if (startWhenMissing)
                    StateTreeLibraryUtil.SetFloat(context, timerKey, now);
                return seconds <= 0f;
            }

            if (now - startedAt < seconds)
                return false;

            if (restartOnElapsed)
                context.blackboard.Remove(timerKey);
            return true;
        }
    }
}
