using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Stops a Graph Toolkit window from opening with NOTHING but its canvas — the failure mode
    /// that has no way out from inside the editor.
    ///
    /// WHAT GOES WRONG. Every panel and toolbar of a graph window (Asset Management, Breadcrumbs,
    /// Blackboard, Graph Inspector, Options, Panel Toggles, Error Notifications) is an
    /// <see cref="Overlay"/>, and their visibility is saved PER WINDOW TYPE — not per project —
    /// in <c>~/Library/Preferences/Unity/Editor-5.x/Overlays/CanvasesSaveData.asset</c>. Once that
    /// file records <c>displayed: 0</c> for all of them, every graph window opened afterwards,
    /// in every project, comes up as a bare canvas with a right-click menu and nothing else.
    ///
    /// WHY IT NEEDS CODE TO RECOVER. The affordances that would turn a panel back on ARE
    /// overlays: the Panel Toggles toolbar and the Options menu. When the saved state hides
    /// everything it hides its own undo, and the only survivor is the ` overlay menu — a
    /// shortcut nobody finds while staring at an empty window. So the guard restores the set the
    /// overlays themselves declare as default (<c>OverlayAttribute.defaultDisplay</c>, which is
    /// every one of them except the MiniMap).
    ///
    /// WHY IT FIRES ONCE PER WINDOW. Repairing on a timer would fight the author: closing the
    /// Blackboard is a legitimate thing to want. The guard therefore inspects each window the
    /// FIRST time it sees it and only acts on the unrecoverable state — not one panel hidden,
    /// but every last overlay hidden. After that the window is left alone forever.
    ///
    /// WHY IT NAMES THE WINDOW TYPE AS A STRING. <c>GraphViewEditorWindowImp</c> is internal, so
    /// it cannot be named at compile time (the same wall <see cref="GraphEditor"/>'s highlight
    /// hits). Matching on the full type name costs nothing here and keeps this file out of the
    /// Graph Toolkit firewall assembly — it uses only the public <see cref="Overlay"/> API.
    /// </summary>
    [InitializeOnLoad]
    internal static class GraphWindowPanelGuard
    {
        /// <summary>Prefix on every message, so a console line says which system is talking.</summary>
        private const string k_Tag = "Draw To Play: ";

        /// <summary>The window this guards, by full type name — see the class remarks for why it
        /// is a string and not a <c>typeof</c>.</summary>
        private const string k_GraphWindowTypeName =
            "Unity.GraphToolkit.Editor.Implementation.GraphViewEditorWindowImp";

        /// <summary>How often the guard looks for windows it has not seen yet. Graph windows are
        /// opened by hand, so a lazy scan is enough and keeps a FindObjectsOfTypeAll out of the
        /// editor's per-frame work.</summary>
        private const double k_ScanInterval = 1.0;

        /// <summary>The windows already inspected, so a deliberate "close the Blackboard" is
        /// never undone on the next scan. Keyed by the window OBJECT rather than its instance id
        /// — <c>Object.GetInstanceID</c> is obsolete-as-an-error in 6000.5 — and swept of closed
        /// windows on each scan so a long session cannot grow it without bound.</summary>
        private static readonly HashSet<EditorWindow> s_Seen = new HashSet<EditorWindow>();

        private static double s_NextScan;

        static GraphWindowPanelGuard()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < s_NextScan)
                return;
            s_NextScan = EditorApplication.timeSinceStartup + k_ScanInterval;

            s_Seen.RemoveWhere(seen => seen == null);

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.GetType().FullName != k_GraphWindowTypeName)
                    continue;
                if (!s_Seen.Add(window))
                    continue;
                RestoreIfBlank(window);
            }
        }

        /// <summary>
        /// Turn the default overlays back on for one window, but only when it has NONE showing.
        /// </summary>
        /// <param name="window">A graph window, freshly seen by the scan.</param>
        /// <returns>How many overlays were turned back on; 0 when the window was already fine.</returns>
        internal static int RestoreIfBlank(EditorWindow window)
        {
            List<Overlay> overlays = OverlaysOf(window);
            if (overlays == null || overlays.Count == 0)
                return 0;

            for (int i = 0; i < overlays.Count; i++)
            {
                // One panel showing means the author still has a way to reach the rest, so their
                // arrangement is theirs to keep.
                if (overlays[i].displayed)
                    return 0;
            }

            int restored = Restore(overlays);
            if (restored > 0)
            {
                window.Repaint();
                Debug.Log(k_Tag + "Graph window \"" + window.titleContent.text
                    + "\" opened with every panel hidden (a saved editor-preference state that hides"
                    + " its own toggles) — restored " + restored + " default panels.");
            }
            return restored;
        }

        /// <summary>Menu escape hatch for the same repair, for a window the once-per-window scan
        /// has already passed over — or one the author emptied by hand and wants back.</summary>
        [MenuItem("Tools/Draw To Play/Restore Graph Window Panels")]
        private static void RestoreAllMenuItem()
        {
            int windows = 0;
            int restored = 0;
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.GetType().FullName != k_GraphWindowTypeName)
                    continue;
                int turnedOn = Restore(OverlaysOf(window));
                if (turnedOn > 0)
                {
                    window.Repaint();
                    restored += turnedOn;
                }
                windows++;
            }

            if (windows == 0)
                Debug.Log(k_Tag + "Restore graph window panels: no graph window is open.");
            else
                Debug.Log(k_Tag + "Restore graph window panels: " + restored
                    + " panels turned back on across " + windows + " window(s).");
        }

        /// <summary>Show every overlay its own <see cref="OverlayAttribute"/> declares as a
        /// default (all of them but the MiniMap), and leave the rest as they are.</summary>
        /// <param name="overlays">The window's overlays, or null.</param>
        /// <returns>How many were switched from hidden to shown.</returns>
        private static int Restore(List<Overlay> overlays)
        {
            if (overlays == null)
                return 0;

            int restored = 0;
            for (int i = 0; i < overlays.Count; i++)
            {
                Overlay overlay = overlays[i];
                if (overlay == null || overlay.displayed || !DefaultDisplayOf(overlay))
                    continue;
                overlay.displayed = true;
                restored++;
            }
            return restored;
        }

        /// <summary>Whether an overlay is one of the ones meant to be on out of the box. The
        /// attribute's flag is not exposed as a property, hence the field read; an overlay whose
        /// attribute cannot be read is left alone rather than force-shown.</summary>
        private static bool DefaultDisplayOf(Overlay overlay)
        {
            var attribute = Attribute.GetCustomAttribute(overlay.GetType(), typeof(OverlayAttribute))
                as OverlayAttribute;
            if (attribute == null)
                return false;

            FieldInfo field = typeof(OverlayAttribute).GetField("m_DefaultDisplay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(attribute) is bool display && display;
        }

        /// <summary>The window's overlays. <c>EditorWindow.overlayCanvas</c> and the canvas's
        /// overlay list are both internal, so this is the one reflection hop the guard needs; a
        /// Unity version that moves them turns the guard off rather than throwing.</summary>
        private static List<Overlay> OverlaysOf(EditorWindow window)
        {
            if (window == null)
                return null;

            PropertyInfo canvasProperty = typeof(EditorWindow).GetProperty("overlayCanvas",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object canvas = canvasProperty?.GetValue(window);
            if (canvas == null)
                return null;

            FieldInfo overlaysField = canvas.GetType().GetField("m_Overlays",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(overlaysField?.GetValue(canvas) is IEnumerable<Overlay> overlays))
                return null;

            return new List<Overlay>(overlays);
        }
    }
}
