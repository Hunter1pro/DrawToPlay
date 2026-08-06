using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// WASD / arrow keys → two context-scope numbers ("input:x", "input:y", −1..1) — movement
    /// input in the same shape every trigger already takes: the component only WRITES state,
    /// and whatever behavior wants to move (a <see cref="MoveOwnerByAxisTask"/> in the hero's
    /// alive state, usually) READS it. Swap this for a gamepad reader or an AI driving the
    /// same keys and the tree neither knows nor cares — controls are data on the spine.
    /// </summary>
    public sealed class AxisInputBehaviour : MonoBehaviour
    {
        public StateTreeContextKind scope = StateTreeContextKind.Player;

        public string contextId = "";

        public string xKey = "input:x";

        public string yKey = "input:y";

        private void Update()
        {
            Vector2 axis = ReadAxis();
            StateTreeContextHost host =
                StateTreeContextHost.Resolve(gameObject, scope, contextId);
            if (host == null)
                return;
            host.Context.blackboard[xKey] = axis.x;
            host.Context.blackboard[yKey] = axis.y;
        }

        private static Vector2 ReadAxis()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector2.zero;
            float x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
            return Vector2.zero;
#endif
        }
    }
}
