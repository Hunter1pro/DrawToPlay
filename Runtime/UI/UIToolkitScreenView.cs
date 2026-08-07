using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The UI Toolkit VIEW over <see cref="UIScreenBehaviour"/> — the blessed pattern for
    /// real screens in this architecture, and deliberately nothing more than a view: the
    /// tree owns what is open, the screen seam owns the data and the click reports, and
    /// this component only draws. No MVVM, no navigation stack, no store — "open the
    /// inventory" stays one state transition, exactly as cheap as the TextMesh demo view
    /// proved it, just drawn with panels instead of glyphs.
    ///
    /// Sits ON the screen's <see cref="UIScreenBehaviour.visualRoot"/> child beside a
    /// <see cref="UIDocument"/>, so the screen's Show/Hide GameObject toggle IS the
    /// document's visibility — no second visibility system. The panel is built in code
    /// with USS classes on every element (<c>dtp-screen</c>, <c>dtp-screen__title</c>,
    /// <c>dtp-screen__row</c>, <c>dtp-screen__detail</c>, <c>dtp-screen__close</c>), so a
    /// theme style sheet on the PanelSettings can restyle everything without touching
    /// code — that is the whole theming story.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIToolkitScreenView : MonoBehaviour
    {
        public UIScreenBehaviour screen;

        /// <summary>Panel heading; empty falls back to the screen id, uppercased.</summary>
        public string title = "";

        /// <summary>Docked left or right — two screens side by side is the master-detail
        /// demo layout; a theme can override the positioning entirely via the classes.</summary>
        public bool alignRight;

        public float width = 280f;

        private VisualElement m_Card;
        private ScrollView m_List;
        private Label m_Detail;

        private void OnEnable()
        {
            if (screen == null)
                screen = GetComponentInParent<UIScreenBehaviour>();

            BuildPanel();

            if (screen != null)
            {
                screen.listBound += OnListBound;
                screen.detailBound += OnDetailBound;
                OnListBound(screen.entries);
            }
        }

        private void OnDisable()
        {
            if (screen != null)
            {
                screen.listBound -= OnListBound;
                screen.detailBound -= OnDetailBound;
            }
        }

        private void BuildPanel()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();

            m_Card = new VisualElement();
            m_Card.AddToClassList("dtp-screen");
            m_Card.style.position = Position.Absolute;
            m_Card.style.top = 40f;
            if (alignRight)
                m_Card.style.right = 24f;
            else
                m_Card.style.left = 24f;
            m_Card.style.width = width;
            m_Card.style.paddingLeft = 12f;
            m_Card.style.paddingRight = 12f;
            m_Card.style.paddingTop = 8f;
            m_Card.style.paddingBottom = 10f;
            m_Card.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 0.92f);
            m_Card.style.borderTopLeftRadius = 6f;
            m_Card.style.borderTopRightRadius = 6f;
            m_Card.style.borderBottomLeftRadius = 6f;
            m_Card.style.borderBottomRightRadius = 6f;
            root.Add(m_Card);

            var header = new VisualElement();
            header.AddToClassList("dtp-screen__header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;
            m_Card.Add(header);

            var heading = new Label(string.IsNullOrEmpty(title)
                ? (screen != null ? screen.screenId.ToUpperInvariant() : "")
                : title);
            heading.AddToClassList("dtp-screen__title");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 15f;
            heading.style.color = new Color(0.92f, 0.93f, 0.96f);
            header.Add(heading);

            var close = new Button(() => screen?.ReportClose()) { text = "✕" };
            close.AddToClassList("dtp-screen__close");
            close.style.width = 24f;
            close.tooltip = "Close — the tree decides what that means for a modal screen; "
                + "a passive panel just hides.";
            header.Add(close);

            m_List = new ScrollView();
            m_List.AddToClassList("dtp-screen__list");
            m_List.style.maxHeight = 220f;
            m_Card.Add(m_List);

            m_Detail = new Label(string.Empty);
            m_Detail.AddToClassList("dtp-screen__detail");
            m_Detail.style.whiteSpace = WhiteSpace.Normal;
            m_Detail.style.marginTop = 6f;
            m_Detail.style.color = new Color(0.80f, 0.83f, 0.88f);
            m_Detail.style.display = DisplayStyle.None;
            m_Card.Add(m_Detail);
        }

        private void OnListBound(IReadOnlyList<UIListEntry> entries)
        {
            if (m_List == null)
                return;

            m_List.Clear();
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                UIListEntry entry = entries[i];
                var row = new Button(() => screen?.ReportItemClick(entry.itemId))
                {
                    text = entry.label + (entry.count > 1 ? "  ×" + entry.count : "")
                };
                row.AddToClassList("dtp-screen__row");
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.style.marginBottom = 2f;
                m_List.Add(row);
            }
        }

        private void OnDetailBound(string text)
        {
            if (m_Detail == null)
                return;
            m_Detail.text = text ?? string.Empty;
            m_Detail.style.display = string.IsNullOrEmpty(text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }
}
