using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Turns the owner to face the blackboard target, then finishes immediately (Success).
    /// The 2D equivalent of Godot's <c>look_at</c> / <c>face_world_target</c> calls that its
    /// AI tasks scatter through their bodies — pulled out into its own component precisely
    /// because it is the kind of one-line wiring brief §7.2 says should never be re-typed.
    ///
    /// FACING IS THE SIGN OF localScale.x, the drawn-shape convention: a shape authored facing
    /// right is mirrored by negating X. The MAGNITUDE of the scale is preserved, so a
    /// non-uniformly scaled or Pose-animated body keeps its authored proportions; only the
    /// sign moves. Rotation is untouched — rotating a 2D character to look at something turns
    /// it upside down when the target is behind it, which is why the flip exists at all.
    ///
    /// INSTANT SUCCESS, not Running: facing is a single state statement, and a task that
    /// finishes lets the node's completion transition fire on the same tick. To hold facing
    /// while something else runs, put this task in the same node as the long-running one —
    /// the runner ticks every task in a node and only checks completion transitions once ALL
    /// of them are done.
    ///
    /// No target = Failure (library rule), which is a useful signal in its own right: a node
    /// whose facing task fails has nothing to face.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Face Target", fileName = "FaceTarget")]
    [StateTreeCategory("Tasks/Movement", "Flip facing toward target")]
    public sealed class FaceTargetTask : StateTreeTaskAsset
    {
        /// <summary>Ignore direction changes smaller than this (world units), so an entity
        /// standing almost exactly on its target does not strobe between facings on
        /// floating-point noise.</summary>
        public float deadZone = 0.01f;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null)
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return StateTreeStatus.Failure;

            float dx = StateTreeLibraryUtil.PlanarOffset(owner, target).x;
            if (Mathf.Abs(dx) > deadZone)
                ApplyFacing(owner.transform, dx);

            return StateTreeStatus.Success;
        }

        /// <summary>Point <paramref name="ownerTransform"/> along the sign of
        /// <paramref name="dx"/> by flipping localScale.x, preserving its magnitude. Shared
        /// with <see cref="ChaseTargetTask"/> so both components mean the same thing by
        /// "facing" — one definition, not two that drift.</summary>
        public static void ApplyFacing(Transform ownerTransform, float dx)
        {
            if (ownerTransform == null || dx == 0f)
                return;

            Vector3 scale = ownerTransform.localScale;
            float magnitude = Mathf.Abs(scale.x);
            float signed = dx < 0f ? -magnitude : magnitude;
            if (scale.x == signed)
                return;

            scale.x = signed;
            ownerTransform.localScale = scale;
        }
    }
}
