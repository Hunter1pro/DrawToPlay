using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Scene-view Paint tool — the Unity port of terrain_paint.gd's brush mode
    /// (_brush_input / _spawn_painted / _set_radius, lines 879-985, plus the brush cursor in
    /// _forward_canvas_draw_over_viewport lines 840-859). Drag paints the selected texture
    /// slot into the shape's mask through <see cref="ShapePaint"/>; Shift erases; the bracket
    /// keys resize the brush. On release the stroke ALSO sculpts: its capsule unions into the
    /// outline (Shift carves it back) and a stroke that lands entirely off the shape becomes
    /// its own painted sibling.
    ///
    /// One collapsed undo step per stroke, covering the mask bytes + mask rect, the curve when
    /// the stroke sculpted, and any object the stroke spawned.
    ///
    /// Input mapping deviations from Godot (see the M2 report):
    ///  - Godot toggles brush mode from its toolbar; here the tool is a component tool bound to
    ///    <see cref="DrawnShapeRenderer"/>, so it is only offered when a drawn shape is selected.
    ///  - Shift means "erase" exactly as in Godot; the Draw tool's Ctrl/Cmd remap does not apply
    ///    because the brush never used Shift for picking.
    ///  - A missing <see cref="ShapePaint"/> is added on the first stroke instead of being a
    ///    precondition (Godot's paint() lives on the shape node itself).
    /// </summary>
    [EditorTool("Paint Shape", typeof(DrawnShapeRenderer))]
    public sealed class PaintShapeTool : EditorTool
    {
        private static readonly int s_ControlHint = "PowerOfFire.DrawToPlay.PaintShapeTool".GetHashCode();

        private const string k_DrawnAssetFolder = "Assets/DrawToPlay/Drawn";
        private const string k_DrawnAssetName = "DrawnShape";

        /// <summary>Mirror of <see cref="DrawnShapeAsset.bakeInterval"/>'s default, used when an
        /// asset somehow carries a degenerate interval.</summary>
        private const float k_DefaultBakeInterval = 0.125f;

        /// <summary>Mirror of <see cref="DrawnShapeAsset.brushRadius"/>'s default — only used to
        /// size the cursor before a shape has an asset.</summary>
        private const float k_DefaultBrushRadius = 0.1f;

        private const string k_PaintLabel = "Paint";
        private const string k_EraseLabel = "Erase Paint";
        private const string k_SpawnLabel = "Paint New Shape";

        /// <summary>Stroke points in SHAPE-LOCAL space (Godot `_paint_pts`).</summary>
        private readonly List<Vector2> m_StrokePoints = new List<Vector2>();

        private DrawnShapeRenderer m_ActiveShape;
        private ShapePaint m_ActivePaint;
        private Matrix4x4 m_ShapeToWorld = Matrix4x4.identity;
        private Matrix4x4 m_WorldToShape = Matrix4x4.identity;
        private Vector3 m_PlaneOrigin = Vector3.zero;
        private Vector3 m_PlaneNormal = Vector3.forward;

        private bool m_Painting;
        private bool m_Erasing;
        private bool m_CursorShift;
        private bool m_CursorValid;
        private Vector2 m_CursorGuiPoint;
        private Vector2 m_LastSampleGuiPoint;

        // Godot `_stroke_before` / `_stroke_before_rect`: the undo snapshot AND the state
        // _spawn_painted reverts the source to when the stroke turns into its own object.
        private byte[] m_StrokeMaskPng = Array.Empty<byte>();
        private Rect m_StrokeMaskRect;
        private int m_UndoGroup = -1;

        private GUIContent m_ToolbarIcon;

        public override GUIContent toolbarIcon => m_ToolbarIcon ??= DrawToolSettings.BuildToolbarIcon(
            "Grid.PaintTool",
            "Paint",
            "Paint Shape: drag to paint the selected texture slot (Shift = erase, [ / ] = brush size). " +
            "Strokes over the edge sculpt the outline; strokes off the shape spawn a painted sibling.");

        public override void OnActivated()
        {
            CacheShapeSpace(ResolveTarget());

            // Undo restores the serialized mask bytes on the asset, but the runtime mask image
            // and the paint layer are derived — they have to be told (Godot apply_mask_png).
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            CancelStroke();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView))
                return;

            var currentEvent = Event.current;
            TrackCursor(currentEvent, sceneView);

            // Key events are read from the RAW type: Event.GetTypeForControl masks keyboard
            // events away from controls that do not hold keyboardControl, and a passive scene
            // tool never does.
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
                    // Claim the fallback control so a drag in empty space paints instead of
                    // rubber-band selecting (Godot consumes the same events in _brush_input).
                    HandleUtility.AddDefaultControl(controlId);
                    if (!m_Painting)
                        CacheShapeSpace(ResolveTarget());
                    break;

                case EventType.MouseDown:
                    if (currentEvent.button != 0 || currentEvent.alt || HandleUtility.nearestControl != controlId)
                        break;
                    BeginStroke(currentEvent);
                    GUIUtility.hotControl = controlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    UpdateStroke(currentEvent);
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    GUIUtility.hotControl = 0;
                    EndStroke();
                    currentEvent.Use();
                    sceneView.Repaint();
                    break;

                case EventType.Repaint:
                    DrawBrushCursor();
                    break;
            }
        }

        // --- input ------------------------------------------------------------------------

        /// <summary>Godot updates `_cursor_view` / `_cursor_shift` on every mouse event and calls
        /// update_overlays() (line 889-892). Key events are tracked too so the cursor turns red
        /// the moment Shift goes down, without waiting for a mouse move.</summary>
        private void TrackCursor(Event currentEvent, SceneView sceneView)
        {
            if (currentEvent.isMouse)
            {
                m_CursorGuiPoint = currentEvent.mousePosition;
                m_CursorValid = true;
                m_CursorShift = currentEvent.shift;
                sceneView.Repaint();
                return;
            }

            if (currentEvent.isKey)
            {
                m_CursorShift = currentEvent.shift;
                sceneView.Repaint();
            }
        }

        /// <summary>Port of the key branch of _brush_input (lines 881-888): [ / ] resize the
        /// brush by 2 Godot px. Escape leaves the tool, matching the plugin's Esc handling
        /// (terrain_paint.gd line 145-152) rather than cancelling a stroke, which Godot's brush
        /// has no concept of.</summary>
        private bool HandleKeyDown(Event currentEvent)
        {
            switch (currentEvent.keyCode)
            {
                case KeyCode.LeftBracket:
                    return AdjustRadius(-DrawToolSettings.BrushRadiusStep);

                case KeyCode.RightBracket:
                    return AdjustRadius(DrawToolSettings.BrushRadiusStep);

                case KeyCode.Escape:
                    if (m_Painting)
                        return false;
                    Tools.current = Tool.Move;
                    return true;
            }

            return false;
        }

        /// <summary>Port of _set_radius (lines 977-979): clamp to [0.5, 128] Godot px and refresh
        /// the size readout (the overlay listens on <see cref="DrawToolSettings.paintStateChanged"/>).</summary>
        private bool AdjustRadius(float delta)
        {
            var shape = m_Painting ? m_ActiveShape : ResolveTarget();
            var asset = shape != null ? shape.asset : null;
            if (asset == null)
                return false;

            var radius = DrawToolSettings.ClampBrushRadius(asset.brushRadius + delta);
            if (!Mathf.Approximately(radius, asset.brushRadius))
            {
                Undo.RecordObject(asset, "Brush Radius");
                asset.brushRadius = radius;
                EditorUtility.SetDirty(asset);
                DrawToolSettings.NotifyPaintStateChanged();
            }

            return true;
        }

        // --- stroke -----------------------------------------------------------------------

        /// <summary>Port of the press branch of _brush_input (lines 894-900): latch the erase
        /// modifier, snapshot the mask for undo, and stamp the first sample.</summary>
        private void BeginStroke(Event currentEvent)
        {
            m_ActiveShape = ResolveTarget();
            CacheShapeSpace(m_ActiveShape);
            if (m_ActiveShape == null)
                return;

            m_Erasing = currentEvent.shift;
            var label = m_Erasing ? k_EraseLabel : k_PaintLabel;

            // Everything the stroke touches — the auto-added component, the mask bytes, the
            // sculpted curve, any spawned object — collapses into this one group.
            var group = BeginUndoGroup(label);

            var asset = EnsureAsset(m_ActiveShape);
            var paint = EnsurePaint(m_ActiveShape);
            if (asset == null || paint == null)
                return;

            Undo.RecordObject(asset, label);
            m_StrokeMaskPng = CloneBytes(asset.maskPng);
            m_StrokeMaskRect = asset.maskRect;

            m_UndoGroup = group;
            m_ActivePaint = paint;
            m_Painting = true;

            var point = ScreenToShape(currentEvent.mousePosition);
            m_StrokePoints.Clear();
            m_StrokePoints.Add(point);
            m_LastSampleGuiPoint = currentEvent.mousePosition;

            paint.Paint(point, m_Erasing, -1f, DrawToolSettings.paintSlot);
        }

        /// <summary>Port of the motion branch of _brush_input (lines 941-947): the stroke POINT
        /// list is thinned to the 1.5 screen-px step (it drives the sculpt capsule), but a stamp
        /// lands on every motion event so the paint stays gap-free.</summary>
        private void UpdateStroke(Event currentEvent)
        {
            if (!m_Painting || m_ActivePaint == null)
                return;

            var point = ScreenToShape(currentEvent.mousePosition);
            if (m_StrokePoints.Count == 0 ||
                Vector2.Distance(m_LastSampleGuiPoint, currentEvent.mousePosition) > DrawToolSettings.StrokeSampleStepPixels)
            {
                m_StrokePoints.Add(point);
                m_LastSampleGuiPoint = currentEvent.mousePosition;
            }

            m_ActivePaint.Paint(point, m_Erasing, -1f, DrawToolSettings.paintSlot);
        }

        /// <summary>Port of the release branch of _brush_input (lines 901-939): commit the mask,
        /// then let the stroke capsule sculpt the outline / spawn its own object.</summary>
        private void EndStroke()
        {
            if (!m_Painting)
                return;

            m_Painting = false;

            var shape = m_ActiveShape;
            var paint = m_ActivePaint;
            var group = m_UndoGroup;
            m_ActivePaint = null;
            m_UndoGroup = -1;

            if (paint != null)
                paint.Commit();

            var asset = shape != null ? shape.asset : null;
            var spawned = false;

            if (asset != null && m_StrokePoints.Count > 0)
            {
                var radius = DrawToolSettings.ClampBrushRadius(asset.brushRadius);
                var capsule = DrawKit.StrokeCapsule(m_StrokePoints, radius);
                var tolerance = CapsuleToleranceAtCentroid();

                if (capsule != null && capsule.Count >= 3)
                {
                    if (DrawToolSettings.forceNew && !m_Erasing)
                    {
                        var fitted = DrawKit.FitCurve(capsule, true, tolerance, DrawToolSettings.CapsuleSmoothPasses);
                        spawned = SpawnPainted(shape, paint, fitted, radius);
                    }
                    else if (asset.paintSculptsEdges)
                    {
                        spawned = SculptWithCapsule(shape, paint, asset, capsule, tolerance, radius);
                    }
                }
            }

            if (asset != null)
                EditorUtility.SetDirty(asset);

            m_StrokePoints.Clear();

            if (group >= 0)
            {
                if (spawned)
                    Undo.SetCurrentGroupName(k_SpawnLabel);
                Undo.CollapseUndoOperations(group);
            }
        }

        /// <summary>Port of the SHAPE_SCRIPT branch (lines 909-926): merged/adopt/cleared sculpt
        /// the outline in place, a disjoint union becomes its own painted object, and the
        /// inside/hole results Godot's brush never acts on are ignored here too.</summary>
        private bool SculptWithCapsule(DrawnShapeRenderer shape, ShapePaint paint, DrawnShapeAsset asset,
            List<Vector2> capsule, float tolerance, float radius)
        {
            var bakeInterval = asset.bakeInterval > 0f ? asset.bakeInterval : k_DefaultBakeInterval;
            var tolerances = DrawToolSettings.TolerancesAt(CurrentWorldPerPixel());

            // Godot's merge_stroke takes only a tolerance; the epsilon and the refit clamp are
            // constants inside it, exposed as parameters by the unit-agnostic Unity port.
            var result = DrawKit.MergeStroke(asset.curve, capsule, m_Erasing, tolerance, false, bakeInterval,
                insideAreaEpsilon: tolerances.insideAreaEpsilon,
                refitToleranceMin: tolerances.refitMin,
                refitToleranceMax: tolerances.refitMax);

            switch (result.op)
            {
                case DrawKit.StrokeOp.Merged:
                case DrawKit.StrokeOp.Adopt:
                case DrawKit.StrokeOp.Cleared:
                {
                    if (result.curve == null)
                        return false;
                    var label = m_Erasing ? k_EraseLabel : k_PaintLabel;
                    Undo.RecordObject(asset, label);
                    Undo.RecordObject(shape, label);
                    shape.AdoptCurve(result.curve);
                    EditorUtility.SetDirty(asset);
                    return false;
                }

                case DrawKit.StrokeOp.Disjoint:
                    if (m_Erasing)
                        return false;
                    return SpawnPainted(shape, paint, result.curve, radius);
            }

            return false;
        }

        /// <summary>Port of _spawn_painted (lines 955-974): the stroke landed entirely OFF the
        /// shape, so the (invisible) paint on the source is reverted to the pre-stroke snapshot
        /// and a style-copied sibling is created from the stroke capsule, with the stroke
        /// REPLAYED onto it so the painted channel comes along. Spawn plumbing mirrors
        /// DrawShapeTool.SpawnShape.</summary>
        private bool SpawnPainted(DrawnShapeRenderer source, ShapePaint sourcePaint, DrawnCurve fitted, float radius)
        {
            if (source == null || fitted == null || fitted.pointCount < 3)
                return false;

            var sourceAsset = source.asset;
            if (sourceAsset != null)
            {
                sourceAsset.maskRect = m_StrokeMaskRect;
                if (sourcePaint != null)
                    sourcePaint.ApplyMaskPng(CloneBytes(m_StrokeMaskPng));
                else
                    sourceAsset.maskPng = CloneBytes(m_StrokeMaskPng);
                EditorUtility.SetDirty(sourceAsset);
            }

            var centroid = DrawKit.RecenterCurve(fitted);
            var sourceTransform = source.transform;

            var go = new GameObject(sourceTransform.name);
            StageUtility.PlaceGameObjectInCurrentStage(go);
            go.transform.SetParent(sourceTransform.parent, false);
            go.transform.rotation = sourceTransform.rotation;
            go.transform.localScale = sourceTransform.localScale;

            // Godot `node.position = _target.transform * centroid` — the centroid is in the
            // source's local space, so the sibling lands on the painted stroke.
            go.transform.position = sourceTransform.TransformPoint(centroid);

            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, k_SpawnLabel);

            var renderer = go.AddComponent<DrawnShapeRenderer>();
            var asset = CreateShapeAsset(sourceAsset);
            if (asset.curve == null)
                asset.curve = new DrawnCurve();
            asset.curve.CopyFrom(fitted);

            renderer.asset = asset;
            renderer.Regenerate();

            // Replay with the SOURCE brush radius (Godot passes `r` explicitly) so the painted
            // trail matches what the user just drew, whatever the clone's own radius says.
            var paint = go.AddComponent<ShapePaint>();
            for (var i = 0; i < m_StrokePoints.Count; ++i)
                paint.Paint(m_StrokePoints[i] - centroid, false, radius, DrawToolSettings.paintSlot);
            paint.Commit();

            EditorUtility.SetDirty(asset);
            Selection.activeGameObject = go;
            return true;
        }

        /// <summary>Drop an in-flight stroke without committing anything extra — the mask stamps
        /// already applied stay, exactly as they would if the mouse had been released, and the
        /// group is closed so nothing leaks into the next gesture.</summary>
        private void CancelStroke()
        {
            if (m_Painting && m_ActivePaint != null)
                m_ActivePaint.Commit();

            if (m_UndoGroup >= 0)
                Undo.CollapseUndoOperations(m_UndoGroup);

            m_Painting = false;
            m_UndoGroup = -1;
            m_ActivePaint = null;
            m_StrokePoints.Clear();
        }

        // --- assets -----------------------------------------------------------------------

        /// <summary>Style clone of `source` with geometry AND paint stripped (spawn_like skips
        /// curve / mask_png / mask_rect / hole_curves), saved under Assets/DrawToPlay/Drawn.
        /// Same recipe as DrawShapeTool.CreateShapeAsset, plus the mask fields M0 did not have.</summary>
        private static DrawnShapeAsset CreateShapeAsset(DrawnShapeAsset source)
        {
            var asset = source != null
                ? UnityEngine.Object.Instantiate(source)
                : ScriptableObject.CreateInstance<DrawnShapeAsset>();

            asset.curve = new DrawnCurve();
            asset.holeCurves = new List<DrawnCurve>();
            asset.maskPng = Array.Empty<byte>();
            asset.maskRect = new Rect();

            var folder = EnsureDrawnFolder();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{k_DrawnAssetName}.asset");
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Create every missing folder along Assets/DrawToPlay/Drawn.</summary>
        private static string EnsureDrawnFolder()
        {
            var segments = k_DrawnAssetFolder.Split('/');
            var path = segments[0]; // "Assets"
            for (var i = 1; i < segments.Length; ++i)
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

        /// <summary>A renderer created from the menu starts without an asset; the first stroke
        /// that mutates it creates and assigns one (inside the stroke's undo group). Mirror of
        /// DrawShapeTool.EnsureAsset.</summary>
        private static DrawnShapeAsset EnsureAsset(DrawnShapeRenderer renderer)
        {
            if (renderer == null)
                return null;
            if (renderer.asset != null)
                return renderer.asset;

            var asset = CreateShapeAsset(null);
            Undo.RecordObject(renderer, "Create Drawn Shape Asset");
            renderer.asset = asset;
            EditorUtility.SetDirty(renderer);
            return asset;
        }

        /// <summary>In Godot paint() lives on the shape node itself; here the mask lives on a
        /// separate component, so the first stroke adds it (undoable, inside the stroke group).</summary>
        private static ShapePaint EnsurePaint(DrawnShapeRenderer renderer)
        {
            if (renderer == null)
                return null;

            var paint = renderer.GetComponent<ShapePaint>();
            return paint != null ? paint : Undo.AddComponent<ShapePaint>(renderer.gameObject);
        }

        // --- cursor -----------------------------------------------------------------------

        /// <summary>Port of the brush cursor in _forward_canvas_draw_over_viewport (lines
        /// 840-859): the radius ring (never below 2 screen px), the centre dot, and — while a
        /// sculpting stroke is live — the faded capsule footprint the release will union in.
        /// Godot always draws the ring green; here the ring takes the armed channel's colour so
        /// the target slot is visible without looking at the overlay, red while erasing.</summary>
        private void DrawBrushCursor()
        {
            // Fall back to the repaint's own mouse position so the ring exists even before the
            // first tracked move event.
            var guiPoint = m_CursorValid ? m_CursorGuiPoint : Event.current.mousePosition;
            if (!DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var center))
                return;

            var asset = m_ActiveShape != null ? m_ActiveShape.asset : null;
            var radius = DrawToolSettings.ClampBrushRadius(asset != null ? asset.brushRadius : k_DefaultBrushRadius);
            var worldPerPixel = DrawToolSettings.WorldPerPixel(center, m_PlaneOrigin, m_PlaneNormal);
            var drawRadius = Mathf.Max(radius, DrawToolSettings.BrushCursorMinPixels * worldPerPixel);

            var erasing = m_Painting ? m_Erasing : m_CursorShift;
            var color = erasing
                ? DrawToolSettings.EraseBrushColor
                : DrawToolSettings.PaintChannelColor(DrawToolSettings.paintSlot);

            if (m_Painting && m_StrokePoints.Count > 1 && asset != null && asset.paintSculptsEdges)
            {
                var trail = new Vector3[m_StrokePoints.Count];
                for (var i = 0; i < m_StrokePoints.Count; ++i)
                    trail[i] = ShapeToWorld(m_StrokePoints[i]);

                var faded = new Color(color.r, color.g, color.b, DrawToolSettings.BrushTrailAlpha);
                using (new Handles.DrawingScope(faded))
                {
                    Handles.DrawAAPolyLine(2f * drawRadius / Mathf.Max(worldPerPixel, 1e-6f), trail);
                    Handles.DrawSolidDisc(trail[0], m_PlaneNormal, drawRadius);
                    Handles.DrawSolidDisc(trail[trail.Length - 1], m_PlaneNormal, drawRadius);
                }
            }

            using (new Handles.DrawingScope(color))
            {
                Handles.DrawWireDisc(center, m_PlaneNormal, drawRadius);
                Handles.DrawSolidDisc(center, m_PlaneNormal, 1.5f * worldPerPixel);
            }
        }

        // --- helpers ----------------------------------------------------------------------

        /// <summary>The component this tool was activated for, with a selection fallback so the
        /// tool also works when `target` has not been resolved to a component.</summary>
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

        /// <summary>Cache the paint space for the whole gesture — Godot's `_to_local()` is the
        /// target's global transform inverse, so stroke points are recorded in shape-local
        /// space, which is also the space the mask rect lives in.</summary>
        private void CacheShapeSpace(DrawnShapeRenderer renderer)
        {
            m_ActiveShape = renderer;
            if (renderer != null)
            {
                var shapeTransform = renderer.transform;
                m_ShapeToWorld = shapeTransform.localToWorldMatrix;
                m_WorldToShape = shapeTransform.worldToLocalMatrix;
                m_PlaneOrigin = shapeTransform.position;
                m_PlaneNormal = shapeTransform.forward.sqrMagnitude > 1e-8f
                    ? shapeTransform.forward.normalized
                    : Vector3.forward;
                return;
            }

            m_ShapeToWorld = Matrix4x4.identity;
            m_WorldToShape = Matrix4x4.identity;
            m_PlaneOrigin = Vector3.zero;
            m_PlaneNormal = Vector3.forward;
        }

        private Vector3 ShapeToWorld(Vector2 point) => m_ShapeToWorld.MultiplyPoint3x4(point);

        private Vector2 ScreenToShape(Vector2 guiPoint)
        {
            DrawToolSettings.TryScreenToPlane(guiPoint, m_PlaneOrigin, m_PlaneNormal, out var world);
            return m_WorldToShape.MultiplyPoint3x4(world);
        }

        private float CurrentWorldPerPixel()
        {
            var probe = m_CursorValid && DrawToolSettings.TryScreenToPlane(m_CursorGuiPoint, m_PlaneOrigin, m_PlaneNormal, out var world)
                ? world
                : m_PlaneOrigin;
            return DrawToolSettings.WorldPerPixel(probe, m_PlaneOrigin, m_PlaneNormal);
        }

        /// <summary>Godot `cap_tol := clampf(3.0 / zoom, 0.1, 2.5)` measured where the stroke
        /// actually is — the capsule clamp is TIGHTER than the draw tool's [0.1, 6] px.</summary>
        private float CapsuleToleranceAtCentroid()
        {
            var centroid = Vector2.zero;
            if (m_StrokePoints.Count > 0)
            {
                for (var i = 0; i < m_StrokePoints.Count; ++i)
                    centroid += m_StrokePoints[i];
                centroid /= m_StrokePoints.Count;
            }

            var worldPerPixel = DrawToolSettings.WorldPerPixel(ShapeToWorld(centroid), m_PlaneOrigin, m_PlaneNormal);
            return DrawToolSettings.CapsuleToleranceAt(worldPerPixel);
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<byte>();
            var copy = new byte[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static int BeginUndoGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        /// <summary>Undo restores the SERIALIZED mask bytes and curve on the asset; the mask
        /// image, the display texture and the derived mesh are all runtime state that has to be
        /// rebuilt from them — the Unity equivalent of Godot's undo calling apply_mask_png.</summary>
        private static void OnUndoRedoPerformed()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go == null)
                    continue;

                var renderer = go.GetComponent<DrawnShapeRenderer>();
                if (renderer != null)
                    renderer.Regenerate();

                var paint = go.GetComponent<ShapePaint>();
                if (paint == null)
                    continue;

                var asset = renderer != null ? renderer.asset : null;
                paint.ApplyMaskPng(asset != null ? asset.maskPng : Array.Empty<byte>());
                paint.SyncLayer();
            }

            SceneView.RepaintAll();
        }
    }
}
