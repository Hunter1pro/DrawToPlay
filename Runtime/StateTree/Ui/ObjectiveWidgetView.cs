using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE QUEST LINE ON SCREEN — the current objective's name (with progress where
    /// counting means something) and the navigation arrows: for every target that is off
    /// camera, a pointer sits on the screen edge aimed at it (<see cref="OffscreenIcon"/>);
    /// one that comes on camera drops its pointer, because a pointer at something you can
    /// already see is noise. A UI ROW's view (shown by the session tree through the UI
    /// service), reading the LEVEL's objective service — a ridge with no objectives simply
    /// shows nothing.
    ///
    /// BOTH ARE BUTTONS. Tapping the line asks to be shown the nearest target; tapping a
    /// pointer asks for THAT one, which is the whole reason there is a pointer per target
    /// rather than one that keeps changing its mind. The view asks
    /// <see cref="ObjectiveService.Focus"/> and stops there: what "show me" does to a camera
    /// is the game's business, and this file has never heard of one.
    /// </summary>
    [AddComponentMenu("Draw To Play/UI/Objective Widget")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ObjectiveWidgetView : UiViewBehaviour
    {
        private Label m_Name;
        private Label m_Zone;
        private VisualElement m_Root;

        /// <summary>One pointer per off-screen target, grown as a level needs them and kept
        /// afterwards — a list of six raiders becomes six labels once, not six every tick.</summary>
        private readonly List<Label> m_Arrows = new List<Label>();

        /// <summary>What each pointer is pointing AT, by index — the answer a tap needs, and
        /// the reason the pointers are not interchangeable.</summary>
        private readonly List<WorldObjectBehaviour> m_Pointed = new List<WorldObjectBehaviour>();

        private readonly List<WorldObjectBehaviour> m_Targets = new List<WorldObjectBehaviour>();

        private ObjectiveService m_Wired;
        private string m_FlashText;
        private float m_FlashUntil;

        private static readonly Color k_FlashColor = new Color(0.55f, 0.9f, 0.55f);

        private void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_Root.pickingMode = PickingMode.Ignore;

            m_Zone = new Label("");
            // THE BANNER IS A BUTTON (HT's SubscribeSearch): the two lines at the top of the
            // screen are the one thing on it that always refers to somewhere, so tapping them
            // asks to be shown it. Position picking, not Ignore, or the tap goes through to
            // the joystick's zone underneath.
            m_Zone.pickingMode = PickingMode.Position;
            m_Zone.RegisterCallback<ClickEvent>(_ => FocusNearest());
            m_Zone.style.position = Position.Absolute;
            m_Zone.style.top = 4f;
            m_Zone.style.left = 0f;
            m_Zone.style.right = 0f;
            m_Zone.style.unityTextAlign = TextAnchor.UpperCenter;
            m_Zone.style.fontSize = 11f;
            m_Zone.style.color = new Color(1f, 1f, 1f, 0.55f);
            m_Root.Add(m_Zone);

            m_Name = new Label("");
            m_Name.pickingMode = PickingMode.Position;
            m_Name.RegisterCallback<ClickEvent>(_ => FocusNearest());
            m_Name.style.position = Position.Absolute;
            m_Name.style.top = 20f;
            m_Name.style.left = 0f;
            m_Name.style.right = 0f;
            m_Name.style.unityTextAlign = TextAnchor.UpperCenter;
            m_Name.style.fontSize = 16f;
            m_Name.style.color = new Color(0.95f, 0.92f, 0.75f);
            m_Name.style.unityFontStyleAndWeight = FontStyle.Bold;
            m_Root.Add(m_Name);

        }

        /// <summary>One more pointer, built the first time a level needs it. Each carries its
        /// own INDEX into <see cref="m_Pointed"/> rather than a captured target, so a pointer
        /// re-aimed at somebody else next tick still taps the right body.</summary>
        private Label MakeArrow(int index)
        {
            var arrow = new Label("➤");
            // A TAP TARGET, not just a glyph: the label is padded out to something a thumb can
            // hit, which is why the position maths below offsets by half of it.
            arrow.pickingMode = PickingMode.Position;
            arrow.style.position = Position.Absolute;
            arrow.style.fontSize = 26f;
            arrow.style.width = k_ArrowSize;
            arrow.style.height = k_ArrowSize;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.color = new Color(0.95f, 0.92f, 0.75f);
            arrow.style.display = DisplayStyle.None;
            arrow.RegisterCallback<ClickEvent>(_ => FocusPointed(index));
            m_Root.Add(arrow);
            return arrow;
        }

        private const float k_ArrowSize = 44f;

        /// <summary>The banner's tap: show the nearest thing the row is about.</summary>
        private void FocusNearest()
        {
            m_Wired?.Focus(null);
        }

        /// <summary>A pointer's tap: show the one THIS pointer means, which is the difference
        /// between six raiders and "a raider, somewhere".</summary>
        private void FocusPointed(int index)
        {
            if (m_Wired == null || index < 0 || index >= m_Pointed.Count)
                return;
            WorldObjectBehaviour target = m_Pointed[index];
            if (target != null)
                m_Wired.Focus(target);
        }

        private void HideArrowsFrom(int index)
        {
            for (int i = index; i < m_Arrows.Count; i++)
                m_Arrows[i].style.display = DisplayStyle.None;
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
                HideArrowsFrom(0);
                return;
            }

            ObjectiveDef current = service != null ? service.current : null;
            if (current == null)
            {
                m_Name.text = "";
                m_Zone.text = "";
                HideArrowsFrom(0);
                return;
            }

            ZoneDef zoneRow = service.activeZoneRow;
            m_Zone.text = zoneRow != null && zoneRow.asset != null
                ? (string.IsNullOrEmpty(zoneRow.asset.displayName)
                    ? zoneRow.asset.name : zoneRow.asset.displayName)
                : "";
            m_Name.style.color = current.accentColor;

            var counted = current.kind == ObjectiveKind.EnemyKill
                || current.kind == ObjectiveKind.Pickup;
            m_Name.text = string.IsNullOrEmpty(current.displayName)
                ? current.name
                : current.displayName;
            if (counted && current.count > 1)
                m_Name.text += "  " + service.progress + " / " + current.count;

            UpdateArrows(service, current);
        }

        /// <summary>
        /// A POINTER PER TARGET, off-screen ones only — HT's enemy indicators, which build a
        /// state per model item, skip the ones already in view, and hand each its own identity
        /// so a tap can mean "that one".
        /// </summary>
        private void UpdateArrows(ObjectiveService service, ObjectiveDef current)
        {
            Camera camera = Camera.main;
            float width = m_Root.resolvedStyle.width;
            float height = m_Root.resolvedStyle.height;
            if (camera == null || width <= 0f || height <= 0f)
            {
                HideArrowsFrom(0);
                return;
            }

            service.CurrentTargets(m_Targets);
            m_Pointed.Clear();

            string glyph = string.IsNullOrEmpty(current.arrowGlyph) ? "➤" : current.arrowGlyph;
            int shown = 0;
            for (int i = 0; i < m_Targets.Count && shown < k_MaxArrows; i++)
            {
                WorldObjectBehaviour target = m_Targets[i];
                if (target == null)
                    continue;

                Vector3 viewport = camera.WorldToViewportPoint(target.transform.position);
                if (!OffscreenIcon.Resolve(viewport, 0.06f, out Vector2 anchor, out float angle))
                    continue;   // in view already: the eye does not need help

                while (m_Arrows.Count <= shown)
                    m_Arrows.Add(MakeArrow(m_Arrows.Count));

                Label arrow = m_Arrows[shown];
                arrow.text = glyph;
                arrow.style.color = current.accentColor;
                arrow.style.display = DisplayStyle.Flex;
                arrow.style.left = anchor.x * width - k_ArrowSize * 0.5f;
                // Viewport y is up; the panel's y is down.
                arrow.style.top = (1f - anchor.y) * height - k_ArrowSize * 0.5f;
                arrow.style.rotate = new Rotate(-angle);

                m_Pointed.Add(target);
                shown++;
            }

            HideArrowsFrom(shown);
        }

        /// <summary>A ceiling, because a screen edge crowded with twenty pointers points at
        /// nothing. Nearest-first ordering means the ones dropped are the far ones.</summary>
        private const int k_MaxArrows = 8;

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
