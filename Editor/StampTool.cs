using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view Stamp tool — the Unity port of terrain_paint.gd's stamp mode
    /// (_stamp_input / _scatter_at / _commit_stamp_batch / _do_place, lines 989-1046). Drag with
    /// LMB and copies of the armed stamp are scattered along the drag: one placement per
    /// <see cref="StampOverlay.spacing"/> world units, each with positional jitter, an optional
    /// horizontal flip and a uniform random scale. The whole drag is ONE undo step.
    ///
    /// The tool is inert while nothing is armed in the Stamps overlay — it does not claim the
    /// scene view's default control, so selection and picking keep working. That mirrors Godot,
    /// where `_stamp_input` is only reached when `_armed != null` (line 178).
    ///
    /// Godot deviations, all listed in the M2 report:
    ///  - Godot places `Sprite2D`s only. Here a stamp is either a prefab (instantiated with
    ///    PrefabUtility, so it brings its own physics/lights — brief §4) or a texture (a new
    ///    GameObject with a SpriteRenderer).
    ///  - `flip_h` becomes SpriteRenderer.flipX, applied to every SpriteRenderer in a stamped
    ///    prefab. Mirroring via a negative Transform scale was rejected: PhysicsCore2D bodies
    ///    carry no scale, so a mirrored prefab's collision would silently stay unmirrored.
    ///  - Godot places live during the drag and only afterwards writes a synthetic undo action
    ///    (`commit_action(false)`), which leaves an aborted drag un-undoable. Here every spawn is
    ///    registered with Undo.RegisterCreatedObjectUndo as it happens and the group is collapsed
    ///    at release, so even an Esc-aborted drag is a single, complete undo step.
    /// </summary>
    [EditorTool("Scatter Stamps")]
    public sealed class StampTool : EditorTool
    {
        private static readonly int s_ControlHint = "PowerOfFire.DrawToPlay.StampTool".GetHashCode();

        /// <summary>Fallback pixels-per-unit for a texture with no imported Sprite, matching the
        /// project's "1 world unit == 32 Godot px" convention.</summary>
        private const float k_PixelsPerUnit = 32f;

        /// <summary>Godot `jitter = ... * _spacing.value * 0.2` (_scatter_at line 1015).</summary>
        private const float k_JitterFactor = 0.2f;

        /// <summary>Godot `_rng.randf() &lt; 0.5` for flip_h.</summary>
        private const float k_FlipChance = 0.5f;

        /// <summary>Fallback ghost footprint when a stamp's size cannot be measured.</summary>
        private static readonly Vector2 k_FallbackFootprint = new Vector2(1f, 1f);

        private static readonly Color k_GhostColor = new Color(1f, 0.85f, 0.3f, 0.9f);
        private static readonly Color k_GhostFillColor = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color k_SpacingColor = new Color(1f, 0.85f, 0.3f, 0.35f);

        /// <summary>In-memory sprites built for textures that have no Sprite sub-asset, one per
        /// asset path so a drag does not allocate a Sprite per placement.</summary>
        private static readonly Dictionary<string, Sprite> s_FallbackSprites = new Dictionary<string, Sprite>();

        /// <summary>A private generator so scattering never perturbs UnityEngine.Random's global
        /// stream (Godot uses its own `_rng` for the same reason).</summary>
        private readonly System.Random m_Rng = new System.Random();

        private Transform m_Parent;
        private Vector3 m_PlaneOrigin = Vector3.zero;
        private Vector3 m_PlaneNormal = Vector3.forward;
        private Matrix4x4 m_ParentToWorld = Matrix4x4.identity;
        private Matrix4x4 m_WorldToParent = Matrix4x4.identity;

        private bool m_Stamping;
        private bool m_HasLast;
        private Vector2 m_LastLocal;
        private int m_UndoGroup;
        private int m_BatchCount;

        private GUIContent m_ToolbarIcon;

        public override GUIContent toolbarIcon => m_ToolbarIcon ??= DrawToolSettings.BuildToolbarIcon(
            "Grid.PaintTool",
            "Stamp",
            "Scatter Stamps: arm a stamp in the Stamps overlay, then drag to scatter it.");

        public override void OnActivated()
        {
            CacheParentSpace(ResolveParent());
        }

        public override void OnWillBeDeactivated()
        {
            // A drag interrupted by a tool switch still has to close its undo group cleanly.
            if (m_Stamping)
                EndBatch();
            ResetGesture();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var currentEvent = Event.current;
            var controlId = GUIUtility.GetControlID(s_ControlHint, FocusType.Passive);

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    // Only steal the fallback control while a stamp is armed — an unarmed stamp
                    // tool must not break rubber-band selection.
                    if (StampOverlay.isArmed)
                        HandleUtility.AddDefaultControl(controlId);
                    if (!m_Stamping)
                        CacheParentSpace(ResolveParent());
                    break;

                case EventType.MouseMove:
                    // The ghost reads Event.current.mousePosition when it repaints; this is only
                    // here so hovering actually triggers that repaint.
                    if (StampOverlay.isArmed)
                        sceneView.Repaint();
                    break;

                case EventType.MouseDown:
                    if (currentEvent.button != 0 || currentEvent.alt || !StampOverlay.isArmed)
                        break;
                    if (HandleUtility.nearestControl != controlId)
                        break;

                    BeginBatch();
                    ScatterAt(ScreenToParent(currentEvent.mousePosition));
                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                        break;

                    if (m_Stamping)
                        ScatterAt(ScreenToParent(currentEvent.mousePosition));
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;

                    GUIUtility.hotControl = 0;
                    EndBatch();
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.KeyDown:
                    if (currentEvent.keyCode != KeyCode.Escape)
                        break;

                    // Godot's Esc runs _disarm (line 148). Anything already scattered stays —
                    // it is in the undo group, so one Ctrl+Z removes the whole batch.
                    if (m_Stamping)
                    {
                        if (GUIUtility.hotControl == controlId)
                            GUIUtility.hotControl = 0;
                        EndBatch();
                    }

                    if (StampOverlay.isArmed)
                        StampOverlay.Disarm();
                    else
                        Tools.current = Tool.Move;

                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.Repaint:
                    DrawGhost();
                    break;
            }
        }

        // --- batch ----------------------------------------------------------------------------

        private void BeginBatch()
        {
            CacheParentSpace(ResolveParent());

            m_Stamping = true;
            m_HasLast = false;
            m_BatchCount = 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Scatter Stamps");
            m_UndoGroup = Undo.GetCurrentGroup();
        }

        /// <summary>Port of _commit_stamp_batch (line 1030): the drag is one undo action named
        /// after the number of stamps it placed.</summary>
        private void EndBatch()
        {
            if (!m_Stamping)
                return;

            m_Stamping = false;
            m_HasLast = false;

            if (m_BatchCount <= 0)
                return;

            // Nothing has incremented the undo group since BeginBatch, so the current group is
            // still m_UndoGroup and naming it here names the collapsed step.
            Undo.SetCurrentGroupName($"Scatter {m_BatchCount} stamp(s)");
            Undo.CollapseUndoOperations(m_UndoGroup);
            m_BatchCount = 0;
        }

        private void ResetGesture()
        {
            m_Stamping = false;
            m_HasLast = false;
            m_BatchCount = 0;
        }

        // --- scatter --------------------------------------------------------------------------

        /// <summary>Port of _scatter_at (line 1005). The spacing gate measures the RAW cursor
        /// position and remembers it un-jittered, exactly as Godot does — jitter must not feed
        /// back into the spacing rhythm.</summary>
        private void ScatterAt(Vector2 local)
        {
            var stamp = StampOverlay.armedStamp;
            if (stamp == null)
                return;

            var spacing = StampOverlay.spacing;
            if (m_HasLast && Vector2.Distance(m_LastLocal, local) < spacing)
                return;

            m_LastLocal = local;
            m_HasLast = true;

            var path = StampOverlay.armedPath;
            var name = ToPascalCase(System.IO.Path.GetFileNameWithoutExtension(path));

            var instance = CreateStampInstance(stamp, name);
            if (instance == null)
                return;

            // Small positional jitter so drag rows don't look machine-placed.
            var jitter = new Vector2(NextSigned(), NextSigned()) * spacing * k_JitterFactor;
            var placement = local + jitter;

            var transform = instance.transform;
            transform.SetParent(m_Parent, false);
            transform.localPosition = new Vector3(placement.x, placement.y, 0f);

            // Godot assigns `spr.scale = Vector2.ONE * s` because a fresh Sprite2D is always at
            // scale 1. Multiplying instead keeps a prefab whose root is authored at some other
            // scale proportional, and is identical to Godot for the texture path.
            var scale = NextRange(Mathf.Min(StampOverlay.scaleMin, StampOverlay.scaleMax),
                Mathf.Max(StampOverlay.scaleMin, StampOverlay.scaleMax));
            var baseScale = transform.localScale;
            transform.localScale = new Vector3(baseScale.x * scale, baseScale.y * scale, baseScale.z);

            if (StampOverlay.randomFlip && m_Rng.NextDouble() < k_FlipChance)
                ApplyFlip(instance);

            var marker = instance.GetComponent<StampMarker>();
            if (marker == null)
                marker = instance.AddComponent<StampMarker>();
            marker.sourceAssetPath = path;

            instance.name = name;
            GameObjectUtility.EnsureUniqueNameForSibling(instance);

            Undo.RegisterCreatedObjectUndo(instance, "Scatter Stamps");
            ++m_BatchCount;
        }

        /// <summary>Prefab → a real prefab instance (it keeps its components, so a stamped
        /// lantern brings its light and its body). Texture → a bare GameObject with a
        /// SpriteRenderer, Godot's Sprite2D equivalent.</summary>
        private static GameObject CreateStampInstance(UnityEngine.Object stamp, string name)
        {
            if (stamp is GameObject prefab)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                    return null;
                StageUtility.PlaceGameObjectInCurrentStage(instance);
                return instance;
            }

            if (!(stamp is Texture2D texture))
                return null;

            var go = new GameObject(name);
            StageUtility.PlaceGameObjectInCurrentStage(go);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = ResolveSprite(texture);
            return go;
        }

        /// <summary>The imported Sprite sub-asset when the texture's Texture Type is Sprite —
        /// that is a real asset reference and survives saving the scene. Otherwise a Sprite built
        /// in memory at 32 px per unit, which shows up immediately but is NOT persistent (the
        /// Stamps overlay says so in the entry's tooltip).
        ///
        /// Sprite.Create(Texture2D, Rect, Vector2 pivot, float pixelsPerUnit): rect is in texture
        /// pixels, pivot is NORMALISED inside that rect, so (0.5, 0.5) is the centre — the same
        /// anchoring as Godot's `Sprite2D.centered = true` default.</summary>
        private static Sprite ResolveSprite(Texture2D texture)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path))
            {
                var imported = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (imported != null)
                    return imported;

                if (s_FallbackSprites.TryGetValue(path, out var cached) && cached != null)
                    return cached;
            }

            var created = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                k_PixelsPerUnit);
            created.name = texture.name;

            if (!string.IsNullOrEmpty(path))
                s_FallbackSprites[path] = created;

            return created;
        }

        /// <summary>Godot `spr.flip_h = true`. Applied to every SpriteRenderer in the instance so
        /// a multi-sprite prefab mirrors as a whole, and never via a negative Transform scale —
        /// PhysicsCore2D geometry carries no scale, so mirroring a prefab that way would leave
        /// its collision facing the original direction.</summary>
        private static void ApplyFlip(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<SpriteRenderer>(true);
            for (var i = 0; i < renderers.Length; ++i)
                renderers[i].flipX = !renderers[i].flipX;
        }

        // --- ghost ----------------------------------------------------------------------------

        /// <summary>Preview of what the next click would drop: the stamp's thumbnail at its real
        /// footprint, its outline, and the spacing ring that says how far the cursor has to travel
        /// before the next copy appears. Godot draws no stamp preview at all — this is a Unity
        /// addition, because Unity's scene view gives no other feedback that a tool is armed.</summary>
        private void DrawGhost()
        {
            var stamp = StampOverlay.armedStamp;
            if (stamp == null)
                return;

            // During a Repaint the scene view keeps Event.current.mousePosition at the live
            // cursor position, so no separate hover tracking is needed.
            var guiPoint = Event.current.mousePosition;
            if (!DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var center))
                return;

            var midScale = 0.5f * (Mathf.Clamp(StampOverlay.scaleMin, StampOverlay.MinScale, StampOverlay.MaxScale) +
                                   Mathf.Clamp(StampOverlay.scaleMax, StampOverlay.MinScale, StampOverlay.MaxScale));
            var footprint = GetFootprint(stamp) * Mathf.Max(midScale, 0.01f);

            var right = m_ParentToWorld.MultiplyVector(Vector3.right).normalized;
            var up = m_ParentToWorld.MultiplyVector(Vector3.up).normalized;
            if (right.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f)
            {
                right = Vector3.right;
                up = Vector3.up;
            }

            var halfX = right * (footprint.x * 0.5f);
            var halfY = up * (footprint.y * 0.5f);
            var corners = new[]
            {
                center - halfX - halfY,
                center + halfX - halfY,
                center + halfX + halfY,
                center - halfX + halfY,
                center - halfX - halfY
            };

            var preview = GetGhostTexture(stamp);
            if (preview != null)
                DrawGhostTexture(preview, corners);

            using (new Handles.DrawingScope(k_GhostColor))
                Handles.DrawAAPolyLine(2f, corners);

            using (new Handles.DrawingScope(k_SpacingColor))
                Handles.DrawWireDisc(center, m_PlaneNormal, StampOverlay.spacing);
        }

        /// <summary>Screen-space blit of the thumbnail into the quad's bounding box. Handles have
        /// no textured-quad primitive, so this drops into IMGUI for the fill and lets the
        /// world-space outline above carry the exact footprint.</summary>
        private static void DrawGhostTexture(Texture texture, Vector3[] corners)
        {
            var min = HandleUtility.WorldToGUIPoint(corners[0]);
            var max = min;
            for (var i = 1; i < corners.Length; ++i)
            {
                var point = HandleUtility.WorldToGUIPoint(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var rect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            if (rect.width < 1f || rect.height < 1f || rect.width > 4096f || rect.height > 4096f)
                return;

            Handles.BeginGUI();
            var previousColor = GUI.color;
            GUI.color = k_GhostFillColor;
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previousColor;
            Handles.EndGUI();
        }

        private static Texture GetGhostTexture(UnityEngine.Object stamp)
        {
            if (stamp is Texture2D texture)
                return texture;

            var preview = AssetPreview.GetAssetPreview(stamp);
            return preview != null ? preview : AssetPreview.GetMiniThumbnail(stamp);
        }

        /// <summary>Unscaled world-space size of a stamp: a sprite's own bounds, a raw texture at
        /// 32 px per unit, or a prefab's renderer bounds.</summary>
        private static Vector2 GetFootprint(UnityEngine.Object stamp)
        {
            if (stamp is Texture2D texture)
            {
                var path = AssetDatabase.GetAssetPath(texture);
                var sprite = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    var size = sprite.bounds.size;
                    if (size.x > 1e-4f && size.y > 1e-4f)
                        return new Vector2(size.x, size.y);
                }

                return new Vector2(texture.width, texture.height) / k_PixelsPerUnit;
            }

            if (!(stamp is GameObject prefab))
                return k_FallbackFootprint;

            // UNVERIFIED: Renderer.bounds read from a PREFAB ASSET (a hierarchy that was never
            // instantiated into a scene) is not formally documented; in practice it reports the
            // volume relative to the prefab root, which is what the ghost needs. A wrong value can
            // only mis-size the preview rectangle, never a placement, and the guard below falls
            // back to a 1x1 box.
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return k_FallbackFootprint;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; ++i)
                bounds.Encapsulate(renderers[i].bounds);

            var extents = bounds.size;
            if (extents.x < 1e-4f || extents.y < 1e-4f)
                return k_FallbackFootprint;

            return new Vector2(extents.x, extents.y);
        }

        // --- helpers --------------------------------------------------------------------------

        /// <summary>Godot parents stamps under `_target`. The Unity equivalent is the selected
        /// drawn shape; with nothing (or something else) selected the stamps land at the scene
        /// root, which is also where the drag's coordinate plane comes from.</summary>
        private static Transform ResolveParent()
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return null;

            var renderer = active.GetComponent<DrawnShapeRenderer>();
            return renderer != null ? renderer.transform : null;
        }

        /// <summary>Cache the scatter space for the whole drag, so re-selecting mid-drag cannot
        /// move the parent out from under the batch.
        ///
        /// Spacing and jitter are world-unit constants applied in PARENT-LOCAL space; a drawn
        /// shape is expected to sit at scale 1, and a scaled parent shifts the effective spacing
        /// by that scale.</summary>
        private void CacheParentSpace(Transform parent)
        {
            m_Parent = parent;
            if (parent != null)
            {
                m_ParentToWorld = parent.localToWorldMatrix;
                m_WorldToParent = parent.worldToLocalMatrix;
                m_PlaneOrigin = parent.position;
                m_PlaneNormal = parent.forward.sqrMagnitude > 1e-8f ? parent.forward.normalized : Vector3.forward;
                return;
            }

            m_ParentToWorld = Matrix4x4.identity;
            m_WorldToParent = Matrix4x4.identity;
            m_PlaneOrigin = Vector3.zero;
            m_PlaneNormal = Vector3.forward;
        }

        private Vector2 ScreenToParent(Vector2 guiPoint)
        {
            DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var world);
            return m_WorldToParent.MultiplyPoint3x4(world);
        }

        /// <summary>Godot `_rng.randf_range(-1.0, 1.0)`.</summary>
        private float NextSigned()
        {
            return (float)(m_Rng.NextDouble() * 2.0 - 1.0);
        }

        /// <summary>Godot `_rng.randf_range(min, max)`.</summary>
        private float NextRange(float min, float max)
        {
            return min + (float)m_Rng.NextDouble() * (max - min);
        }

        /// <summary>Godot `String.to_pascal_case`: split on separators and on lower→upper
        /// boundaries, capitalise every run. "mossy_rock 02" and "mossyRock02" both become
        /// "MossyRock02".</summary>
        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Stamp";

            var builder = new StringBuilder(value.Length);
            var startOfWord = true;

            for (var i = 0; i < value.Length; ++i)
            {
                var c = value[i];
                if (!char.IsLetterOrDigit(c))
                {
                    startOfWord = true;
                    continue;
                }

                if (!startOfWord && char.IsUpper(c) && i > 0 && char.IsLower(value[i - 1]))
                    startOfWord = true;

                builder.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = false;
            }

            return builder.Length > 0 ? builder.ToString() : "Stamp";
        }
    }
}
