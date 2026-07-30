using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Shared, EditorPrefs-backed state for the Draw-to-Play scene tools plus the handful of
    /// view-space helpers both tools need. This is the Unity stand-in for the terrain_paint
    /// toolbar state (`_shape_opt`, `_force_new_btn`) and its px constants (GIZMO_RING, the
    /// 3 px fit tolerance, the 1.5 px stroke sampling step).
    ///
    /// Unit note: every constant below is expressed in Godot viewport pixels exactly as in
    /// terrain_paint.gd; callers convert to world units with <see cref="WorldPerPixel"/>
    /// (world units per SCREEN pixel at a given world position), which is the Unity
    /// equivalent of Godot's `1.0 / _view_xform().get_scale().x`.
    /// </summary>
    public static class DrawToolSettings
    {
        /// <summary>Draw-tool geometry mode. Values match the Godot `_shape_opt` item order
        /// (0 = Free, 1 = Circle, 2 = Rect) so the ported branches read the same.</summary>
        public enum ShapeMode
        {
            Free = 0,
            Circle = 1,
            Rect = 2
        }

        // --- Godot constants (terrain_paint.gd), in viewport pixels ------------------------

        /// <summary>terrain_paint.gd GIZMO_RING (line 55).</summary>
        public const float GizmoRingPixels = 46f;

        /// <summary>Ring pick slop — `absf(dist - GIZMO_RING) &lt; 12.0` in _move_input.</summary>
        public const float GizmoRingPickPixels = 12f;

        /// <summary>Godot pivot dot radii (_forward_canvas_draw_over_viewport lines 818-819).</summary>
        public const float PivotHaloPixels = 13f;
        public const float PivotDotPixels = 3f;

        /// <summary>Freehand sampling step — `min_d := 1.5 / zoom` in _draw_input.</summary>
        public const float StrokeSampleStepPixels = 1.5f;

        /// <summary>Fit tolerance — `3.0 / zoom` in _finish_draw / _apply_stroke_poly.</summary>
        public const float FitTolerancePixels = 3f;

        /// <summary>Refit clamp from curve_kit.merge_stroke (`clampf(tolerance, 0.1, 2.5)`).</summary>
        public const float RefitToleranceMinPixels = 0.1f;
        public const float RefitToleranceMaxPixels = 2.5f;

        /// <summary>"Stroke fully inside" area epsilon — `&lt; 1.0` px² in merge_stroke.</summary>
        public const float InsideAreaEpsilonPixelsSquared = 1f;

        /// <summary>Resample spacing floor — `maxf(tol, 0.3)` in _finish_draw.</summary>
        public const float ResampleFloorPixels = 0.3f;

        /// <summary>Circle segment count — `for i in 48` in _shape_poly.</summary>
        public const int CircleSegments = 48;

        /// <summary>Degenerate-drag rejection in _shape_poly (circle radius / rect diagonal).</summary>
        public const float CircleMinRadiusPixels = 2f;
        public const float RectMinDiagonalPixels = 3f;

        /// <summary>Stroke/preview colors from _forward_canvas_draw_over_viewport (lines 801-802,
        /// 867-868): amber while adding, red while carving.</summary>
        public static readonly Color AddColor = new Color(1f, 0.85f, 0.3f, 0.9f);
        public static readonly Color SubtractColor = new Color(1f, 0.35f, 0.3f, 0.9f);

        /// <summary>Transform gizmo colors (lines 818-822).</summary>
        public static readonly Color PivotHaloColor = new Color(0.5f, 0.85f, 1f, 0.18f);
        public static readonly Color PivotDotColor = new Color(0.5f, 0.85f, 1f, 0.95f);
        public static readonly Color RingColor = new Color(1f, 0.72f, 0.3f, 0.55f);
        public static readonly Color RingHotColor = new Color(1f, 0.72f, 0.3f, 1f);
        public static readonly Color RingLabelColor = new Color(1f, 0.85f, 0.5f, 1f);

        /// <summary>Fallback used when a screen-to-plane probe fails (degenerate camera):
        /// 1 world unit == 32 Godot px, i.e. Godot parity at zoom 1.</summary>
        public const float FallbackWorldPerPixel = 1f / 32f;

        /// <summary>Rotation snap when Shift is held (_move_input line 263 snaps 15°).</summary>
        public const float RotationSnapDegrees = 15f;

        // --- Persisted tool state ---------------------------------------------------------

        private const string k_ShapeModeKey = "PowerOfFire.DrawToPlay.ShapeMode";
        private const string k_ForceNewKey = "PowerOfFire.DrawToPlay.ForceNew";

        /// <summary>Draw-tool geometry mode (Godot `_shape_opt.selected`).</summary>
        public static ShapeMode shapeMode
        {
            get => (ShapeMode)EditorPrefs.GetInt(k_ShapeModeKey, (int)ShapeMode.Free);
            set => EditorPrefs.SetInt(k_ShapeModeKey, (int)value);
        }

        /// <summary>Every stroke becomes its own object (Godot `_force_new_btn`).</summary>
        public static bool forceNew
        {
            get => EditorPrefs.GetBool(k_ForceNewKey, false);
            set => EditorPrefs.SetBool(k_ForceNewKey, value);
        }

        // --- Tolerance bundle -------------------------------------------------------------

        /// <summary>World-unit tolerances for one stroke commit, derived from the current
        /// zoom. Mirrors the Godot `tol := clampf(3.0 / zoom, 0.1, 6.0)` derivation with the
        /// px constants scaled by the same world-per-screen-pixel factor.</summary>
        public struct Tolerances
        {
            public float fit;
            public float refitMin;
            public float refitMax;
            public float insideAreaEpsilon;
            public float resampleFloor;
        }

        /// <summary>Build the tolerance bundle for a given world-units-per-screen-pixel scale.
        /// Only the fit tolerance is zoom-derived (Godot `3.0 / zoom` canvas px → 3 screen px
        /// in world units). Every CLAMP BOUND and epsilon is an ABSOLUTE canvas-px constant in
        /// Godot, and "1 canvas px" maps to the fixed 1/32 world unit (FallbackWorldPerPixel),
        /// so those use the constant factor — at Godot zoom 1 (wpp == 1/32) the two derivations
        /// agree exactly, and zooming matches Godot's clamp-engages-at-low-zoom behavior.</summary>
        public static Tolerances TolerancesAt(float worldPerPixel)
        {
            var wpp = Mathf.Max(worldPerPixel, 1e-6f);
            const float pxToUnit = FallbackWorldPerPixel;
            return new Tolerances
            {
                fit = Mathf.Clamp(FitTolerancePixels * wpp, 0.1f * pxToUnit, 6f * pxToUnit),
                refitMin = RefitToleranceMinPixels * pxToUnit,
                refitMax = RefitToleranceMaxPixels * pxToUnit,
                insideAreaEpsilon = InsideAreaEpsilonPixelsSquared * pxToUnit * pxToUnit,
                resampleFloor = ResampleFloorPixels * pxToUnit
            };
        }

        // --- Shared view-space helpers ----------------------------------------------------

        /// <summary>Intersect the ray under a GUI point with the shape's drawing plane —
        /// the Unity equivalent of Godot's `_to_local(viewport_pos)` before the local
        /// transform is applied. Plane math is done by hand so no Plane sign conventions
        /// are assumed.</summary>
        public static bool TryScreenToPlane(Vector2 guiPoint, Vector3 planeOrigin, Vector3 planeNormal, out Vector3 world)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            var denominator = Vector3.Dot(planeNormal, ray.direction);
            if (Mathf.Abs(denominator) < 1e-8f)
            {
                world = planeOrigin;
                return false;
            }

            var distance = Vector3.Dot(planeNormal, planeOrigin - ray.origin) / denominator;
            world = ray.origin + ray.direction * distance;
            return true;
        }

        /// <summary>World units covered by one screen (GUI) pixel at a world position, measured
        /// on the drawing plane. Replaces Godot's `_view_xform().get_scale().x` (zoom).</summary>
        public static float WorldPerPixel(Vector3 worldPosition, Vector3 planeOrigin, Vector3 planeNormal)
        {
            var guiPoint = HandleUtility.WorldToGUIPoint(worldPosition);
            if (!TryScreenToPlane(guiPoint, planeOrigin, planeNormal, out var a) ||
                !TryScreenToPlane(guiPoint + Vector2.right, planeOrigin, planeNormal, out var b))
                return FallbackWorldPerPixel;

            var scale = Vector3.Distance(a, b);
            return scale > 1e-7f ? scale : FallbackWorldPerPixel;
        }

        /// <summary>Toolbar icon with a guaranteed text fallback: built-in icon names are not
        /// part of the documented API surface, so a miss degrades to a label instead of an
        /// empty button.</summary>
        public static GUIContent BuildToolbarIcon(string builtInIconName, string fallbackText, string tooltip)
        {
            // UNVERIFIED: built-in editor icon names ("EditCollider", "MoveTool") are not part of
            // the documented API; a miss is caught here and degrades to the text fallback.
            GUIContent icon = null;
            try
            {
                icon = EditorGUIUtility.IconContent(builtInIconName);
            }
            catch (System.Exception)
            {
                icon = null;
            }

            if (icon != null && icon.image != null)
                return new GUIContent(icon.image, tooltip);

            return new GUIContent(fallbackText, tooltip);
        }
    }
}
