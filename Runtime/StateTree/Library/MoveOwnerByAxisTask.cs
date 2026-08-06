using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Move the owner by the axis a context scope holds — the hero's legs, as a LATENT task:
    /// it runs (and moves) exactly while its state is active, so "the player can move" is a
    /// fact about being in the alive state, not a component that must be toggled. A stun, a
    /// cutscene, a death are all just states without this task; the tree already knows how to
    /// take controls away.
    ///
    /// Reads the keys <see cref="AxisInputBehaviour"/> writes (or whatever else writes them —
    /// an AI possessing the hero writes the same two numbers). Diagonals are normalized;
    /// <see cref="clampExtents"/> keeps an arcade arena without a physics wall.
    /// </summary>
    [CreateAssetMenu(menuName = "Draw To Play/AI/Tasks/Move Owner By Axis",
        fileName = "MoveOwnerByAxis")]
    [StateTreeCategory("Tasks/Movement", "Move the owner by a context-scope input axis while active")]
    public sealed class MoveOwnerByAxisTask : StateTreeTaskAsset
    {
        public StateTreeContextKind scope = StateTreeContextKind.Player;

        public string scopeId = "";

        public string xKey = "input:x";

        public string yKey = "input:y";

        /// <summary>World units per second.</summary>
        public float speed = 2.4f;

        /// <summary>Half-extents of the allowed area around the origin; zero on an axis =
        /// unlimited there.</summary>
        public Vector2 clampExtents = Vector2.zero;

        public override StateTreeStatus OnTick(StateTreeContext context, float deltaTime)
        {
            if (context == null || context.owner == null)
                return StateTreeStatus.Failure;

            StateTreeContextHost host =
                StateTreeContextHost.Resolve(context.owner, scope, scopeId);
            if (host == null)
                return StateTreeStatus.Running;

            var scoped = host.Context.blackboard;
            float x = scoped.TryGetValue(xKey, out object hx) && hx is float fx ? fx : 0f;
            float y = scoped.TryGetValue(yKey, out object hy) && hy is float fy ? fy : 0f;
            var axis = new Vector2(x, y);
            if (axis.sqrMagnitude > 1f)
                axis.Normalize();

            Vector3 position = context.owner.transform.position;
            position += new Vector3(axis.x, axis.y, 0f) * (speed * deltaTime);
            if (clampExtents.x > 0f)
                position.x = Mathf.Clamp(position.x, -clampExtents.x, clampExtents.x);
            if (clampExtents.y > 0f)
                position.y = Mathf.Clamp(position.y, -clampExtents.y, clampExtents.y);
            context.owner.transform.position = position;

            return StateTreeStatus.Running;
        }
    }
}
