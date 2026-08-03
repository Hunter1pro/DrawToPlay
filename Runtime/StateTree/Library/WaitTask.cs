using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Holds a state open for <see cref="seconds"/>, then Succeeds — idle beats, attack
    /// recovery, the pause between wander steps. Brief §7.2's "Wait".
    ///
    /// THE CLOCK IS THE RUNNER'S deltaTime, accumulated, not <see cref="Time.time"/>. That is
    /// the whole reason this component exists rather than pairing
    /// <see cref="TimerElapsedCondition"/> with an empty state: <c>StateTreeRunner.TickTree</c>
    /// is public so EditMode tests can drive a tree headless at any step size, and a task that
    /// reads the global clock is untestable there. It also means a wait scales with
    /// <c>Time.timeScale</c> for free, because the runner feeds it <c>Time.deltaTime</c>.
    ///
    /// CANCELLED-SAFE: the elapsed accumulator is reset in OnEnter (so a state re-entered
    /// after an interrupt waits the full duration again, never inheriting the fraction it had
    /// already served) AND in OnExit for every status including Cancelled (so the task never
    /// sits in the pool holding stale time). Both, not one: OnEnter alone would leave the
    /// field dirty for anything that inspects it between states, OnExit alone would break if
    /// a future runner change ever entered a task without exiting the previous one.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Wait", fileName = "Wait")]
    [StateTreeCategory("Tasks/Timing", "Wait seconds (optional jitter)")]
    public sealed class WaitTask : StateTreeTaskAsset
    {
        /// <summary>Seconds to hold. 0 or less Succeeds on the first tick.</summary>
        public float seconds = 1f;

        /// <summary>Random extra seconds in [0, jitter] rolled per entry. Without it a row of
        /// identical idlers moves in lockstep, which reads as a machine rather than a
        /// crowd.</summary>
        public float jitter;

        private float m_Elapsed;
        private float m_Duration;

        public override void OnEnter(StateTreeContext context)
        {
            m_Elapsed = 0f;
            m_Duration = seconds + (jitter > 0f ? Random.Range(0f, jitter) : 0f);
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            m_Elapsed += deltaTime;
            return m_Elapsed >= m_Duration ? StateTreeStatus.Success : StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            m_Elapsed = 0f;
        }
    }
}
