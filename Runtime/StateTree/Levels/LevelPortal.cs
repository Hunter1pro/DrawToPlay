using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// Scene furniture that travels: a trigger plus WHICH VERB of the
    /// <see cref="LevelService"/> it speaks. A doorway to the cavern is
    /// <see cref="PortalKind.GoTo"/> + "cavern"; an expedition entrance is
    /// <see cref="PortalKind.Expedition"/> + the expedition's name (the service remembers
    /// where you came from); the expedition's way out is <see cref="PortalKind.Return"/> —
    /// where it leads is the SERVICE's memory, so the portal does not say.
    ///
    /// This is the layering made small: the component holds data and calls a verb; the
    /// service holds the policy; the session tree only handles state. Hotkey-triggered here
    /// because the demo has no walkable player — a trigger-volume portal would call exactly
    /// the same verbs from OnTriggerEnter2D.
    /// </summary>
    [AddComponentMenu("Draw To Play/Level Portal")]
    public sealed class LevelPortal : MonoBehaviour
    {
        public enum PortalKind
        {
            /// <summary>Travel to <see cref="levelName"/>.</summary>
            GoTo,

            /// <summary>Travel to <see cref="levelName"/>, remembering the way back.</summary>
            Expedition,

            /// <summary>Travel back to wherever the expedition was entered from.</summary>
            Return
        }

#if ENABLE_INPUT_SYSTEM
        public Key hotkey = Key.P;
#else
        public KeyCode hotkey = KeyCode.P;
#endif

        public PortalKind kind = PortalKind.GoTo;

        /// <summary>Destination for <see cref="PortalKind.GoTo"/> and
        /// <see cref="PortalKind.Expedition"/>; unused for Return.</summary>
        public string levelName = "";

        [InjectService] private LevelService m_Service;

        private void Start()
        {
            // Plain components opt into injection with one line; the injector reports a
            // missing service loudly, once.
            StateTreeServiceInjector.Inject(this, gameObject);
        }

        private void Update()
        {
            if (!PressedThisFrame() || m_Service == null)
                return;

            switch (kind)
            {
                case PortalKind.Expedition:
                    m_Service.EnterExpedition(levelName);
                    break;
                case PortalKind.Return:
                    m_Service.ReturnFromExpedition();
                    break;
                default:
                    m_Service.RequestLevel(levelName);
                    break;
            }
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
