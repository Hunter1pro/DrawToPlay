using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Gate that is true only when a named cooldown has expired, and (optionally) re-arms
    /// itself the moment it lets something through. The archer's "may I loose another arrow?"
    /// transition, and the general answer to brief §7.2's "Cooldown ready" seed condition.
    ///
    /// STATE LIVES IN THE BLACKBOARD, NOT ON THE COMPONENT: <see cref="cooldownKey"/> holds an
    /// absolute <see cref="Time.time"/> deadline. That is what makes the cooldown TASK-SCOPED
    /// rather than asset-scoped — the runner deep-copies conditions per runner, so an instance
    /// field would already be per-entity, but a blackboard deadline is additionally shared
    /// between the condition that GATES the shot and any task that wants to read or extend it,
    /// and it survives the state changing underneath.
    ///
    /// AN ABSENT KEY MEANS READY. A fresh entity has never fired, so its first shot must not
    /// be blocked; this is the one place where "no value" is deliberately true rather than the
    /// library's usual false. Nothing is being asked about a missing target here — the question
    /// is "has enough time passed", and for an unset clock the honest answer is yes.
    ///
    /// CLOCK: <see cref="Time.time"/>, so cooldowns respect <c>Time.timeScale</c> (a hit-stop
    /// or a paused game must not tick attack cooldowns down). It does not advance in a headless
    /// EditMode test — a test that needs to exercise cooldown branching should drive the tree
    /// with a stub condition instead.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Cooldown Ready",
        fileName = "CooldownReady")]
    [StateTreeCategory("Conditions/Timing", "Blackboard-keyed cooldown deadline elapsed")]
    public sealed class CooldownReadyCondition : StateTreeConditionAsset
    {
        /// <summary>Blackboard key holding the absolute time the cooldown expires. Name it
        /// after what it gates ("attackCooldown", "shootCooldown") so two abilities on one
        /// entity do not share a clock by accident.</summary>
        public string cooldownKey = "attackCooldown";

        /// <summary>Seconds re-armed when <see cref="armOnReady"/> is set.</summary>
        public float cooldown = 1f;

        /// <summary>Re-arm the cooldown as soon as this returns true, making the component a
        /// self-contained rate limiter. Leave OFF when the task behind the transition arms the
        /// same key itself — otherwise the cooldown is charged twice and the entity fires at
        /// half its authored rate.</summary>
        public bool armOnReady;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null || string.IsNullOrEmpty(cooldownKey))
                return false;

            float now = Time.time;
            if (StateTreeLibraryUtil.TryGetFloat(context, cooldownKey, out float readyAt)
                && now < readyAt)
                return false;

            if (armOnReady)
                StateTreeLibraryUtil.SetFloat(context, cooldownKey, now + Mathf.Max(cooldown, 0f));
            return true;
        }
    }
}
