using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// A code-built world-space VIEW over one <see cref="UIScreenBehaviour"/> — TextMesh rows,
    /// clicks hit-tested through the Input System mouse (no UGUI package in this project, no
    /// colliders, no legacy physics). It draws what the screen's events say and reports what
    /// the mouse does; every piece it creates lives under the screen's <c>visualRoot</c>, so
    /// the TREE's Show/Hide governs visibility and this component never asks who is open.
    /// Swapping it for a UI Toolkit view later changes nothing above it — that seam is the
    /// point of <see cref="UIScreenBehaviour"/>.
    /// </summary>
    public sealed class UIScreenTextView : MonoBehaviour
    {
        public UIScreenBehaviour screen;

        public string title = "";

        public float rowHeight = 0.55f;

        public float rowWidth = 4.5f;

        private sealed class Row
        {
            public GameObject holder;
            public string itemId;
            public bool isClose;
        }

        private readonly List<Row> m_Rows = new List<Row>();
        private TextMesh m_Detail;
        private Font m_Font;

        private void OnEnable()
        {
            if (screen == null)
                screen = GetComponent<UIScreenBehaviour>();
            if (screen == null)
                return;

            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureScaffold();
            screen.listBound += OnListBound;
            screen.detailBound += OnDetailBound;
            OnListBound(screen.entries);
        }

        private void OnDisable()
        {
            if (screen == null)
                return;
            screen.listBound -= OnListBound;
            screen.detailBound -= OnDetailBound;
        }

        private void Update()
        {
            if (screen == null || !screen.isVisible)
                return;
            if (!TryReadClick(out Vector2 screenPosition))
                return;
            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 world = camera.ScreenToWorldPoint(screenPosition);
            for (int i = 0; i < m_Rows.Count; i++)
            {
                Row row = m_Rows[i];
                if (row.holder == null)
                    continue;
                Vector3 center = row.holder.transform.position;
                var bounds = new Rect(center.x - rowWidth * 0.5f, center.y - rowHeight * 0.5f,
                    rowWidth, rowHeight);
                if (!bounds.Contains(new Vector2(world.x, world.y)))
                    continue;

                if (row.isClose)
                    screen.ReportClose();
                else
                    screen.ReportItemClick(row.itemId);
                return;
            }
        }

        /// <summary>The RagdollDemoInput backend arrangement: new Input System first, legacy
        /// behind its define, nothing at all when neither exists (the desk-compile
        /// harness).</summary>
        private static bool TryReadClick(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif
            screenPosition = default;
            return false;
        }

        /// <summary>Title, detail line, and the close row — built once, under the visual root
        /// (created here when the screen has none, so a bare screen component still shows).</summary>
        private void EnsureScaffold()
        {
            if (screen.visualRoot == null)
            {
                var panel = new GameObject("Panel");
                panel.transform.SetParent(transform, false);
                panel.SetActive(false);
                screen.visualRoot = panel;
            }

            MakeText("Title", new Vector3(0f, 0.9f, 0f),
                string.IsNullOrEmpty(title) ? screen.screenId.ToUpperInvariant() : title,
                new Color(1f, 0.9f, 0.6f));
            m_Detail = MakeText("Detail", new Vector3(0f, 0.45f, 0f), "",
                new Color(0.7f, 0.9f, 1f));

            var close = new Row { isClose = true };
            close.holder = MakeText("Close", new Vector3(0f, -1.6f, 0f), "[ close ]",
                new Color(1f, 0.6f, 0.6f)).gameObject;
            m_Rows.Add(close);
        }

        private void OnListBound(IReadOnlyList<UIListEntry> entries)
        {
            for (int i = m_Rows.Count - 1; i >= 0; i--)
            {
                if (m_Rows[i].isClose)
                    continue;
                if (m_Rows[i].holder != null)
                    Destroy(m_Rows[i].holder);
                m_Rows.RemoveAt(i);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                UIListEntry entry = entries[i];
                TextMesh mesh = MakeText("Row " + entry.itemId,
                    new Vector3(0f, -i * rowHeight, 0f),
                    "> " + entry.label + "  x" + entry.count, Color.white);
                m_Rows.Add(new Row { holder = mesh.gameObject, itemId = entry.itemId });
            }
        }

        private void OnDetailBound(string text)
        {
            if (m_Detail != null)
                m_Detail.text = text;
        }

        private TextMesh MakeText(string goName, Vector3 localPosition, string text, Color color)
        {
            var holder = new GameObject(goName);
            holder.transform.SetParent(screen.visualRoot.transform, false);
            holder.transform.localPosition = localPosition;
            var mesh = holder.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = m_Font;
            mesh.fontSize = 48;
            mesh.characterSize = 0.08f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = color;
            var renderer = holder.GetComponent<MeshRenderer>();
            if (m_Font != null && renderer != null)
                renderer.sharedMaterial = m_Font.material;
            return mesh;
        }
    }
}
