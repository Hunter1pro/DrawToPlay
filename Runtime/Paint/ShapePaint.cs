using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The paint half of curve_shape_2d.gd on a Unity component: brush stamping into a
    /// <see cref="PaintMask"/>, commit/undo through DrawnShapeAsset.maskPng, and the hidden
    /// child renderer that draws the mask clipped to the shape (port of paint / erase /
    /// commit / apply_mask_png / _ensure_mask / _sync_paint_layer, lines 204-392).
    ///
    /// EDIT-MODE MODEL (no [ExecuteAlways], matching DrawnShapeRenderer): Unity does not run
    /// OnEnable/Update on a plain MonoBehaviour in edit mode, so nothing here depends on them.
    /// The paint layer and its material are created lazily from the public API — Paint, Erase,
    /// ApplyMaskPng and <see cref="SyncLayer"/> — which is what the paint tool calls. OnValidate
    /// (which DOES run in edit mode) only refreshes a layer that already exists: creating a
    /// GameObject or calling AddComponent from inside OnValidate trips Unity's "SendMessage
    /// cannot be called during OnValidate" warning, and DrawnShapeRenderer raises
    /// geometryChanged from its own OnValidate, so the geometry handler is on the same
    /// refresh-only path. OnEnable additionally subscribes for play-mode parity.
    ///
    /// The mask is rebuilt from DrawnShapeAsset.maskPng on first touch — the Unity stand-in for
    /// Godot's _ready() load path, which has no edit-mode equivalent.
    /// </summary>
    [RequireComponent(typeof(DrawnShapeRenderer))]
    public sealed class ShapePaint : MonoBehaviour
    {
        /// <summary>Child name doubles as the ownership mark: the reference below is not
        /// serialized, so after a domain reload the layer is re-adopted by name instead of
        /// being duplicated (same trick DrawnShapeRenderer uses for its mesh/materials).</summary>
        private const string k_LayerName = "PaintLayer";

        private const string k_MeshName = "DrawnShape Paint Mesh (generated)";
        private const string k_MaterialName = "DrawnShape Paint Material (generated)";
        private const string k_ShaderName = "PowerOfFire/DrawToPlay/PaintedShape";

        /// <summary>_ensure_mask pads the shape bbox and every stamp by 8 Godot px so painting
        /// is never bounded by the outline: 8 / 32 world units.</summary>
        private const float k_MaskPad = 0.25f;

        /// <summary>Same ear-clipping tolerance DrawnShapeRenderer uses for the fill.</summary>
        private const float k_GeometryEpsilon = 1e-5f;

        // Submesh slots on the sibling DrawnShapeRenderer (shadow 0, fill 1, rim+outline 2).
        private const int k_FillSubmesh = 1;
        private const int k_LineSubmesh = 2;

        /// <summary>Used when the sibling renderer's materials cannot be read yet: the base
        /// renderer builds them at "Sprites/Default".renderQueue (3000) + 0/1/2.</summary>
        private const int k_FallbackQueue = 3002;

        /// <summary>Paint sits between the base fill and the rim/outline. The base renderer
        /// packs its three layers into consecutive queues (fill = base+1, rim+outline = base+2),
        /// so with the stock 3000/3001/3002 there is no free integer in between:
        /// <see cref="ResolvePaintQueue"/> shares the outline's queue and lets transparent
        /// sorting break the tie. SortingCriteria.CommonTransparent sorts by render queue FIRST
        /// and back-to-front distance second, so a paint layer that sits farther from the camera
        /// than the base renderer's bounds centre is submitted before the outline while still
        /// landing after the fill's lower queue. This z is deliberately past every base layer
        /// (shadow 0.0015, fill 0.001, rim 0.0005, outline 0) so the comparison holds whether or
        /// not the shadow contributes to the bounds. ZWrite is Off everywhere, so the value has
        /// no depth-test meaning — it is purely a sort key for a camera looking down +Z.
        /// If a project ever spaces the base queues out, ResolvePaintQueue picks the clean
        /// integer slot instead and this stops mattering.</summary>
        private const float k_PaintZ = 0.002f;

        private static readonly int k_MaskTexId = Shader.PropertyToID("_MaskTex");
        private static readonly int k_MaskOriginId = Shader.PropertyToID("_MaskOrigin");
        private static readonly int k_MaskSizePxId = Shader.PropertyToID("_MaskSizePx");
        private static readonly int k_ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int k_FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int k_FillTexId = Shader.PropertyToID("_FillTex");
        private static readonly int k_UseFillTexId = Shader.PropertyToID("_UseFillTex");
        private static readonly int k_PaintTex2Id = Shader.PropertyToID("_PaintTex2");
        private static readonly int k_HasTex2Id = Shader.PropertyToID("_HasTex2");
        private static readonly int k_PaintTex3Id = Shader.PropertyToID("_PaintTex3");
        private static readonly int k_HasTex3Id = Shader.PropertyToID("_HasTex3");
        private static readonly int k_FillScaleId = Shader.PropertyToID("_FillScale");

        private DrawnShapeRenderer m_Renderer;
        private GameObject m_Layer;
        private MeshFilter m_LayerFilter;
        private MeshRenderer m_LayerRenderer;
        private Mesh m_Mesh;
        private Material m_Material;
        private PaintMask m_Mask;
        private ShapeTessellator.MeshBuilder m_Builder;

        private bool m_Loaded;
        private bool m_Dirty;
        private bool m_MeshDirty = true;
        private bool m_Subscribed;

        private readonly List<int> m_Triangles = new List<int>();
        private readonly List<IReadOnlyList<Vector2>> m_Holes = new List<IReadOnlyList<Vector2>>();

        /// <summary>True when this shape carries paint — either a live mask in memory or
        /// committed bytes on the asset that have not been loaded yet. Side-effect free, so it
        /// is safe to poll from inspector/overlay GUI.</summary>
        public bool hasMask
        {
            get
            {
                if (m_Mask != null && m_Mask.isValid)
                    return true;
                var style = asset;
                return style != null && style.maskPng != null && style.maskPng.Length > 0;
            }
        }

        private DrawnShapeAsset asset => EnsureRenderer() ? m_Renderer.asset : null;

        private bool hasLiveMask => m_Mask != null && m_Mask.isValid;

        // --- public API (the paint tool codes against exactly this) --------------------

        /// <summary>Stamp the brush at a shape-LOCAL position; call <see cref="Commit"/> when
        /// the stroke ends. <paramref name="radius"/> below zero means DrawnShapeAsset.brushRadius,
        /// <paramref name="channel"/> selects texture slot 1/2/3 (0/1/2). Port of paint().</summary>
        public void Paint(Vector2 localPos, bool erase = false, float radius = -1f, int channel = 0)
        {
            var style = asset;
            if (style == null)
                return;
            float r = radius > 0f ? radius : style.brushRadius;
            if (r <= 0f)
                return;
            EnsureLoaded();
            EnsureMask(localPos, r);
            if (!hasLiveMask)
                return;
            Vector2 centerPixels = (localPos - m_Mask.rect.min) * m_Mask.resolution;
            m_Mask.Stamp(centerPixels, r * m_Mask.resolution, erase, channel, style.brushSoftness);
            m_Dirty = true;
            // Godot ends paint() with queue_redraw() -> _draw -> _sync_paint_layer
            SyncLayer();
        }

        /// <summary>Erase coverage at a shape-local position (leaves the blended weights
        /// underneath intact). Port of erase().</summary>
        public void Erase(Vector2 localPos, float radius = -1f)
        {
            Paint(localPos, true, radius);
        }

        /// <summary>Encode the mask into DrawnShapeAsset.maskPng + maskRect — the serialised
        /// form AND the undo snapshot unit. Port of commit(); no-op when nothing was painted
        /// since the last commit. The caller owns Undo.RecordObject/SetDirty on the asset
        /// (this assembly cannot reference UnityEditor).</summary>
        public void Commit()
        {
            var style = asset;
            if (style == null || !hasLiveMask || !m_Dirty)
                return;
            m_Dirty = false;
            style.maskPng = m_Mask.EncodePng();
            style.maskRect = m_Mask.rect;
            SyncLayer();
        }

        /// <summary>Undo path: replace the mask with previously committed bytes. Empty bytes
        /// clear the paint layer. Port of apply_mask_png() — and like Godot, the caller must
        /// restore DrawnShapeAsset.maskRect BEFORE calling when the stroke grew the canvas,
        /// because a PNG carries pixels only.</summary>
        public void ApplyMaskPng(byte[] bytes)
        {
            var style = asset;
            if (style == null)
                return;
            style.maskPng = bytes ?? System.Array.Empty<byte>();
            ReleaseMask();
            m_Loaded = true;
            m_Dirty = false;
            if (style.maskPng.Length > 0)
                m_Mask = PaintMask.FromPng(style.maskPng, style.maskRect, MaskResolution(style));
            SyncLayer();
        }

        /// <summary>Create/refresh the hidden paint layer: re-clip it to the current shape,
        /// push the mask and the style uniforms, hide it when there is nothing painted. The
        /// editor tool calls this after any change it makes outside Paint/Commit (edit mode has
        /// no OnEnable/Update to do it automatically). Port of _sync_paint_layer.</summary>
        public void SyncLayer()
        {
            EnsureLoaded();
            bool live = hasLiveMask;
            if (!ResolveLayer(live))
                return;
            UpdateLayer(live);
        }

        // --- lifecycle -----------------------------------------------------------------

        private void OnEnable()
        {
            Subscribe();
            SyncLayer();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        // Runs in edit mode (script load, scene open, inspector edit) where OnEnable does not.
        // Refresh only — see the class comment for why nothing is created from here. EnsureLoaded
        // is deliberately NOT called: decoding a PNG needs a scratch texture, and tearing that
        // down means DestroyImmediate, which Unity explicitly warns against from OnValidate.
        private void OnValidate()
        {
            Subscribe();
            m_MeshDirty = true;
            if (ResolveLayer(false))
                UpdateLayer(hasLiveMask);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseGenerated();
        }

        private void Subscribe()
        {
            if (m_Subscribed || !EnsureRenderer())
                return;
            m_Renderer.geometryChanged += OnGeometryChanged;
            m_Subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed || m_Renderer == null)
            {
                m_Subscribed = false;
                return;
            }
            m_Renderer.geometryChanged -= OnGeometryChanged;
            m_Subscribed = false;
        }

        /// <summary>Every sculpt gesture funnels through DrawnShapeRenderer.Regenerate, so this
        /// is where the paint layer follows a reshaped outline. Refresh-only: Regenerate is also
        /// called from the renderer's OnValidate, where creating GameObjects is illegal.</summary>
        private void OnGeometryChanged()
        {
            m_MeshDirty = true;
            if (ResolveLayer(false))
                UpdateLayer(hasLiveMask);
        }

        // --- mask ----------------------------------------------------------------------

        /// <summary>Unity has no _ready() in edit mode, so the committed mask is decoded the
        /// first time anything touches this component.</summary>
        private void EnsureLoaded()
        {
            if (m_Loaded)
                return;
            m_Loaded = true;
            var style = asset;
            if (style == null || style.maskPng == null || style.maskPng.Length == 0)
                return;
            m_Mask = PaintMask.FromPng(style.maskPng, style.maskRect, MaskResolution(style));
        }

        /// <summary>Port of _ensure_mask: create the mask over the shape bbox grown by 8 Godot px
        /// and merged with the stamp rect, or grow an existing one so a stamp landing outside is
        /// never clipped. DrawnShapeAsset.maskRect tracks the live rect immediately, exactly like
        /// Godot's mask_rect export, so an undo snapshot taken before the stroke is complete.</summary>
        private void EnsureMask(Vector2 center, float radius)
        {
            var style = asset;
            if (style == null)
                return;
            float pad = radius + k_MaskPad;
            var needed = new Rect(center - Vector2.one * pad, Vector2.one * pad * 2f);

            if (!hasLiveMask)
            {
                Rect bounds = needed;
                var points = BuildOuterRing();
                if (points != null && points.Count >= 3)
                {
                    bounds = new Rect(points[0], Vector2.zero);
                    for (int i = 0; i < points.Count; i++)
                        bounds = PaintMask.Expand(bounds, points[i]);
                    bounds = PaintMask.Union(PaintMask.Grow(bounds, k_MaskPad), needed);
                }
                m_Mask = PaintMask.Create(bounds, MaskResolution(style));
                style.maskRect = m_Mask.rect;
                return;
            }

            if (PaintMask.Encloses(m_Mask.rect, needed))
                return;
            if (m_Mask.Grow(PaintMask.Union(m_Mask.rect, needed)))
                style.maskRect = m_Mask.rect;
        }

        private void ReleaseMask()
        {
            if (m_Mask != null)
                m_Mask.Release();
            m_Mask = null;
        }

        private static float MaskResolution(DrawnShapeAsset style)
        {
            return Mathf.Max(style.maskResolution, 1f);
        }

        // --- paint layer ---------------------------------------------------------------

        /// <summary>Resolve (and optionally create) the hidden child. Creation happens only from
        /// the public API — never from OnValidate or the geometryChanged handler.</summary>
        private bool ResolveLayer(bool allowCreate)
        {
            if (m_LayerRenderer != null && m_LayerFilter != null)
                return true;
            if (m_Layer == null)
            {
                var found = transform.Find(k_LayerName);
                if (found != null)
                    m_Layer = found.gameObject;
            }
            if (m_Layer == null)
            {
                if (!allowCreate)
                    return false;
                m_Layer = new GameObject(k_LayerName, typeof(MeshFilter), typeof(MeshRenderer))
                {
                    hideFlags = HideFlags.DontSave
                };
                m_Layer.transform.SetParent(transform, false);
                m_MeshDirty = true;
            }
            m_Layer.layer = gameObject.layer;
            // the layer lives in the shape's own local space: the mesh carries the z sort offset
            // so object-space XY still equals the shape-local XY the shader expects
            var layerTransform = m_Layer.transform;
            layerTransform.localPosition = Vector3.zero;
            layerTransform.localRotation = Quaternion.identity;
            layerTransform.localScale = Vector3.one;

            // AddComponent is also off-limits on the refresh-only path (it is what actually
            // raises the OnValidate SendMessage warning), so a child stripped of its components
            // is only repaired when creation is allowed
            if (!m_Layer.TryGetComponent(out m_LayerFilter))
            {
                if (!allowCreate)
                {
                    m_LayerFilter = null;
                    m_LayerRenderer = null;
                    return false;
                }
                m_LayerFilter = m_Layer.AddComponent<MeshFilter>();
            }
            if (!m_Layer.TryGetComponent(out m_LayerRenderer))
            {
                if (!allowCreate)
                {
                    m_LayerFilter = null;
                    m_LayerRenderer = null;
                    return false;
                }
                m_LayerRenderer = m_Layer.AddComponent<MeshRenderer>();
            }
            m_LayerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_LayerRenderer.receiveShadows = false;
            m_LayerRenderer.lightProbeUsage = LightProbeUsage.Off;
            m_LayerRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_LayerRenderer.allowOcclusionWhenDynamic = false;
            return true;
        }

        private void UpdateLayer(bool live)
        {
            if (m_LayerRenderer == null)
                return;
            if (!live)
            {
                m_LayerRenderer.enabled = false;
                return;
            }
            if (m_MeshDirty || m_Mesh == null)
            {
                m_MeshDirty = false;
                var fill = BuildFillPolygon();
                BuildMesh(fill);
            }
            if (m_Mesh == null || m_Mesh.vertexCount == 0 || !EnsureMaterial())
            {
                m_LayerRenderer.enabled = false;
                return;
            }
            ApplyUniforms();
            m_LayerRenderer.enabled = true;
        }

        /// <summary>The polygon the paint is clipped to: the same fill DrawnShapeRenderer draws —
        /// DISPLACED outer ring (Godot passes _points(), i.e. the wobbled ring, to
        /// _sync_paint_layer) with the hole rings keyholed in, then ear-clipped. The renderer's
        /// equivalent helpers are private, so the ring/hole assembly is mirrored here rather than
        /// widening its API.</summary>
        private List<Vector2> BuildFillPolygon()
        {
            var outer = BuildOuterRing();
            if (outer == null || outer.Count < 3)
                return null;
            CollectHoles(outer);
            var fill = m_Holes.Count > 0 ? DrawKit.Keyhole(outer, m_Holes) : outer;
            if (fill == null || fill.Count < 3)
                fill = outer;
            return fill;
        }

        /// <summary>Port of _points() — delegated to DrawnShapeRenderer.BuildRenderRing()
        /// (M4): baked ring → FORM MORPH blend → render wobble, the one implementation every
        /// deformable consumer shares. The paint layer therefore follows a morph for free:
        /// PoseAnimator's Regenerate raises geometryChanged, which re-clips this layer.</summary>
        private List<Vector2> BuildOuterRing()
        {
            return EnsureRenderer() ? m_Renderer.BuildRenderRing() : null;
        }

        /// <summary>Mirror of DrawnShapeRenderer.CollectHoles: bake + wobble each hole with its
        /// own seed offset and keep only the rings that stay inside the outline.</summary>
        private void CollectHoles(IReadOnlyList<Vector2> outer)
        {
            m_Holes.Clear();
            var style = asset;
            if (style == null || style.holeCurves == null)
                return;
            float interval = Mathf.Max(style.bakeInterval, 1e-4f);
            int index = 0;
            for (int i = 0; i < style.holeCurves.Count; i++)
            {
                // Godot increments before skipping, so a null/short entry still consumes a seed
                index++;
                var holeCurve = style.holeCurves[i];
                if (holeCurve == null || holeCurve.pointCount < 3)
                    continue;
                var ring = BuildBakedRing(holeCurve, interval);
                if (ring.Count < 3)
                    continue;
                var displaced = DrawKit.Displace(ring, style.edgeNoiseAmp,
                    style.edgeNoiseWavelength, style.edgeNoiseSeed + index, true);
                if (displaced == null || displaced.Count < 3)
                    continue;
                if (!IsRingInside(displaced, outer))
                    continue;
                m_Holes.Add(displaced);
            }
        }

        /// <summary>Mirror of DrawnShapeRenderer.BuildBakedRing: bake, then drop the point that
        /// duplicates the start on a manually closed loop (epsilon = interval * 0.125).</summary>
        private static List<Vector2> BuildBakedRing(DrawnCurve curve, float interval)
        {
            var points = curve != null ? curve.GetBakedPoints(interval) : null;
            if (points == null)
                return new List<Vector2>();
            float epsilon = Mathf.Max(interval * 0.125f, 1e-6f);
            if (points.Count > 1
                && (points[0] - points[points.Count - 1]).sqrMagnitude < epsilon * epsilon)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private static bool IsRingInside(IReadOnlyList<Vector2> ring, IReadOnlyList<Vector2> outer)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                if (!PolyBool.IsPointInPolygon(ring[i], outer))
                    return false;
            }
            return true;
        }

        private void BuildMesh(IReadOnlyList<Vector2> fill)
        {
            var mesh = EnsureMesh();
            if (mesh == null)
                return;
            if (m_Builder == null)
                m_Builder = new ShapeTessellator.MeshBuilder();
            m_Builder.Clear();
            m_Builder.EnsureSubmeshCount(1);
            m_Builder.currentSubmesh = 0;
            if (fill != null && fill.Count >= 3)
            {
                ShapeTessellator.Triangulate(fill, m_Triangles, k_GeometryEpsilon);
                // UV0 is position (uvScale 1) purely for parity with the base renderer — this
                // shader derives both of its UV sets from the object-space position instead.
                ShapeTessellator.AppendTriangles(m_Builder, fill, m_Triangles, Vector2.zero,
                    k_PaintZ, Color.white, 1f);
            }
            m_Builder.ToMesh(mesh);
            if (m_LayerFilter != null && m_LayerFilter.sharedMesh != mesh)
                m_LayerFilter.sharedMesh = mesh;
        }

        private Mesh EnsureMesh()
        {
            if (m_Mesh != null)
                return m_Mesh;
            if (m_LayerFilter == null)
                return null;
            var existing = m_LayerFilter.sharedMesh;
            if (existing != null && existing.name == k_MeshName)
            {
                m_Mesh = existing;
                return m_Mesh;
            }
            m_Mesh = new Mesh
            {
                name = k_MeshName,
                hideFlags = HideFlags.DontSave
            };
            return m_Mesh;
        }

        private bool EnsureMaterial()
        {
            if (m_Material == null && m_LayerRenderer != null)
            {
                var existing = m_LayerRenderer.sharedMaterial;
                if (existing != null && existing.name == k_MaterialName)
                    m_Material = existing;
            }
            if (m_Material == null)
            {
                var shader = Shader.Find(k_ShaderName);
                if (shader == null)
                    return false;
                m_Material = new Material(shader)
                {
                    name = k_MaterialName,
                    hideFlags = HideFlags.DontSave
                };
            }
            int queue = ResolvePaintQueue();
            if (m_Material.renderQueue != queue)
                m_Material.renderQueue = queue;
            if (m_LayerRenderer != null && m_LayerRenderer.sharedMaterial != m_Material)
                m_LayerRenderer.sharedMaterial = m_Material;
            return true;
        }

        /// <summary>Slot the paint between the base fill and the base rim/outline. Reads the
        /// sibling renderer's own materials rather than assuming 3000/3001/3002, so a changed
        /// base shader moves the paint with it. See <see cref="k_PaintZ"/> for how the
        /// no-free-integer case is resolved.</summary>
        private int ResolvePaintQueue()
        {
            if (!TryGetComponent<MeshRenderer>(out var baseRenderer))
                return k_FallbackQueue;
            var materials = baseRenderer.sharedMaterials;
            if (materials == null || materials.Length <= k_LineSubmesh)
                return k_FallbackQueue;
            var fill = materials[k_FillSubmesh];
            var line = materials[k_LineSubmesh];
            if (fill == null || line == null)
                return k_FallbackQueue;
            int fillQueue = fill.renderQueue;
            int lineQueue = line.renderQueue;
            if (lineQueue - fillQueue > 1)
                return fillQueue + 1;
            return Mathf.Max(fillQueue, lineQueue);
        }

        /// <summary>Port of the set_shader_parameter block in _sync_paint_layer.</summary>
        private void ApplyUniforms()
        {
            var style = asset;
            if (m_Material == null || style == null)
                return;
            m_Material.SetTexture(k_MaskTexId, m_Mask != null ? m_Mask.texture : null);
            Vector2 origin = m_Mask != null ? m_Mask.rect.min : Vector2.zero;
            m_Material.SetVector(k_MaskOriginId, new Vector4(origin.x, origin.y, 0f, 0f));
            float sizeX = m_Mask != null ? m_Mask.width : 1f;
            float sizeY = m_Mask != null ? m_Mask.height : 1f;
            m_Material.SetVector(k_MaskSizePxId, new Vector4(sizeX, sizeY, 0f, 0f));
            // the mask's own resolution, not the asset field: PaintMask may have lowered it to
            // stay inside its size cap, and editing the field later must not shift existing paint
            m_Material.SetFloat(k_ResolutionId,
                m_Mask != null ? m_Mask.resolution : MaskResolution(style));

            m_Material.SetColor(k_FillColorId, style.fillColor);
            m_Material.SetTexture(k_FillTexId, style.fillTexture);
            m_Material.SetFloat(k_UseFillTexId, style.fillTexture != null ? 1f : 0f);
            m_Material.SetTexture(k_PaintTex2Id, style.paintTexture2);
            m_Material.SetFloat(k_HasTex2Id, style.paintTexture2 != null ? 1f : 0f);
            m_Material.SetTexture(k_PaintTex3Id, style.paintTexture3);
            m_Material.SetFloat(k_HasTex3Id, style.paintTexture3 != null ? 1f : 0f);
            m_Material.SetFloat(k_FillScaleId, Mathf.Max(style.textureScale, 1e-4f));
        }

        // --- plumbing ------------------------------------------------------------------

        private bool EnsureRenderer()
        {
            if (m_Renderer == null)
                TryGetComponent(out m_Renderer);
            return m_Renderer != null;
        }

        /// <summary>Destroying is confined to OnDestroy (DestroyImmediate is illegal from
        /// OnValidate/OnDisable). The child GameObject is only torn down in play mode, where
        /// Destroy is deferred and safe; in the editor it carries HideFlags.DontSave, so it is
        /// never serialised and disappears with the next scene load anyway.</summary>
        private void ReleaseGenerated()
        {
            ReleaseMask();
            DestroyGenerated(m_Mesh);
            DestroyGenerated(m_Material);
            m_Mesh = null;
            m_Material = null;
            m_LayerFilter = null;
            m_LayerRenderer = null;
            if (Application.isPlaying && m_Layer != null)
                Destroy(m_Layer);
            m_Layer = null;
        }

        private static void DestroyGenerated(Object generated)
        {
            if (generated == null)
                return;
            if (Application.isPlaying)
                Destroy(generated);
            else
                DestroyImmediate(generated);
        }
    }
}
