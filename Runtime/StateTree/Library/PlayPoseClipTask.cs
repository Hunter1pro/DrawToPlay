using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Plays an M4 <see cref="PoseClipAsset"/> on the owner's <see cref="PoseAnimator"/> —
    /// brief §7.2's "PlayPoseClip (waits for clip or window)". The bridge between the state
    /// tree and the animation system: a state IS a pose clip for most of a character's
    /// behaviour.
    ///
    /// TWO MODES. <see cref="waitForEnd"/> off = fire and forget, instant Success, the clip
    /// keeps playing while the tree moves on (a looping walk cycle started by the chase state).
    /// On = Running until the clip reaches its end, so the state's duration IS the animation's
    /// duration (a wind-up whose recovery must not be cut short).
    ///
    /// COMPLETION TEST: <c>animator.time &gt;= clip.length</c>, or the animator having stopped
    /// on its own. PoseAnimator clamps time and clears <c>playing</c> at the end of a
    /// non-looping clip, and lets time run past length on a looping one (Sample wraps with
    /// Repeat) — so the same comparison ends a one-shot exactly at its last pose and a looping
    /// clip after exactly one cycle. A zero-length clip Succeeds immediately rather than
    /// hanging the state forever.
    ///
    /// CANCELLED TEARDOWN (the M6 exit criterion in miniature): an interrupted animation state
    /// calls <c>animator.Stop()</c>, so a zombie yanked out of its attack wind-up by a
    /// stagger does not keep playing the swing. Only on Cancelled — a clip that finished
    /// naturally (Success) has already stopped itself, and calling Stop on it would suppress a
    /// hold-last-frame that the next state may be relying on. This is the exact hook brief
    /// §7.1 describes: "teardown (nav goals, timers, spawned VFX) clean without bespoke
    /// cleanup code".
    ///
    /// NO ANIMATOR = Failure. The animator is resolved from the owner outward (see
    /// <see cref="StateTreeLibraryUtil.ResolveComponent{T}"/>) since a character's PoseAnimator
    /// usually sits beside the rig root, not on the entity root, and is cached per runner —
    /// task instances are deep-copied per <c>StateTreeRunner</c>, so one lookup per entity per
    /// tree, not one per tick.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Play Pose Clip", fileName = "PlayPoseClip")]
    [StateTreeCategory("Tasks/Animation", "Play a pose clip, optionally waiting for the end")]
    public sealed class PlayPoseClipTask : StateTreeTaskAsset
    {
        /// <summary>Clip name — PoseAnimator looks its clips up by ASSET NAME, so this is the
        /// PoseClipAsset's file name, not a path.</summary>
        public string clipName = "";

        /// <summary>Stay Running until the clip reaches its length.</summary>
        public bool waitForEnd = true;

        /// <summary>Restart from t=0 even when this clip is already the current one. Off keeps
        /// a looping locomotion clip running smoothly across state re-entries instead of
        /// snapping it back to its first pose every time.</summary>
        public bool restart = true;

        /// <summary>Playback rate written to <see cref="PoseAnimator.speed"/> on entry.
        /// 0 or less leaves the animator's own speed alone.</summary>
        public float speed;

        private PoseAnimator m_Animator;
        private bool m_Started;

        public override void OnEnter(StateTreeContext context)
        {
            m_Started = false;
            m_Animator = ResolveAnimator(context);
            if (m_Animator == null || string.IsNullOrEmpty(clipName))
                return;

            if (speed > 0f)
                m_Animator.speed = speed;

            // Godot's Play("") resumes whatever is current; passing the name is what selects
            // and rewinds. Skipping the call when the clip is already current is the
            // no-restart path.
            if (restart || m_Animator.current != clipName)
                m_Animator.Play(clipName);
            else
                m_Animator.Play();

            m_Started = true;
        }

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (!m_Started || m_Animator == null)
                return StateTreeStatus.Failure;
            if (!waitForEnd)
                return StateTreeStatus.Success;

            PoseClipAsset clip = m_Animator.Clip();
            if (clip == null || clip.length <= 0f)
                return StateTreeStatus.Success;

            // The animator advances itself in Update (play mode); this only observes.
            if (!m_Animator.playing || m_Animator.time >= clip.length)
                return StateTreeStatus.Success;

            return StateTreeStatus.Running;
        }

        public override void OnExit(StateTreeContext context, StateTreeStatus status)
        {
            if (status == StateTreeStatus.Cancelled && m_Animator != null)
                m_Animator.Stop();
            m_Started = false;
        }

        /// <summary>Cached per runner: task instances are deep-copied on StartTree, so the
        /// cache is per-entity and a destroyed animator is re-resolved rather than
        /// reused.</summary>
        private PoseAnimator ResolveAnimator(StateTreeContext context)
        {
            if (m_Animator != null)
                return m_Animator;
            return context == null
                ? null
                : StateTreeLibraryUtil.ResolveComponent<PoseAnimator>(context.owner);
        }
    }
}
