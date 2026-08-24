using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The player's INTENT, as a service: a stick, an aim, a trigger, a dash — and nothing
    /// else. Whoever produces it (an on-screen thumbstick, a keyboard, a replay, a network
    /// command later) writes here; whoever moves the body reads here — so "mobile joystick"
    /// is a view decision, not a gameplay one. Scoped by whoever provides it: the session for
    /// a game whose input outlives levels, the level when it does not.
    /// </summary>
    public sealed class InputService
    {
        /// <summary>-1..1 on each axis, screen-relative.</summary>
        public Vector2 move { get; set; }

        /// <summary>True while the stick is actually pushed — movement starts on non-zero
        /// input, not on touch-down, so resting a thumb does not walk.</summary>
        public bool hasMove => move.sqrMagnitude > 0.0004f;

        /// <summary>Raised by the action button; consumed by whoever acts on it.</summary>
        public bool attackPressed { get; set; }

        /// <summary>Where the player is POINTING — a unit-ish direction in world axes. Zero
        /// when nobody aims; whoever reads it keeps the last direction it liked.</summary>
        public Vector2 aim { get; set; }

        /// <summary>The trigger, as a STATE: held is firing at whatever rate the weapon's
        /// ability allows. (The push button stays an event; a trigger is not.)</summary>
        public bool fireHeld { get; set; }

        /// <summary>The dash press — one-shot, consumed by whoever moves the body.</summary>
        public bool dashPressed { get; set; }

        public bool ConsumeDash()
        {
            if (!dashPressed)
                return false;
            dashPressed = false;
            return true;
        }

        public bool ConsumeAttack()
        {
            if (!attackPressed)
                return false;
            attackPressed = false;
            return true;
        }
    }
}
