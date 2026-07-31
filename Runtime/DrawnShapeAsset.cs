using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The drawing is the source of truth: a fitted Bézier outline plus hole rings and
    /// render style. Collision, skin mesh, and render mesh are all derived from this
    /// asset and regenerate non-destructively (draw-tool-port-brief.md §3).
    /// M0 scope: outline + holes + fill/outline/rim style + organic edge wobble.
    /// Skin params, morph targets, and paint mask arrive with M2/M3/M4.
    ///
    /// Unit convention: 1 Unity world unit ≈ 32 Godot px. Style defaults below are the
    /// curve_shape_2d.gd defaults divided by 32; DrawKit itself is unit-agnostic
    /// (tolerances are passed in by callers, editor tools derive them from screen px).
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/Drawn Shape", fileName = "DrawnShape")]
    public sealed class DrawnShapeAsset : ScriptableObject
    {
        public DrawnCurve curve = new DrawnCurve();
        public List<DrawnCurve> holeCurves = new List<DrawnCurve>();

        [Header("Style")]
        public Color fillColor = new Color(0.22f, 0.31f, 0.47f);
        public Texture2D fillTexture;
        public float textureScale = 2f;
        public Color outlineColor = new Color(0.04f, 0.06f, 0.09f);
        public float outlineWidth = 0.05f;
        public Color rimColor = new Color(0.5f, 0.64f, 0.79f);
        public float rimWidth = 0f;
        public float bakeInterval = 0.125f;

        [Header("Organic edge (hand-drawn wobble)")]
        [Range(0f, 0.5f)] public float edgeNoiseAmp = 0f;
        public float edgeNoiseWavelength = 0.4f;
        public int edgeNoiseSeed = 7;

        [Header("Paint (texture-override brush)")]
        public Texture2D paintTexture2;
        public Texture2D paintTexture3;
        /// <summary>Brush strokes crossing the outline also sculpt it (stroke capsule
        /// union/carve) — curve_shape_2d.gd paint_sculpts_edges.</summary>
        public bool paintSculptsEdges = true;
        /// <summary>Mask pixels per world unit — Godot paint_resolution 4 px/px × 32.</summary>
        public float maskResolution = 128f;
        [Range(0.015625f, 4f)] public float brushRadius = 0.1f;
        [Range(0f, 1f)] public float brushSoftness = 0.5f;
        /// <summary>Painted weight mask as PNG bytes (RGB = texture slots 1-3 weights,
        /// A = feathered presence) + its local-space rect. The undo snapshot unit,
        /// exactly like Godot mask_png/mask_rect. Managed by ShapePaint — hand-editing
        /// serialized bytes is never intended.</summary>
        [HideInInspector] public byte[] maskPng = System.Array.Empty<byte>();
        [HideInInspector] public Rect maskRect;

        [Header("Form morph (shape keyframes)")]
        /// <summary>Captured form variants (whole-curve snapshots). The renderer blends
        /// along [base, targets...] by DrawnShapeRenderer.morphWeight — correspondence is
        /// positional (resample + align), so redrawing any form keeps morphs working.</summary>
        public List<DrawnCurve> morphTargets = new List<DrawnCurve>();

        [Header("Skeleton (skinned deform)")]
        /// <summary>Interior lattice spacing for the skin mesh — Godot skin_detail 6 px.</summary>
        public float skinDetail = 0.1875f;
        /// <summary>Weight falloff softness — Godot skin_softness 4 px.</summary>
        public float skinSoftness = 0.125f;
        /// <summary>Bone names this shape binds to (empty = all bones of the rig).
        /// Animation references bones by NAME, so redrawing never breaks bindings.</summary>
        public List<string> includeBones = new List<string>();
        public List<string> excludeBones = new List<string>();

        [Header("Depth")]
        [Range(0f, 1f)] public float fillShade = 0f;
        public Color shadowColor = new Color(0f, 0f, 0f, 0f);
        // Godot's (2, 3) is down-right in Y-down screen space; Unity is Y-up, so the
        // faithful drop shadow needs the Y sign flipped.
        public Vector2 shadowOffset = new Vector2(0.06f, -0.09f);
    }
}
