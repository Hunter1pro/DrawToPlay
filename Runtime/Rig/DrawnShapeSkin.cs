using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Skinned-deform layer for a drawn shape — the Unity port of curve_shape_2d.gd's
    /// _sync_skin/_bone_weights/_sync_skin_outline path. When a ShapeRig is assigned,
    /// the base DrawnShapeRenderer's MeshRenderer is disabled and a hidden child
    /// SkinnedMeshRenderer takes over: outline ring + interior Delaunay lattice
    /// (spacing asset.skinDetail), triangles filtered by centroid-in-polygon, top-2
    /// distance-falloff weights (1/(d² + softness²)) against bone REST segments
    /// (tips clamped at the outline via DrawKit.ClampSegmentTip), fill vertex colors/
    /// UVs + a keyholed outline band as a second submesh with the same weights.
    /// Regenerates on renderer.geometryChanged — redraw never breaks the binding
    /// because bones are matched by NAME through asset.includeBones/excludeBones.
    ///
    /// EDIT-MODE MODEL (no [ExecuteAlways], mirroring ShapePaint): the hidden layer is only
    /// ever created from <see cref="RegenerateSkin"/>, which the rig tool calls explicitly.
    /// OnValidate and the geometryChanged handler are refresh-only, because creating a
    /// GameObject or calling AddComponent from OnValidate trips Unity's "SendMessage cannot be
    /// called during OnValidate" warning — and DrawnShapeRenderer raises geometryChanged from
    /// its own OnValidate. A refresh that cannot find the layer re-enables the base
    /// MeshRenderer, so a scene reopened without its (HideFlags.DontSave) skin layer shows the
    /// undeformed shape instead of nothing.
    ///
    /// BINDING INVARIANT: bindposes are built from the RigAsset's REST chain mapped through
    /// the rig root's current transform — never from the posed bone Transforms. Binding from
    /// a posed skeleton is the exact bug Godot's rest-based _bone_rest_global exists to
    /// prevent: every rebuild would then bake the current pose in and the shape would drift.
    /// </summary>
    [RequireComponent(typeof(DrawnShapeRenderer))]
    public sealed class DrawnShapeSkin : MonoBehaviour
    {
        /// <summary>Child name doubles as the ownership mark: the reference below is not
        /// serialized, so after a domain reload the layer is re-adopted by name rather than
        /// duplicated (the trick DrawnShapeRenderer and ShapePaint both use).</summary>
        private const string k_LayerName = "SkinLayer";

        private const string k_MeshName = "DrawnShape Skin Mesh (generated)";

        /// <summary>Submesh slots on the sibling DrawnShapeRenderer (shadow 0, fill 1,
        /// rim+outline 2) — the skin reuses its generated fill/line materials, so a style
        /// change on the base renderer carries over without a second material set.</summary>
        private const int k_BaseFillSlot = 1;
        private const int k_BaseLineSlot = 2;

        /// <summary>The scene skeleton this shape binds to.</summary>
        public ShapeRig rig;

        private DrawnShapeRenderer m_Renderer;
        private MeshRenderer m_BaseRenderer;
        private GameObject m_Layer;
        private SkinnedMeshRenderer m_SkinRenderer;
        private Mesh m_Mesh;
        private SkinMeshBuilder.SkinData m_Data;

        private readonly List<string> m_BoneNames = new List<string>();
        private readonly List<Transform> m_BoneTransforms = new List<Transform>();
        private readonly List<int> m_BoneAssetIndices = new List<int>();
        private readonly List<Matrix4x4> m_RestWorld = new List<Matrix4x4>();
        private readonly List<SkinMeshBuilder.BoneSegment> m_Segments =
            new List<SkinMeshBuilder.BoneSegment>();
        private Transform[] m_Bones = System.Array.Empty<Transform>();
        private Matrix4x4[] m_Bindposes = System.Array.Empty<Matrix4x4>();
        private readonly Material[] m_Materials = new Material[2];

        private bool m_Skinned;
        private bool m_Subscribed;

        /// <summary>True when a valid rig with at least one usable bone is bound and
        /// the skinned layer is active.</summary>
        public bool isSkinned
        {
            get { return m_Skinned && m_SkinRenderer != null && m_SkinRenderer.enabled; }
        }

        /// <summary>Rebuild the skin mesh, weights, and bindposes from the current
        /// drawing + rig REST data. Callable in edit mode (no [ExecuteAlways];
        /// editor tooling calls this explicitly, mirroring the ShapePaint pattern).</summary>
        public void RegenerateSkin()
        {
            Rebuild(true);
        }

        /// <summary>Per-vertex influence data of the last generated skin, for the
        /// debug overlay: positions (shape-local), dominant bone index per vertex,
        /// and the bone names in index order. False when not skinned.
        ///
        /// The lists are the live internal buffers — read them, never mutate them, and expect
        /// them to be replaced by the next RegenerateSkin. Godot cached only the outline
        /// slice (_skin_outline_weights); this returns the whole fill lattice, so the overlay
        /// can show interior binding too.</summary>
        public bool TryGetInfluences(out List<Vector2> vertices, out List<int> dominantBone,
            out List<string> boneNames)
        {
            vertices = null;
            dominantBone = null;
            boneNames = null;
            if (!isSkinned || m_Data == null || m_Data.influenceVertices.Count == 0)
                return false;
            vertices = m_Data.influenceVertices;
            dominantBone = m_Data.influenceDominantBone;
            boneNames = m_BoneNames;
            return true;
        }

        // --- lifecycle -----------------------------------------------------------------

        private void OnEnable()
        {
            Subscribe();
            Rebuild(true);
        }

        private void OnDisable()
        {
            Unsubscribe();
            // a disabled skin must never leave the shape invisible
            m_Skinned = false;
            if (m_SkinRenderer != null)
                m_SkinRenderer.enabled = false;
            SetBaseRendererEnabled(true);
        }

        // Runs in edit mode (script load, scene open, inspector edit) where OnEnable does not.
        // Refresh-only: see the class comment for why nothing is created from here.
        private void OnValidate()
        {
            Subscribe();
            Rebuild(false);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            SetBaseRendererEnabled(true);
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
        /// is where a redrawn shape gets a fresh skin — the binding survives because bones are
        /// matched by name, not by index into the old geometry.</summary>
        private void OnGeometryChanged()
        {
            Rebuild(false);
        }

        // --- build ---------------------------------------------------------------------

        private void Rebuild(bool allowCreate)
        {
            if (!EnsureRenderer())
                return;
            DrawnShapeAsset style = m_Renderer.asset;
            List<Vector2> ring = style != null && CollectBones(style) ? BuildOuterRing() : null;
            if (ring == null || ring.Count < 3)
            {
                Unskin();
                return;
            }
            if (!ResolveLayer(allowCreate))
            {
                Unskin();
                return;
            }

            BuildSegments(ring);
            if (m_Data == null)
                m_Data = new SkinMeshBuilder.SkinData();
            Mesh mesh = EnsureMesh();
            if (mesh == null || !SkinMeshBuilder.Build(mesh, m_Data, ring, style, m_Segments))
            {
                Unskin();
                return;
            }

            BuildBindposes();
            mesh.boneWeights = m_Data.boneWeights;
            mesh.bindposes = m_Bindposes;

            if (m_SkinRenderer.sharedMesh != mesh)
                m_SkinRenderer.sharedMesh = mesh;
            m_SkinRenderer.bones = m_Bones;
            // bounds live in the root bone's space; the first bound bone is the closest thing
            // this rig has to a root, and updateWhenOffscreen keeps them honest while posing
            m_SkinRenderer.rootBone = m_Bones.Length > 0 ? m_Bones[0] : rig.transform;
            m_SkinRenderer.updateWhenOffscreen = true;
            // top-2 weights, so anything above Bone2 would only cost bandwidth
            m_SkinRenderer.quality = SkinQuality.Bone2;
            ApplyMaterials();

            m_SkinRenderer.enabled = true;
            m_Skinned = true;
            SetBaseRendererEnabled(false);
        }

        /// <summary>Hand rendering back to the base MeshRenderer (rig removed, no bones, no
        /// shape, or a layer that cannot be created on a refresh-only path).</summary>
        private void Unskin()
        {
            m_Skinned = false;
            if (m_SkinRenderer != null)
                m_SkinRenderer.enabled = false;
            SetBaseRendererEnabled(true);
        }

        /// <summary>Port of the bone collection + filter block (lines 594-621): every rig bone
        /// in asset order, includeBones first (when non-empty ONLY those), then excludeBones
        /// removed. Bones with no scene Transform are skipped — Godot's bones ARE nodes, so it
        /// has no such case; here a RigAsset entry can exist before SyncBones ran.</summary>
        private bool CollectBones(DrawnShapeAsset style)
        {
            m_BoneNames.Clear();
            m_BoneTransforms.Clear();
            m_BoneAssetIndices.Clear();
            if (rig == null || rig.rig == null || rig.rig.bones == null)
                return false;

            List<RigAsset.RigBone> bones = rig.rig.bones;
            List<string> include = style.includeBones;
            List<string> exclude = style.excludeBones;
            for (int i = 0; i < bones.Count; i++)
            {
                string boneName = bones[i].name;
                if (string.IsNullOrEmpty(boneName))
                    continue;
                if (include != null && include.Count > 0 && !include.Contains(boneName))
                    continue;
                if (exclude != null && exclude.Count > 0 && exclude.Contains(boneName))
                    continue;
                Transform bone = rig.FindBone(boneName);
                if (bone == null)
                    continue;
                m_BoneNames.Add(boneName);
                m_BoneTransforms.Add(bone);
                m_BoneAssetIndices.Add(i);
            }
            // "never attach to a boneless skeleton — the shape would vanish" (line 618)
            if (m_BoneNames.Count == 0)
                return false;
            if (m_Bones.Length != m_BoneTransforms.Count)
                m_Bones = new Transform[m_BoneTransforms.Count];
            for (int i = 0; i < m_BoneTransforms.Count; i++)
                m_Bones[i] = m_BoneTransforms[i];
            return true;
        }

        /// <summary>Port of _bone_rest_global + the segs loop (lines 624-630): each bone's REST
        /// pose accumulated to rig-root space, pushed through the rig root's transform into the
        /// world and back into shape-local space, with the tip at (length, 0) clamped where the
        /// segment leaves the outline so an oversized default bone binds only up to the edge.
        /// </summary>
        private void BuildSegments(IReadOnlyList<Vector2> ring)
        {
            m_Segments.Clear();
            m_RestWorld.Clear();
            Matrix4x4 rigToWorld = rig.transform.localToWorldMatrix;
            Matrix4x4 worldToShape = transform.worldToLocalMatrix;
            List<RigAsset.RigBone> bones = rig.rig.bones;
            for (int i = 0; i < m_BoneAssetIndices.Count; i++)
            {
                int assetIndex = m_BoneAssetIndices[i];
                Matrix4x4 restWorld = rigToWorld * rig.rig.RestRootMatrix(assetIndex);
                m_RestWorld.Add(restWorld);
                Vector2 start = worldToShape.MultiplyPoint3x4(restWorld.MultiplyPoint3x4(Vector3.zero));
                Vector3 tipRest = new Vector3(bones[assetIndex].length, 0f, 0f);
                Vector2 tip = worldToShape.MultiplyPoint3x4(restWorld.MultiplyPoint3x4(tipRest));
                m_Segments.Add(new SkinMeshBuilder.BoneSegment
                {
                    start = start,
                    tip = DrawKit.ClampSegmentTip(start, tip, ring)
                });
            }
        }

        /// <summary>bindpose[i] = (bone REST world)⁻¹ * shape local-to-world. Using the REST
        /// matrix rather than bone.worldToLocalMatrix is what makes a rebuild while the
        /// skeleton is posed a no-op instead of a drift.</summary>
        private void BuildBindposes()
        {
            if (m_Bindposes.Length != m_RestWorld.Count)
                m_Bindposes = new Matrix4x4[m_RestWorld.Count];
            Matrix4x4 shapeToWorld = transform.localToWorldMatrix;
            for (int i = 0; i < m_RestWorld.Count; i++)
                m_Bindposes[i] = m_RestWorld[i].inverse * shapeToWorld;
        }

        /// <summary>Port of _points() — delegated to DrawnShapeRenderer.BuildRenderRing()
        /// (M4): baked ring → FORM MORPH blend → render wobble. Godot passes _points()
        /// (morphed + wobbled) to _sync_skin, so the skin follows the drawn edge exactly like
        /// the paint layer does — collision keeps using the raw ring. Sharing the renderer's
        /// single implementation is what makes a morph channel deform the skinned mesh too:
        /// PoseAnimator writes morphWeight and calls Regenerate, geometryChanged lands here,
        /// and this ring already carries the blend.</summary>
        private List<Vector2> BuildOuterRing()
        {
            return m_Renderer != null ? m_Renderer.BuildRenderRing() : null;
        }

        // --- generated objects ----------------------------------------------------------

        /// <summary>Resolve (and optionally create) the hidden SkinnedMeshRenderer child.
        /// Creation happens only from RegenerateSkin — never from OnValidate or the
        /// geometryChanged handler.</summary>
        private bool ResolveLayer(bool allowCreate)
        {
            if (m_SkinRenderer != null)
            {
                SyncLayerTransform();
                return true;
            }
            if (m_Layer == null)
            {
                Transform found = transform.Find(k_LayerName);
                if (found != null)
                    m_Layer = found.gameObject;
            }
            if (m_Layer == null)
            {
                if (!allowCreate)
                    return false;
                m_Layer = new GameObject(k_LayerName, typeof(SkinnedMeshRenderer))
                {
                    hideFlags = HideFlags.DontSave
                };
                m_Layer.transform.SetParent(transform, false);
            }
            if (!m_Layer.TryGetComponent(out m_SkinRenderer))
            {
                // AddComponent is what actually raises the OnValidate SendMessage warning, so a
                // child stripped of its renderer is only repaired when creation is allowed
                if (!allowCreate)
                    return false;
                m_SkinRenderer = m_Layer.AddComponent<SkinnedMeshRenderer>();
            }
            SyncLayerTransform();
            m_SkinRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_SkinRenderer.receiveShadows = false;
            m_SkinRenderer.lightProbeUsage = LightProbeUsage.Off;
            m_SkinRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_SkinRenderer.allowOcclusionWhenDynamic = false;
            return true;
        }

        /// <summary>The layer lives in the shape's own local space; skinned vertices ignore this
        /// transform entirely (bones drive them), but keeping it identity means the mesh the
        /// builder produces is directly comparable to the base renderer's.</summary>
        private void SyncLayerTransform()
        {
            if (m_Layer == null)
                return;
            m_Layer.layer = gameObject.layer;
            Transform layer = m_Layer.transform;
            layer.localPosition = Vector3.zero;
            layer.localRotation = Quaternion.identity;
            layer.localScale = Vector3.one;
        }

        private Mesh EnsureMesh()
        {
            if (m_Mesh != null)
                return m_Mesh;
            if (m_SkinRenderer == null)
                return null;
            Mesh existing = m_SkinRenderer.sharedMesh;
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

        /// <summary>Reuse the base renderer's generated fill/line materials: submesh 0 of the
        /// skin is the fill, submesh 1 the outline band. Reading them (rather than making new
        /// ones) keeps fill texture, colour space and render queues in one place.</summary>
        private void ApplyMaterials()
        {
            Material fill = null;
            Material line = null;
            if (EnsureBaseRenderer())
            {
                Material[] materials = m_BaseRenderer.sharedMaterials;
                if (materials != null)
                {
                    if (materials.Length > k_BaseFillSlot)
                        fill = materials[k_BaseFillSlot];
                    if (materials.Length > k_BaseLineSlot)
                        line = materials[k_BaseLineSlot];
                }
            }
            m_Materials[0] = fill;
            m_Materials[1] = line;
            Material[] current = m_SkinRenderer.sharedMaterials;
            if (current != null && current.Length == 2 && current[0] == fill && current[1] == line)
                return;
            m_SkinRenderer.sharedMaterials = m_Materials;
        }

        private void SetBaseRendererEnabled(bool enabledValue)
        {
            if (!EnsureBaseRenderer())
                return;
            if (m_BaseRenderer.enabled != enabledValue)
                m_BaseRenderer.enabled = enabledValue;
        }

        private bool EnsureRenderer()
        {
            if (m_Renderer == null)
                TryGetComponent(out m_Renderer);
            return m_Renderer != null;
        }

        private bool EnsureBaseRenderer()
        {
            if (m_BaseRenderer == null)
                TryGetComponent(out m_BaseRenderer);
            return m_BaseRenderer != null;
        }

        /// <summary>Destroying is confined to OnDestroy (DestroyImmediate is illegal from
        /// OnValidate/OnDisable). The child GameObject is only torn down in play mode, where
        /// Destroy is deferred and safe; in the editor it carries HideFlags.DontSave, so it is
        /// never serialised and disappears with the next scene load anyway.</summary>
        private void ReleaseGenerated()
        {
            if (m_Mesh != null)
            {
                if (Application.isPlaying)
                    Destroy(m_Mesh);
                else
                    DestroyImmediate(m_Mesh);
            }
            m_Mesh = null;
            m_SkinRenderer = null;
            if (Application.isPlaying && m_Layer != null)
                Destroy(m_Layer);
            m_Layer = null;
        }
    }
}
