using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view Rig tool — the Unity port of terrain_paint.gd's RIG mode (_rig_input lines
    /// 277-291, _commit_rig_joints lines 297-372, _bind_shape lines 374-401, plus the joint
    /// preview in _forward_canvas_draw_over_viewport lines 833-842). Click a chain of joints
    /// over a drawn limb; Enter (or a double-click) turns the polyline into a bone chain on a
    /// sibling "Skeleton" object and binds the shape to it.
    ///
    /// Godot builds Skeleton2D + nested Bone2D nodes. Here the same data lives in a
    /// <see cref="RigAsset"/> (rest pose + length per bone, parent by index) which
    /// <see cref="ShapeRig"/> materializes as plain child Transforms — the brief's
    /// "no Skeleton2D equivalent needed" (draw-tool-port-brief.md §4). Everything else is
    /// ported verbatim: joints are recorded in the shape's PARENT space, rests accumulate down
    /// the chain, chain bones are named "{ShapeName}Bone{N}", and re-rigging a shape REPLACES
    /// its previous chain instead of piling stale bones on top of it.
    ///
    /// Input mapping deviations from Godot (see the M3 report):
    ///  - Godot commits when the toolbar's Rig toggle turns off (Enter flips that toggle). Here
    ///    Enter/KP-Enter and a double-click commit, and the tool STAYS armed afterwards so
    ///    several limbs can be rigged in one go; Esc clears pending joints, then exits the tool.
    ///  - The tool is a component tool bound to <see cref="DrawnShapeRenderer"/> (like the Paint
    ///    tool), so it is only offered while a drawn shape is selected.
    ///  - Godot bails when the target's parent is not a Node2D; a Unity shape at the scene root
    ///    has no parent Transform at all, so "parent space" simply degrades to world space and
    ///    the Skeleton object is created as a root sibling.
    ///
    /// This class also owns SETUP MODE (_process lines 474-496 + _reset_rests lines 549-563):
    /// while the overlay toggle is on, an editor-update poll writes moved bone Transforms back
    /// into the asset's rests and re-generates every skin bound to that rig — "rests follow
    /// bones" while you are building the rig.
    /// </summary>
    [EditorTool("Rig Shape", typeof(DrawnShapeRenderer))]
    public sealed class RigShapeTool : EditorTool
    {
        private static readonly int s_ControlHint = "PowerOfFire.DrawToPlay.RigShapeTool".GetHashCode();

        private const string k_DrawnAssetFolder = "Assets/DrawToPlay/Drawn";

        /// <summary>Godot names the created node "Skeleton2D"; the Unity rig root is a plain
        /// GameObject, so it takes the plainer name.</summary>
        private const string k_SkeletonName = "Skeleton";

        private const string k_CommitLabel = "Create Bone Chain";
        private const string k_SetupLabel = "Setup Bone Rests";

        /// <summary>Joint / chain preview colours from _forward_canvas_draw_over_viewport
        /// (lines 836-842): the committed polyline and its joints at alpha 0.9, the dashed
        /// cursor segment at 0.5.</summary>
        private static readonly Color k_ChainColor = new Color(0.55f, 0.8f, 1f, 0.9f);
        private static readonly Color k_CursorColor = new Color(0.55f, 0.8f, 1f, 0.5f);

        /// <summary>Godot `draw_circle(v, 4.0, ...)` / `draw_line(..., 2.0)` /
        /// `draw_dashed_line(..., 1.5, 6.0)` — screen pixels, converted per draw.</summary>
        private const float k_JointDotPixels = 4f;
        private const float k_ChainWidth = 2f;
        private const float k_CursorDashSize = 6f;

        /// <summary>Pending joints in the shape's PARENT space (Godot `_rig_joints`).</summary>
        private readonly List<Vector2> m_Joints = new List<Vector2>();

        private DrawnShapeRenderer m_Shape;
        private Matrix4x4 m_ParentToWorld = Matrix4x4.identity;
        private Matrix4x4 m_WorldToParent = Matrix4x4.identity;
        private Vector3 m_PlaneOrigin = Vector3.zero;
        private Vector3 m_PlaneNormal = Vector3.forward;

        /// <summary>Parent-space Z of the shape's drawing plane. Joints are 2D, but the preview
        /// has to be drawn back on the plane the user clicked on, which is the shape's plane —
        /// not necessarily the parent's Z = 0 plane. (Godot is flat 2D and has no such case.)</summary>
        private float m_ParentPlaneZ;

        private bool m_CursorValid;
        private Vector2 m_CursorGuiPoint;

        private GUIContent m_ToolbarIcon;

        public override GUIContent toolbarIcon => m_ToolbarIcon ??= DrawToolSettings.BuildToolbarIcon(
            "Avatar Icon",
            "Rig",
            "Rig Shape: click a chain of joints along the limb, Enter (or double-click) to build " +
            "the bone chain and bind the shape to it. Esc clears the pending joints.");

        public override void OnActivated()
        {
            CacheSpaces(ResolveTarget());
            m_Joints.Clear();
            m_CursorValid = false;

            // Undo restores the rig asset and the bone objects, but the skinned mesh derived
            // from them is not serialized — it has to be rebuilt (same rule as the other tools).
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            m_Joints.Clear();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var currentEvent = Event.current;

            if (currentEvent.isMouse)
            {
                m_CursorGuiPoint = currentEvent.mousePosition;
                m_CursorValid = true;
                sceneView.Repaint();
            }

            // Key events are read from the RAW type: Event.GetTypeForControl masks keyboard
            // events away from controls that do not hold keyboardControl, and a passive scene
            // tool never does (same note as PaintShapeTool).
            if (currentEvent.type == EventType.KeyDown && HandleKeyDown(currentEvent))
            {
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            var controlId = GUIUtility.GetControlID(s_ControlHint, FocusType.Passive);

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    // Claim the fallback control so a click in empty space adds a joint instead
                    // of clearing the selection (Godot consumes the same events in _rig_input).
                    HandleUtility.AddDefaultControl(controlId);
                    CacheSpaces(ResolveTarget());
                    break;

                case EventType.MouseDown:
                    if (currentEvent.button != 0 || currentEvent.alt || HandleUtility.nearestControl != controlId)
                        break;

                    // Second click of a double-click commits instead of stacking a duplicate
                    // joint on top of the one the first click already added.
                    if (currentEvent.clickCount >= 2)
                        Commit();
                    else
                        m_Joints.Add(ScreenToParent(currentEvent.mousePosition));

                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseDrag:
                    // Rig mode is click-only in Godot; swallow the drag so it cannot start a
                    // rubber-band selection behind the tool.
                    if (GUIUtility.hotControl == controlId)
                        currentEvent.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;

                case EventType.Repaint:
                    DrawPreview();
                    break;
            }
        }

        /// <summary>Port of the key branch of _rig_input (lines 286-290) plus the plugin's Esc
        /// handling: Enter commits, Esc drops the pending joints and then leaves the tool.</summary>
        private bool HandleKeyDown(Event currentEvent)
        {
            switch (currentEvent.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Commit();
                    return true;

                case KeyCode.Escape:
                    if (m_Joints.Count > 0)
                    {
                        m_Joints.Clear();
                        return true;
                    }

                    Tools.current = Tool.Move;
                    return true;
            }

            return false;
        }

        // --- commit -------------------------------------------------------------------------

        /// <summary>Port of _commit_rig_joints (lines 297-372): build the bone chain from the
        /// clicked joints, replacing whatever chain this shape was bound to before, then bind
        /// the shape to it. Everything lands in ONE collapsed undo group.</summary>
        private void Commit()
        {
            var shape = m_Shape != null ? m_Shape : ResolveTarget();

            // Godot: `if joints.size() < 2 or _target == null: return` — the pending joints are
            // kept so a stray Enter does not throw the work away.
            if (shape == null || m_Joints.Count < 2)
                return;

            CacheSpaces(shape);

            var group = BeginUndoGroup(k_CommitLabel);

            var shapeRig = FindSiblingRig(shape);
            if (shapeRig == null)
                shapeRig = CreateSiblingRig(shape);

            var rigAsset = shapeRig.rig;
            if (rigAsset == null)
            {
                rigAsset = CreateRigAsset(shape);
                Undo.RecordObject(shapeRig, k_CommitLabel);
                shapeRig.rig = rigAsset;
                EditorUtility.SetDirty(shapeRig);
            }

            Undo.RecordObject(rigAsset, k_CommitLabel);

            var asset = shape.asset;
            var bones = rigAsset.bones != null
                ? new List<RigAsset.RigBone>(rigAsset.bones)
                : new List<RigAsset.RigBone>();

            // Re-rigging a shape REPLACES its previous chain (lines 336-347) — no stale bones,
            // no need to delete the skeleton to start over.
            var removedNames = new List<string>();
            RemovePreviousChain(bones, asset != null ? asset.includeBones : null, removedNames);
            DestroyBoneObjects(shapeRig, removedNames);

            var chainNames = AppendChain(bones, shape, shapeRig);
            rigAsset.bones = bones;
            EditorUtility.SetDirty(rigAsset);

            // Materialize the bone Transforms, then put the ones SyncBones just created into the
            // undo group (SyncBones is runtime code and knows nothing about Undo).
            var before = CollectDescendants(shapeRig.transform);
            shapeRig.SyncBones();
            RegisterCreatedBones(shapeRig.transform, before);

            // The new chain belongs to the selected shape ONLY: bind it with an include list so
            // other chains never affect this part (lines 359-372).
            if (asset != null)
                BindShapeToRig(shape, shapeRig, chainNames, true, k_CommitLabel);

            Undo.CollapseUndoOperations(group);

            m_Joints.Clear();
            SceneView.RepaintAll();
        }

        /// <summary>Port of the bone-building loop (lines 315-331): each bone's rest is measured
        /// in the space accumulated from the rig root down the chain, so nesting the Transforms
        /// reproduces the clicked polyline exactly.</summary>
        private List<string> AppendChain(List<RigAsset.RigBone> bones, DrawnShapeRenderer shape, ShapeRig shapeRig)
        {
            var chainNames = new List<string>(Mathf.Max(m_Joints.Count - 1, 0));

            var used = new HashSet<string>();
            for (int i = 0; i < bones.Count; i++)
                used.Add(bones[i].name);

            // Godot: `var acc := Transform2D() if created else skel.transform`. The rig root's
            // parent-space matrix covers both cases — a freshly created Skeleton is parented with
            // worldPositionStays:false, so its local matrix IS the identity.
            var acc = LocalMatrix(shapeRig.transform);
            int parentIndex = -1;

            for (int i = 0; i < m_Joints.Count - 1; i++)
            {
                var inverse = acc.inverse;
                var localA = (Vector2)inverse.MultiplyPoint3x4(m_Joints[i]);
                var localDir = (Vector2)inverse.MultiplyVector(m_Joints[i + 1] - m_Joints[i]);
                var rotation = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;

                var name = UniqueBoneName(used, $"{shape.name}Bone{i + 1}");
                used.Add(name);

                bones.Add(new RigAsset.RigBone
                {
                    name = name,
                    parentIndex = parentIndex,
                    restPosition = localA,
                    restRotationDegrees = rotation,
                    // Godot disables autocalculate and sets the length explicitly.
                    length = localDir.magnitude
                });

                parentIndex = bones.Count - 1;
                chainNames.Add(name);

                acc *= Matrix4x4.TRS(localA, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
            }

            return chainNames;
        }

        /// <summary>Port of the old-chain removal rule (lines 336-347): drop every bone that is
        /// in the shape's previous include list and whose parent is NOT — i.e. the ROOTS of that
        /// list — together with their subtrees, exactly as removing a Bone2D node takes its
        /// children with it. Parent indices are remapped onto the compacted list.</summary>
        private static void RemovePreviousChain(List<RigAsset.RigBone> bones, List<string> previousInclude,
            List<string> removedNames)
        {
            if (bones.Count == 0 || previousInclude == null || previousInclude.Count == 0)
                return;

            var include = new HashSet<string>(previousInclude);
            var remove = new HashSet<int>();

            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                if (!include.Contains(bone.name))
                    continue;

                // A bone whose parent is also in the list is not a root — it goes away with the
                // subtree of the root above it.
                if (bone.parentIndex >= 0 && bone.parentIndex < bones.Count &&
                    include.Contains(bones[bone.parentIndex].name))
                    continue;

                MarkSubtree(bones, i, remove);
            }

            if (remove.Count == 0)
                return;

            var remap = new int[bones.Count];
            var kept = new List<RigAsset.RigBone>(bones.Count - remove.Count);
            for (int i = 0; i < bones.Count; i++)
            {
                if (remove.Contains(i))
                {
                    remap[i] = -1;
                    removedNames.Add(bones[i].name);
                    continue;
                }

                remap[i] = kept.Count;
                kept.Add(bones[i]);
            }

            // Removal is subtree-closed, so a surviving bone's parent always survived too.
            for (int i = 0; i < kept.Count; i++)
            {
                var bone = kept[i];
                bone.parentIndex = bone.parentIndex >= 0 ? remap[bone.parentIndex] : -1;
                kept[i] = bone;
            }

            bones.Clear();
            bones.AddRange(kept);
        }

        private static void MarkSubtree(List<RigAsset.RigBone> bones, int root, HashSet<int> remove)
        {
            if (!remove.Add(root))
                return;

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].parentIndex == root)
                    MarkSubtree(bones, i, remove);
            }
        }

        /// <summary>Bone names are the binding key (asset.includeBones), so a duplicate would
        /// silently steer weights at the wrong bone. Godot gets uniqueness for free from
        /// `add_child(bone, true)`; here the name is disambiguated explicitly.</summary>
        private static string UniqueBoneName(HashSet<string> used, string candidate)
        {
            if (!used.Contains(candidate))
                return candidate;

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                var name = $"{candidate}_{suffix}";
                if (!used.Contains(name))
                    return name;
            }

            return $"{candidate}_{System.Guid.NewGuid():N}";
        }

        // --- binding ------------------------------------------------------------------------

        /// <summary>Port of _bind_shape (lines 374-401). `include` empty leaves the shape's bind
        /// list alone (Godot's _bind_selected path: an empty include list means "every bone of
        /// the rig"); non-empty either REPLACES it (the rig-tool commit) or merges additively,
        /// order-preserving and duplicate-free (lines 383-391).
        ///
        /// The Godot "re-stamp degenerate rests" step (lines 394-397) is deliberately skipped:
        /// it repairs Bone2D nodes whose `rest` was never authored, and every RigAsset rest is
        /// written by this tool.
        ///
        /// Returns the bound skin, or null when the rig has no bones (Godot toasts
        /// "Skeleton has no Bone2D bones — use Rig to click a chain first").</summary>
        public static DrawnShapeSkin BindShapeToRig(DrawnShapeRenderer shape, ShapeRig rig,
            IReadOnlyList<string> include, bool replaceInclude, string undoLabel)
        {
            if (shape == null || rig == null)
                return null;
            if (rig.rig == null || rig.rig.bones == null || rig.rig.bones.Count == 0)
                return null;

            var asset = shape.asset;
            if (asset != null && include != null && include.Count > 0)
            {
                Undo.RecordObject(asset, undoLabel);

                var merged = new List<string>();
                if (!replaceInclude && asset.includeBones != null)
                {
                    for (int i = 0; i < asset.includeBones.Count; i++)
                    {
                        var name = asset.includeBones[i];
                        if (!string.IsNullOrEmpty(name) && !merged.Contains(name))
                            merged.Add(name);
                    }
                }

                for (int i = 0; i < include.Count; i++)
                {
                    var name = include[i];
                    if (!string.IsNullOrEmpty(name) && !merged.Contains(name))
                        merged.Add(name);
                }

                asset.includeBones = merged;
                EditorUtility.SetDirty(asset);
            }

            // Godot's bind writes `skeleton_path` on the shape node itself; the Unity skin lives
            // on its own component, so a missing one is added here (undoable).
            var skin = shape.GetComponent<DrawnShapeSkin>();
            if (skin == null)
                skin = Undo.AddComponent<DrawnShapeSkin>(shape.gameObject);

            if (skin.rig != rig)
            {
                Undo.RecordObject(skin, undoLabel);
                skin.rig = rig;
                EditorUtility.SetDirty(skin);
            }

            skin.RegenerateSkin();
            return skin;
        }

        /// <summary>Port of the sibling search in _bind_selected / _commit_rig_joints (lines
        /// 305-310, 393-399): the first sibling carrying a <see cref="ShapeRig"/>. Shapes at the
        /// scene root search the scene's root objects instead.</summary>
        public static ShapeRig FindSiblingRig(DrawnShapeRenderer shape)
        {
            if (shape == null)
                return null;

            var parent = shape.transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    var rig = parent.GetChild(i).GetComponent<ShapeRig>();
                    if (rig != null)
                        return rig;
                }

                return null;
            }

            var roots = shape.gameObject.scene.IsValid()
                ? shape.gameObject.scene.GetRootGameObjects()
                : System.Array.Empty<GameObject>();

            for (int i = 0; i < roots.Length; i++)
            {
                var rig = roots[i] != null ? roots[i].GetComponent<ShapeRig>() : null;
                if (rig != null)
                    return rig;
            }

            return null;
        }

        private static ShapeRig CreateSiblingRig(DrawnShapeRenderer shape)
        {
            var go = new GameObject(k_SkeletonName);
            StageUtility.PlaceGameObjectInCurrentStage(go);

            // worldPositionStays:false — the rig root starts at the parent's origin with an
            // identity local matrix, which is Godot's freshly constructed `Transform2D()`.
            go.transform.SetParent(shape.transform.parent, false);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, k_CommitLabel);

            return Undo.AddComponent<ShapeRig>(go);
        }

        // --- setup mode ---------------------------------------------------------------------

        /// <summary>EditorPrefs key behind <see cref="setupMode"/>. Public so the Draw-to-Play
        /// overlay (and later the Flow window's Rig stage) drives the same toggle.</summary>
        public const string SetupModePrefKey = "PowerOfFire.DrawToPlay.SetupMode";

        /// <summary>~30 Hz. Godot's _process runs every frame; the poll only compares floats,
        /// but an editor-update callback runs even while nothing is happening, so it is
        /// throttled.</summary>
        private const double k_SetupPollInterval = 1.0 / 30.0;

        /// <summary>Rest-vs-pose comparison slack, standing in for Godot's
        /// `Transform2D.is_equal_approx`. Both values are written from the same conversion the
        /// comparison uses, so a settled bone compares exactly equal.</summary>
        private const float k_RestPositionEpsilon = 1e-5f;
        private const float k_RestRotationEpsilonDegrees = 1e-3f;

        private static double s_NextSetupPoll;

        /// <summary>While on, moving a bone writes its rest into the RigAsset and re-generates
        /// every skin bound to that rig — Godot's Setup toggle (_process lines 474-496), i.e.
        /// "rests follow bones" while the rig is being built. The poll is only subscribed while
        /// the toggle is on.</summary>
        public static bool setupMode
        {
            get => EditorPrefs.GetBool(SetupModePrefKey, false);
            set
            {
                if (EditorPrefs.GetBool(SetupModePrefKey, false) == value)
                    return;
                EditorPrefs.SetBool(SetupModePrefKey, value);
                SyncSetupPoll();
            }
        }

        [InitializeOnLoadMethod]
        private static void InitializeSetupMode() => SyncSetupPoll();

        private static void SyncSetupPoll()
        {
            EditorApplication.update -= PollSetupMode;
            if (!setupMode)
                return;

            s_NextSetupPoll = 0d;
            EditorApplication.update += PollSetupMode;
        }

        /// <summary>Port of _process (lines 474-496): every bone whose Transform no longer
        /// matches its rest re-stamps that rest, then the shapes bound to the rig are rebuilt
        /// (_rebind_shapes lines 512-518).</summary>
        private static void PollSetupMode()
        {
            if (!setupMode)
            {
                SyncSetupPoll();
                return;
            }

            if (EditorApplication.timeSinceStartup < s_NextSetupPoll)
                return;
            s_NextSetupPoll = EditorApplication.timeSinceStartup + k_SetupPollInterval;

            // TODO(M4): port _any_anim_playing (lines 503-510) — never bake a PLAYING animation
            // into rests. There is no PoseAnimator/preview playback until M4, so the only
            // equivalent today is play mode, which is guarded below.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var rigs = CollectSetupRigs();
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null || rig.rig == null)
                    continue;
                if (BakeRestsFromPose(rig))
                    RegenerateBoundSkins(rig);
            }
        }

        /// <summary>The rigs the poll watches: whatever the selection points at (a bone, the
        /// Skeleton object itself, or a bound shape). With nothing relevant selected it falls
        /// back to every rig in the stage, which is Godot's behaviour (_find_skeletons walks the
        /// whole edited scene).</summary>
        private static List<ShapeRig> CollectSetupRigs()
        {
            var rigs = new List<ShapeRig>();
            var selection = Selection.gameObjects;

            if (selection != null)
            {
                for (int i = 0; i < selection.Length; i++)
                {
                    var go = selection[i];
                    if (go == null)
                        continue;

                    // A selected bone is a child of the rig root; a selected shape points at its
                    // rig through the skin.
                    var rig = go.GetComponentInParent<ShapeRig>();
                    if (rig == null)
                    {
                        var skin = go.GetComponent<DrawnShapeSkin>();
                        rig = skin != null ? skin.rig : null;
                    }

                    if (rig != null && !rigs.Contains(rig))
                        rigs.Add(rig);
                }
            }

            if (rigs.Count > 0)
                return rigs;

            var stage = StageUtility.GetCurrentStageHandle();
            var all = stage.IsValid() ? stage.FindComponentsOfType<ShapeRig>() : System.Array.Empty<ShapeRig>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    rigs.Add(all[i]);
            }

            return rigs;
        }

        /// <summary>Write every moved bone Transform into the asset's rest data. Returns true
        /// when anything changed. This is _reset_rests' "rest = current transform" rule applied
        /// continuously instead of on demand — the same rule, which is why Setup mode makes the
        /// separate Godot menu entry unnecessary here.</summary>
        private static bool BakeRestsFromPose(ShapeRig rig)
        {
            var asset = rig.rig;
            if (asset == null || asset.bones == null)
                return false;

            var changed = false;
            for (int i = 0; i < asset.bones.Count; i++)
            {
                var bone = asset.bones[i];
                var boneTransform = rig.FindBone(bone.name);
                if (boneTransform == null)
                    continue;

                var position = (Vector2)boneTransform.localPosition;
                var rotation = LocalAngleDegrees(boneTransform);

                if ((position - bone.restPosition).sqrMagnitude <= k_RestPositionEpsilon * k_RestPositionEpsilon &&
                    Mathf.Abs(Mathf.DeltaAngle(bone.restRotationDegrees, rotation)) <= k_RestRotationEpsilonDegrees)
                    continue;

                if (!changed)
                {
                    Undo.RecordObject(asset, k_SetupLabel);
                    changed = true;
                }

                bone.restPosition = position;
                bone.restRotationDegrees = rotation;
                asset.bones[i] = bone;
            }

            if (changed)
                EditorUtility.SetDirty(asset);

            return changed;
        }

        /// <summary>Port of _rebind_shapes (lines 512-518): every shape bound to this rig is
        /// rebuilt. Godot only needs queue_redraw because its skin is drawn immediately; here
        /// the skinned mesh is derived data, so it is regenerated.</summary>
        public static void RegenerateBoundSkins(ShapeRig rig)
        {
            if (rig == null)
                return;

            var stage = StageUtility.GetCurrentStageHandle();
            var skins = stage.IsValid()
                ? stage.FindComponentsOfType<DrawnShapeSkin>()
                : System.Array.Empty<DrawnShapeSkin>();

            for (int i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                if (skin != null && skin.rig == rig)
                    skin.RegenerateSkin();
            }
        }

        /// <summary>Put every bone Transform back on its asset rest (ShapeRig.ResetToRest), as
        /// one undo step covering the bone Transforms it moves, then rebuild the bound skins.
        /// This is the "back to bind pose" companion of Setup mode.</summary>
        public static void ResetRigPose(ShapeRig rig, string undoLabel)
        {
            if (rig == null || rig.rig == null || rig.rig.bones == null)
                return;

            var group = BeginUndoGroup(undoLabel);

            for (int i = 0; i < rig.rig.bones.Count; i++)
            {
                var boneTransform = rig.FindBone(rig.rig.bones[i].name);
                if (boneTransform != null)
                    Undo.RecordObject(boneTransform, undoLabel);
            }

            rig.ResetToRest();
            RegenerateBoundSkins(rig);

            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        /// <summary>Z rotation of a 2D bone read off its local basis rather than
        /// `localEulerAngles`, which has no stable representation once any X/Y component sneaks
        /// in. Bones are authored flat, so the X axis angle IS the bone angle.</summary>
        private static float LocalAngleDegrees(Transform boneTransform)
        {
            var right = boneTransform.localRotation * Vector3.right;
            return Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        }

        // --- assets -------------------------------------------------------------------------

        /// <summary>A rig asset next to the shape's own asset (so a limb's drawing and its
        /// skeleton live together), falling back to Assets/DrawToPlay/Drawn like every other
        /// generated asset in the toolset.</summary>
        private static RigAsset CreateRigAsset(DrawnShapeRenderer shape)
        {
            var folder = k_DrawnAssetFolder;
            var shapeAssetPath = shape.asset != null ? AssetDatabase.GetAssetPath(shape.asset) : null;
            if (!string.IsNullOrEmpty(shapeAssetPath))
            {
                var directory = Path.GetDirectoryName(shapeAssetPath);
                if (!string.IsNullOrEmpty(directory))
                    folder = directory.Replace('\\', '/');
            }

            if (!AssetDatabase.IsValidFolder(folder))
                folder = EnsureDrawnFolder();

            var asset = ScriptableObject.CreateInstance<RigAsset>();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SanitizeFileName(shape.name)}Rig.asset");
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Create every missing folder along Assets/DrawToPlay/Drawn. Same recipe as the
        /// Draw / Paint tools (their copies are private to those files).</summary>
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
                return "Shape";

            var invalid = Path.GetInvalidFileNameChars();
            var buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (System.Array.IndexOf(invalid, buffer[i]) >= 0)
                    buffer[i] = '_';
            }

            return new string(buffer);
        }

        // --- bone objects -------------------------------------------------------------------

        /// <summary>Destroy the scene Transforms of bones the re-rig dropped, deepest first, so
        /// each destruction is its own undo record instead of relying on hierarchy restore.
        /// ShapeRig.SyncBones would remove them anyway — but silently, outside the undo group.</summary>
        private static void DestroyBoneObjects(ShapeRig rig, List<string> names)
        {
            if (rig == null || names == null || names.Count == 0)
                return;

            var targets = new List<Transform>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                var boneTransform = rig.FindBone(names[i]);
                if (boneTransform != null && boneTransform != rig.transform && !targets.Contains(boneTransform))
                    targets.Add(boneTransform);
            }

            targets.Sort((a, b) => HierarchyDepth(b).CompareTo(HierarchyDepth(a)));

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null)
                    Undo.DestroyObjectImmediate(targets[i].gameObject);
            }
        }

        private static int HierarchyDepth(Transform boneTransform)
        {
            int depth = 0;
            while (boneTransform != null && boneTransform.parent != null)
            {
                boneTransform = boneTransform.parent;
                depth++;
            }

            return depth;
        }

        private static HashSet<Transform> CollectDescendants(Transform root)
        {
            var set = new HashSet<Transform>();
            if (root == null)
                return set;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                set.Add(all[i]);
            return set;
        }

        private static void RegisterCreatedBones(Transform root, HashSet<Transform> before)
        {
            if (root == null)
                return;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || before.Contains(all[i]))
                    continue;
                Undo.RegisterCreatedObjectUndo(all[i].gameObject, k_CommitLabel);
            }
        }

        // --- preview ------------------------------------------------------------------------

        /// <summary>Port of the rig branch of _forward_canvas_draw_over_viewport (lines
        /// 833-842): the joint polyline with a dot per joint, and a dashed segment from the last
        /// joint to the cursor.</summary>
        private void DrawPreview()
        {
            if (m_Joints.Count == 0)
                return;

            var points = new Vector3[m_Joints.Count];
            for (int i = 0; i < m_Joints.Count; i++)
                points[i] = ParentToWorld(m_Joints[i]);

            var worldPerPixel = DrawToolSettings.WorldPerPixel(points[0], m_PlaneOrigin, m_PlaneNormal);

            using (new Handles.DrawingScope(k_ChainColor))
            {
                if (points.Length > 1)
                    Handles.DrawAAPolyLine(k_ChainWidth, points);

                for (int i = 0; i < points.Length; i++)
                    Handles.DrawSolidDisc(points[i], m_PlaneNormal, k_JointDotPixels * worldPerPixel);
            }

            if (!m_CursorValid ||
                !DrawToolSettings.TryScreenToPlane(m_CursorGuiPoint, m_PlaneOrigin, m_PlaneNormal, out var cursor))
                return;

            using (new Handles.DrawingScope(k_CursorColor))
                Handles.DrawDottedLine(points[points.Length - 1], cursor, k_CursorDashSize);
        }

        // --- helpers ------------------------------------------------------------------------

        /// <summary>The component this tool was activated for, with a selection fallback (same
        /// resolution as PaintShapeTool).</summary>
        private DrawnShapeRenderer ResolveTarget()
        {
            if (target is DrawnShapeRenderer component && component != null)
                return component;
            if (target is GameObject targetObject && targetObject != null)
            {
                var fromTarget = targetObject.GetComponent<DrawnShapeRenderer>();
                if (fromTarget != null)
                    return fromTarget;
            }

            var active = Selection.activeGameObject;
            return active != null ? active.GetComponent<DrawnShapeRenderer>() : null;
        }

        /// <summary>Cache the spaces the gesture works in. Godot records joints through
        /// `_parent_xform().affine_inverse()`, i.e. in the PARENT's space — the space the
        /// Skeleton sibling lives in — while the clicks themselves land on the shape's drawing
        /// plane.</summary>
        private void CacheSpaces(DrawnShapeRenderer renderer)
        {
            m_Shape = renderer;

            if (renderer == null)
            {
                m_ParentToWorld = Matrix4x4.identity;
                m_WorldToParent = Matrix4x4.identity;
                m_PlaneOrigin = Vector3.zero;
                m_PlaneNormal = Vector3.forward;
                m_ParentPlaneZ = 0f;
                return;
            }

            var shapeTransform = renderer.transform;
            m_PlaneOrigin = shapeTransform.position;
            m_PlaneNormal = shapeTransform.forward.sqrMagnitude > 1e-8f
                ? shapeTransform.forward.normalized
                : Vector3.forward;

            var parent = shapeTransform.parent;
            m_ParentToWorld = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            m_WorldToParent = parent != null ? parent.worldToLocalMatrix : Matrix4x4.identity;
            m_ParentPlaneZ = m_WorldToParent.MultiplyPoint3x4(m_PlaneOrigin).z;
        }

        private Vector2 ScreenToParent(Vector2 guiPoint)
        {
            DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var world);
            var local = m_WorldToParent.MultiplyPoint3x4(world);
            return new Vector2(local.x, local.y);
        }

        private Vector3 ParentToWorld(Vector2 point) =>
            m_ParentToWorld.MultiplyPoint3x4(new Vector3(point.x, point.y, m_ParentPlaneZ));

        private static Matrix4x4 LocalMatrix(Transform t) =>
            Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        /// <summary>Undo restores the rig asset, the bone objects and the bind lists; the skinned
        /// mesh built from them is derived state that has to be rebuilt (the same rule the Draw
        /// and Paint tools follow for their meshes and masks).</summary>
        private static void OnUndoRedoPerformed()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null)
                    continue;

                var renderer = go.GetComponent<DrawnShapeRenderer>();
                if (renderer != null)
                    renderer.Regenerate();

                var skin = go.GetComponent<DrawnShapeSkin>();
                if (skin != null)
                    skin.RegenerateSkin();

                var rig = go.GetComponent<ShapeRig>();
                if (rig != null)
                    RegenerateBoundSkins(rig);
            }

            SceneView.RepaintAll();
        }
    }
}
