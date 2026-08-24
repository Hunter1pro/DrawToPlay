using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The Pose Sheet — Unity port of pose_sheet.gd, the deliberately tiny dope sheet this
    /// toolset needs: ONE summary row of pose columns on a <see cref="PoseClipAsset"/>. No
    /// per-track keys, no curves (draw-tool-port-brief.md §3).
    ///
    ///   click empty timeline   scrub (the rig previews the pose live)
    ///   drag a diamond         retime that pose column (snapped)
    ///   Key                    capture the whole rig at the playhead
    ///   Dup / Del              copy the selected column to the playhead / remove it
    ///   ▶                      preview playback
    ///   ●                      AUTO-KEY: rig edits key themselves at the playhead
    ///
    /// IMGUI, not UI Toolkit, and deliberately so: the timeline is an immediate-mode canvas
    /// (background, ruler, diamonds, playhead) with one drag/scrub interaction, which is exactly
    /// what <see cref="OnGUI"/> is good at — the UI Toolkit equivalent would be a custom
    /// VisualElement with a generateVisualContent painter for no gain. The Godot original is
    /// likewise a hand-drawn `Control` (`_tl_draw` / `_tl_input`).
    ///
    /// Deviations from the Godot panel (all deliberate, see the M4 report):
    ///  - Godot's plugin dock keeps the panel alive and its `_process` polls every frame. An
    ///    EditorWindow comes and goes, so the poll lives on <see cref="EditorApplication.update"/>
    ///    while the window is open, throttled to ~30 Hz (the same treatment M3 gave Setup mode).
    ///  - Godot's editor runs @tool `_process`, so the animator advances its own time during
    ///    preview. A plain MonoBehaviour gets no edit-mode Update, so the window ticks
    ///    <see cref="PoseAnimator.Advance"/> itself while previewing (and stands down in play
    ///    mode, where the component's own Update does it).
    ///  - Auto-key IS undo-recorded here (one step per detected change). Godot explicitly does
    ///    not record undo for auto-keys; the M4 conventions require it.
    /// </summary>
    public sealed class PoseSheetWindow : EditorWindow
    {
        // --- Godot layout constants (pose_sheet.gd), in UI pixels --------------------------

        /// <summary>`const PAD := 14.0` — the timeline's left/right margin.</summary>
        private const float k_Pad = 14f;

        /// <summary>`var d := 7.0` — diamond half-diagonal.</summary>
        private const float k_DiamondRadius = 7f;

        /// <summary>`distance_to(event.position) < 9.0` — diamond pick slop.</summary>
        private const float k_PickRadius = 9f;

        /// <summary>`var mid := _tl.size.y * 0.55`.</summary>
        private const float k_MidFraction = 0.55f;

        /// <summary>`var big_every := 5` and the ruler's `maxf(_snap_spin.value, 0.05)` floor.</summary>
        private const int k_BigTickEvery = 5;
        private const float k_MinTickStep = 0.05f;

        /// <summary>Ruler safety valve with no Godot counterpart: a 600 s clip at 0.01 s snap is
        /// 60 000 ticks, and IMGUI draws every one of them. The step doubles until the ruler fits
        /// in this many ticks — the snap itself (what drags round to) is never touched.</summary>
        private const int k_MaxTicks = 400;

        /// <summary>`custom_minimum_size = Vector2(0, 110)` for the whole panel; here it is the
        /// timeline canvas alone, since the Unity toolbar rows are laid out above it.</summary>
        private const float k_TimelineHeight = 92f;

        // --- Godot palette (pose_sheet.gd _tl_draw) ---------------------------------------

        private static readonly Color k_Background = new Color(0.13f, 0.14f, 0.16f);
        private static readonly Color k_TickBig = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color k_TickSmall = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color k_Lane = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color k_Diamond = new Color(0.85f, 0.88f, 0.95f);
        private static readonly Color k_DiamondSelected = new Color(1f, 0.8f, 0.3f);
        private static readonly Color k_Playhead = new Color(0.45f, 0.7f, 1f, 0.9f);
        private static readonly Color k_HintText = new Color(1f, 1f, 1f, 0.4f);

        /// <summary>`_auto_btn` font colours (lines 99-100), used to tint the armed toggle.</summary>
        private static readonly Color k_AutoArmed = new Color(1f, 0.2f, 0.15f);

        // --- change-detection epsilons (port of `_same_val`, lines 292-297) ---------------
        //
        // Godot compares floats at 0.0005 and Vector2 at 0.01. Those are Godot units: radians for
        // rotation, PIXELS for position. The project's 1 wu == 32 px rule converts the positional
        // one; scale and morph are dimensionless and keep the raw numbers. Position and scale are
        // both Vector2 channels, so the two are told apart by the channel's ":scale" suffix.

        /// <summary>0.01 Godot px of movement.</summary>
        private const float k_PositionEpsilon = 0.01f / 32f;

        /// <summary>0.0005 rad, expressed in the degrees this port stores rotations in.</summary>
        private const float k_AngleEpsilonDegrees = 0.0005f * Mathf.Rad2Deg;

        /// <summary>Dimensionless channels (scale distance / morph weight).</summary>
        private const float k_ScaleEpsilon = 0.01f;
        private const float k_FloatEpsilon = 0.0005f;

        private const string k_ScaleSuffix = ":scale";

        /// <summary>`float(sh.get("morph")) <= 0.001` — "this shape sits on its base form".</summary>
        private const float k_ZeroMorph = 0.001f;

        // --- poll / preview ---------------------------------------------------------------

        /// <summary>~30 Hz. Godot polls every frame; an editor-update callback fires even while
        /// nothing is happening, so it is throttled exactly like M3's Setup-mode poll.</summary>
        private const double k_PollInterval = 1.0 / 30.0;

        /// <summary>Preview advance clamp: the editor-update delta jumps after a compile or a
        /// modal dialog, and Godot's `_process(delta)` never sees those.</summary>
        private const double k_MaxPreviewDelta = 0.1;

        private static string k_DrawnAssetFolder => DrawToPlayFolders.Drawn;

        // --- state ------------------------------------------------------------------------

        [SerializeField] private PoseAnimator m_Animator;
        [SerializeField] private string m_NewClipName = string.Empty;
        [SerializeField] private int m_Selected = -1;

        /// <summary>`_dragging` / `_drag_time` / `_scrubbing` — live gesture state.</summary>
        private bool m_Dragging;
        private float m_DragTime;
        private bool m_Scrubbing;

        /// <summary>`_play_btn.button_pressed`.</summary>
        private bool m_Preview;

        /// <summary>`_auto_prev` — the previous rig snapshot the poll diffs against, keyed by
        /// channel path exactly like the Godot dictionary.</summary>
        private readonly Dictionary<string, PoseChannel> m_AutoPrevious = new Dictionary<string, PoseChannel>();
        private readonly Dictionary<string, PoseChannel> m_AutoCurrent = new Dictionary<string, PoseChannel>();

        /// <summary>Reused buffer for <see cref="PoseAnimator.CollectPose"/> — the animator's
        /// `_collect` walk, which is what the poll diffs (no clip is touched).</summary>
        private readonly PoseColumn m_CollectBuffer = new PoseColumn();

        /// <summary>`_shape_sigs` — per-shape curve signature + pre-edit snapshot, keyed by the
        /// shape's identity (Godot `get_instance_id`; GetInstanceID is [Obsolete] in 6000.5, so
        /// this uses its named replacement, exactly like TerrainBlob's cache key).</summary>
        private readonly Dictionary<EntityId, FormSignature> m_ShapeSignatures =
            new Dictionary<EntityId, FormSignature>();

        private double m_NextPoll;
        private double m_LastUpdate;

        /// <summary>Skinned shapes rebuild their (HideFlags.DontSave) skin layer on demand only,
        /// so a freshly opened scene has none and Apply — which never creates objects — would
        /// scrub the un-skinned base shape. Set whenever the binding changes; consumed by the
        /// next editor tick, outside any serialization callback.</summary>
        private bool m_SkinRefreshPending;

        private GUIStyle m_TickStyle;
        private GUIStyle m_HintStyle;

        [MenuItem("Tools/Draw To Play/Pose Sheet")]
        public static void Open()
        {
            var window = GetWindow<PoseSheetWindow>();
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Pose Sheet");
            minSize = new Vector2(420f, 190f);

            PoseSheetState.windowOpen = true;
            PoseSheetState.NotifyChanged();

            m_LastUpdate = EditorApplication.timeSinceStartup;
            m_SkinRefreshPending = true;
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            BindFromSelection();
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;

            // Leaving `playing` on would keep the runtime animator running with nothing driving
            // it in edit mode; Godot's panel un-toggles the same way when it stops previewing.
            StopPreview();

            PoseSheetState.windowOpen = false;
            PoseSheetState.NotifyChanged();
        }

        // --- binding ----------------------------------------------------------------------

        /// <summary>The clip being edited — `_clip()` (lines 138-141).</summary>
        private PoseClipAsset CurrentClip()
        {
            if (m_Animator == null)
                return null;
            return m_Animator.Clip();
        }

        /// <summary>The transform pose paths resolve under. Mirrors the animator's documented
        /// default ("null = this transform's parent") with a final fallback to its own transform
        /// so a root-level animator still resolves its children.</summary>
        private static Transform ResolveRoot(PoseAnimator animator)
        {
            if (animator == null)
                return null;
            if (animator.root != null)
                return animator.root;
            return animator.transform.parent != null ? animator.transform.parent : animator.transform;
        }

        /// <summary>Godot binds whatever PoseAnimator node the user selects (`set_animator`).
        /// Here the selection is usually a shape or a bone, so the search widens to the rig the
        /// selection belongs to. A miss keeps the previous binding — selecting a bone must not
        /// unbind the sheet.</summary>
        private void BindFromSelection()
        {
            var found = FindAnimatorForSelection();
            if (found == null || found == m_Animator)
                return;

            m_Animator = found;
            m_Selected = -1;
            ClearPollCaches();
            m_SkinRefreshPending = true;
        }

        private static PoseAnimator FindAnimatorForSelection()
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return null;

            var animator = active.GetComponent<PoseAnimator>();
            if (animator != null)
                return animator;

            animator = active.GetComponentInParent<PoseAnimator>();
            if (animator != null)
                return animator;

            animator = active.GetComponentInChildren<PoseAnimator>(true);
            if (animator != null)
                return animator;

            // Nothing on the selection itself: look around the rig it belongs to, which is the
            // Godot layout (the PoseAnimator node sits next to the Skeleton, under the actor).
            var rig = FindRigForSelection();
            if (rig == null)
                return null;

            var host = rig.transform.parent != null ? rig.transform.parent : rig.transform;
            animator = host.GetComponentInParent<PoseAnimator>();
            return animator != null ? animator : host.GetComponentInChildren<PoseAnimator>(true);
        }

        private static ShapeRig FindRigForSelection()
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return null;

            var rig = active.GetComponentInParent<ShapeRig>();
            if (rig != null)
                return rig;

            var skin = active.GetComponent<DrawnShapeSkin>();
            if (skin != null && skin.rig != null)
                return skin.rig;

            return active.GetComponentInChildren<ShapeRig>(true);
        }

        /// <summary>Create the animator Godot-style: a child node of the actor with its root
        /// pointing back at that actor, so bones (under Skeleton) and shapes (its siblings) are
        /// both descendants of the root and resolve by name.</summary>
        private void CreateAnimatorForSelection()
        {
            var rig = FindRigForSelection();
            var host = rig != null
                ? (rig.transform.parent != null ? rig.transform.parent.gameObject : rig.gameObject)
                : Selection.activeGameObject;
            if (host == null)
                return;

            var existing = host.GetComponentInChildren<PoseAnimator>(true);
            if (existing != null)
            {
                m_Animator = existing;
                Refresh();
                return;
            }

            var group = BeginUndoGroup("Add Pose Animator");

            var go = new GameObject("PoseAnimator");
            StageUtility.PlaceGameObjectInCurrentStage(go);
            go.transform.SetParent(host.transform, false);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, "Add Pose Animator");

            var animator = Undo.AddComponent<PoseAnimator>(go);
            Undo.RecordObject(animator, "Add Pose Animator");
            animator.root = host.transform;
            EditorUtility.SetDirty(animator);

            Undo.CollapseUndoOperations(group);

            m_Animator = animator;
            m_Selected = -1;
            ClearPollCaches();
            Selection.activeGameObject = go;
            Refresh();
        }

        // --- OnGUI ------------------------------------------------------------------------

        private void OnGUI()
        {
            EnsureStyles();

            DrawBindingRow();
            DrawClipRow();
            DrawActionRow();

            var timeline = GUILayoutUtility.GetRect(10f, 100000f, k_TimelineHeight, 100000f);
            HandleTimelineInput(timeline);
            DrawTimeline(timeline);
        }

        private void EnsureStyles()
        {
            if (m_TickStyle == null)
            {
                m_TickStyle = new GUIStyle(EditorStyles.miniLabel);
                m_TickStyle.normal.textColor = k_HintText;
                m_TickStyle.fontSize = 9;
                m_TickStyle.padding = new RectOffset(0, 0, 0, 0);
            }

            if (m_HintStyle == null)
            {
                m_HintStyle = new GUIStyle(EditorStyles.label);
                m_HintStyle.normal.textColor = k_HintText;
                m_HintStyle.wordWrap = true;
            }
        }

        private void DrawBindingRow()
        {
            EditorGUILayout.BeginHorizontal();

            var picked = (PoseAnimator)EditorGUILayout.ObjectField(
                "Animator", m_Animator, typeof(PoseAnimator), true);
            if (picked != m_Animator)
            {
                m_Animator = picked;
                m_Selected = -1;
                ClearPollCaches();
                m_SkinRefreshPending = true;
                Refresh();
            }

            using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
            {
                if (GUILayout.Button(new GUIContent("Add",
                        "Create a PoseAnimator on the selection's rig (a child of the actor, with " +
                        "its root pointing at the actor so bones and shapes both resolve)."),
                    GUILayout.Width(44f)))
                    CreateAnimatorForSelection();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>The Godot toolbar's left half: clip picker, new-clip name + New, then the
        /// clip's own fields (length / loop) and the timeline snap.</summary>
        private void DrawClipRow()
        {
            var clip = CurrentClip();

            EditorGUILayout.BeginHorizontal();

            var names = ClipNames(out var currentIndex);
            using (new EditorGUI.DisabledScope(m_Animator == null || names.Length == 0))
            {
                var picked = EditorGUILayout.Popup(currentIndex, names, GUILayout.Width(130f));
                if (picked != currentIndex && picked >= 0 && picked < names.Length)
                    SelectClip(names[picked]);
            }

            // Godot shows this as `_name_edit.placeholder_text = "new clip name"`; IMGUI text
            // fields have no placeholder, so the hint is a label in front of it.
            GUILayout.Label(new GUIContent("name", "name for the next New clip"),
                EditorStyles.miniLabel, GUILayout.Width(32f));
            m_NewClipName = EditorGUILayout.TextField(m_NewClipName, GUILayout.Width(110f));

            using (new EditorGUI.DisabledScope(m_Animator == null))
            {
                if (GUILayout.Button(new GUIContent("New", "Create a new pose clip asset next to " +
                        "the rig's assets and bind it to this animator."), GUILayout.Width(44f)))
                    CreateClip();
            }

            GUILayout.Space(8f);

            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 32f;

            using (new EditorGUI.DisabledScope(clip == null))
            {
                var length = EditorGUILayout.FloatField(new GUIContent("len", "clip length (s)"),
                    clip != null ? clip.length : 1f, GUILayout.Width(78f));
                if (clip != null && !Mathf.Approximately(length, clip.length))
                    SetClipLength(clip, length);

                EditorGUIUtility.labelWidth = 34f;
                var looping = EditorGUILayout.Toggle(new GUIContent("loop", "wrap the playhead at the clip length"),
                    clip != null && clip.looping, GUILayout.Width(52f));
                if (clip != null && looping != clip.looping)
                    SetClipLooping(clip, looping);
            }

            EditorGUIUtility.labelWidth = 38f;
            var snap = EditorGUILayout.FloatField(new GUIContent("snap", "snap (s)"),
                PoseSheetState.snap, GUILayout.Width(84f));
            if (!Mathf.Approximately(snap, PoseSheetState.snap))
                PoseSheetState.snap = snap;

            EditorGUIUtility.labelWidth = labelWidth;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>The Godot toolbar's right half: preview, AUTO-KEY, Key / Dup / Del, status.</summary>
        private void DrawActionRow()
        {
            var clip = CurrentClip();

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(clip == null))
            {
                var preview = GUILayout.Toggle(m_Preview,
                    new GUIContent("▶ Play", "Preview playback"),
                    EditorStyles.miniButton, GUILayout.Width(62f));
                if (preview != m_Preview)
                    TogglePreview(preview);
            }

            var background = GUI.backgroundColor;
            if (PoseSheetState.autoKey)
                GUI.backgroundColor = k_AutoArmed;

            var auto = GUILayout.Toggle(PoseSheetState.autoKey,
                new GUIContent("● Auto Key",
                    "AUTO-KEY: while red, any rig change (bones, shape transforms, morphs) keys " +
                    "itself at the playhead automatically, and editing a shape's outline captures " +
                    "a form variant. Turn it off when done."),
                EditorStyles.miniButton, GUILayout.Width(86f));
            GUI.backgroundColor = background;

            if (auto != PoseSheetState.autoKey)
                ToggleAutoKey(auto);

            GUILayout.Space(6f);

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button(new GUIContent("Key",
                        "Capture the WHOLE rig as a pose at the playhead (bones + shape transforms + morphs)"),
                    EditorStyles.miniButton, GUILayout.Width(40f)))
                    KeyPose();
            }

            var hasSelection = clip != null && m_Selected >= 0 && m_Selected < clip.poseCount;
            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button(new GUIContent("Dup", "Copy the selected pose column to the playhead"),
                    EditorStyles.miniButton, GUILayout.Width(40f)))
                    DuplicatePose();

                if (GUILayout.Button(new GUIContent("Del", "Delete the selected pose column"),
                    EditorStyles.miniButton, GUILayout.Width(40f)))
                    DeletePose();
            }

            GUILayout.Space(8f);
            GUILayout.Label(PoseSheetState.status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

        // --- clip plumbing ----------------------------------------------------------------

        /// <summary>Clip names for the picker. Godot sorts the dictionary keys because its clip
        /// map has no order; the Unity list is authored, so its order is kept (it is the same
        /// order the animator's inspector shows).</summary>
        private string[] ClipNames(out int currentIndex)
        {
            currentIndex = -1;
            if (m_Animator == null || m_Animator.clips == null)
                return Array.Empty<string>();

            var names = new List<string>(m_Animator.clips.Count);
            for (int i = 0; i < m_Animator.clips.Count; i++)
            {
                var clip = m_Animator.clips[i];
                if (clip == null)
                    continue;
                if (string.Equals(clip.name, m_Animator.current, StringComparison.Ordinal))
                    currentIndex = names.Count;
                names.Add(clip.name);
            }

            return names.ToArray();
        }

        private void SelectClip(string clipName)
        {
            if (m_Animator == null)
                return;

            Undo.RecordObject(m_Animator, "Select Pose Clip");
            m_Animator.current = clipName;
            EditorUtility.SetDirty(m_Animator);
            m_Selected = -1;
            ClearPollCaches();
            Refresh();
        }

        private void SetClipLength(PoseClipAsset clip, float length)
        {
            Undo.RecordObject(clip, "Pose Clip Length");
            clip.length = Mathf.Clamp(length, PoseSheetState.ClipLengthMin, PoseSheetState.ClipLengthMax);
            EditorUtility.SetDirty(clip);
            Repaint();
        }

        private void SetClipLooping(PoseClipAsset clip, bool looping)
        {
            Undo.RecordObject(clip, "Pose Clip Looping");
            clip.looping = looping;
            EditorUtility.SetDirty(clip);
            Repaint();
        }

        /// <summary>Port of `_new_clip` (lines 167-178). Godot builds a clip Resource in memory;
        /// a Unity clip is an asset file, created next to the rig's own assets (the rule the Rig
        /// tool uses for RigAsset) and registered on the animator as one undo step.</summary>
        private void CreateClip()
        {
            if (m_Animator == null)
                return;

            var name = (m_NewClipName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                var count = m_Animator.clips != null ? m_Animator.clips.Count : 0;
                name = $"clip{count + 1}";
            }

            var folder = ResolveClipFolder();
            var clip = CreateInstance<PoseClipAsset>();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SanitizeFileName(name)}.asset");
            clip.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(clip, path);

            var group = BeginUndoGroup("New Pose Clip");
            Undo.RecordObject(m_Animator, "New Pose Clip");
            if (m_Animator.clips == null)
                m_Animator.clips = new List<PoseClipAsset>();
            m_Animator.clips.Add(clip);
            m_Animator.current = clip.name;
            EditorUtility.SetDirty(m_Animator);
            Undo.CollapseUndoOperations(group);

            m_NewClipName = string.Empty;
            m_Selected = -1;
            ClearPollCaches();
            GUI.FocusControl(null);
            Refresh();
        }

        /// <summary>Next to the rig asset the animator drives, else next to a drawn shape's asset
        /// (so a character's clips live with its drawing), else the shared Drawn folder.</summary>
        private string ResolveClipFolder()
        {
            var root = ResolveRoot(m_Animator);
            string assetPath = null;

            if (root != null)
            {
                var rig = root.GetComponentInChildren<ShapeRig>(true);
                if (rig != null && rig.rig != null)
                    assetPath = AssetDatabase.GetAssetPath(rig.rig);

                if (string.IsNullOrEmpty(assetPath))
                {
                    var shapes = root.GetComponentsInChildren<DrawnShapeRenderer>(true);
                    for (int i = 0; i < shapes.Length && string.IsNullOrEmpty(assetPath); i++)
                    {
                        if (shapes[i] != null && shapes[i].asset != null)
                            assetPath = AssetDatabase.GetAssetPath(shapes[i].asset);
                    }
                }
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                var directory = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    var folder = directory.Replace('\\', '/');
                    if (AssetDatabase.IsValidFolder(folder))
                        return folder;
                }
            }

            return EnsureDrawnFolder();
        }

        /// <summary>Create every missing folder along Assets/DrawToPlayExamples/Drawn — same recipe as the
        /// Draw / Paint / Rig tools (their copies are private to those files).</summary>
        private static string EnsureDrawnFolder()
        {
            var segments = k_DrawnAssetFolder.Split('/');
            var path = segments[0]; // "Assets"
            for (int i = 1; i < segments.Length; i++)
            {
                var next = $"{path}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(path, segments[i]);
                if (!AssetDatabase.IsValidFolder(next))
                    return path;
                path = next;
            }

            return path;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "PoseClip";

            var invalid = Path.GetInvalidFileNameChars();
            var buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalid, buffer[i]) >= 0)
                    buffer[i] = '_';
            }

            return new string(buffer);
        }

        // --- undo-wrapped pose ops (port of `_wrap`, lines 302-317) -----------------------

        /// <summary>Godot snapshots the clip, runs the op, and pushes a copy_from/copy_from pair
        /// into the plugin UndoRedo. Unity's <see cref="Undo.RecordObject"/> before the mutation
        /// is the direct equivalent, so the whole gesture is ONE undo step.</summary>
        private void WrapClipEdit(string action, Action op)
        {
            var clip = CurrentClip();
            if (clip == null)
                return;

            var group = BeginUndoGroup(action);
            Undo.RecordObject(clip, action);
            op();
            EditorUtility.SetDirty(clip);
            Undo.CollapseUndoOperations(group);

            Refresh();
        }

        /// <summary>`_key_pose` (lines 320-324): capture exactly where the playhead is.</summary>
        private void KeyPose()
        {
            if (m_Animator == null)
                return;

            var time = m_Animator.time;
            WrapClipEdit("Key Pose", () => m_Animator.CapturePose(time));
        }

        /// <summary>`_dup_pose` (lines 327-333). PoseChannel is a struct, so copying the channel
        /// list IS Godot's `duplicate(true)`.</summary>
        private void DuplicatePose()
        {
            var clip = CurrentClip();
            if (clip == null || m_Selected < 0 || m_Selected >= clip.poseCount || m_Animator == null)
                return;

            var time = m_Animator.time;
            var source = clip.poses[m_Selected];
            var copy = new PoseColumn { channels = new List<PoseChannel>(source.channels) };
            WrapClipEdit("Duplicate Pose", () => m_Selected = clip.InsertPose(time, copy));
        }

        /// <summary>`_del_pose` (lines 336-342).</summary>
        private void DeletePose()
        {
            var clip = CurrentClip();
            if (clip == null || m_Selected < 0 || m_Selected >= clip.poseCount)
                return;

            var index = m_Selected;
            m_Selected = -1;
            WrapClipEdit("Delete Pose", () => clip.RemovePose(index));
        }

        // --- preview ----------------------------------------------------------------------

        /// <summary>`_play_btn.toggled` (lines 90-94): restart from 0 when the playhead is parked
        /// at (or past) the end.</summary>
        private void TogglePreview(bool on)
        {
            if (m_Animator == null)
                return;

            m_Preview = on;
            m_Animator.playing = on;

            if (on)
            {
                var clip = CurrentClip();
                var length = clip != null ? clip.length : 1f;
                if (m_Animator.time >= length)
                    m_Animator.time = 0f;

                // A preview is not authoring input — drop the auto-key baseline so the frames it
                // paints are never mistaken for a rig edit (Godot clears `_auto_prev` on the same
                // transitions inside `_autokey_poll`).
                m_AutoPrevious.Clear();
                ApplyAt(m_Animator.time);
            }

            m_LastUpdate = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void StopPreview()
        {
            if (!m_Preview)
                return;

            m_Preview = false;
            if (m_Animator != null)
                m_Animator.playing = false;
        }

        /// <summary>Move the playhead and push the pose onto the rig. <see cref="PoseAnimator.Seek"/>
        /// is the 1:1 port of Godot's `seek()` and applies internally (it is `time = t; Apply();`),
        /// so nothing calls Apply a second time — a scrub frame would otherwise re-run the morph
        /// Regenerate, and that rebuilds skins.</summary>
        private void ApplyAt(float time)
        {
            if (m_Animator == null)
                return;

            m_Animator.Seek(time);
            SceneView.RepaintAll();
        }

        /// <summary>A skinned shape's skin layer is HideFlags.DontSave, so a freshly opened scene
        /// has none, and <see cref="PoseAnimator.Apply"/> deliberately never creates objects — a
        /// morph scrub would silently show the un-skinned base shape. Rebuilding the skins once
        /// per binding (from an editor tick, never from OnEnable) is the fix.</summary>
        private void RefreshSkinLayers()
        {
            var root = ResolveRoot(m_Animator);
            if (root == null)
                return;

            var skins = root.GetComponentsInChildren<DrawnShapeSkin>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                // NOT gated on isSkinned: that property is false precisely in the case this
                // fixes (no skin renderer yet, because the DontSave layer was never saved).
                if (skins[i] != null && skins[i].rig != null)
                    skins[i].RegenerateSkin();
            }
        }

        // --- editor update: preview advance + auto-key poll --------------------------------

        private void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = now - m_LastUpdate;
            m_LastUpdate = now;
            if (delta < 0d)
                delta = 0d;
            if (delta > k_MaxPreviewDelta)
                delta = k_MaxPreviewDelta;

            if (m_SkinRefreshPending)
            {
                m_SkinRefreshPending = false;
                RefreshSkinLayers();
            }

            AdvancePreview((float)delta);

            if (now < m_NextPoll)
                return;
            m_NextPoll = now + k_PollInterval;

            AutoKeyPoll();
        }

        /// <summary>Drive the animator's own `_process` body — <see cref="PoseAnimator.Advance"/>,
        /// which advances the base clip AND every layer and re-applies. Unity gives a plain
        /// MonoBehaviour no edit-mode Update, so the window ticks it; in play mode the component's
        /// Update already does and this only repaints (Godot `_process` lines 184-187).</summary>
        private void AdvancePreview(float delta)
        {
            if (m_Animator == null)
                return;

            // The animator stopping itself (a non-looping clip reaching its end) un-toggles the
            // button, exactly like `_play_btn.set_pressed_no_signal(false)`.
            if (!m_Animator.playing)
            {
                if (m_Preview)
                {
                    m_Preview = false;
                    Repaint();
                }
                return;
            }

            if (!m_Preview)
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Repaint();
                return;
            }

            m_Animator.Advance(delta);
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>Port of `_autokey_poll` (lines 196-222): while armed and idle, snapshot the
        /// rig each tick and capture a pose the moment any channel moves. Repeated captures at the
        /// same playhead REPLACE the same column (PoseClipAsset.InsertPose merges within eps), so
        /// dragging a bone converges on one key instead of spraying them.</summary>
        private void AutoKeyPoll()
        {
            if (!PoseSheetState.autoKey || m_Animator == null)
                return;

            // Godot line 199 + terrain_paint.gd's "never bake a playing animation" rule.
            if (m_Scrubbing || m_Dragging || m_Preview || m_Animator.playing ||
                EditorApplication.isPlayingOrWillChangePlaymode || PoseSheetState.AnyPreviewPlaying())
            {
                m_AutoPrevious.Clear();
                return;
            }

            var clip = CurrentClip();
            if (clip == null)
            {
                m_AutoPrevious.Clear();
                return;
            }

            var root = ResolveRoot(m_Animator);
            if (root == null)
                return;

            // pose_sheet.gd line 206 calls the animator's own `_collect`; CollectPose is that walk
            // exactly (bones the RigAsset lists + every DrawnShapeRenderer's transform and morph)
            // and touches no clip, which is what lets the poll look without keying.
            m_Animator.CollectPose(m_CollectBuffer);
            m_AutoCurrent.Clear();
            for (int i = 0; i < m_CollectBuffer.channels.Count; i++)
            {
                var channel = m_CollectBuffer.channels[i];
                m_AutoCurrent[channel.path] = channel;
            }

            // Godot runs the form poll on every tick, including the priming one.
            PollShapeForms(root, clip);

            if (m_AutoPrevious.Count == 0)
            {
                CopySamples(m_AutoCurrent, m_AutoPrevious);
                return;
            }

            var changed = false;
            foreach (var pair in m_AutoCurrent)
            {
                if (!m_AutoPrevious.TryGetValue(pair.Key, out var previous)
                    || !SameValue(pair.Key, previous, pair.Value))
                {
                    changed = true;
                    break;
                }
            }

            CopySamples(m_AutoCurrent, m_AutoPrevious);
            if (!changed)
                return;

            // Deviation from Godot (which records no undo for auto-keys): the M4 conventions ask
            // for one undo step per detected change, so the capture is wrapped like a manual Key.
            var group = BeginUndoGroup("Auto-key Pose");
            Undo.RecordObject(clip, "Auto-key Pose");
            m_Animator.CapturePose(m_Animator.time);
            EditorUtility.SetDirty(clip);
            Undo.CollapseUndoOperations(group);

            SetStatus($"{clip.poseCount} poses (auto-keyed)");
            Repaint();
        }

        /// <summary>Form tracking — port of `_shape_poll` (lines 229-260). While recording,
        /// editing a shape's CURVE becomes an animated form change automatically: the pre-edit
        /// outline is pinned as a variant (so existing poses keep showing what they were authored
        /// with), the post-edit outline becomes a new variant, morph points at it, and the pose is
        /// keyed.</summary>
        private void PollShapeForms(Transform root, PoseClipAsset clip)
        {
            var shapes = root.GetComponentsInChildren<DrawnShapeRenderer>(true);
            for (int i = 0; i < shapes.Length; i++)
            {
                var shape = shapes[i];
                if (shape == null || shape.transform == root)
                    continue;               // `_find_shapes` walks base's CHILDREN, not base
                var asset = shape.asset;
                var curve = asset != null ? asset.curve : null;
                if (curve == null || curve.pointCount == 0)
                    continue;

                var signature = curve.Signature();
                var id = shape.GetEntityId();

                if (!m_ShapeSignatures.TryGetValue(id, out var record))
                {
                    m_ShapeSignatures[id] = new FormSignature(signature, curve.Clone());
                    continue;
                }

                if (SignaturesEqual(record.signature, signature))
                    continue;

                var group = BeginUndoGroup("Auto-key Form");
                Undo.RecordObject(asset, "Auto-key Form");
                Undo.RecordObject(shape, "Auto-key Form");
                Undo.RecordObject(clip, "Auto-key Form");

                var targets = asset.morphTargets != null
                    ? new List<DrawnCurve>(asset.morphTargets)
                    : new List<DrawnCurve>();

                // Pin the PRE-edit form only when this shape is currently ON its base form: poses
                // that referenced the live base (morph 0, or no morph key at all) would otherwise
                // start showing the NEW outline, so they are retargeted at the pinned variant.
                if (shape.morphWeight <= k_ZeroMorph)
                {
                    targets.Add(record.snapshot);
                    RetargetZeroMorphs(clip, shape.name, targets.Count);
                }

                // The POST-edit form becomes the variant this pose uses.
                targets.Add(curve.Clone());
                asset.morphTargets = targets;
                shape.morphWeight = targets.Count;

                EditorUtility.SetDirty(asset);
                EditorUtility.SetDirty(shape);
                shape.Regenerate();      // fires geometryChanged → skin / paint layers follow

                m_Animator.CapturePose(m_Animator.time);
                EditorUtility.SetDirty(clip);
                Undo.CollapseUndoOperations(group);

                m_ShapeSignatures[id] = new FormSignature(curve.Signature(), curve.Clone());
                SetStatus($"form tracked (variant {targets.Count})");
                Repaint();
                SceneView.RepaintAll();
            }
        }

        /// <summary>Port of `_retarget_zero_morphs` (lines 270-280). Every pose that did NOT state
        /// a morph for this shape — or stated 0, i.e. "the live base outline" — is rewritten to
        /// point at the freshly pinned pre-edit variant, so redrawing a shape mid-animation never
        /// retroactively changes the poses already authored against its old form.
        ///
        /// <paramref name="index"/> is the 1-BASED position of the pinned variant, matching the
        /// morph scale (0 = base, N = fully morphTargets[N-1]).</summary>
        private static void RetargetZeroMorphs(PoseClipAsset clip, string shapeName, int index)
        {
            if (clip == null || clip.poses == null)
                return;

            var key = $"{shapeName}:morph";
            for (int p = 0; p < clip.poses.Count; p++)
            {
                var column = clip.poses[p];
                if (column == null || column.channels == null)
                    continue;

                var found = -1;
                for (int c = 0; c < column.channels.Count; c++)
                {
                    if (string.Equals(column.channels[c].path, key, StringComparison.Ordinal))
                    {
                        found = c;
                        break;
                    }
                }

                if (found < 0)
                {
                    // no key = authored with the (pre-edit) base form implicitly
                    column.channels.Add(PoseChannel.Float(key, index));
                    continue;
                }

                if (column.channels[found].floatValue <= k_ZeroMorph)
                    column.channels[found] = PoseChannel.Float(key, index);
            }
        }

        // --- timeline ---------------------------------------------------------------------

        /// <summary>`_t2x` (lines 347-348).</summary>
        private static float TimeToX(float time, PoseClipAsset clip, Rect rect)
            => rect.x + k_Pad + time / Mathf.Max(clip.length, 0.001f) * (rect.width - k_Pad * 2f);

        /// <summary>`_x2t` (lines 351-352).</summary>
        private static float XToTime(float x, PoseClipAsset clip, Rect rect)
            => Mathf.Clamp01((x - rect.x - k_Pad) / Mathf.Max(rect.width - k_Pad * 2f, 1f)) * clip.length;

        /// <summary>`_tl_draw` (lines 355-395).</summary>
        private void DrawTimeline(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, k_Background);

            var clip = CurrentClip();
            if (clip == null)
            {
                var message = m_Animator == null
                    ? "bind a PoseAnimator and create a clip"
                    : "no clip - create one";
                GUI.Label(new Rect(rect.x + k_Pad, rect.y + rect.height * 0.55f, rect.width - k_Pad * 2f, 18f),
                    message, m_HintStyle);
                return;
            }

            var mid = rect.y + rect.height * k_MidFraction;

            // ruler
            var step = Mathf.Max(PoseSheetState.snap, k_MinTickStep);
            while (step * k_MaxTicks < clip.length)
                step *= 2f;

            var tick = 0;
            for (var t = 0f; t <= clip.length + 0.001f; t += step, tick++)
            {
                var tx = TimeToX(t, clip, rect);
                var big = tick % k_BigTickEvery == 0;
                EditorGUI.DrawRect(new Rect(tx, rect.y + 4f, 1f, big ? 8f : 4f), big ? k_TickBig : k_TickSmall);
                if (big)
                    GUI.Label(new Rect(tx + 2f, rect.y + 2f, 48f, 12f),
                        t.ToString("0.00", CultureInfo.InvariantCulture), m_TickStyle);
            }

            EditorGUI.DrawRect(new Rect(rect.x + k_Pad, mid - 1f, rect.width - k_Pad * 2f, 2f), k_Lane);

            // pose diamonds
            for (int p = 0; p < clip.poseCount; p++)
            {
                var time = m_Dragging && p == m_Selected ? m_DragTime : clip.times[p];
                var x = TimeToX(time, clip, rect);
                DrawDiamond(new Vector2(x, mid), k_DiamondRadius,
                    p == m_Selected ? k_DiamondSelected : k_Diamond);
            }

            // playhead
            if (m_Animator != null)
            {
                var px = TimeToX(Mathf.Clamp(m_Animator.time, 0f, clip.length), clip, rect);
                EditorGUI.DrawRect(new Rect(px - 1f, rect.y + 2f, 2f, rect.height - 4f), k_Playhead);
            }
        }

        /// <summary>Godot fills a 4-point polygon; IMGUI has no polygon primitive, so the diamond
        /// is a square rotated 45° about its centre (side = radius * √2 puts the corners back on
        /// the axes at exactly `radius`).</summary>
        private static void DrawDiamond(Vector2 center, float radius, Color color)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, center);
            var side = radius * 1.41421356f;
            EditorGUI.DrawRect(new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side), color);
            GUI.matrix = matrix;
        }

        /// <summary>`_tl_input` (lines 398-433): click picks a diamond (else scrubs), drag moves it
        /// live, release commits the move snapped to the grid.</summary>
        private void HandleTimelineInput(Rect rect)
        {
            var control = GUIUtility.GetControlID(FocusType.Passive);
            var clip = CurrentClip();
            if (clip == null || m_Animator == null)
                return;

            var e = Event.current;
            var mid = rect.y + rect.height * k_MidFraction;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !rect.Contains(e.mousePosition))
                        break;

                    m_Selected = -1;
                    for (int p = 0; p < clip.poseCount; p++)
                    {
                        var x = TimeToX(clip.times[p], clip, rect);
                        if (Vector2.Distance(new Vector2(x, mid), e.mousePosition) < k_PickRadius)
                            m_Selected = p;     // no break: Godot keeps the LAST match
                    }

                    if (m_Selected >= 0)
                    {
                        m_Dragging = true;
                        m_DragTime = clip.times[m_Selected];
                    }
                    else
                    {
                        m_Scrubbing = true;
                        m_AutoPrevious.Clear();
                        ApplyAt(XToTime(e.mousePosition.x, clip, rect));
                    }

                    GUIUtility.hotControl = control;
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != control)
                        break;

                    if (m_Dragging)
                    {
                        m_DragTime = Mathf.Clamp(XToTime(e.mousePosition.x, clip, rect), 0f, clip.length);
                        e.Use();
                        Repaint();
                    }
                    else if (m_Scrubbing)
                    {
                        ApplyAt(XToTime(e.mousePosition.x, clip, rect));
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != control)
                        break;

                    GUIUtility.hotControl = 0;
                    if (m_Dragging)
                    {
                        m_Dragging = false;
                        var index = m_Selected;
                        var to = PoseSheetState.SnapTo(m_DragTime);
                        WrapClipEdit("Move Pose", () => m_Selected = clip.MovePose(index, to));
                    }

                    m_Scrubbing = false;
                    m_AutoPrevious.Clear();
                    e.Use();
                    Repaint();
                    break;
            }
        }

        // --- shared plumbing --------------------------------------------------------------

        /// <summary>`_refresh` (lines 144-164) minus the widget rebuilds IMGUI does not need.</summary>
        private void Refresh()
        {
            if (m_Animator == null)
            {
                SetStatus("select a PoseAnimator node");
            }
            else
            {
                var clip = CurrentClip();
                SetStatus(clip != null ? $"{clip.poseCount} poses" : "no clip - create one");
            }

            Repaint();
        }

        private void SetStatus(string text)
        {
            PoseSheetState.status = text;
        }

        /// <summary>`_auto_btn.toggled` (lines 102-104): arming or disarming drops BOTH caches, so
        /// nothing that happened while the arm was off is mistaken for a fresh edit.</summary>
        private void ToggleAutoKey(bool on)
        {
            PoseSheetState.autoKey = on;
            ClearPollCaches();
            Repaint();
        }

        private void ClearPollCaches()
        {
            m_AutoPrevious.Clear();
            m_AutoCurrent.Clear();
            m_ShapeSignatures.Clear();
        }

        private void OnSelectionChanged()
        {
            BindFromSelection();
            Refresh();
        }

        /// <summary>Undo restores the clip, the morph targets and the rig transforms; anything
        /// derived from them (the render mesh, and through it the skin) has to be rebuilt — the
        /// same rule the Draw / Paint / Rig tools follow.</summary>
        private void OnUndoRedoPerformed()
        {
            ClearPollCaches();

            var root = ResolveRoot(m_Animator);
            if (root != null)
            {
                var shapes = root.GetComponentsInChildren<DrawnShapeRenderer>(true);
                for (int i = 0; i < shapes.Length; i++)
                {
                    if (shapes[i] != null)
                        shapes[i].Regenerate();
                }
            }

            Refresh();
            SceneView.RepaintAll();
        }

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        private static void CopySamples(Dictionary<string, PoseChannel> from,
            Dictionary<string, PoseChannel> into)
        {
            into.Clear();
            foreach (var pair in from)
                into[pair.Key] = pair.Value;
        }

        /// <summary>Godot compares `PackedFloat32Array`s exactly; so does this.</summary>
        private static bool SignaturesEqual(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        /// <summary>Port of `_same_val` (lines 292-297) with this project's units: 0.01 Godot px
        /// of movement, 0.0005 rad of rotation (in degrees here), and the raw 0.01 / 0.0005 for
        /// the dimensionless channels. Position and scale share the Vector2 kind, so the path's
        /// ":scale" suffix picks which of the two Vector2 epsilons applies.</summary>
        private static bool SameValue(string path, PoseChannel a, PoseChannel b)
        {
            if (a.kind != b.kind)
                return false;

            switch (a.kind)
            {
                case PoseChannel.Kind.Vector2:
                    var epsilon = path != null && path.EndsWith(k_ScaleSuffix, StringComparison.Ordinal)
                        ? k_ScaleEpsilon
                        : k_PositionEpsilon;
                    return Vector2.Distance(a.vectorValue, b.vectorValue) < epsilon;
                case PoseChannel.Kind.Angle:
                    return Mathf.Abs(Mathf.DeltaAngle(a.floatValue, b.floatValue)) < k_AngleEpsilonDegrees;
                default:
                    return Mathf.Abs(a.floatValue - b.floatValue) < k_FloatEpsilon;
            }
        }

        /// <summary>`_shape_sigs[id]` — the control-point signature plus the pre-edit outline that
        /// gets pinned as a variant when the shape is redrawn.</summary>
        private sealed class FormSignature
        {
            public readonly float[] signature;
            public readonly DrawnCurve snapshot;

            public FormSignature(float[] signature, DrawnCurve snapshot)
            {
                this.signature = signature;
                this.snapshot = snapshot;
            }
        }
    }
}
