using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE QUEST LINE ON SCREEN — the current objective's name (with progress where
    /// counting means something) and the navigation arrow: when the objective's target is
    /// off camera, a pointer sits on the screen edge aimed at it (<see cref="OffscreenIcon"/>);
    /// on camera it stays out of the way. A UI ROW's view (shown by the session tree through
    /// the UI service), reading the LEVEL's objective service — a ridge with no objectives
    /// simply shows nothing.
    /// </summary>
    [AddComponentMenu("Draw To Play/UI/Objective Widget")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ObjectiveWidgetView : UiViewBehaviour
    {
        private Label m_Name;
        private Label m_Zone;
        private Label m_Arrow;
        private VisualElement m_Root;

        private ObjectiveService m_Wired;
        private string m_FlashText;
        private float m_FlashUntil;

        private static readonly Color k_FlashColor = new Color(0.55f, 0.9f, 0.55f);

        private void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_Root.pickingMode = PickingMode.Ignore;

            m_Zone = new Label("");
            m_Zone.pickingMode = PickingMode.Ignore;
            m_Zone.style.position = Position.Absolute;
            m_Zone.style.top = 4f;
            m_Zone.style.left = 0f;
            m_Zone.style.right = 0f;
            m_Zone.style.unityTextAlign = TextAnchor.UpperCenter;
            m_Zone.style.fontSize = 11f;
            m_Zone.style.color = new Color(1f, 1f, 1f, 0.55f);
            m_Root.Add(m_Zone);

            m_Name = new Label("");
            m_Name.pickingMode = PickingMode.Ignore;
            m_Name.style.position = Position.Absolute;
            m_Name.style.top = 20f;
            m_Name.style.left = 0f;
            m_Name.style.right = 0f;
            m_Name.style.unityTextAlign = TextAnchor.UpperCenter;
            m_Name.style.fontSize = 16f;
            m_Name.style.color = new Color(0.95f, 0.92f, 0.75f);
            m_Name.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Root.Add(m_Name);

            m_Arrow = new Label("➤");
            m_Arrow.pickingMode = PickingMode.Ignore;
            m_Arrow.style.position = Position.Absolute;
            m_Arrow.style.fontSize = 26f;
            m_Arrow.style.color = new Color(0.95f, 0.92f, 0.75f);
            m_Arrow.style.display = DisplayStyle.None;
            m_Root.Add(m_Arrow);
        }

        private void OnDisable()
        {
            if (m_Wired != null)
            {
                m_Wired.completedObjective -= OnCompleted;
                m_Wired = null;
            }
        }

        /// <summary>The completion BEAT (HT's checkmark moment): a green tick with the
        /// finished name holds the line for a second before the next ask takes it.</summary>
        private void OnCompleted(ObjectiveDef done)
        {
            m_FlashText = "✓  " + (string.IsNullOrEmpty(done.displayName)
                ? done.name : done.displayName);
            m_FlashUntil = Time.time + 1.2f;
        }

        private void Update()
        {
            ObjectiveService service = ResolveService();
            if (!ReferenceEquals(service, m_Wired))
            {
                if (m_Wired != null)
                    m_Wired.completedObjective -= OnCompleted;
                m_Wired = service;
                if (m_Wired != null)
                    m_Wired.completedObjective += OnCompleted;
            }

            if (Time.time < m_FlashUntil && m_FlashText != null)
            {
                m_Name.text = m_FlashText;
                m_Name.style.color = k_FlashColor;
                m_Arrow.style.display = DisplayStyle.None;
                return;
            }

            ObjectiveDef current = service != null ? service.current : null;
            if (current == null)
            {
                m_Name.text = "";
                m_Zone.text = "";
                m_Arrow.style.display = DisplayStyle.None;
                return;
            }

            ZoneDef zoneRow = service.activeZoneRow;
            m_Zone.text = zoneRow != null
                ? (string.IsNullOrEmpty(zoneRow.displayName) ? zoneRow.name : zoneRow.displayName)
                : "";
            m_Name.style.color = current.accentColor;
            m_Arrow.style.color = current.accentColor;
            m_Arrow.text = string.IsNullOrEmpty(current.arrowGlyph)
                ? "➤" : current.arrowGlyph;

            var counted = current.kind == ObjectiveKind.EnemyKill
                || current.kind == ObjectiveKind.Pickup;
            m_Name.text = string.IsNullOrEmpty(current.displayName)
                ? current.name
                : current.displayName;
            if (counted && current.count > 1)
                m_Name.text += "  " + service.progress + " / " + current.count;

            UpdateArrow(service);
        }

        private void UpdateArrow(ObjectiveService service)
        {
            Camera camera = Camera.main;
            Vector3? target = service.CurrentTargetPosition();
            if (camera == null || !target.HasValue)
            {
                m_Arrow.style.display = DisplayStyle.None;
                return;
            }

            Vector3 viewport = camera.WorldToViewportPoint(target.Value);
            if (!OffscreenIcon.Resolve(viewport, 0.06f, out Vector2 anchor, out float angle))
            {
                m_Arrow.style.display = DisplayStyle.None;
                return;
            }

            float width = m_Root.resolvedStyle.width;
            float height = m_Root.resolvedStyle.height;
            if (width <= 0f || height <= 0f)
                return;
            m_Arrow.style.display = DisplayStyle.Flex;
            m_Arrow.style.left = anchor.x * width - 13f;
            // Viewport y is up; the panel's y is down.
            m_Arrow.style.top = (1f - anchor.y) * height - 13f;
            m_Arrow.style.rotate = new Rotate(-angle);
        }

        private ObjectiveService ResolveService()
        {
            // THROUGH THE PLAYER, not the level: every spawned mind is a Level-kind host,
            // so 'the level' is ambiguous from a root-scoped view — but the PLAYER is
            // unique, and the service chain walked up from wherever the level put it finds
            // the level's objectives (the HUD's route to level-scoped services).
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(gameObject, StateTreeContextKind.Player);
            return player != null
                ? StateTreeContextHost.FindService<ObjectiveService>(player.gameObject)
                : null;
        }
    }
}
