using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A drawn prop that breaks (brief §5 Destructibles): a dynamic EntityBody whose
    /// shapes come from convex decomposition of the drawing's raw baked ring, and which
    /// can Fragment (point pattern) or Slice (cut ray) via PhysicsDestructor. Fragments
    /// spawn as new drawn shapes: each broken polygon becomes a FragmentBody + a
    /// DrawnShapeRenderer whose curve is re-fit from the polygon with DrawKit.FitCurve —
    /// the visual stays hand-drawn all the way down. Impact auto-fragment triggers from
    /// contact events when the approach speed exceeds the threshold.
    ///
    /// Collision derivation is the M1 TerrainBlob Solid path (composer: outer ring OR, hole
    /// rings NOT, CreatePolygonGeometry) against the RAW baked ring, so collision truth stays
    /// pre-wobble; sculpting in play mode rebuilds it live through renderer.geometryChanged.
    ///
    /// IMPACT TRIGGER (deviation from the m5 plan, forced by the verified API — see the report):
    /// the plan named PhysicsCallbacks.IContactCallback, but PhysicsEvents.ContactBeginEvent
    /// carries only contactId/shapeA/shapeB — no velocity and no impulse (events-api). The one
    /// member in the whole surface that exposes impact severity is
    /// PhysicsEvents.ContactHitEvent.approachSpeed ("the speed the shapes are approaching,
    /// typically in meters per second"), measured at the START of the step and therefore the
    /// only value that survives the solver having already cancelled the impact. Hit events are
    /// polled, not dispatched — there is no IContactHitCallback — so this component enables
    /// hitEvents on its shapes and reads PhysicsWorld.contactHitEvents from
    /// PhysicsEvents.PostSimulate. That hook keeps every property the plan relied on: it is
    /// post-step and main-thread, so destroying this body and creating fragment bodies inline
    /// is legal (WORM only forbids writes from in-step callbacks such as IPreSolveCallback),
    /// and every handle is re-validated before use because a shape in an event may already
    /// have been destroyed.
    ///
    /// <see cref="impactSpeedThreshold"/> therefore maps 1:1 onto approachSpeed in world
    /// units/second. The world gates event GENERATION with its own
    /// PhysicsWorld.contactHitEventThreshold, so a threshold below that would silently never
    /// fire; the world value is lowered (never raised) to match when a destructible needs a
    /// finer trigger. That is a deliberate global write: its only effect is that more hit
    /// events are produced, and every consumer applies its own threshold anyway.
    /// </summary>
    [RequireComponent(typeof(DrawnShapeRenderer))]
    public sealed class DestructibleShape : EntityBody
    {
        // Godot/renderer parity (TerrainBlob k_RingCloseEpsilonRatio): _ring_of drops the point
        // duplicating the start when it is within 1/8 of the bake interval.
        private const float k_RingCloseEpsilonRatio = 0.125f;

        // PhysicsDestructor.Fragment requires more than one fragment point.
        private const int k_MinFragmentPoints = 2;

        // Fragment points are scattered between these fractions of shardScatterRadius so the
        // shard pattern never looks like a stamped rosette.
        private const float k_ScatterJitterMin = 0.45f;
        private const float k_ScatterJitterMax = 1f;

        // A scatter radius below this collapses every fragment point onto the impact point,
        // which produces no fragment regions at all (Godot 0.32 px).
        private const float k_MinScatterRadius = 0.01f;

        // DrawKit.FitCurve tolerance for a fragment outline: 0.06 world units ~= 2 Godot px
        // (m5 plan), scaled down for small shards so RDP cannot simplify them below a triangle.
        private const float k_FragmentFitTolerance = 0.06f;
        private const float k_MinFragmentFitTolerance = 0.01f;
        private const float k_FitToleranceAreaFactor = 0.25f;
        private const int k_FragmentFitSmoothPasses = 1;

        // Below this the outward impulse direction is meaningless (the piece centroid sits on
        // the impact point) and a random direction is used instead.
        private const float k_MinImpulseDirectionSqr = 1e-8f;

        // PhysicsDestructor.Slice requires a non-zero translation.
        private const float k_MinSliceDirectionSqr = 1e-8f;

        // Guard against a Transform parented under a collapsed (zero) scale.
        private const float k_MinScaleComponent = 1e-4f;

        /// <summary>Auto-fragment when a contact's relative approach speed exceeds this
        /// (world units/sec); 0 disables impact triggering.</summary>
        public float impactSpeedThreshold = 4f;

        public int shardCount = 6;
        /// <summary>Fragment-point scatter radius around the impact point.</summary>
        public float shardScatterRadius = 0.4f;
        /// <summary>Outward impulse applied to each fragment.</summary>
        public float shardImpulse = 1.5f;
        /// <summary>Seconds before spawned debris despawns; 0 = keep forever.</summary>
        public float debrisLifetime = 6f;
        /// <summary>Fragments smaller than this area are discarded (debris cleanup).</summary>
        public float minFragmentArea = 0.005f;

        public PhysicsShapeDefinition shapeDefinition = PhysicsShapeDefinition.defaultDefinition;

        /// <summary>True once this prop has broken. The GameObject is destroyed at the end of
        /// the breaking frame, so gameplay code (Health.died and friends) can use this to avoid
        /// breaking the same prop twice within one frame.</summary>
        public bool isBroken => m_Broken;

        private DrawnShapeRenderer m_Renderer;
        private readonly List<PhysicsShape> m_Shapes = new List<PhysicsShape>();
        private readonly List<Vector2[]> m_HoleRings = new List<Vector2[]>();
        private readonly List<PolygonGeometry> m_BodyPolygons = new List<PolygonGeometry>();
        // Parallel: the convex polygons of the piece being spawned and their PolygonGeometry
        // centroids, both in the destructor's result frame. Always filled together.
        private readonly List<Vector2[]> m_PieceGeometry = new List<Vector2[]>();
        private readonly List<Vector2> m_PieceCentroids = new List<Vector2>();
        private readonly List<Vector2> m_PieceVertices = new List<Vector2>();
        private readonly List<int> m_PieceSizes = new List<int>();
        private bool m_Broken;
        private bool m_ImpactHooked;

        protected override void OnEnable()
        {
            // Subscribe before the body exists: the handler is a no-op until there is
            // something to rebuild, and every sculpt gesture funnels through Regenerate.
            var renderer = ResolveRenderer();
            if (renderer != null)
                renderer.geometryChanged += OnRendererGeometryChanged;

            base.OnEnable();
        }

        protected override void OnDisable()
        {
            if (m_Renderer != null)
                m_Renderer.geometryChanged -= OnRendererGeometryChanged;

            UnhookImpactDetection();
            base.OnDisable();
        }

        // Definition/threshold edits invalidate the derived collision (hitEvents lives on the
        // shape definition). The body only exists in play mode, so this is a no-op while
        // authoring.
        private void OnValidate()
        {
            if (!body.isValid)
                return;

            RebuildCollision();
            HookImpactDetection();
        }

        /// <summary>A destructible prop is dynamic by definition — the serialized type is
        /// overridden rather than trusted, the same way TerrainBlob pins Static. Density (and
        /// therefore mass) comes from <see cref="shapeDefinition"/>.</summary>
        protected override void ConfigureBodyDefinition(ref PhysicsBodyDefinition definition)
        {
            definition.type = PhysicsBody.BodyType.Dynamic;
        }

        protected override void OnBodyCreated()
        {
            RebuildCollision();
            HookImpactDetection();
        }

        protected override void OnBodyDestroying()
        {
            // Destroying the body destroys its shapes; the handles just go stale.
            m_Shapes.Clear();
        }

        /// <summary>Destroy the derived shapes and re-derive them from the renderer. Safe to
        /// call any time; the physics work only happens while the body is valid. Main-thread
        /// only — this creates and destroys world objects (WORM).</summary>
        public void RebuildCollision()
        {
            DestroyDerivedCollision();
            Compose();
        }

        // --- derivation -------------------------------------------------------------

        private void OnRendererGeometryChanged()
        {
            // Live regen-on-sculpt, exactly like TerrainBlob: the drawing stays the source of
            // truth even while the prop is simulating.
            if (!m_Broken)
                RebuildCollision();
        }

        /// <summary>Composer pipeline (TerrainBlob's Solid path, duplicated rather than shared
        /// because TerrainBlob is not mine to edit this milestone): outer ring OR, hole rings
        /// NOT, convex decomposition, attached as one shape batch.</summary>
        private void Compose()
        {
            m_HoleRings.Clear();

            if (!body.isValid)
                return;

            var renderer = ResolveRenderer();
            if (renderer == null || !renderer.hasShape)
                return;

            var asset = renderer.asset;
            if (asset == null)
                return;

            // Physics has no scale, so lossyScale is baked into the vertices. Non-uniform
            // scale combined with rotation is unsupported (it needs a shear physics cannot
            // represent) and mirrored scale flips the winding — both are authoring errors.
            var scale = (Vector2)transform.lossyScale;

            // The RAW ring, not BuildRenderRing: wobble and form morphs are render-only, so
            // collision keeps agreeing with the sculpted geometry (M1 collision-truth rule).
            var outerRing = ToScaledRing(renderer.GetBakedRing(), scale);
            if (outerRing == null)
                return;

            CollectHoleRings(asset, scale);

            var composer = PhysicsComposer.Create(Allocator.Temp);
            try
            {
                // First layer is the base and is always treated as OR.
                composer.AddLayer(outerRing, PhysicsTransform.identity);

                for (var i = 0; i < m_HoleRings.Count; i++)
                    composer.AddLayer(m_HoleRings[i], PhysicsTransform.identity, PhysicsComposer.Operation.NOT);

                AttachSolid(composer);
            }
            finally
            {
                composer.Destroy();
            }
        }

        /// <summary>vertexScale is Vector2.one because scaling is already baked into the input
        /// vertices — passing one still avoids the "too small" rejection path of the no-scale
        /// overload (TerrainBlob.ComposeSolid).</summary>
        private void AttachSolid(PhysicsComposer composer)
        {
            var polygons = composer.CreatePolygonGeometry(Vector2.one, Allocator.Temp);
            try
            {
                if (!polygons.IsCreated || polygons.Length == 0)
                    return;

                // Work on a copy so the authored definition is never rewritten: hit events are
                // an implementation detail of the impact trigger, not a user setting.
                var definition = shapeDefinition;
                definition.hitEvents = impactSpeedThreshold > 0f;

                var shapes = body.CreateShapeBatch(polygons, definition, Allocator.Temp);
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

        private void DestroyDerivedCollision()
        {
            for (var i = 0; i < m_Shapes.Count; i++)
            {
                var shape = m_Shapes[i];
                if (shape.isValid)
                    shape.Destroy();
            }
            m_Shapes.Clear();
        }

        // --- impact trigger ---------------------------------------------------------

        private void HookImpactDetection()
        {
            if (impactSpeedThreshold <= 0f || !body.isValid)
            {
                UnhookImpactDetection();
                return;
            }

            // Local copy: C# forbids property setters on a by-value struct returned from a
            // property, and PhysicsWorld is a handle struct so the write reaches the same world.
            var world = PhysicsWorld.defaultWorld;
            if (world.isValid && world.contactHitEventThreshold > impactSpeedThreshold)
                world.contactHitEventThreshold = impactSpeedThreshold;

            if (m_ImpactHooked)
                return;

            PhysicsEvents.PostSimulate += OnPostSimulate;
            m_ImpactHooked = true;
        }

        private void UnhookImpactDetection()
        {
            if (!m_ImpactHooked)
                return;

            PhysicsEvents.PostSimulate -= OnPostSimulate;
            m_ImpactHooked = false;
        }

        /// <summary>Post-step, main-thread: read the hit events the simulation just produced,
        /// keep the strongest one that involves this body, then break. The event buffer is
        /// invalidated by any world write, so everything needed (speed and point) is extracted
        /// into locals BEFORE a single body is destroyed.</summary>
        private void OnPostSimulate(PhysicsWorld world, float deltaTime)
        {
            if (m_Broken || impactSpeedThreshold <= 0f || !body.isValid)
                return;

            // EntityBody only ever builds bodies in the default world; another world's events
            // can never reference this body.
            if (!world.isValid || !world.isDefaultWorld)
                return;

            var hitEvents = world.contactHitEvents;
            var strongestSpeed = 0f;
            var impactPoint = Vector2.zero;
            var hit = false;

            for (var i = 0; i < hitEvents.Length; i++)
            {
                var hitEvent = hitEvents[i];
                if (hitEvent.approachSpeed < impactSpeedThreshold || hitEvent.approachSpeed <= strongestSpeed)
                    continue;
                if (!InvolvesThisBody(hitEvent.shapeA, hitEvent.shapeB))
                    continue;

                strongestSpeed = hitEvent.approachSpeed;
                impactPoint = hitEvent.point;
                hit = true;
            }

            if (hit)
                Fragment(impactPoint);
        }

        /// <summary>Shapes reported by an event may already have been destroyed (events-api),
        /// so validity is checked before the handle is dereferenced.</summary>
        private bool InvolvesThisBody(PhysicsShape shapeA, PhysicsShape shapeB)
        {
            var self = body;
            if (shapeA.isValid && shapeA.body == self)
                return true;
            return shapeB.isValid && shapeB.body == self;
        }

        // --- destruction ------------------------------------------------------------

        /// <summary>Break around a world-space impact point into shardCount pieces.
        /// Destroys this GameObject and spawns FragmentBody children in its parent.</summary>
        public void Fragment(Vector2 worldImpactPoint)
        {
            if (m_Broken || !body.isValid)
                return;

            // The polygon list backs a ReadOnlySpan inside the FragmentGeometry, so it must
            // stay alive (and unmodified) until the Fragment call returns.
            if (!CollectBodyPolygons())
                return;

            var polygons = m_BodyPolygons.ToArray();
            var target = new PhysicsDestructor.FragmentGeometry(body.transform,
                new ReadOnlySpan<PolygonGeometry>(polygons));
            var fragmentPoints = BuildFragmentPoints(worldImpactPoint);

            using (var result = PhysicsDestructor.Fragment(target, fragmentPoints, Allocator.Temp))
            {
                // Fragmenting is all-or-nothing: a single fragment point overlapping the target
                // puts EVERY resulting polygon in brokenGeometry, and a pattern that misses the
                // prop entirely puts everything in unbrokenGeometry (destructor-api). An
                // off-target hit therefore leaves the prop standing instead of vaporising it,
                // which is also why unbrokenGeometry is never respawned here.
                if (!result.brokenGeometry.IsCreated || result.brokenGeometry.Length == 0)
                    return;

                var context = BeginBreak(result.transform, worldImpactPoint, true);

                var index = 0;
                foreach (var polygon in result.brokenGeometry)
                {
                    if (!polygon.isValid)
                        continue;

                    var ring = ToRing(polygon);
                    if (ring == null)
                        continue;

                    // One shard = one convex polygon: the destructor only splits a single shard
                    // across several polygons when it exceeds MaxPolygonVertices, and treating
                    // those halves as separate debris is the accepted M5 simplification.
                    m_PieceGeometry.Clear();
                    m_PieceCentroids.Clear();
                    m_PieceGeometry.Add(ring);
                    m_PieceCentroids.Add(polygon.centroid);

                    if (SpawnPiece(context, ring, index))
                        index++;
                }
            }

            EndBreak();
        }

        /// <summary>Cut along a world-space ray into two pieces (pre-authored cut lines /
        /// the Draw tool's stroke gesture doubles as the slice authoring gesture later).</summary>
        public void Slice(Vector2 worldOrigin, Vector2 worldDirection)
        {
            if (m_Broken || !body.isValid)
                return;
            if (worldDirection.sqrMagnitude < k_MinSliceDirectionSqr)
                return;
            if (!CollectBodyPolygons())
                return;

            var polygons = m_BodyPolygons.ToArray();
            var target = new PhysicsDestructor.FragmentGeometry(body.transform,
                new ReadOnlySpan<PolygonGeometry>(polygons));

            // The outline is captured before the body dies: each side's visual ring is the
            // source ring clipped by the same half-plane the destructor cut along.
            var sourceRing = CollectLocalRing();

            using (var result = PhysicsDestructor.Slice(target, worldOrigin, worldDirection, Allocator.Temp))
            {
                var leftCount = result.leftGeometry.IsCreated ? result.leftGeometry.Length : 0;
                var rightCount = result.rightGeometry.IsCreated ? result.rightGeometry.Length : 0;

                // A ray that misses (or grazes) puts everything on one side: that is not a cut,
                // and replacing the prop with a copy of itself would only lose its velocity.
                if (leftCount == 0 || rightCount == 0)
                    return;

                var context = BeginBreak(result.transform, worldOrigin, false);

                // Slice rays are world-space; the rings are in the result frame.
                var localOrigin = result.transform.InverseTransformPoint(worldOrigin);
                var localDirection = result.transform.rotation.InverseRotateVector(worldDirection);

                SpawnSide(context, result.leftGeometry, sourceRing, localOrigin, localDirection, true, 0);
                SpawnSide(context, result.rightGeometry, sourceRing, localOrigin, localDirection, false, 1);
            }

            EndBreak();
        }

        /// <summary>One side of a cut is returned as a CONVEX DECOMPOSITION, not as a single
        /// polygon, so the whole side becomes ONE debris body carrying every polygon as a
        /// shape — a cut plank must fall as two planks, not as a handful of blocks.</summary>
        private void SpawnSide(in PieceContext context, NativeArray<PolygonGeometry> side,
            List<Vector2> sourceRing, Vector2 localOrigin, Vector2 localDirection, bool keepLeft,
            int index)
        {
            m_PieceGeometry.Clear();
            m_PieceCentroids.Clear();
            foreach (var polygon in side)
            {
                if (!polygon.isValid)
                    continue;
                var ring = ToRing(polygon);
                if (ring == null)
                    continue;
                m_PieceGeometry.Add(ring);
                m_PieceCentroids.Add(polygon.centroid);
            }

            if (m_PieceGeometry.Count == 0)
                return;

            // Visual outline: the source ring clipped to this side. Exact for a convex prop;
            // a concave prop keeps a hairline bridge where the cut re-enters the shape, which
            // is the same "visual and collision drift slightly" trade the renderer already
            // makes with wobble.
            var outline = ClipRingToHalfPlane(sourceRing, localOrigin, localDirection, keepLeft);
            SpawnPiece(context, outline, index);
        }

        /// <summary>Everything a spawned piece needs that must be read BEFORE the source body
        /// is destroyed.</summary>
        private readonly struct PieceContext
        {
            public readonly PhysicsTransform frame;
            public readonly Transform parent;
            public readonly DrawnShapeAsset style;
            public readonly Vector2 linearVelocity;
            public readonly float angularVelocity;
            public readonly Vector2 impactPoint;
            public readonly bool applyImpulse;

            public PieceContext(PhysicsTransform frame, Transform parent, DrawnShapeAsset style,
                Vector2 linearVelocity, float angularVelocity, Vector2 impactPoint, bool applyImpulse)
            {
                this.frame = frame;
                this.parent = parent;
                this.style = style;
                this.linearVelocity = linearVelocity;
                this.angularVelocity = angularVelocity;
                this.impactPoint = impactPoint;
                this.applyImpulse = applyImpulse;
            }
        }

        /// <summary>Capture the spawn context, then remove the prop from the simulation. The
        /// body is destroyed here rather than left to OnDisable: Destroy(gameObject) is
        /// deferred to the end of the frame, and a frame can contain several fixed steps, so
        /// waiting would let the original body and its own debris overlap for a step and blow
        /// them apart. The destructor result is native memory and does not depend on the body.</summary>
        private PieceContext BeginBreak(PhysicsTransform frame, Vector2 impactPoint, bool applyImpulse)
        {
            var renderer = ResolveRenderer();
            var context = new PieceContext(
                frame,
                transform.parent,
                renderer != null ? renderer.asset : null,
                body.linearVelocity,
                body.angularVelocity,
                impactPoint,
                applyImpulse);

            m_Broken = true;
            UnhookImpactDetection();
            DestroyBody();
            return context;
        }

        /// <summary>The prop is gone once BeginBreak ran, whether or not any piece survived
        /// minFragmentArea — a break that culls everything reads as "pulverised", which is what
        /// a large cull area asks for.</summary>
        private void EndBreak()
        {
            m_BodyPolygons.Clear();
            m_PieceGeometry.Clear();
            m_PieceCentroids.Clear();

            // Deferred: the renderer tears its generated mesh down in OnDestroy, and the
            // debris spawned above are siblings, not children, so nothing else goes with it.
            Destroy(gameObject);
        }

        /// <summary>Spawn one debris object from the convex pieces currently in
        /// <see cref="m_PieceGeometry"/> (expressed in the destructor's result frame). Returns
        /// false when the piece was culled.</summary>
        private bool SpawnPiece(in PieceContext context, IReadOnlyList<Vector2> outline, int index)
        {
            var pieces = m_PieceGeometry;
            if (pieces.Count == 0)
                return false;

            var area = 0f;
            for (var i = 0; i < pieces.Count; i++)
                area += Mathf.Abs(DrawKit.SignedArea(pieces[i]));

            if (area < minFragmentArea)
                return false;

            // Piece-local space is centred on the area-weighted mean of the polygon centroids,
            // so collision and visual share one origin and the piece spins about its middle.
            var origin = AreaWeightedCentroid(area);

            m_PieceVertices.Clear();
            m_PieceSizes.Clear();
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece.Length < 3)
                    continue;
                for (var v = 0; v < piece.Length; v++)
                    m_PieceVertices.Add(piece[v] - origin);
                m_PieceSizes.Add(piece.Length);
            }

            if (m_PieceSizes.Count == 0)
                return false;

            var localOutline = ToLocalOutline(outline, origin);
            var worldPosition = context.frame.TransformPoint(origin);

            var fragmentObject = new GameObject(gameObject.name + " Fragment");

            // Configure while inactive: AddComponent on an active GameObject runs OnEnable
            // immediately, and EntityBody.OnEnable creates the body — the outline and the
            // definitions have to be in place first.
            fragmentObject.SetActive(false);
            fragmentObject.layer = gameObject.layer;

            var fragmentTransform = fragmentObject.transform;
            fragmentTransform.SetParent(context.parent, false);
            fragmentTransform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
            fragmentTransform.rotation = Quaternion.Euler(0f, 0f, context.frame.rotation.degrees);

            // The piece vertices are already in world scale (the source baked lossyScale into
            // its collision), so the debris must end up at world scale 1 whatever the parent does.
            fragmentTransform.localScale = InverseScale(context.parent);

            var asset = BuildFragmentAsset(context.style, localOutline, index);
            if (asset != null)
            {
                var renderer = fragmentObject.AddComponent<DrawnShapeRenderer>();
                renderer.asset = asset;
            }

            var fragment = fragmentObject.AddComponent<FragmentBody>();
            fragment.bodyDefinition = BuildFragmentBodyDefinition(context);
            fragment.shapeDefinition = shapeDefinition;
            fragment.runtimeAsset = asset;
            fragment.ownsRuntimeAsset = asset != null;
            fragment.SetPieces(m_PieceVertices, m_PieceSizes);

            fragmentObject.SetActive(true);

            ApplyOutwardImpulse(context, fragment, worldPosition);

            if (debrisLifetime > 0f)
                Destroy(fragmentObject, debrisLifetime);

            return true;
        }

        private void ApplyOutwardImpulse(in PieceContext context, FragmentBody fragment,
            Vector2 worldPosition)
        {
            if (!context.applyImpulse || shardImpulse <= 0f)
                return;

            var fragmentBody = fragment.body;
            if (!fragmentBody.isValid)
                return;

            var direction = worldPosition - context.impactPoint;
            direction = direction.sqrMagnitude > k_MinImpulseDirectionSqr
                ? direction.normalized
                : UnityEngine.Random.insideUnitCircle.normalized;

            fragmentBody.ApplyLinearImpulseToCenter(direction * shardImpulse, true);
        }

        /// <summary>Debris inherits the prop's body settings (density scale, gravity scale,
        /// damping) and its velocity at the moment of the break, so a thrown crate's shards
        /// keep flying instead of dropping dead in mid-air. EntityBody overwrites position and
        /// rotation from the Transform, and FragmentBody pins the type to Dynamic.</summary>
        private PhysicsBodyDefinition BuildFragmentBodyDefinition(in PieceContext context)
        {
            var definition = bodyDefinition;
            definition.type = PhysicsBody.BodyType.Dynamic;
            definition.linearVelocity = context.linearVelocity;
            definition.angularVelocity = context.angularVelocity;
            definition.awake = true;
            return definition;
        }

        /// <summary>Style-cloned in-memory asset for a fragment: the source's look with its
        /// content dropped (holes, paint mask, form morphs and bone bindings all belong to the
        /// whole prop, not to a shard), and an outline re-fit from the fragment geometry so the
        /// debris is a genuine drawn shape rather than a polygon. Never touches AssetDatabase —
        /// this is a runtime instance owned by the FragmentBody.</summary>
        private DrawnShapeAsset BuildFragmentAsset(DrawnShapeAsset source, List<Vector2> outline,
            int index)
        {
            if (outline == null || outline.Count < 3)
                return null;

            // M0 winding contract: outer rings have positive signed area.
            if (DrawKit.SignedArea(outline) < 0f)
                outline.Reverse();

            var curve = DrawKit.FitCurve(outline, true, FitTolerance(outline), k_FragmentFitSmoothPasses);
            if (curve == null || curve.pointCount < 3)
                curve = CrispCurve(outline);
            if (curve.pointCount < 3)
                return null;

            var asset = ScriptableObject.CreateInstance<DrawnShapeAsset>();
            asset.name = "DrawnShape Fragment (generated)";
            asset.hideFlags = HideFlags.DontSave;
            asset.curve = curve;

            if (source != null)
            {
                asset.fillColor = source.fillColor;
                asset.fillTexture = source.fillTexture;
                asset.textureScale = source.textureScale;
                asset.outlineColor = source.outlineColor;
                asset.outlineWidth = source.outlineWidth;
                asset.rimColor = source.rimColor;
                asset.rimWidth = source.rimWidth;
                asset.bakeInterval = source.bakeInterval;
                asset.edgeNoiseAmp = source.edgeNoiseAmp;
                asset.edgeNoiseWavelength = source.edgeNoiseWavelength;
                // Offset per shard the way the renderer offsets hole seeds, so neighbouring
                // shards do not wobble in lockstep.
                asset.edgeNoiseSeed = source.edgeNoiseSeed + index + 1;
                asset.fillShade = source.fillShade;
                asset.shadowColor = source.shadowColor;
                asset.shadowOffset = source.shadowOffset;
            }

            return asset;
        }

        /// <summary>FitCurve resamples at its tolerance and then simplifies at the same
        /// tolerance, so a fixed 0.06 would erase a small shard entirely. The tolerance is
        /// scaled by the piece's own size and floored, keeping every surviving shard a shape.</summary>
        private static float FitTolerance(List<Vector2> outline)
        {
            var area = Mathf.Abs(DrawKit.SignedArea(outline));
            return Mathf.Clamp(Mathf.Sqrt(area) * k_FitToleranceAreaFactor,
                k_MinFragmentFitTolerance, k_FragmentFitTolerance);
        }

        /// <summary>Fallback when fitting collapses the ring: the polygon verbatim, no
        /// handles, closed with the fit_curve convention (last point duplicates the first).</summary>
        private static DrawnCurve CrispCurve(List<Vector2> outline)
        {
            var curve = new DrawnCurve();
            for (var i = 0; i < outline.Count; i++)
                curve.AddPoint(outline[i]);
            curve.AddPoint(outline[0]);
            return curve;
        }

        // --- geometry helpers -------------------------------------------------------

        /// <summary>Read the body's polygon shapes at break time rather than caching the
        /// composer output: the live body is the single source of truth, so shapes added by
        /// anything else break along with the prop and a stale cache can never be used.</summary>
        private bool CollectBodyPolygons()
        {
            m_BodyPolygons.Clear();

            var shapes = body.GetShapes(Allocator.Temp);
            try
            {
                if (!shapes.IsCreated)
                    return false;

                for (var i = 0; i < shapes.Length; i++)
                {
                    var shape = shapes[i];
                    if (!shape.isValid || shape.shapeType != PhysicsShape.ShapeType.Polygon)
                        continue;
                    m_BodyPolygons.Add(shape.polygonGeometry);
                }
            }
            finally
            {
                if (shapes.IsCreated)
                    shapes.Dispose();
            }

            return m_BodyPolygons.Count > 0;
        }

        /// <summary>Ring of world-space fragment points around the impact: a jittered circle,
        /// which is the radial-crack pattern the destructor's Voronoi-style regions expect.</summary>
        private Vector2[] BuildFragmentPoints(Vector2 center)
        {
            var count = Mathf.Max(shardCount, k_MinFragmentPoints);
            var radius = Mathf.Max(shardScatterRadius, k_MinScatterRadius);
            var points = new Vector2[count];
            var step = Mathf.PI * 2f / count;
            var phase = UnityEngine.Random.value * Mathf.PI * 2f;

            for (var i = 0; i < count; i++)
            {
                var angle = phase + i * step;
                var distance = radius * UnityEngine.Random.Range(k_ScatterJitterMin, k_ScatterJitterMax);
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            }
            return points;
        }

        private static Vector2[] ToRing(PolygonGeometry polygon)
        {
            // The span aliases the geometry's inline vertex array, so it is taken from a local
            // that outlives the copy loop.
            var vertices = polygon.AsReadOnlySpan();
            if (vertices.Length < 3)
                return null;

            var ring = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
                ring[i] = vertices[i];
            return ring;
        }

        /// <summary>Area-weighted mean of the pieces' PolygonGeometry centroids. A single
        /// fragment therefore lands exactly on poly.centroid; a sliced side lands on the
        /// middle of its decomposition.</summary>
        private Vector2 AreaWeightedCentroid(float totalArea)
        {
            if (totalArea <= 0f)
                return m_PieceCentroids.Count > 0 ? m_PieceCentroids[0] : Vector2.zero;

            var centroid = Vector2.zero;
            for (var i = 0; i < m_PieceGeometry.Count; i++)
                centroid += m_PieceCentroids[i] * Mathf.Abs(DrawKit.SignedArea(m_PieceGeometry[i]));
            return centroid / totalArea;
        }

        private static List<Vector2> ToLocalOutline(IReadOnlyList<Vector2> outline, Vector2 origin)
        {
            if (outline == null || outline.Count < 3)
                return null;

            var local = new List<Vector2>(outline.Count);
            for (var i = 0; i < outline.Count; i++)
                local.Add(outline[i] - origin);
            return local;
        }

        /// <summary>Sutherland-Hodgman clip of a closed ring against the half-plane on one side
        /// of the slice ray. "Left" matches PhysicsDestructor.Slice: looking along the
        /// direction, left is the +90 degree side, i.e. cross(direction, p - origin) &gt; 0.</summary>
        private static List<Vector2> ClipRingToHalfPlane(List<Vector2> ring, Vector2 origin,
            Vector2 direction, bool keepLeft)
        {
            if (ring == null || ring.Count < 3)
                return null;

            var sign = keepLeft ? 1f : -1f;
            var clipped = new List<Vector2>(ring.Count + 4);

            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var da = sign * Cross(direction, a - origin);
                var db = sign * Cross(direction, b - origin);
                var aInside = da >= 0f;
                var bInside = db >= 0f;

                if (aInside)
                    clipped.Add(a);

                // Crossing edge: da - db cannot be zero because the signs differ.
                if (aInside != bInside)
                    clipped.Add(Vector2.LerpUnclamped(a, b, da / (da - db)));
            }

            return clipped.Count >= 3 ? clipped : null;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>Local scale that cancels the parent's world scale, so the debris renders
        /// and collides at the size its vertices already describe.</summary>
        private static Vector3 InverseScale(Transform parent)
        {
            if (parent == null)
                return Vector3.one;

            var scale = parent.lossyScale;
            return new Vector3(SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
        }

        private static float SafeInverse(float value)
        {
            return Mathf.Abs(value) < k_MinScaleComponent ? 1f : 1f / value;
        }

        // --- source rings -----------------------------------------------------------

        /// <summary>The prop's outer ring in the same space as its collision polygons (local,
        /// lossyScale baked in) — the slice visual is cut out of this.</summary>
        private List<Vector2> CollectLocalRing()
        {
            var renderer = ResolveRenderer();
            if (renderer == null || !renderer.hasShape)
                return null;

            var scale = (Vector2)transform.lossyScale;
            var ring = renderer.GetBakedRing();
            if (ring == null || ring.Count < 3)
                return null;

            for (var i = 0; i < ring.Count; i++)
                ring[i] = new Vector2(ring[i].x * scale.x, ring[i].y * scale.y);
            return ring;
        }

        /// <summary>Bake the asset's hole curves the way the renderer bakes them (same
        /// interval, same closing-duplicate epsilon) so collision and fill see the same rings.
        /// Wobble is deliberately not applied: it is render-only.</summary>
        private void CollectHoleRings(DrawnShapeAsset asset, Vector2 scale)
        {
            var holeCurves = asset.holeCurves;
            if (holeCurves == null)
                return;

            var interval = Mathf.Max(asset.bakeInterval, 1e-4f);
            for (var i = 0; i < holeCurves.Count; i++)
            {
                var holeCurve = holeCurves[i];
                if (holeCurve == null || holeCurve.pointCount < 3)
                    continue;

                var ring = ToScaledRing(BuildBakedRing(holeCurve, interval), scale);
                if (ring != null)
                    m_HoleRings.Add(ring);
            }
        }

        /// <summary>Port of the renderer's _ring_of equivalent: bake, then drop the point that
        /// duplicates the start on a manually closed loop.</summary>
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

        private static Vector2[] ToScaledRing(List<Vector2> ring, Vector2 scale)
        {
            if (ring == null || ring.Count < 3)
                return null;

            var scaled = new Vector2[ring.Count];
            for (var i = 0; i < ring.Count; i++)
                scaled[i] = new Vector2(ring[i].x * scale.x, ring[i].y * scale.y);
            return scaled;
        }

        private DrawnShapeRenderer ResolveRenderer()
        {
            if (m_Renderer == null)
                TryGetComponent(out m_Renderer);
            return m_Renderer;
        }
    }
}
