using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A stretch of clip time during which a strike volume is armed — the brief's §6.2 "Hitbox
    /// windows keyed to pose columns (a pose column can carry <c>hitbox:active</c> keys — the pose
    /// dictionary model makes this trivial)".
    ///
    /// The window is the TIMING half of a strike; the volume half is authored on the
    /// <see cref="WeaponDefAsset"/> (<c>hitTrack</c> sweeps a <c>hitSize</c> box over
    /// <c>activeTime</c>). Splitting them that way is what lets one weapon's swing be re-timed per
    /// animation without re-authoring its reach.
    ///
    /// M5 is DATA MODEL ONLY: nothing here arms anything. <see cref="CollectFrom"/> is the one
    /// piece of behaviour, and it is a pure read of <see cref="PoseClipAsset"/> data — it turns the
    /// gate channel a Pose Sheet author keys ON and OFF across columns into the contiguous time
    /// ranges a future Hitbox component would consume. The channel is a normal pose channel, so it
    /// keys, scrubs, moves with a column and survives a re-time exactly like a bone rotation does.
    /// </summary>
    [Serializable]
    public sealed class HitboxWindow
    {
        /// <summary>The conventional gate channel path. Pose channel paths are "Target:prop"
        /// (PoseChannel), so this reads as "the hitbox target's active property".</summary>
        public const string activeChannel = "hitbox:active";

        /// <summary>A keyed value at or above this counts as ON. A midpoint threshold, not an
        /// exact compare, because pose columns LERP: a gate that goes 0 → 1 between two columns
        /// flips at the halfway point instead of only at the exact key.</summary>
        private const float k_OnThreshold = 0.5f;

        /// <summary>Pose channel path this window was gated by. Multiple windows can coexist on
        /// one clip under different channels — "hitbox:active" for the blade, another for a shield
        /// bash — which is why the channel travels with the window instead of being assumed.</summary>
        public string channel = activeChannel;

        /// <summary>Clip time the window opens, in SECONDS (clip times are seconds everywhere in
        /// this toolset — m4 conventions).</summary>
        public float startTime;

        /// <summary>Clip time the window closes, in seconds.</summary>
        public float endTime;

        public float duration => Mathf.Max(endTime - startTime, 0f);

        /// <summary>A zero-length or inverted window arms nothing.</summary>
        public bool isValid => endTime > startTime;

        /// <summary>Half-open [start, end): the frame the gate is keyed back OFF is already
        /// outside the window, so two back-to-back windows never both claim the same instant.</summary>
        public bool Contains(float time)
        {
            return time >= startTime && time < endTime;
        }

        public bool Overlaps(HitboxWindow other)
        {
            return other != null && startTime < other.endTime && other.startTime < endTime;
        }

        /// <summary>
        /// Read every window a clip's <paramref name="channel"/> gate describes into
        /// <paramref name="into"/> (cleared first). A window opens at the time of the first column
        /// whose gate is ON and closes at the first column after it whose gate is OFF; a gate still
        /// ON at the last column closes at the clip's length, so "armed to the end" needs no
        /// trailing key.
        ///
        /// Columns that do not mention the channel at all count as OFF. That matches
        /// PoseClipAsset.Sample's hold semantics only for the closing edge, and is the safer
        /// reading of a missing key: an un-keyed column should not silently arm a strike.
        /// </summary>
        public static void CollectFrom(PoseClipAsset clip, string channel, List<HitboxWindow> into)
        {
            if (into == null)
                return;
            into.Clear();

            if (clip == null || string.IsNullOrEmpty(channel))
                return;

            // times and poses are parallel lists; a hand-edited asset could desync them.
            int count = Mathf.Min(clip.poseCount, clip.poses.Count);
            float openedAt = 0f;
            bool open = false;

            for (int i = 0; i < count; i++)
            {
                bool on = IsGateOn(clip.poses[i], channel);
                if (on == open)
                    continue;

                if (on)
                {
                    openedAt = clip.times[i];
                    open = true;
                }
                else
                {
                    into.Add(new HitboxWindow
                    {
                        channel = channel,
                        startTime = openedAt,
                        endTime = clip.times[i]
                    });
                    open = false;
                }
            }

            if (open)
            {
                into.Add(new HitboxWindow
                {
                    channel = channel,
                    startTime = openedAt,
                    endTime = Mathf.Max(clip.length, openedAt)
                });
            }
        }

        /// <summary>Every window on the conventional <see cref="activeChannel"/>.</summary>
        public static void CollectFrom(PoseClipAsset clip, List<HitboxWindow> into)
        {
            CollectFrom(clip, activeChannel, into);
        }

        /// <summary>Gate state of one column. Vector channels are ignored — a gate is a scalar,
        /// and reading a position channel as a gate would be a silent mis-arm.</summary>
        private static bool IsGateOn(PoseColumn column, string channel)
        {
            if (column == null)
                return false;

            var channels = column.channels;
            for (int i = 0; i < channels.Count; i++)
            {
                var candidate = channels[i];
                if (candidate.path != channel)
                    continue;
                return candidate.kind != PoseChannel.Kind.Vector2
                    && candidate.floatValue >= k_OnThreshold;
            }
            return false;
        }
    }
}
