using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>One row a screen's list shows — id for the wiring, label and count for the
    /// human. A struct the binding task fills from the registry, so views never look items
    /// up themselves.</summary>
    [Serializable]
    public struct UIListEntry
    {
        public string itemId;
        public string label;
        public int count;
    }

    /// <summary>
    /// One screen the UI TREE can open — the seam between behavior and pixels (brief §3.5).
    /// The tree side is fixed and framework-free: <see cref="Show"/>/<see cref="Hide"/> from
    /// the state that owns the screen, <c>Bind*</c> pushing content in, and the
    /// <c>Report*</c> methods a VIEW calls when the human interacts — which surface to the
    /// tree as task results (<see cref="ShowScreenTask"/>: click completes with the item id
    /// as an output, close completes the other way). What actually renders is whatever view
    /// subscribes to the events — a TextMesh demo view, UI Toolkit later, or a test calling
    /// Report* directly; the tree cannot tell the difference, which is the point.
    ///
    /// VISIBILITY TOGGLES A CHILD, never this GameObject: the component must stay enabled
    /// while hidden or the registry would lose the screen the moment it closed and no state
    /// could ever open it again. With no <see cref="visualRoot"/> assigned, Show/Hide only
    /// track <see cref="isVisible"/> and raise the event — the code-driven-view case.
    /// </summary>
    public sealed class UIScreenBehaviour : MonoBehaviour
    {
        /// <summary>How trees name this screen — ordinal, unique per UI service scope.</summary>
        public string screenId = "";

        /// <summary>The CHILD object holding the visuals, toggled by Show/Hide. Never this
        /// component's own GameObject (see class note).</summary>
        public GameObject visualRoot;

        public event Action<string> itemClicked;
        public event Action closeRequested;
        public event Action<IReadOnlyList<UIListEntry>> listBound;
        public event Action<string> detailBound;
        public event Action<bool> visibilityChanged;

        private readonly List<UIListEntry> m_Entries = new List<UIListEntry>();
        private UIService m_RegisteredWith;
        private bool m_Visible;

        public bool isVisible => m_Visible;

        /// <summary>Whether some UI service holds this screen — the adoption sweep's
        /// question.</summary>
        public bool isRegistered => m_RegisteredWith != null;

        /// <summary>The last bound list — a view that appears late still has content.</summary>
        public IReadOnlyList<UIListEntry> entries => m_Entries;

        private void OnEnable()
        {
            RegisterToUI();
        }

        private void OnDisable()
        {
            UnregisterFromUI();
        }

        public void RegisterToUI()
        {
            if (m_RegisteredWith != null)
                return;
            UIService service = StateTreeContextHost.FindService<UIService>(gameObject);
            if (service == null)
                return;
            service.Register(this);
            m_RegisteredWith = service;
        }

        public void UnregisterFromUI()
        {
            if (m_RegisteredWith == null)
                return;
            m_RegisteredWith.Unregister(this);
            m_RegisteredWith = null;
        }

        internal void MarkRegistered(UIService service)
        {
            m_RegisteredWith = service;
        }

        // --- the tree side ---------------------------------------------------------------

        public void Show()
        {
            m_Visible = true;
            if (visualRoot != null)
                visualRoot.SetActive(true);
            visibilityChanged?.Invoke(true);
        }

        public void Hide()
        {
            m_Visible = false;
            if (visualRoot != null)
                visualRoot.SetActive(false);
            visibilityChanged?.Invoke(false);
        }

        public void BindList(List<UIListEntry> newEntries)
        {
            m_Entries.Clear();
            if (newEntries != null)
                m_Entries.AddRange(newEntries);
            listBound?.Invoke(m_Entries);
        }

        public void SetDetail(string text)
        {
            detailBound?.Invoke(text ?? "");
        }

        // --- the view side ---------------------------------------------------------------

        /// <summary>A view reports a row click; the running <see cref="ShowScreenTask"/> turns
        /// it into a Success with the id as a routed output. Ignored while hidden — a stale
        /// view cannot drive a closed screen.</summary>
        public void ReportItemClick(string itemId)
        {
            if (m_Visible)
                itemClicked?.Invoke(itemId);
        }

        public void ReportClose()
        {
            if (m_Visible)
                closeRequested?.Invoke();
        }
    }
}
