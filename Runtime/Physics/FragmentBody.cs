using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One piece of a broken <see cref="DestructibleShape"/> (draw-tool-port-brief.md §5): a
    /// dynamic EntityBody whose collision is a set of CONVEX pieces stored in local space.
    /// PhysicsDestructor already hands back convex PolygonGeometry, so the pieces are stored
    /// verbatim and re-created with <c>PolygonGeometry.Create</c> — no decomposition, no
    /// geometry drift between what the destructor computed and what the debris collides with.
    ///
    /// Storage is a flat vertex array plus a per-piece vertex count, because Unity does not
    /// serialize jagged arrays: pieces[i] is m_Vertices[offset .. offset + m_PieceSizes[i]].
    /// One piece = an impact shard; several pieces = one side of a slice (which the destructor
    /// returns as a convex decomposition, not as a single polygon).
    ///
    /// Any outline with more than <see cref="PhysicsConstants.MaxPolygonVertices"/> vertices —
    /// only reachable when a caller hands over a hand-authored ring rather than destructor
    /// output — is decomposed on the fly with <c>PolygonGeometry.CreatePolygons</c>, which also
    /// accepts concave rings. Both paths bake the Transform's lossyScale into the vertices
    /// (physics has no scale), exactly like TerrainBlob's derivation.
    ///
    /// The matching visual is a normal DrawnShapeRenderer driven by an in-memory
    /// DrawnShapeAsset clone that DestructibleShape fits from the same geometry. That clone
    /// belongs to nobody else, so this component destroys it with itself when
    /// <see cref="ownsRuntimeAsset"/> is set — otherwise the ScriptableObject would leak for
    /// every shard ever spawned.
    /// </summary>
    public sealed class FragmentBody : EntityBody
    {
        // A polygon needs 3 points; anything shorter is a degenerate sliver from a near-miss
        // cut and is dropped rather than fed to physics (PolygonGeometry.Create would reject
        // it with a warning anyway).
        private const int k_MinPieceVertices = 3;

        /// <summary>Shape settings for every piece (density drives the debris mass, the
        /// surface material its friction/bounciness). DestructibleShape copies its own
        /// definition in so debris feels like the prop it came from.</summary>
        public PhysicsShapeDefinition shapeDefinition = PhysicsShapeDefinition.defaultDefinition;

        /// <summary>The runtime DrawnShapeAsset clone this fragment renders from, when it was
        /// created for this fragment alone (see <see cref="ownsRuntimeAsset"/>).</summary>
        public DrawnShapeAsset runtimeAsset;

        /// <summary>True when <see cref="runtimeAsset"/> is an in-memory instance owned by this
        /// component and must be destroyed with it. False when the asset is a shared project
        /// asset that merely happens to be referenced here.</summary>
        public bool ownsRuntimeAsset;

        [SerializeField] private Vector2[] m_Vertices = Array.Empty<Vector2>();
        [SerializeField] private int[] m_PieceSizes = Array.Empty<int>();

        private readonly List<PhysicsShape> m_Shapes = new List<PhysicsShape>();
        private Vector2[] m_ScratchVertices = Array.Empty<Vector2>();

        /// <summary>Number of convex pieces this fragment is built from.</summary>
        public int pieceCount => m_PieceSizes != null ? m_PieceSizes.Length : 0;

        /// <summary>Replace the collision with a single convex piece (the impact-shard case).
        /// Vertices are in this Transform's local space, counter-clockwise or clockwise alike —
        /// PolygonGeometry.Create hulls them. Rebuilds live when a body already exists.</summary>
        public void SetPiece(IReadOnlyList<Vector2> vertices)
        {
            var sizes = vertices != null ? new[] { vertices.Count } : Array.Empty<int>();
            SetPieces(vertices, sizes);
        }

        /// <summary>Replace the collision with a run of convex pieces packed into one vertex
        /// list (the slice case: one side of a cut is a convex decomposition). The i-th piece
        /// consumes <paramref name="pieceSizes"/>[i] vertices from <paramref name="vertices"/>,
        /// in order. Copies both inputs — the caller keeps ownership of its lists.</summary>
        public void SetPieces(IReadOnlyList<Vector2> vertices, IReadOnlyList<int> pieceSizes)
        {
            m_Vertices = ToArray(vertices);
            m_PieceSizes = ToArray(pieceSizes);
            if (body.isValid)
                RebuildCollision();
        }

        /// <summary>Debris is dynamic by definition — a static or kinematic shard would hang
        /// in the air where the prop broke. The serialized type is overridden rather than
        /// trusted, the same way TerrainBlob pins Static.</summary>
        protected override void ConfigureBodyDefinition(ref PhysicsBodyDefinition definition)
        {
            definition.type = PhysicsBody.BodyType.Dynamic;
        }

        protected override void OnBodyCreated()
        {
            RebuildCollision();
        }

        protected override void OnBodyDestroying()
        {
            // Destroying the body destroys its shapes; the handles just go stale.
            m_Shapes.Clear();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeAsset();
        }

        /// <summary>Destroy the current shapes and re-create them from the stored pieces.
        /// Main-thread only (it creates and destroys world objects, so never from a solver
        /// callback — WORM). No-op without a live body, which is every edit-mode call.</summary>
        public void RebuildCollision()
        {
            DestroyShapes();

            if (!body.isValid || m_Vertices == null || m_PieceSizes == null)
                return;

            // Physics has no scale, so lossyScale is baked into the vertices. Non-uniform
            // scale combined with rotation is unsupported (it needs a shear physics cannot
            // represent) — the same authoring rule as TerrainBlob.
            var scale = (Vector2)transform.lossyScale;

            var cursor = 0;
            for (var i = 0; i < m_PieceSizes.Length; i++)
            {
                var size = m_PieceSizes[i];

                // A corrupt size run (hand-edited serialized data) would walk off the end of
                // the vertex array — stop rather than read garbage.
                if (size < k_MinPieceVertices || cursor + size > m_Vertices.Length)
                    break;

                if (size <= PhysicsConstants.MaxPolygonVertices)
                    CreateConvexPiece(cursor, size, scale);
                else
                    CreateDecomposedPiece(cursor, size, scale);

                cursor += size;
            }
        }

        /// <summary>The destructor-output path: the piece already fits a single
        /// PolygonGeometry, so it becomes exactly one shape with no re-tessellation.</summary>
        private void CreateConvexPiece(int offset, int count, Vector2 scale)
        {
            var scaled = ScaleIntoScratch(offset, count, scale);
            var geometry = PolygonGeometry.Create(new ReadOnlySpan<Vector2>(scaled, 0, count));
            if (!geometry.isValid)
                return;

            var shape = body.CreateShape(geometry, shapeDefinition);
            if (shape.isValid)
                m_Shapes.Add(shape);
        }

        /// <summary>The general path: a ring too long for one polygon (or concave) is convex-
        /// decomposed. vertexScale carries the Transform scale instead of pre-scaled vertices
        /// because CreatePolygons applies its "too small" rejection AFTER scaling, so scaling
        /// here is what keeps small pieces alive.</summary>
        private void CreateDecomposedPiece(int offset, int count, Vector2 scale)
        {
            var vertices = new ReadOnlySpan<Vector2>(m_Vertices, offset, count);
            var polygons = PolygonGeometry.CreatePolygons(vertices, PhysicsTransform.identity,
                scale, Allocator.Temp);
            try
            {
                if (!polygons.IsCreated || polygons.Length == 0)
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

        private Vector2[] ScaleIntoScratch(int offset, int count, Vector2 scale)
        {
            if (m_ScratchVertices.Length < count)
                m_ScratchVertices = new Vector2[Mathf.NextPowerOfTwo(count)];

            for (var i = 0; i < count; i++)
            {
                var vertex = m_Vertices[offset + i];
                m_ScratchVertices[i] = new Vector2(vertex.x * scale.x, vertex.y * scale.y);
            }
            return m_ScratchVertices;
        }

        private void DestroyShapes()
        {
            for (var i = 0; i < m_Shapes.Count; i++)
            {
                var shape = m_Shapes[i];
                if (shape.isValid)
                    shape.Destroy();
            }
            m_Shapes.Clear();
        }

        /// <summary>The in-memory style clone dies with the fragment. Debris is spawned by the
        /// hundred over a play session, so leaving these to the domain reload is a real leak.
        /// DestroyImmediate is only ever reachable from an editor-driven teardown; the runtime
        /// path is always Destroy (DrawnShapeRenderer uses the same guard for its mesh).</summary>
        private void ReleaseRuntimeAsset()
        {
            if (!ownsRuntimeAsset || runtimeAsset == null)
                return;

            var asset = runtimeAsset;
            runtimeAsset = null;
            ownsRuntimeAsset = false;

            if (Application.isPlaying)
                Destroy(asset);
            else
                DestroyImmediate(asset);
        }

        private static Vector2[] ToArray(IReadOnlyList<Vector2> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<Vector2>();

            var copy = new Vector2[source.Count];
            for (var i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return copy;
        }

        private static int[] ToArray(IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<int>();

            var copy = new int[source.Count];
            for (var i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return copy;
        }
    }
}
