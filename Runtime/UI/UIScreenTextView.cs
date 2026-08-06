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

        /// <summary>Hit width around each row's center. Sized to the TEXT, not the panel: a
        /// rect much wider than what the eye aims at silently claims off-target clicks for
        /// whichever row happens to share the height.</summary>
        public float rowWidth = 3f;

        /// <summary>Log every click this view judges — pixel position, world position, and the
        /// verdict — the deep-log rule applied to picking, because a wrong hit is invisible
        /// exactly when it matters. The demo builder turns it on.</summary>
        public bool debugClicks;

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
            if (m_FlashMesh != null && Time.time >= m_FlashUntil)
            {
                m_FlashMesh.color = m_FlashRestore;
                m_FlashMesh = null;
            }

            if (screen == null || !screen.isVisible)
                return;
            if (TryReadClick(out Vector2 screenPosition))
                HandleClick(screenPosition);
        }

        /// <summary>
        /// Judge one click at a game-view pixel position — public so a probe can drive the
        /// EXACT shipping path with synthetic positions. The hit row FLASHES, which turns
        /// "clicks feel wrong" from a guess into an observation: the flash is what the view
        /// decided, wherever the press seemed to land.
        /// </summary>
        public void HandleClick(Vector2 screenPosition)
        {
            Camera camera = Camera.main;
            if (camera == null || screen == null)
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

                if (debugClicks)
                {
                    Debug.Log("[UIView " + screen.screenId + "] click px=" + screenPosition
                        + " world=(" + world.x.ToString("F2") + "," + world.y.ToString("F2")
                        + ") -> " + (row.isClose ? "[close]" : "'" + row.itemId + "'"), this);
                }
                Flash(row.holder);

                if (row.isClose)
                    screen.ReportClose();
                else
                    screen.ReportItemClick(row.itemId);
                return;
            }

            if (debugClicks)
            {
                Debug.Log("[UIView " + screen.screenId + "] click px=" + screenPosition
                    + " world=(" + world.x.ToString("F2") + "," + world.y.ToString("F2")
                    + ") -> miss (no row there)", this);
            }
        }

        private TextMesh m_FlashMesh;
        private Color m_FlashRestore;
        private float m_FlashUntil;

        /// <summary>Tint the judged row for a beat. One row at a time — a second click restores
        /// the first before flashing the next, so colors cannot stick.</summary>
        private void Flash(GameObject rowHolder)
        {
            if (m_FlashMesh != null)
                m_FlashMesh.color = m_FlashRestore;
            TextMesh mesh = rowHolder != null ? rowHolder.GetComponent<TextMesh>() : null;
            if (mesh == null)
                return;
            m_FlashRestore = mesh.color;
            mesh.color = new Color(1f, 0.85f, 0.2f);
            m_FlashMesh = mesh;
            m_FlashUntil = Time.time + 0.25f;
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
