using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Walks the owner toward the blackboard target until it is within
    /// <see cref="stopRange"/> — port of <c>ai_move_to_target_task.gd</c> reduced to what M6
    /// needs. Success on arrival, Failure with no valid target, Running while closing.
    ///
    /// MOVEMENT IS A TRANSFORM WRITE (M6 conventions, explicit): <c>transform.position +=
    /// dir * speed * dt</c>. No navigation, no steering, no physics. Godot drives movement by
    /// activating a "move" ability on an AbilityHost; there is no ability host in this port
    /// yet, and a mover COMPONENT (kinematic body sweep, ground snapping, obstacle slide) is
    /// M7+ polish. The consequence to know: an owner that also carries an
    /// <see cref="EntityBody"/> will fight this task, because the body writes the Transform
    /// after every simulation step and this writes it back — chase targets in M6 should be
    /// bodyless, or the mover component must land first.
    ///
    /// Z IS PRESERVED. Movement is planar (XY); the Transform's Z stays whatever the author
    /// set it to, because Z is render sorting depth in this toolset, not a third axis.
    ///
    /// TEARDOWN: this task is deliberately STATELESS — it allocates nothing, registers no
    /// navigation goal and spawns nothing, so there is nothing for OnExit to undo and no
    /// OnEnter reset that could be forgotten. An interrupt simply stops the owner where it
    /// stands, which is exactly right for a cancelled chase. Cancelled-safety by construction
    /// beats Cancelled-safety by cleanup code; the tasks below that DO hold state
    /// (<see cref="WaitTask"/>, <see cref="PlayPoseClipTask"/>) pay for it in their OnExit.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Chase Target", fileName = "ChaseTarget")]
    [StateTreeCategory("Tasks/Movement", "Move toward blackboard target until stop range")]
    public sealed class ChaseTargetTask : StateTreeTaskAsset
    {
        /// <summary>World units per second.</summary>
        public float moveSpeed = 2f;

        /// <summary>Stop this far from the target, world units. Should be a hair under the
        /// attacker's range or the entity oscillates across the range boundary.</summary>
        public float stopRange = 0.75f;

        /// <summary>Read <see cref="StateTreeLibraryUtil.MoveSpeedKey"/> instead of
        /// <see cref="moveSpeed"/>.</summary>
        public bool useBlackboardSpeed;

        /// <summary>Flip localScale.x toward the direction of travel while chasing, so the
        /// drawn body faces where it is going without a second task in the node. Same
        /// convention as <see cref="FaceTargetTask"/>: positive X = facing right.</summary>
        public bool faceTarget = true;

        /// <summary>Give up when the target gets further away than this (world units).
        /// 0 = never give up. Godot's <c>max_chase_distance</c>: without it a fast target
        /// drags the whole level's worth of enemies behind it forever.</summary>
        public float maxChaseDistance;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null)
                return StateTreeStatus.Failure;

            GameObject owner = context.owner;
            GameObject target = StateTreeLibraryUtil.GetValidTarget(context, owner);
            if (target == null)
                return StateTreeStatus.Failure;

            Vector2 offset = StateTreeLibraryUtil.PlanarOffset(owner, target);
            float distance = offset.magnitude;
            // A negative stop range would make arrival unreachable AND let the overshoot
            // clamp below step past the target; zero is the smallest meaningful value.
            float stop = Mathf.Max(stopRange, 0f);

            if (maxChaseDistance > 0f && distance > maxChaseDistance)
                return StateTreeStatus.Failure;

            if (distance <= stop)
                return StateTreeStatus.Success;

            if (faceTarget)
                FaceTargetTask.ApplyFacing(owner.transform, offset.x);

            float speed = StateTreeLibraryUtil.ResolveFloat(context,
                StateTreeLibraryUtil.MoveSpeedKey, moveSpeed, useBlackboardSpeed);
            if (speed <= 0f || distance <= 0f)
                return StateTreeStatus.Running;

            // Never overshoot into (or past) the stop band in one step: at low frame rates a
            // raw speed * dt step would tunnel through the target and the entity would
            // jitter around it forever.
            float step = Mathf.Min(speed * deltaTime, distance - stop);
            if (step <= 0f)
                return StateTreeStatus.Running;

            Vector2 direction = offset / distance;
            Transform ownerTransform = owner.transform;
            Vector3 position = ownerTransform.position;
            ownerTransform.position = new Vector3(
                position.x + direction.x * step,
                position.y + direction.y * step,
                position.z);

            return StateTreeStatus.Running;
        }
    }
}
