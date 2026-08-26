using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>One animated channel value. Paths are "TargetName:prop" where TargetName
    /// resolves by descendant NAME under the animator root (bone names are the binding
    /// key everywhere in this toolset) and prop is position/rotation/scale/morph.
    /// rotation is DEGREES and lerps shortest-path.</summary>
    [Serializable]
    public struct PoseChannel
    {
        public enum Kind { Float, Angle, Vector2 }

        public string path;
        public Kind kind;
        public float floatValue;
        public Vector2 vectorValue;

        public static PoseChannel Float(string path, float v)
            => new PoseChannel { path = path, kind = Kind.Float, floatValue = v };
        public static PoseChannel Angle(string path, float degrees)
            => new PoseChannel { path = path, kind = Kind.Angle, floatValue = degrees };
        public static PoseChannel Vector(string path, Vector2 v)
            => new PoseChannel { path = path, kind = Kind.Vector2, vectorValue = v };
    }

    /// <summary>A full rig statement at one time — Godot's pose Dictionary as a list.</summary>
    [Serializable]
    public sealed class PoseColumn
    {
        public List<PoseChannel> channels = new List<PoseChannel>();
    }

    /// <summary>
    /// Pose-column animation asset — direct port of pose_clip.gd: RAW KEYS only, no
    /// curves; playback lerps float/Vector2 linearly between columns (rotation channels
    /// shortest-path) and the pose-column model is the product (deliberately NOT
    /// AnimationClip, per the brief §3). Authored by the Pose Sheet; played by PoseAnimator.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Pose Clip", fileName = "PoseClip")]
    public sealed class PoseClipAsset : ScriptableObject
    {
        /// <summary>Structure tag gameplay routes by: "default" loops on base, "aim" is
        /// scrubbed by input, "shot" is a one-shot overlay…</summary>
        public string kind = "default";

        /// <summary>When non-empty, CapturePose records ONLY channels whose path contains
        /// one of these substrings — how partial-body layer clips are authored.</summary>
        public List<string> captureFilter = new List<string>();

        public float length = 1f;
        public bool looping = true;

        /// <summary>Sorted pose times; poses[i] is the column at times[i].</summary>
        public List<float> times = new List<float>();
        public List<PoseColumn> poses = new List<PoseColumn>();

        public int poseCount => times.Count;

        /// <summary>Insert (or replace, within eps) a full pose column. Returns index.
        /// Port of insert_pose (pose_clip.gd lines 30-44), incl. the length grow.</summary>
        public int InsertPose(float t, PoseColumn values, float eps = 0.02f)
        {
            for (int i = 0; i < times.Count; i++)
            {
                if (Mathf.Abs(times[i] - t) <= eps)
                {
                    poses[i] = values;
                    return i;
                }
            }
            int at = 0;
            while (at < times.Count && times[at] < t)
                at++;
            times.Insert(at, t);
            poses.Insert(at, values);
            if (t > length)
                length = t;
            return at;
        }

        public void RemovePose(int i)
        {
            if (i < 0 || i >= times.Count)
                return;
            times.RemoveAt(i);
            poses.RemoveAt(i);
        }

        /// <summary>Move ("move") an entire column to a new time. Returns new index.</summary>
        public int MovePose(int i, float t)
        {
            if (i < 0 || i >= times.Count)
                return -1;
            var vals = poses[i];
            RemovePose(i);
            return InsertPose(t, vals);
        }

        public int IndexAt(float t, float eps = 0.02f)
        {
            for (int i = 0; i < times.Count; i++)
                if (Mathf.Abs(times[i] - t) <= eps)
                    return i;
            return -1;
        }

        /// <summary>Interpolated channel values at time t (looping wraps t) merged into
        /// <paramref name="result"/> (cleared first). Port of sample/_lerp_val
        /// (pose_clip.gd lines 71-104): channels present only in the LATER column snap in
        /// at u==1; channels only in the earlier column hold their value.</summary>
        public void Sample(float t, Dictionary<string, PoseChannel> result)
        {
            result.Clear();
            int n = times.Count;
            if (n == 0)
                return;
            if (looping && length > 0f)
                t = Mathf.Repeat(t, length);
            if (t <= times[0] || n == 1)
            {
                CopyColumn(poses[0], result);
                return;
            }
            if (t >= times[n - 1])
            {
                CopyColumn(poses[n - 1], result);
                return;
            }
            int i0 = 0;
            while (i0 < n - 2 && times[i0 + 1] <= t)
                i0++;
            var a = poses[i0];
            var b = poses[i0 + 1];
            float u = (t - times[i0]) / Mathf.Max(times[i0 + 1] - times[i0], 0.0001f);

            // Godot: for k in a → lerp(a[k], b.get(k, a[k])); for k only in b → b[k] raw.
            CopyColumn(b, result);
            foreach (var ch in a.channels)
            {
                if (result.TryGetValue(ch.path, out var to) && to.kind == ch.kind)
                    result[ch.path] = Lerp(ch, to, u);
                else
                    result[ch.path] = ch; // b lacks the channel: hold a's value
            }
        }

        private static void CopyColumn(PoseColumn column, Dictionary<string, PoseChannel> into)
        {
            foreach (var ch in column.channels)
                into[ch.path] = ch;
        }

        public static PoseChannel Lerp(PoseChannel a, PoseChannel b, float u)
        {
            switch (a.kind)
            {
                case PoseChannel.Kind.Angle:
                    a.floatValue = Mathf.LerpAngle(a.floatValue, b.floatValue, u);
                    return a;
                case PoseChannel.Kind.Float:
                    a.floatValue = Mathf.Lerp(a.floatValue, b.floatValue, u);
                    return a;
                default:
                    a.vectorValue = Vector2.LerpUnclamped(a.vectorValue, b.vectorValue, u);
                    return a;
            }
        }
    }
}
