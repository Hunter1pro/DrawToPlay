using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// True when nothing solid stands between the owner and the blackboard target — the
    /// PhysicsCore2D half of brief §7.2's seed conditions ("LineOfSight (PhysicsCore2D
    /// <c>world.CastRay</c> with <c>QueryFilter</c> categories)"). This is what stops a zombie
    /// walking into a wall because someone is audible on the other side of it.
    ///
    /// VERIFIED API (unity-physicscore2d-queries + world-api skills):
    /// <c>world.CastRay(PhysicsQuery.CastRayInput, PhysicsQuery.QueryFilter,
    /// PhysicsQuery.WorldCastMode, Allocator)</c> returning a NativeArray of
    /// <c>PhysicsQuery.WorldCastResult</c> that MUST be disposed;
    /// <c>CastRayInput.FromTo(from, to)</c>; <c>WorldCastResult.shape</c>;
    /// <c>PhysicsShape.body</c>; <c>PhysicsBody.transformObject</c> (a
    /// <c>UnityEngine.Transform</c>). <see cref="PhysicsQuery.QueryFilter.Everything"/> is the
    /// M6 filter per conventions — categories become meaningful in M7 when layers are
    /// authored, and this component's <see cref="filter"/> is the single line that changes.
    ///
    /// WHY AllSorted AND NOT Closest: the ray starts INSIDE the owner's own body, so the
    /// nearest hit is very often a shape belonging to the caster itself, and with
    /// <c>WorldCastMode.Closest</c> that single result is all we get — every entity with a
    /// collider would permanently report itself blind. AllSorted returns hits nearest-first,
    /// so the walk below skips shapes owned by the owner (and by the target: arriving at the
    /// target's own body IS the definition of an unobstructed line) and treats the first
    /// foreign shape as the blocker. Cost is a full hit list per evaluation instead of one hit
    /// — a handful of shapes at authoring scale, and worth it for a correct answer.
    ///
    /// NO WORLD, NO SIGHT-BLOCKING: outside play mode there is no valid default world and
    /// therefore no geometry to occlude anything. <see cref="visibleWithoutWorld"/> decides
    /// whether that reads as "clear" (default — an edit-mode tree preview should not report
    /// every target as hidden) or "blocked".
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Conditions/Line Of Sight",
        fileName = "LineOfSight")]
    [StateTreeCategory("Conditions/Perception", "Raycast owner to target - false when blocked")]
    public sealed class LineOfSightCondition : StateTreeConditionAsset
    {
        /// <summary>Eye offset from the owner's origin, in the owner's world axes (world
        /// units). A drawn blob's origin sits at its centroid; a ray from the feet would clip
        /// the ground it is standing on.</summary>
        public Vector2 originOffset = Vector2.zero;

        /// <summary>Aim offset applied to the target's origin, same units.</summary>
        public Vector2 targetOffset = Vector2.zero;

        /// <summary>Maximum sight distance in world units. 0 or less = unlimited (the ray
        /// still ends at the target).</summary>
        public float maxDistance;

        /// <summary>Result when no valid physics world exists (edit mode). True = "cannot be
        /// blocked by geometry that does not exist yet".</summary>
        public bool visibleWithoutWorld = true;

        public override bool Evaluate(StateTreeContext context)
        {
            if (context == null)
                return false;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return false;

            Vector2 from = (Vector2)owner.transform.position + originOffset;
            Vector2 to = (Vector2)target.transform.position + targetOffset;
            if (maxDistance > 0f && (to - from).sqrMagnitude > maxDistance * maxDistance)
                return false;

            PhysicsWorld world = PhysicsWorld.defaultWorld;
            if (!world.isValid)
                return visibleWithoutWorld;

            // QueryFilter.Everything is the M6 filter (conventions). M7 authors collision
            // categories and this single argument becomes the authored filter — deliberately
            // NOT a serialized field here: a QueryFilter carries PhysicsMask values with no
            // inspector representation yet, and a half-initialised filter struct silently
            // hits NOTHING, which reads as "always visible" rather than as a broken setup.
            using NativeArray<PhysicsQuery.WorldCastResult> hits = world.CastRay(
                PhysicsQuery.CastRayInput.FromTo(from, to),
                PhysicsQuery.QueryFilter.Everything,
                PhysicsQuery.WorldCastMode.AllSorted,
                Allocator.Temp);

            for (int i = 0; i < hits.Length; i++)
            {
                PhysicsShape shape = hits[i].shape;
                if (!shape.isValid)
                    continue;

                // transformObject is the Transform EntityBody links on creation; a body built
                // by something that never set it cannot be attributed to a GameObject, so it
                // is treated as world geometry and blocks.
                Transform hitTransform = shape.body.transformObject;
                if (hitTransform == null)
                    return false;

                if (BelongsTo(hitTransform, owner) || BelongsTo(hitTransform, target))
                    continue;

                return false;
            }
            return true;
        }

        /// <summary>Whether a hit Transform is part of <paramref name="entity"/>'s hierarchy —
        /// either the entity itself, a child (a limb body on a ragdoll), or an ancestor (the
        /// entity is a visual child of the object carrying the body).</summary>
        private static bool BelongsTo(Transform hitTransform, GameObject entity)
        {
            if (hitTransform == null || entity == null)
                return false;
            Transform entityTransform = entity.transform;
            return hitTransform == entityTransform
                || hitTransform.IsChildOf(entityTransform)
                || entityTransform.IsChildOf(hitTransform);
        }
    }
}
