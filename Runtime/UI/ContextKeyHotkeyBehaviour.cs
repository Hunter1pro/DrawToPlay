using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// One key press → one context-scope blackboard key — the smallest possible TRIGGER
    /// source. This is the "trigger from a button or the level" of the notebook's flow: the
    /// press writes the key, the UI tree's interrupt sees it, the handling state consumes it.
    /// The component knows nothing about what it triggers; anything watching the scope reacts.
    ///
    /// INPUT BACKEND: the <see cref="RagdollDemoInput"/> arrangement — the Sandbox project
    /// runs the new Input System, the legacy branch survives behind its define.
    /// </summary>
    public sealed class ContextKeyHotkeyBehaviour : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM
        public Key hotkey = Key.I;
#else
        public KeyCode hotkey = KeyCode.I;
#endif

        public StateTreeContextKind scope = StateTreeContextKind.Player;

        public string contextId = "";

        public string blackboardKey = "ui:openInventory";

        private void Update()
        {
            if (!PressedThisFrame())
                return;

            StateTreeContextHost host =
                StateTreeContextHost.Resolve(gameObject, scope, contextId);
            if (host != null)
                host.Context.blackboard[blackboardKey] = 1f;
        }

        private bool PressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[hotkey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(hotkey);
#else
            return false;
#endif
        }
    }
}
