using System.Collections.Generic;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    public enum TerrainCollisionMode
    {
        Chain,  // one-sided walkable surface: outer + hole rings as PhysicsChain loops
        Solid   // filled interior: convex decomposition into PolygonGeometry shapes
    }

    /// <summary>
    /// Solid ground derived from a drawing — the Unity port of TerrainBlob2D
    /// (draw-tool-port-brief.md §4/§5). A static PhysicsBody whose collision is derived
    /// from the DrawnShapeRenderer's RAW baked rings (pre-wobble; wobble is render-only)
    /// through PhysicsComposer: outer ring as an OR layer, every hole ring as a NOT
    /// layer, then CreateChainGeometry (Chain mode) or CreatePolygonGeometry (Solid).
    /// Subscribes to DrawnShapeRenderer.geometryChanged, so every sculpt gesture —
    /// including in play mode — rebuilds collision live on the existing body
    /// (shapes/chains destroyed and recreated; the body itself is never touched).
    ///
    /// Derivation is deliberately independent of the body: the composer pipeline is pure
    /// geometry, so <see cref="TryGetDerivedGeometry"/> works in edit mode (where no body
    /// exists) and feeds the collision debug overlay. When a body IS live the same pass
    /// also attaches the shapes, so derived cache and physics can never disagree.
    /// </summary>
    [RequireComponent(typeof(DrawnShapeRenderer))]
    public sealed class TerrainBlob : EntityBody
    {
        // PhysicsChain requires at least 4 points (PhysicsChain docs); shorter composer
        // output is degenerate ground and is dropped rather than warned about per frame.
        private const int k_MinChainVertices = 4;

        // Godot/renderer parity: _ring_of drops the point duplicating the start when it is
        // within 1/8 of the bake interval. Same ratio DrawKit.MergeStroke uses, so sculpt,
        // render and collision all see identical rings.
        private const float k_RingCloseEpsilonRatio = 0.125f;

        public TerrainCollisionMode collisionMode = TerrainCollisionMode.Chain;

        /// <summary>Used by Solid mode polygons (density irrelevant on static bodies but
        /// friction/bounciness live in surfaceMaterial).</summary>
        public PhysicsShapeDefinition shapeDefinition = PhysicsShapeDefinition.defaultDefinition;

        /// <summary>Used by Chain mode loops (per-chain friction/bounciness).</summary>
        public PhysicsChainDefinition chainDefinition = PhysicsChainDefinition.defaultDefinition;

        /// <summary>Rings dropped by the last derivation for being degenerate: source rings
        /// with &lt; 3 points, and composer chain loops with &lt; 4 points.</summary>
        public int lastRejectedRingCount => m_LastRejectedRingCount;

        /// <summary>PhysicsComposer.rejectedGeometryCount after the last derivation —
        /// non-zero means the composer discarded collinear/too-small pieces, which shows up
        /// as thin gaps in the generated collision.</summary>
        public int lastRejectedGeometryCount => m_LastRejectedGeometryCount;

        private DrawnShapeRenderer m_Renderer;
        private readonly List<PhysicsShape> m_Shapes = new List<PhysicsShape>();
        private readonly List<PhysicsChain> m_Chains = new List<PhysicsChain>();
        private readonly List<Vector2[]> m_DerivedPolygons = new List<Vector2[]>();
        private readonly List<Vector2[]> m_DerivedChainLoops = new List<Vector2[]>();
        private readonly List<Vector2[]> m_HoleRings = new List<Vector2[]>();
        private bool m_HasDerived;
        private int m_DerivedHash;
        private int m_LastRejectedRingCount;
        private int m_LastRejectedGeometryCount;

        protected override void OnEnable()
        {
            // Subscribe before the body exists: Regenerate can fire from anywhere and the
            // handler is a no-op until there is something to rebuild.
            var renderer = ResolveRenderer();
            if (renderer != null)
                renderer.geometryChanged += OnRendererGeometryChanged;

            // Creates the body (static, see ConfigureBodyDefinition) and, through
            // OnBodyCreated, derives and attaches the collision.
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            if (m_Renderer != null)
                m_Renderer.geometryChanged -= OnRendererGeometryChanged;

            base.OnDisable();
        }

        // Definition/mode edits (and Transform scale edits) invalidate the derivation. The
        // body only exists in play mode, so the physics rebuild is guarded by isValid; in
        // edit mode this just drops the cache and the next TryGetDerivedGeometry re-derives.
        private void OnValidate()
        {
            m_HasDerived = false;
            if (body.isValid)
                RebuildCollision();
        }

        /// <summary>Terrain is static by definition in M1: a PhysicsChain has no mass, so a
        /// dynamic terrain body would be unsimulatable, and Solid mode terrain that falls
        /// over is never what the level author asked for. The serialized type is therefore
        /// overridden rather than trusted — dynamic drawn props are a separate component.</summary>
        protected override void ConfigureBodyDefinition(ref PhysicsBodyDefinition definition)
        {
            definition.type = PhysicsBody.BodyType.Static;
        }

        protected override void OnBodyCreated()
        {
            RebuildCollision();
        }

        protected override void OnBodyDestroying()
        {
            // Destroying the body destroys every shape and chain attached to it, so the
            // handles just go stale — drop them without touching physics.
            m_Shapes.Clear();
            m_Chains.Clear();
        }

        /// <summary>Destroy currently derived shapes/chains and re-derive from the renderer.
        /// Safe to call any time: the derivation always runs (it is pure geometry), the
        /// physics work only happens while the body is valid. Never touches the body itself.
        /// Main-thread only — this creates and destroys world objects, so it must not be
        /// called from a solver callback (WORM).</summary>
        public void RebuildCollision()
        {
            DestroyDerivedCollision();
            Compose(body.isValid);
        }

        /// <summary>Last derived geometry in BODY-LOCAL space for the collision debug
        /// overlay: Solid mode fills `polygons` (one convex ring each), Chain mode fills
        /// `chainLoops` (closed loops). Returns false when nothing has been derived.
        /// Local space here means the Transform's local space with lossyScale already baked
        /// into the vertices, so an overlay maps them to world space with the Transform's
        /// position and rotation ONLY — applying scale again double-scales the outline.
        /// Re-derives on demand when the source drawing changed, which is what makes this
        /// usable from an editor overlay in edit mode. The returned lists are the live
        /// caches — read them, never mutate them.</summary>
        public bool TryGetDerivedGeometry(out List<Vector2[]> polygons, out List<Vector2[]> chainLoops)
        {
            EnsureDerived();
            polygons = m_DerivedPolygons;
            chainLoops = m_DerivedChainLoops;
            return m_DerivedPolygons.Count > 0 || m_DerivedChainLoops.Count > 0;
        }

        // --- derivation -------------------------------------------------------------

        private void OnRendererGeometryChanged()
        {
            // The live regen-on-sculpt path: every editor gesture funnels through
            // DrawnShapeRenderer.Regenerate, so collision follows the drawing in play mode.
            RebuildCollision();
        }

        /// <summary>Re-derive when the cache is stale. In play mode geometryChanged already
        /// keeps it fresh; in edit mode nothing subscribes (no [ExecuteAlways]), so the
        /// source is fingerprinted instead. The fingerprint costs one curve-signature walk
        /// per call, which is authoring-scale work, not a hot path.</summary>
        private void EnsureDerived()
        {
            var hash = ComputeSourceHash();
            if (m_HasDerived && hash == m_DerivedHash)
                return;

            if (body.isValid)
                RebuildCollision();
            else
                Compose(false);
        }

        /// <summary>Run the composer pipeline: outer ring OR, hole rings NOT, then either a
        /// convex decomposition (Solid) or chain outlines (Chain). Caches the result in
        /// local space and, when <paramref name="attach"/> is set, attaches it to the body.</summary>
        private void Compose(bool attach)
        {
            m_DerivedPolygons.Clear();
            m_DerivedChainLoops.Clear();
            m_HoleRings.Clear();
            m_LastRejectedRingCount = 0;
            m_LastRejectedGeometryCount = 0;

            // The cache is up to date for this source even if it derives nothing.
            m_DerivedHash = ComputeSourceHash();
            m_HasDerived = true;

            var renderer = ResolveRenderer();
            if (renderer == null || !renderer.hasShape)
                return;

            var asset = renderer.asset;
            if (asset == null)
                return;

            // Physics has no scale, so the Transform's lossyScale is baked into the
            // vertices. Non-uniform scale combined with rotation is unsupported (it would
            // need a shear physics cannot represent), and mirrored (negative) scale flips
            // ring winding, which flips a chain's one-sided collision — both are authoring
            // errors rather than cases handled here.
            var scale = (Vector2)transform.lossyScale;

            var outerRing = ToScaledRing(renderer.GetBakedRing(), scale);
            if (outerRing == null)
            {
                m_LastRejectedRingCount++;
                return;
            }

            CollectHoleRings(asset, scale);

            var composer = PhysicsComposer.Create(Allocator.Temp);
            try
            {
                // First layer is the base and is always treated as OR.
                composer.AddLayer(outerRing, PhysicsTransform.identity);

                // Holes are true boolean subtractions here. The renderer drops hole rings
                // that are not fully inside the outline (its keyholed fill cannot represent
                // them); the composer can, so collision carves a partially-overlapping hole
                // that the fill would have ignored. MergeStroke's Hole op only produces
                // fully-interior rings, so in practice the two agree.
                for (var i = 0; i < m_HoleRings.Count; i++)
                    composer.AddLayer(m_HoleRings[i], PhysicsTransform.identity, PhysicsComposer.Operation.NOT);

                if (collisionMode == TerrainCollisionMode.Solid)
                    ComposeSolid(composer, attach);
                else
                    ComposeChains(composer, attach);

                // Only meaningful after a Create*Geometry call, so read it before Destroy.
                m_LastRejectedGeometryCount = composer.rejectedGeometryCount;
            }
            finally
            {
                composer.Destroy();
            }
        }

        /// <summary>Solid mode: convex decomposition into PolygonGeometry, attached as one
        /// shape batch. vertexScale is Vector2.one because scaling is already baked into the
        /// input vertices — passing one still avoids the "too small" rejection path that the
        /// no-scale overload uses.</summary>
        private void ComposeSolid(PhysicsComposer composer, bool attach)
        {
            var polygons = composer.CreatePolygonGeometry(Vector2.one, Allocator.Temp);
            try
            {
                if (!polygons.IsCreated || polygons.Length == 0)
                    return;

                for (var i = 0; i < polygons.Length; i++)
                {
                    // Local copy: AsReadOnlySpan spans the geometry's inline vertex array,
                    // so it must be taken from a local that outlives the copy loop.
                    var polygon = polygons[i];
                    var vertices = polygon.AsReadOnlySpan();
                    var ring = new Vector2[vertices.Length];
                    for (var v = 0; v < vertices.Length; v++)
                        ring[v] = vertices[v];
                    m_DerivedPolygons.Add(ring);
                }

                if (!attach)
                    return;

                var shapes = body.CreateShapeBatch(polygons, shapeDefinition, Allocator.Temp);
                try
                {
                    if (!shapes.IsCreated)
                        return;

                    for (var i = 0; i < shapes.Length; i++)
                        m_Shapes.Add(shapes[i]);
                }
                finally
                {
                    if (shapes.IsCreated)
                        shapes.Dispose();
                }
            }
            finally
            {
                if (polygons.IsCreated)
                    polygons.Dispose();
            }
        }

        /// <summary>Chain mode: composer outlines attached as one PhysicsChain per loop.
        /// NOTE (verified against the shipping 6000.5.3f1 PhysicsCore2D assembly): the
        /// vertex buffer is an OUT parameter the composer allocates — there is no
        /// caller-supplied capacity to size, and therefore no grow-and-retry. It backs the
        /// vertex spans of every returned ChainGeometry, so it must outlive both the
        /// CreateChain calls and the read-back below, and is disposed with the geometry
        /// array afterwards.</summary>
        private void ComposeChains(PhysicsComposer composer, bool attach)
        {
            var chains = composer.CreateChainGeometry(out var chainVertices, Vector2.one, Allocator.Temp);
            try
            {
                if (!chains.IsCreated || chains.Length == 0)
                    return;

                // Composer outlines are closed regions, so ghost vertices are implicit and
                // looping is forced regardless of what the serialized definition says. A
                // copy keeps the authored definition untouched.
                var definition = chainDefinition;
                definition.isLoop = true;

                for (var i = 0; i < chains.Length; i++)
                {
                    var geometry = chains[i];
                    var vertices = geometry.vertices;
                    if (vertices.Length < k_MinChainVertices)
                    {
                        m_LastRejectedRingCount++;
                        continue;
                    }

                    var loop = new Vector2[vertices.Length];
                    for (var v = 0; v < vertices.Length; v++)
                        loop[v] = vertices[v];
                    m_DerivedChainLoops.Add(loop);

                    if (!attach)
                        continue;

                    var chain = body.CreateChain(geometry, definition);
                    if (chain.isValid)
                        m_Chains.Add(chain);
                }
            }
            finally
            {
                if (chains.IsCreated)
                    chains.Dispose();
                if (chainVertices.IsCreated)
                    chainVertices.Dispose();
            }
        }

        /// <summary>Destroy only what this component created. Chains own their segment
        /// shapes and can only be destroyed through the chain handle, which is why chains
        /// and shapes are tracked separately instead of walking body.GetShapes().</summary>
        private void DestroyDerivedCollision()
        {
            for (var i = 0; i < m_Chains.Count; i++)
            {
                var chain = m_Chains[i];
                if (chain.isValid)
                    chain.Destroy();
            }
            m_Chains.Clear();

            for (var i = 0; i < m_Shapes.Count; i++)
            {
                var shape = m_Shapes[i];
                if (shape.isValid)
                    shape.Destroy();
            }
            m_Shapes.Clear();
        }

        // --- source rings -----------------------------------------------------------

        /// <summary>Bake the asset's hole curves exactly the way the renderer bakes them
        /// (same interval, same closing-duplicate epsilon) so collision and fill see the
        /// same rings. Wobble is deliberately not applied: it is render-only, and the
        /// resulting visual/collision drift is visible in the debug overlay by design.</summary>
        private void CollectHoleRings(DrawnShapeAsset asset, Vector2 scale)
        {
            var holeCurves = asset.holeCurves;
            if (holeCurves == null)
                return;

            var interval = BakeInterval(asset);
            for (var i = 0; i < holeCurves.Count; i++)
            {
                var holeCurve = holeCurves[i];
                if (holeCurve == null || holeCurve.pointCount < 3)
                {
                    m_LastRejectedRingCount++;
                    continue;
                }

                var ring = ToScaledRing(BuildBakedRing(holeCurve, interval), scale);
                if (ring == null)
                {
                    m_LastRejectedRingCount++;
                    continue;
                }

                m_HoleRings.Add(ring);
            }
        }

        private static float BakeInterval(DrawnShapeAsset asset)
        {
            return Mathf.Max(asset.bakeInterval, 1e-4f);
        }

        /// <summary>Port of DrawnShapeRenderer's private _ring_of equivalent: bake, then
        /// drop the point that duplicates the start on a manually closed loop.</summary>
        private static List<Vector2> BuildBakedRing(DrawnCurve curve, float interval)
        {
            var points = curve != null ? curve.GetBakedPoints(interval) : null;
            if (points == null)
                return null;

            var epsilon = Mathf.Max(interval * k_RingCloseEpsilonRatio, 1e-6f);
            if (points.Count > 1
                && (points[0] - points[points.Count - 1]).sqrMagnitude < epsilon * epsilon)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        /// <summary>Local-space ring scaled by the Transform's lossyScale, or null when the
        /// ring is too short to be a polygon at all.</summary>
        private static Vector2[] ToScaledRing(List<Vector2> ring, Vector2 scale)
        {
            if (ring == null || ring.Count < 3)
                return null;

            var scaled = new Vector2[ring.Count];
            for (var i = 0; i < ring.Count; i++)
                scaled[i] = new Vector2(ring[i].x * scale.x, ring[i].y * scale.y);
            return scaled;
        }

        // --- cache fingerprint ------------------------------------------------------

        /// <summary>Fingerprint of everything the derivation reads: the curves (control
        /// points via the Godot curve_sig port), bake interval, scale and collision mode.
        /// Change detection, not identity — collisions are harmless because the fingerprint
        /// only ever triggers a re-derive.</summary>
        private int ComputeSourceHash()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 ^ (int)collisionMode;

                var scale = (Vector2)transform.lossyScale;
                hash = hash * 31 ^ scale.x.GetHashCode();
                hash = hash * 31 ^ scale.y.GetHashCode();

                var renderer = ResolveRenderer();
                var asset = renderer != null ? renderer.asset : null;
                if (asset == null)
                    return hash;

                // GetInstanceID is [Obsolete] in 6000.5 — GetEntityId is its named
                // replacement (same identity role, per the deprecation message).
                hash = hash * 31 ^ asset.GetEntityId().GetHashCode();
                hash = hash * 31 ^ asset.bakeInterval.GetHashCode();
                hash = HashCurve(hash, asset.curve);

                var holeCurves = asset.holeCurves;
                if (holeCurves == null)
                    return hash;

                hash = hash * 31 ^ holeCurves.Count;
                for (var i = 0; i < holeCurves.Count; i++)
                    hash = HashCurve(hash, holeCurves[i]);
                return hash;
            }
        }

        private static int HashCurve(int hash, DrawnCurve curve)
        {
            unchecked
            {
                if (curve == null)
                    return hash * 31;

                var signature = curve.Signature();
                hash = hash * 31 ^ signature.Length;
                for (var i = 0; i < signature.Length; i++)
                    hash = hash * 31 ^ signature[i].GetHashCode();
                return hash;
            }
        }

        private DrawnShapeRenderer ResolveRenderer()
        {
            if (m_Renderer == null)
                TryGetComponent(out m_Renderer);
            return m_Renderer;
        }
    }
}
