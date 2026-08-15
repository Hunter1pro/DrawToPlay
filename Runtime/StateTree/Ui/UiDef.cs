using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>What a UI row IS — the three lifetimes a piece of screen can have. Order
    /// serialized, append only.</summary>
    public enum UiKind
    {
        /// <summary>Exclusive with other screens: showing one hides its siblings — the HUD,
        /// a title, a game-over card.</summary>
        Screen = 0,

        /// <summary>Stacks on top of whatever is shown — a confirm, a conversation. A popup
        /// is a STATE in spirit: its time on screen is the state that shows it.</summary>
        Popup = 1,

        /// <summary>A piece that minds its own business — a bar, a marker. Neither exclusive
        /// nor stacked; shown and hidden independently.</summary>
        Widget = 2
    }

    /// <summary>
    /// ONE PIECE OF SCREEN, AS A REGISTRY ROW (the UI pass): the view prefab as a picked
    /// reference, the PANEL ORDER AS DATA — the load-bearing number that decides what draws
    /// over what and what receives a press first stops hiding in a builder constant (the
    /// joystick-eats-the-Talk-press lesson) and becomes a row the dashboard can validate —
    /// and the row's declared PARAMETERS, so one ConfirmPopup row serves every question by
    /// being shown with different arguments, the exit-destination pattern applied to UI.
    /// </summary>
    [Serializable]
    public sealed class UiDef : StateTreeRegistryEntry
    {
        public UiKind kind = UiKind.Screen;

        [Tooltip("The view — a prefab carrying a UIDocument (its sorting order is asserted "
            + "from this row on show) and any UiViewBehaviour that reads the arguments.")]
        public GameObject prefab;

        [Tooltip("Who is on top, and who gets the press: higher draws over and receives "
            + "input before lower. Two rows sharing a value is a reported finding.")]
        public float sortingOrder;

        [Tooltip("The row's tunable surface — what a show-site may override, with the "
            + "defaults it takes when nobody does. Seeded into the spawned view's "
            + "UiViewBehaviour.Bind.")]
        public List<GraphTaskParameter> parameters = new List<GraphTaskParameter>();

        public override string Describe()
        {
            return kind + " · order " + sortingOrder.ToString("0.##")
                + (parameters.Count > 0
                    ? " · " + parameters.Count + " parameter"
                        + (parameters.Count == 1 ? "" : "s")
                    : "")
                + (prefab == null ? " · NO PREFAB" : "");
        }
    }
}
