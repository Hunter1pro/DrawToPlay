using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The M1 exit criterion as a component: a dynamic circle dropped over drawn ground has to
    /// land on it and ROLL. Nothing here is meant to be reusable gameplay — it is the smallest
    /// honest consumer of the collision a <see cref="TerrainBlob"/> derives, so "the drawing is
    /// collidable" stops being a claim and becomes something you can watch.
    ///
    /// Usage: draw a blob, add a TerrainBlob to it (Chain mode gives proper one-sided ground),
    /// drop one of these above it, press Play.
    ///
    /// Deliberately asset-free — no sprite, no material, no prefab. The ball is drawn by
    /// <see cref="OnDrawGizmos"/> as a wire disc with one spoke, and the spoke is the point:
    /// a plain disc rolling and a plain disc sliding look identical.
    /// </summary>
    public sealed class RollingBall : EntityBody
    {
        /// <summary>Below this the circle stops being valid geometry (and invisible as a
        /// gizmo), so both the shape and the gizmo clamp to it.</summary>
        private const float k_MinRadius = 0.01f;

        /// <summary>0.4 is enough dry friction to convert a slide into a roll on the derived
        /// surface without the ball gripping and tipping.</summary>
        private const float k_DefaultFriction = 0.4f;

        /// <summary>PhysicsShapeDefinition's own default; spelled out because mass comes from
        /// density * area here (~0.79 kg at radius 0.5), never from an explicit mass field.</summary>
        private const float k_DefaultDensity = 1f;

        private const int k_GizmoSegments = 32;
        private static readonly Color k_GizmoColor = new Color(1f, 0.72f, 0.3f, 0.9f);

        /// <summary>Circle radius in the shape's own units, before Transform scale.</summary>
        public float radius = 0.5f;

        /// <summary>Surface feel of the ball. A public field initialised from
        /// <c>defaultDefinition</c> (never the default ctor) per the project convention — the
        /// helper below exists only because a field initialiser cannot reach into the nested
        /// surfaceMaterial.</summary>
        public PhysicsShapeDefinition shapeDefinition = CreateDefaultShapeDefinition();

        /// <summary>The radius the shape is actually built with. PhysicsCore2D has no scale, so
        /// TerrainBlob bakes lossyScale into its vertices and this does the equivalent for the
        /// circle — except a circle can only follow a UNIFORM scale, so the larger of |x|, |y|
        /// wins and non-uniform scale is unsupported (it would need an ellipse/capsule, not a
        /// circle). The gizmo reads the same property, so what you see is what collides.</summary>
        public float worldRadius
        {
            get
            {
                var scale = transform.lossyScale;
                float uniform = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                return Mathf.Max(radius * uniform, k_MinRadius);
            }
        }

        protected override void OnEnable()
        {
            // Forced, not merely defaulted: PhysicsBodyDefinition.defaultDefinition is Static,
            // and a static ball is a silent failure — it hangs in mid-air and looks like broken
            // collision rather than a misconfigured body. Every other field of bodyDefinition
            // (gravity scale, damping, sleep, constraints) stays fully author-editable; only the
            // type is overwritten, in memory, immediately before the base class builds the body
            // from it. Setting it in the inspector too is harmless and changes nothing.
            bodyDefinition.type = PhysicsBody.BodyType.Dynamic;

            base.OnEnable();

            if (!body.isValid)
                return;

            body.CreateShape(new CircleGeometry { center = Vector2.zero, radius = worldRadius },
                shapeDefinition);
        }

        // No OnDisable override: the base destroys the body and a body destroys the shapes
        // attached to it, so the circle goes with it. Radius/definition edits are picked up on
        // the next enable (re-enter play mode, or toggle the component).

        private void OnValidate()
        {
            // Clamp only — no physics calls from OnValidate.
            radius = Mathf.Max(radius, k_MinRadius);
        }

        private void OnDrawGizmos()
        {
            float drawRadius = worldRadius;
            Vector3 center = transform.position;

            // Start the ring at the current Z rotation so the spoke and the first segment share
            // an endpoint; in play mode the body writes rotation back through transformObject,
            // which is what makes rolling readable.
            float startRadians = transform.eulerAngles.z * Mathf.Deg2Rad;
            float step = Mathf.PI * 2f / k_GizmoSegments;

            Gizmos.color = k_GizmoColor;
            Vector3 previous = center + new Vector3(Mathf.Cos(startRadians), Mathf.Sin(startRadians), 0f) * drawRadius;
            Gizmos.DrawLine(center, previous);

            for (int i = 1; i <= k_GizmoSegments; i++)
            {
                float angle = startRadians + i * step;
                var point = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * drawRadius;
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        private static PhysicsShapeDefinition CreateDefaultShapeDefinition()
        {
            var definition = PhysicsShapeDefinition.defaultDefinition;
            definition.surfaceMaterial.friction = k_DefaultFriction;
            definition.density = k_DefaultDensity;
            return definition;
        }
    }
}
