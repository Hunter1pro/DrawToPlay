using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The M5 exit criterion's trigger, as a component: press SPACE and the animated character
    /// "switches to ragdoll and back". Nothing more — the whole behaviour lives in
    /// <see cref="RagdollDriver.Toggle"/>; this exists so the demo scene needs no gameplay layer to
    /// prove the switch works, in the same spirit as M1's <see cref="RollingBall"/>.
    ///
    /// INPUT BACKEND: the Sandbox project runs Active Input Handling = "Input System Package (New)"
    /// (ProjectSettings <c>activeInputHandler: 1</c>), so the legacy <c>UnityEngine.Input</c> class
    /// THROWS at runtime here and every Sandbox script — CameraManipulator, Fragmenting, Slicing —
    /// polls <c>Keyboard.current</c> instead. This matches them. The legacy branch is kept behind
    /// its define so the component still works if the project is ever switched to the old backend
    /// or to "Both"; when both defines are present, the new backend wins.
    ///
    /// BUILD REQUIREMENT: this is the first runtime script in the toolset to need the Input System,
    /// so <c>Runtime/PowerOfFire.DrawToPlay.asmdef</c> must list <c>"Unity.InputSystem"</c> in its
    /// <c>references</c>. Those Sandbox scripts get it for free by living in the predefined
    /// Assembly-CSharp (the package's <c>autoReferenced</c> flag only reaches predefined
    /// assemblies); an asmdef has to ask. Without it this file fails with CS0234 on the using
    /// above — a one-line fix, not a code problem.
    /// </summary>
    public sealed class RagdollDemoInput : MonoBehaviour
    {
        /// <summary>Driver to toggle. Left empty it resolves to the first one on this GameObject
        /// or below it, which is the demo scene's layout (both sit on the character root).</summary>
        public RagdollDriver driver;

        private void Awake()
        {
            if (driver == null)
                driver = GetComponentInChildren<RagdollDriver>();
        }

        private void Update()
        {
            if (driver == null || !TogglePressed())
                return;

            driver.Toggle();
        }

        /// <summary>Edge-triggered, not level-triggered: a held key must toggle once, not flip the
        /// ragdoll on and off every frame.</summary>
        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            // Null on a machine with no keyboard (and before the first device is reported).
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }
    }
}
