using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The one shape every reference field takes: a label, a dropdown that shows what is picked (a
    /// value that names nothing is shown as an error, never as a good pick), and an optional ◎ that
    /// selects what it names. There is no text box: a reference is picked.
    /// </summary>
    public sealed class StateTreePickField : VisualElement
    {
        public const string UssClass = "dtp-pick-field";

        private static readonly Color k_Missing = new Color(1f, 0.45f, 0.4f);

        public StateTreePickField(string label, string shown, bool missing, string tooltip, Action<Rect> open)
        {
            AddToClassList(UssClass);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.minHeight = 20f;

            var name = new Label(label);
            name.AddToClassList(BaseField<string>.labelUssClassName);
            name.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            name.style.minWidth = 120f;
            name.style.width = new Length(40f, LengthUnit.Percent);
            name.style.maxWidth = 200f;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.tooltip = tooltip;
            Add(name);

            button = new Button { text = (string.IsNullOrEmpty(shown) ? "(none)" : shown) + (missing ? "   ✕ not found" : "") + "  ▾" };
            button.style.flexGrow = 1f;
            button.style.flexShrink = 1f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.overflow = Overflow.Hidden;
            button.tooltip = tooltip;
            if (missing)
                button.style.color = k_Missing;
            button.clicked += () => open?.Invoke(button.worldBound);
            Add(button);
        }

        public Button button { get; }

        public bool missing => button.text.Contains("✕ not found");

        /// <summary>A small square button after the dropdown — ◎ to select, ▶ to open.</summary>
        public StateTreePickField WithButton(string glyph, string tooltip, Action clicked)
        {
            var extra = new Button(clicked) { text = glyph, tooltip = tooltip };
            extra.style.width = 24f;
            extra.style.flexShrink = 0f;
            Add(extra);
            return this;
        }

        /// <summary>◎ for an asset: select it and ping it.</summary>
        public StateTreePickField WithPing(UnityEngine.Object asset, string what = null)
        {
            if (asset == null)
                return this;
            return WithButton("◎", "Show " + (what ?? asset.name), () =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            });
        }
    }
}
