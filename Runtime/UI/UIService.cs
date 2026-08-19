using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The screen registry — how a state names a screen and gets the live one (brief §3.5's
    /// UI tree needs an address book, nothing more). Deliberately NOT a window manager: it
    /// holds no stack, no focus, no "current screen", because WHAT IS OPEN IS WHICH STATE IS
    /// ACTIVE — the tree already owns that, and a second copy here would drift from it.
    ///
    /// The M9 service shape throughout: connects by placement (put it beside the Player host
    /// for per-player UI, on Root for shared UI), order-free adoption
    /// (<see cref="AdoptStrays"/> + screen self-registration, both idempotent), duplicate ids
    /// keep the last and warn with both names.
    /// </summary>
    public sealed class UIService : StateTreeService
    {

        /// <summary>Built by its scope's installer (M33) — the older screen service, kept for
        /// the demos that still use it.</summary>
        public UIService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
            AdoptStrays();
        }

        private readonly Dictionary<string, UIScreenBehaviour> m_ById =
            new Dictionary<string, UIScreenBehaviour>(StringComparer.Ordinal);
        private readonly List<UIScreenBehaviour> m_Screens = new List<UIScreenBehaviour>();

        public int registeredCount => m_Screens.Count;



        public void Register(UIScreenBehaviour screen)
        {
            if (screen == null || m_Screens.Contains(screen))
                return;

            m_Screens.Add(screen);
            if (!string.IsNullOrEmpty(screen.screenId))
            {
                if (m_ById.TryGetValue(screen.screenId, out UIScreenBehaviour holder)
                    && holder != null && !ReferenceEquals(holder, screen))
                {
                    Debug.LogWarning("UIService: screen id '" + screen.screenId + "' collides — '"
                        + holder.name + "' replaced by '" + screen.name + "'.");
                }
                m_ById[screen.screenId] = screen;
            }
            screen.MarkRegistered(this);
        }

        public void Unregister(UIScreenBehaviour screen)
        {
            if (screen == null || !m_Screens.Remove(screen))
                return;
            if (!string.IsNullOrEmpty(screen.screenId)
                && m_ById.TryGetValue(screen.screenId, out UIScreenBehaviour held)
                && ReferenceEquals(held, screen))
                m_ById.Remove(screen.screenId);
        }

        public void AdoptStrays()
        {
            UIScreenBehaviour[] strays = UnityEngine.Object
                .FindObjectsByType<UIScreenBehaviour>(FindObjectsInactive.Exclude);
            for (int i = 0; i < strays.Length; i++)
            {
                if (!strays[i].isRegistered)
                    Register(strays[i]);
            }
        }

        public UIScreenBehaviour Find(string screenId)
        {
            if (string.IsNullOrEmpty(screenId)
                || !m_ById.TryGetValue(screenId, out UIScreenBehaviour found) || found == null)
                return null;
            return found;
        }
    }
}
