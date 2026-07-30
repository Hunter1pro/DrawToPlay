using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Thin owner of a PhysicsCore2D PhysicsBody (draw-tool-port-brief.md §3): creates the
    /// body from the serialized definition in OnEnable at the Transform's pose, links
    /// body.transformObject = transform for automatic sync, destroys in OnDisable guarded
    /// by isValid. Subclasses add shapes (TerrainBlob derives collision from the drawing).
    /// Deliberately thin — Unity's own PhysicsCore2D wrapper components are expected in
    /// 6000.7 and this layer should stay easy to reconcile.
    ///
    /// Pose convention (M1): the Transform is authoring truth for WHERE the body is, the
    /// definition for WHAT it is. The world's default transform plane is XY, so the body
    /// takes the Transform's world XY position and its world Z rotation; scale has no
    /// physics counterpart and is the subclass's problem (TerrainBlob bakes lossyScale into
    /// its vertices). No [ExecuteAlways]: bodies only ever exist in play mode, which is
    /// what keeps edit-mode authoring free of live physics objects.
    /// </summary>
    public class EntityBody : MonoBehaviour
    {
        public PhysicsBodyDefinition bodyDefinition = PhysicsBodyDefinition.defaultDefinition;

        /// <summary>The owned body — default (invalid) while the component is disabled.</summary>
        public PhysicsBody body { get; protected set; }

        protected virtual void OnEnable()
        {
            CreateBody();
        }

        protected virtual void OnDisable()
        {
            DestroyBody();
        }

        /// <summary>Create the body in PhysicsWorld.defaultWorld at the Transform's world
        /// pose. Idempotent: a valid body is left alone.</summary>
        protected void CreateBody()
        {
            if (body.isValid)
                return;

            // Always the default world (brief §3); a world is only invalid very early in a
            // domain reload, in which case there is nothing to create the body in yet.
            var world = PhysicsWorld.defaultWorld;
            if (!world.isValid)
                return;

            // Work on a copy: the serialized definition stays exactly as authored, so
            // re-enabling the component reproduces the same body.
            var definition = bodyDefinition;

            // Bodies should be created at their final pose — creating at the origin and
            // moving afterwards nearly doubles the cost (PhysicsBodyDefinition.position).
            definition.position = (Vector2)transform.position;

            // 2D rotation from the Transform's world Z Euler angle. Exact for the XY
            // transform plane and Z-only rotations, which is the whole of the 2D authoring
            // case; a Transform tilted out of the XY plane is unsupported.
            definition.rotation = PhysicsRotate.FromDegrees(transform.eulerAngles.z);

            ConfigureBodyDefinition(ref definition);

            // Local first: C# forbids property setters on a by-value struct returned from
            // a property, and PhysicsBody is a handle struct so no writeback is needed
            // (umbrella skill, "C# Compiler Limitation with Setting Properties").
            var createdBody = world.CreateBody(definition);

            // Automatic body -> Transform sync after each simulation step.
            createdBody.transformObject = transform;

            body = createdBody;

            OnBodyCreated();
        }

        /// <summary>Destroy the body and everything attached to it (shapes, chains, joints
        /// are destroyed with their body). Safe to call when no body exists.</summary>
        protected void DestroyBody()
        {
            if (!body.isValid)
            {
                body = default;
                return;
            }

            OnBodyDestroying();
            body.Destroy();
            body = default;
        }

        /// <summary>Last chance to override definition fields before the body is created —
        /// the Transform pose is already applied. TerrainBlob pins the body type here.</summary>
        protected virtual void ConfigureBodyDefinition(ref PhysicsBodyDefinition definition)
        {
        }

        /// <summary>The body now exists: attach shapes here.</summary>
        protected virtual void OnBodyCreated()
        {
        }

        /// <summary>The body is about to be destroyed: drop tracked shape/chain handles
        /// here. Destroying them individually is unnecessary — destroying the body does it.</summary>
        protected virtual void OnBodyDestroying()
        {
        }
    }
}
