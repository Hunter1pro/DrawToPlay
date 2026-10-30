using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The State Tree window's one visual language — the header, the section headings, the dim notes,
    /// the cards and their "More" fold — shared by Mission mode and Dev mode so the two read as one tool.
    /// </summary>
    internal static class StateTreeInspectorLook
    {
        public const string HeaderClass = "dtp-look-header";
        public const string SectionClass = "dtp-look-section";
        public const string NoteClass = "dtp-look-note";
        public const string CardClass = "dtp-look-card";
        public const string CardTitleClass = "dtp-look-card-title";
        public const string MoreClass = "dtp-look-more";
        public const string MenuClass = "dtp-look-menu";

        public static readonly Color CardBackground = new Color(0f, 0f, 0f, 0.16f);

        /// <summary>The top line: a coloured glyph and a bold title; the caller adds its buttons after.</summary>
        public static VisualElement Header(string icon, Color iconColor, string title, out Label titleLabel)
        {
            var header = new VisualElement();
            header.AddToClassList(HeaderClass);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4f;

            var glyph = new Label(string.IsNullOrEmpty(icon) ? "●" : icon);
            glyph.style.fontSize = 18;
            glyph.style.color = iconColor;
            glyph.style.marginRight = 6f;
            header.Add(glyph);

            titleLabel = new Label(title);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 14;
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.flexShrink = 1f;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            header.Add(titleLabel);
            return header;
        }

        public static Label Section(string text)
        {
            var label = new Label(text);
            label.AddToClassList(SectionClass);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8f;
            label.style.marginBottom = 2f;
            return label;
        }

        public static Label Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList(NoteClass);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.8f;
            return label;
        }

        /// <summary>A rounded box with an accent stripe on its left — one task, one condition, one transition.</summary>
        public static VisualElement Card(Color background, Color accent)
        {
            var card = new VisualElement();
            card.AddToClassList(CardClass);
            card.style.backgroundColor = background;
            card.style.borderLeftWidth = 3f;
            card.style.borderLeftColor = accent;
            card.style.paddingLeft = 6f;
            card.style.paddingRight = 6f;
            card.style.paddingTop = 4f;
            card.style.paddingBottom = 6f;
            card.style.marginBottom = 4f;
            card.style.borderTopLeftRadius = 4f;
            card.style.borderTopRightRadius = 4f;
            card.style.borderBottomLeftRadius = 4f;
            card.style.borderBottomRightRadius = 4f;
            return card;
        }

        /// <summary>A card's first line: glyph, bold title, then whatever buttons the caller adds.</summary>
        public static VisualElement CardHeader(string icon, Color iconColor, string title, out Label titleLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20f;

            var glyph = new Label(string.IsNullOrEmpty(icon) ? "●" : icon);
            glyph.style.color = iconColor;
            glyph.style.marginRight = 4f;
            glyph.style.unityTextAlign = TextAnchor.MiddleCenter;
            glyph.style.minWidth = 14f;
            row.Add(glyph);

            titleLabel = new Label(title);
            titleLabel.AddToClassList(CardTitleClass);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.flexShrink = 1f;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(titleLabel);
            return row;
        }

        /// <summary>The small ⋮ that opens a card's own commands (move, remove, …).</summary>
        public static Button MenuButton(string tooltip, Action<Rect> open)
        {
            var button = new Button { text = "⋮", tooltip = tooltip };
            button.AddToClassList(MenuClass);
            button.style.width = 22f;
            button.style.flexShrink = 0f;
            button.clicked += () => open?.Invoke(button.worldBound);
            return button;
        }

        /// <summary>A small button on a card's header line (Open, Edit in Graph, ◎).</summary>
        public static Button SmallButton(string text, string tooltip, Action clicked)
        {
            var button = new Button(clicked) { text = text, tooltip = tooltip };
            button.style.flexShrink = 0f;
            return button;
        }

        /// <summary>The fold a card keeps its quiet fields in — the ones still at their default.</summary>
        public static Foldout More(int count, bool open, Action<bool> remember)
        {
            var more = new Foldout { text = "More (" + count + ")", value = open };
            more.AddToClassList(MoreClass);
            more.style.marginTop = 2f;
            more.style.opacity = 0.9f;
            more.tooltip = "Fields still at their default value.";
            more.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == more)
                    remember?.Invoke(evt.newValue);
            });
            return more;
        }
    }
}
