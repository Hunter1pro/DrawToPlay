using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Plays PoseClipAsset pose-column animations on a rig — port of pose_animator.gd
    /// with a 1:1 gameplay API (Play/Stop/Seek/SeekNormalized, layered slots with
    /// additive diffs and hold-end, CapturePose). Channel paths resolve by descendant
    /// NAME under <see cref="root"/>; props: position/rotation(z°)/scale on Transforms,
    /// morph on DrawnShapeRenderer.morphWeight (Regenerate after write). Apply() is
    /// editor-callable (no [ExecuteAlways]) — the Pose Sheet drives it explicitly.
    ///
    /// PLAY vs EDIT: <see cref="Update"/> only exists in play mode (Unity does not tick a
    /// plain MonoBehaviour in the editor), so the editor preview calls
    /// <see cref="Advance"/>/<see cref="Seek"/>/<see cref="Apply"/> itself. Apply is a pure
    /// write path: it never creates or destroys objects, which is what makes it legal from
    /// an editor tick or an inspector-driven scrub.
    ///
    /// TARGET RESOLUTION: Godot stores full node paths (base.get_path_to(child)); this port
    /// stores plain NAMES, the same binding key skinning uses, so a rig can be reparented or
    /// renamed above the bone without breaking a clip. Names are resolved through a cached
    /// name→Transform / name→DrawnShapeRenderer map of every descendant of the root
    /// (pre-order, first match wins, so the shallowest of two same-named nodes is the target).
    /// The cache is rebuilt on EVERY Apply in edit mode — authoring scale, and the Pose Sheet
    /// creates, renames and re-rigs bones constantly — while in play mode it is kept and only
    /// invalidated when the root changes, when Transform.hierarchyCount changes (any add or
    /// remove in the root's hierarchy), or when a cached target has been destroyed.
    /// </summary>
    public sealed class PoseAnimator : MonoBehaviour
    {
        /// <summary>Rig root the pose paths are relative to (null = this transform's parent).</summary>
        public Transform root;

        /// <summary>Layer application order: later slots override earlier ones on the
        /// channels their clip keys; slots not listed apply last.</summary>
        public List<string> layerOrder = new List<string> { "base", "aim", "shot" };

        /// <summary>Clip lookup is by asset name.</summary>
        public List<PoseClipAsset> clips = new List<PoseClipAsset>();

        public string current = "";
        public bool playing;
        public float speed = 1f;

        [System.NonSerialized] public float time;

        /// <summary>(slot, clipName) when a non-looping, non-held layer finishes.</summary>
        public event System.Action<string, string> layerFinished;

        // Channel props. Godot splits "<path>:<prop>" with rsplit(":", true, 1); the port
        // splits on the LAST colon for the same reason (a name may not contain one, but the
        // split must stay stable if one ever does).
        private const string k_PropPosition = "position";
        private const string k_PropRotation = "rotation";
        private const string k_PropScale = "scale";
        private const string k_PropMorph = "morph";

        private const string k_ChannelPosition = ":position";
        private const string k_ChannelRotation = ":rotation";
        private const string k_ChannelScale = ":scale";
        private const string k_ChannelMorph = ":morph";

        /// <summary>One live slot — Godot's per-slot Dictionary as a struct (so it is written
        /// back into <see cref="m_Layers"/> after every mutation).</summary>
        private struct Layer
        {
            public string clip;
            public float time;
            public bool playing;
            public bool additive;
            public bool hold;
        }

        private readonly Dictionary<string, Layer> m_Layers = new Dictionary<string, Layer>();
        private readonly List<string> m_Slots = new List<string>();
        private readonly List<string> m_Applied = new List<string>();
        private readonly List<string> m_Done = new List<string>();

        private readonly Dictionary<string, PoseChannel> m_Values =
            new Dictionary<string, PoseChannel>();
        private readonly Dictionary<string, PoseChannel> m_Sample =
            new Dictionary<string, PoseChannel>();
        private readonly Dictionary<string, PoseChannel> m_Reference =
            new Dictionary<string, PoseChannel>();
        private readonly List<DrawnShapeRenderer> m_MorphDirty = new List<DrawnShapeRenderer>();
        private readonly HashSet<string> m_CapturedPaths = new HashSet<string>();

        private readonly Dictionary<string, Transform> m_Targets =
            new Dictionary<string, Transform>();
        private readonly Dictionary<string, DrawnShapeRenderer> m_MorphTargets =
            new Dictionary<string, DrawnShapeRenderer>();
        private Transform m_CacheRoot;
        private int m_CacheHierarchyCount;
        private bool m_CacheValid;

        // --- gameplay API ----------------------------------------------------------------

        public PoseClipAsset Clip()
        {
            return FindClip(current);
        }

        public void Play(string clipName = "")
        {
            // Godot: `if clip_name != "" and clips.has(clip_name)` — an unknown name resumes
            // whatever is current instead of clearing it.
            if (!string.IsNullOrEmpty(clipName) && FindClip(clipName) != null)
            {
                current = clipName;
                time = 0f;
            }
            playing = true;
        }

        public void Stop()
        {
            playing = false;
        }

        public void Seek(float t)
        {
            time = t;
            Apply();
        }

        public void SeekNormalized(float u)
        {
            PoseClipAsset c = Clip();
            if (c != null)
                Seek(Mathf.Clamp01(u) * c.length);
        }

        public void LayerPlay(string slot, string clipName, bool restart = true,
            bool additive = false, bool holdEnd = false)
        {
            if (slot == null || FindClip(clipName) == null)
                return;
            bool has = m_Layers.TryGetValue(slot, out Layer layer);
            if (restart || !has || layer.clip != clipName)
            {
                layer = new Layer
                {
                    clip = clipName,
                    time = 0f,
                    playing = true,
                    additive = additive,
                    hold = holdEnd
                };
            }
            else
            {
                layer.playing = true;
                layer.additive = additive;
                layer.hold = holdEnd;
            }
            m_Layers[slot] = layer;
            Apply();
        }

        public void LayerScrub(string slot, string clipName, float u)
        {
            PoseClipAsset c = FindClip(clipName);
            if (slot == null || c == null)
                return;
            // Godot replaces the whole slot dictionary here, so a scrubbed layer is never
            // additive and never held — the struct's default false fields say the same.
            m_Layers[slot] = new Layer
            {
                clip = clipName,
                time = Mathf.Clamp01(u) * c.length,
                playing = false
            };
            Apply();
        }

        public void LayerStop(string slot)
        {
            if (slot == null)
                return;
            m_Layers.Remove(slot);
            Apply();
        }

        // --- playback --------------------------------------------------------------------

        // Godot's _process is a @tool script and ticks in the editor too; a plain
        // MonoBehaviour does not, by the same deliberate no-[ExecuteAlways] rule the rest of
        // this package follows. Editor preview drives Advance/Seek explicitly.
        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Advance the current clip and every playing layer by
        /// <paramref name="deltaTime"/> seconds and re-Apply — the body of _process
        /// (pose_animator.gd lines 101-132). Update calls this in play mode; the editor
        /// preview (which gets no Update) calls it from its own tick.</summary>
        public void Advance(float deltaTime)
        {
            bool advancing = false;
            if (playing)
            {
                PoseClipAsset c = Clip();
                if (c != null)
                {
                    time += deltaTime * speed;
                    if (!c.looping && time >= c.length)
                    {
                        time = c.length;
                        playing = false;
                    }
                    advancing = true;
                }
            }

            m_Done.Clear();
            if (m_Layers.Count > 0)
            {
                SnapshotSlots();
                for (int i = 0; i < m_Slots.Count; i++)
                {
                    string slot = m_Slots[i];
                    if (!m_Layers.TryGetValue(slot, out Layer layer) || !layer.playing)
                        continue;
                    PoseClipAsset lc = FindClip(layer.clip);
                    if (lc == null)
                        continue;
                    layer.time += deltaTime * speed;
                    if (!lc.looping && layer.time >= lc.length)
                    {
                        layer.time = lc.length;
                        layer.playing = false;
                        // held layers stay until LayerStop() (bow draw: hold while pressed)
                        if (!layer.hold)
                            m_Done.Add(slot);
                    }
                    m_Layers[slot] = layer;
                    advancing = true;
                }
            }

            if (advancing)
                Apply();

            for (int i = 0; i < m_Done.Count; i++)
            {
                string slot = m_Done[i];
                // a layerFinished handler may already have stopped or replaced the slot
                if (!m_Layers.TryGetValue(slot, out Layer finished))
                    continue;
                m_Layers.Remove(slot);
                layerFinished?.Invoke(slot, finished.clip);
                Apply();
            }
        }

        /// <summary>Resolve + write every sampled channel to the rig — editor-callable.</summary>
        public void Apply()
        {
            Transform baseRoot = ResolveRoot();
            if (baseRoot == null)
                return;

            m_Values.Clear();
            PoseClipAsset c = Clip();
            if (c != null)
            {
                c.Sample(time, m_Sample);
                MergeFiltered(m_Values, m_Sample, c.captureFilter);
            }

            // layers in declared order, then any undeclared ones — later wins, but only on
            // the channels each clip actually keys
            m_Applied.Clear();
            if (layerOrder != null)
            {
                for (int i = 0; i < layerOrder.Count; i++)
                {
                    string slot = layerOrder[i];
                    if (slot == null || m_Applied.Contains(slot) || !m_Layers.ContainsKey(slot))
                        continue;
                    MergeLayer(slot);
                    m_Applied.Add(slot);
                }
            }
            foreach (KeyValuePair<string, Layer> pair in m_Layers)
            {
                if (!m_Applied.Contains(pair.Key))
                    MergeLayer(pair.Key);
            }

            WriteValues(baseRoot);
        }

        /// <summary>Port of _merge_layer (lines 162-181). The clip's captureFilter is its
        /// CHANNEL OWNERSHIP and is applied at PLAYBACK too, so a full-capture clip recorded
        /// before a filter was set behaves correctly the moment the filter is added.</summary>
        private void MergeLayer(string slot)
        {
            if (!m_Layers.TryGetValue(slot, out Layer layer))
                return;
            PoseClipAsset lc = FindClip(layer.clip);
            if (lc == null)
                return;
            lc.Sample(layer.time, m_Sample);
            List<string> filters = lc.captureFilter;
            if (!layer.additive || lc.poseCount == 0)
            {
                MergeFiltered(m_Values, m_Sample, filters);
                return;
            }

            // additive: the clip's FIRST pose is its reference (RAW, unfiltered — Godot reads
            // lc.poses[0] directly) and only the DIFFERENCE from it is added onto what the
            // lower layers produced, so a release jerk authored at forward-aim composes with
            // any aim angle.
            BuildReference(lc);
            bool all = filters == null || filters.Count == 0;
            foreach (KeyValuePair<string, PoseChannel> pair in m_Sample)
            {
                if (!all && !PassesFilter(pair.Key, filters))
                    continue;
                if (!m_Reference.TryGetValue(pair.Key, out PoseChannel reference)
                    || !m_Values.TryGetValue(pair.Key, out PoseChannel lower))
                {
                    m_Values[pair.Key] = pair.Value;   // nothing to offset against: absolute
                    continue;
                }
                m_Values[pair.Key] = AddValue(lower, reference, pair.Value);
            }
        }

        private void BuildReference(PoseClipAsset clip)
        {
            m_Reference.Clear();
            if (clip.poses == null || clip.poses.Count == 0)
                return;
            PoseColumn first = clip.poses[0];
            if (first == null || first.channels == null)
                return;
            for (int i = 0; i < first.channels.Count; i++)
                m_Reference[first.channels[i].path] = first.channels[i];
        }

        /// <summary>Port of _add_val (lines 195-203): angles compose through the shortest
        /// difference (Godot angle_difference == Mathf.DeltaAngle, both "target - current"
        /// wrapped to ±180 — degrees here, radians there), everything else is a plain delta.
        /// Mixed kinds fall back to the absolute value, exactly like Godot's mismatched-type
        /// branch.</summary>
        private static PoseChannel AddValue(PoseChannel lower, PoseChannel reference,
            PoseChannel value)
        {
            if (lower.kind != reference.kind || lower.kind != value.kind)
                return value;
            switch (lower.kind)
            {
                case PoseChannel.Kind.Angle:
                    lower.floatValue += Mathf.DeltaAngle(reference.floatValue, value.floatValue);
                    return lower;
                case PoseChannel.Kind.Float:
                    lower.floatValue += value.floatValue - reference.floatValue;
                    return lower;
                default:
                    lower.vectorValue += value.vectorValue - reference.vectorValue;
                    return lower;
            }
        }

        /// <summary>Port of _filter_sample + the merge: copy the sample over
        /// <paramref name="into"/>, keeping only the channels the filter owns.</summary>
        private static void MergeFiltered(Dictionary<string, PoseChannel> into,
            Dictionary<string, PoseChannel> sample, List<string> filters)
        {
            bool all = filters == null || filters.Count == 0;
            foreach (KeyValuePair<string, PoseChannel> pair in sample)
            {
                if (all || PassesFilter(pair.Key, filters))
                    into[pair.Key] = pair.Value;
            }
        }

        private static bool PassesFilter(string path, List<string> filters)
        {
            for (int i = 0; i < filters.Count; i++)
            {
                string filter = filters[i];
                // empty entries match nothing (Godot's contains("") would match everything —
                // a half-typed filter row must not silently open the whole rig)
                if (!string.IsNullOrEmpty(filter) && path.Contains(filter))
                    return true;
            }
            return false;
        }

        // --- writing ---------------------------------------------------------------------

        /// <summary>The tail of _apply: split "Name:prop", resolve, write. Transform
        /// channels keep their z (this is a 2D toolset writing into 3D Transforms); a morph
        /// channel drives DrawnShapeRenderer.morphWeight and the renderer is regenerated ONCE
        /// per Apply per renderer, and only when the weight actually changed — Regenerate
        /// raises geometryChanged, which is what rebuilds the skin and the paint layer.</summary>
        private void WriteValues(Transform baseRoot)
        {
            EnsureTargets(baseRoot);
            m_MorphDirty.Clear();
            foreach (KeyValuePair<string, PoseChannel> pair in m_Values)
            {
                string path = pair.Key;
                if (string.IsNullOrEmpty(path))
                    continue;
                int split = path.LastIndexOf(':');
                if (split <= 0 || split >= path.Length - 1)
                    continue;              // Godot: parts.size() != 2 -> skip
                string targetName = path.Substring(0, split);
                string prop = path.Substring(split + 1);
                PoseChannel channel = pair.Value;

                if (prop == k_PropMorph)
                {
                    if (channel.kind == PoseChannel.Kind.Vector2)
                        continue;
                    DrawnShapeRenderer shape = ResolveMorph(targetName);
                    if (shape == null || shape.morphWeight == channel.floatValue)
                        continue;
                    shape.morphWeight = channel.floatValue;
                    if (!m_MorphDirty.Contains(shape))
                        m_MorphDirty.Add(shape);
                    continue;
                }

                Transform target = ResolveTransform(targetName);
                if (target == null)
                    continue;
                switch (prop)
                {
                    case k_PropPosition:
                        if (channel.kind != PoseChannel.Kind.Vector2)
                            break;
                        Vector3 position = target.localPosition;
                        target.localPosition = new Vector3(channel.vectorValue.x,
                            channel.vectorValue.y, position.z);
                        break;
                    case k_PropRotation:
                        if (channel.kind == PoseChannel.Kind.Vector2)
                            break;
                        // DEGREES around z — the only rotation this toolset authors
                        target.localRotation = Quaternion.Euler(0f, 0f, channel.floatValue);
                        break;
                    case k_PropScale:
                        if (channel.kind != PoseChannel.Kind.Vector2)
                            break;
                        Vector3 scale = target.localScale;
                        target.localScale = new Vector3(channel.vectorValue.x,
                            channel.vectorValue.y, scale.z);
                        break;
                }
            }

            for (int i = 0; i < m_MorphDirty.Count; i++)
                m_MorphDirty[i].Regenerate();
            m_MorphDirty.Clear();
        }

        // --- capture ---------------------------------------------------------------------

        /// <summary>Record the rig's current state as a pose column at t on the current
        /// clip (respecting its captureFilter): every ShapeRig bone's rotation+position,
        /// and position/rotation/scale (+ morph when targets exist) of every
        /// DrawnShapeRenderer under the root. Returns column index, -1 on failure.
        /// Callers own Undo/dirty on the clip asset.</summary>
        public int CapturePose(float t)
        {
            PoseClipAsset c = Clip();
            Transform baseRoot = ResolveRoot();
            if (c == null || baseRoot == null)
                return -1;
            PoseColumn column = new PoseColumn();
            CollectPose(column);
            // partial-body clips: keep only channels matching the clip's filter
            List<string> filters = c.captureFilter;
            if (filters != null && filters.Count > 0)
            {
                List<PoseChannel> kept = new List<PoseChannel>(column.channels.Count);
                for (int i = 0; i < column.channels.Count; i++)
                {
                    if (PassesFilter(column.channels[i].path, filters))
                        kept.Add(column.channels[i]);
                }
                column.channels = kept;
            }
            return c.InsertPose(t, column);
        }

        /// <summary>Snapshot the live rig state into <paramref name="into"/> WITHOUT touching
        /// any clip — port of _collect, which the Pose Sheet calls directly to diff for
        /// auto-key (pose_sheet.gd line 206). Unfiltered by design: the auto-key diff watches
        /// the whole rig, and CapturePose applies the clip's filter afterwards.</summary>
        public void CollectPose(PoseColumn into)
        {
            if (into == null)
                return;
            into.channels.Clear();
            Transform baseRoot = ResolveRoot();
            if (baseRoot == null)
                return;
            m_CapturedPaths.Clear();
            baseRoot.TryGetComponent(out ShapeRig rootRig);
            Collect(baseRoot, rootRig, into);
        }

        /// <summary>Godot walks children and tests `ch is Bone2D` / `ch.get("morph_targets")`.
        /// The Unity equivalents: a bone is a descendant of a ShapeRig whose name the rig's
        /// RigAsset lists (the nearest ShapeRig ancestor is carried down the walk), and a
        /// morphing part is a DrawnShapeRenderer — whose transform is part of the pose too
        /// (an arrow slides back with the string, then launches forward).</summary>
        private void Collect(Transform node, ShapeRig rig, PoseColumn into)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child == transform)
                    continue;              // Godot: `if ch == self: continue` (skips the subtree)
                ShapeRig childRig = rig;
                if (child.TryGetComponent(out ShapeRig found))
                    childRig = found;

                if (childRig != null && childRig.rig != null
                    && childRig.rig.IndexOf(child.name) >= 0)
                {
                    Add(into, PoseChannel.Angle(child.name + k_ChannelRotation,
                        child.localEulerAngles.z));
                    Add(into, PoseChannel.Vector(child.name + k_ChannelPosition,
                        child.localPosition));
                }

                if (child.TryGetComponent(out DrawnShapeRenderer shape))
                {
                    Add(into, PoseChannel.Vector(child.name + k_ChannelPosition,
                        child.localPosition));
                    Add(into, PoseChannel.Angle(child.name + k_ChannelRotation,
                        child.localEulerAngles.z));
                    Add(into, PoseChannel.Vector(child.name + k_ChannelScale,
                        child.localScale));
                    DrawnShapeAsset style = shape.asset;
                    if (style != null && style.morphTargets != null && style.morphTargets.Count > 0)
                        Add(into, PoseChannel.Float(child.name + k_ChannelMorph, shape.morphWeight));
                }

                Collect(child, childRig, into);
            }
        }

        /// <summary>First writer of a path wins — paths are names, and a name collision would
        /// otherwise put two columns' worth of values on one target (Godot's dictionary keys
        /// are full paths and cannot collide).</summary>
        private void Add(PoseColumn column, PoseChannel channel)
        {
            if (m_CapturedPaths.Add(channel.path))
                column.channels.Add(channel);
        }

        // --- resolution ------------------------------------------------------------------

        /// <summary>Godot's default root_path is "..". A parentless animator falls back to its
        /// own transform so a self-contained rig still animates (Godot would simply do
        /// nothing).</summary>
        private Transform ResolveRoot()
        {
            if (root != null)
                return root;
            return transform.parent != null ? transform.parent : transform;
        }

        private PoseClipAsset FindClip(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || clips == null)
                return null;
            for (int i = 0; i < clips.Count; i++)
            {
                PoseClipAsset c = clips[i];
                if (c != null && c.name == clipName)
                    return c;
            }
            return null;
        }

        private void SnapshotSlots()
        {
            m_Slots.Clear();
            foreach (KeyValuePair<string, Layer> pair in m_Layers)
                m_Slots.Add(pair.Key);
        }

        private Transform ResolveTransform(string targetName)
        {
            if (m_Targets.TryGetValue(targetName, out Transform target))
            {
                if (target != null)
                    return target;
                m_CacheValid = false;      // destroyed since the map was built
            }
            return null;
        }

        private DrawnShapeRenderer ResolveMorph(string targetName)
        {
            if (m_MorphTargets.TryGetValue(targetName, out DrawnShapeRenderer shape))
            {
                if (shape != null)
                    return shape;
                m_CacheValid = false;
            }
            return null;
        }

        /// <summary>See the class comment: rebuilt per Apply in edit mode, kept in play mode
        /// until the root, the hierarchy size, or a cached target changes.</summary>
        private void EnsureTargets(Transform baseRoot)
        {
            if (m_CacheValid && m_CacheRoot == baseRoot && Application.isPlaying
                && m_CacheHierarchyCount == baseRoot.hierarchyCount)
                return;
            m_Targets.Clear();
            m_MorphTargets.Clear();
            CollectTargets(baseRoot);
            m_CacheRoot = baseRoot;
            m_CacheHierarchyCount = baseRoot.hierarchyCount;
            m_CacheValid = true;
        }

        private void CollectTargets(Transform node)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                string childName = child.name;
                if (!m_Targets.ContainsKey(childName))
                    m_Targets[childName] = child;
                if (child.TryGetComponent(out DrawnShapeRenderer shape)
                    && !m_MorphTargets.ContainsKey(childName))
                    m_MorphTargets[childName] = shape;
                CollectTargets(child);
            }
        }
    }
}
