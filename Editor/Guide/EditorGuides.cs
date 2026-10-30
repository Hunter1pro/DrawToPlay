using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The guides a game registered (from its <c>[InitializeOnLoad]</c> class), and the "?" the package's windows
    /// show while there is one.
    /// </summary>
    public static class EditorGuides
    {
        public const string HelpClass = "dtp-guide-help";

        private static readonly List<EditorGuide> s_Guides = new List<EditorGuide>();

        public static IReadOnlyList<EditorGuide> all => s_Guides;

        /// <summary>Replaces a guide registered under the same id.</summary>
        public static void Register(EditorGuide guide)
        {
            if (guide == null)
                return;
            s_Guides.RemoveAll(g => g.id == guide.id);
            s_Guides.Add(guide);
        }

        public static void Unregister(EditorGuide guide) => s_Guides.Remove(guide);

        public static EditorGuide Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                EditorGuide found = s_Guides.Find(g => g.id == id);
                if (found != null)
                    return found;
            }
            return s_Guides.Count > 0 ? s_Guides[0] : null;
        }

        /// <summary>Per user and per project, and never committed.</summary>
        public static string ProgressPath(string guideId) => "UserSettings/Guides/" + guideId + ".json";

        /// <summary>A "?" that opens the guide; hidden while none is registered.</summary>
        internal static Button HelpButton(bool toolbar)
        {
            Button button = toolbar
                ? new ToolbarButton(() => EditorGuideWindow.Open())
                : new Button(() => EditorGuideWindow.Open());
            button.text = "?";
            button.AddToClassList(HelpClass);
            button.style.flexShrink = 0f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            EditorGuide guide = Find(null);
            button.tooltip = guide != null ? "Open " + guide.title + ": step by step, with checks." : "";
            button.style.display = guide != null ? DisplayStyle.Flex : DisplayStyle.None;
            return button;
        }
    }

    /// <summary>What a step's Show me reaches for: the package's windows, a node, an asset, a control.</summary>
    public static class GuideShow
    {
        public const string LevelManifestTitle = "Level Manifest";
        public const string StateTreeTitle = "State Tree";

        private const double HighlightSeconds = 3.5;
        private static double s_StopAt;

        public static bool levelManifestOpen => EditorWindow.HasOpenInstances<LevelManifestWindow>();

        public static bool stateTreeOpen => EditorWindow.HasOpenInstances<StateTreeEditorWindow>();

        public static void LevelManifest() => LevelManifestWindow.Open();

        /// <summary>The tree in its schema's mode, with <paramref name="node"/> selected when given.</summary>
        public static StateTreeEditorWindow StateTree(StateTreeAsset tree, StateTreeNodeAsset node = null)
        {
            if (tree == null)
                return null;
            StateTreeEditorWindow window = StateTreeEditorWindow.MissionWindows().Find(open => open.treeAsset == tree)
                ?? StateTreeEditorWindow.Open(tree);
            if (window.treeAsset != tree)
                window.LoadTree(tree);
            if (!window.missionMode && window.schema != null)
                window.SetDevMode(false);
            // Selecting only: Added() would save the tree, and a Show me never writes.
            if (node != null)
                window.SelectNode(node.nodeId);
            window.Focus();
            return window;
        }

        /// <summary>Selects and pings an asset or a scene object.</summary>
        public static void Ping(Object target)
        {
            if (target == null)
                return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        /// <summary>
        /// Pulses the control showing <paramref name="text"/> in the open window titled <paramref name="windowTitle"/>
        /// for a few seconds (Unity's <see cref="Highlighter"/>). False when the window or the control is not there:
        /// the caller falls back to selecting or pinging.
        /// </summary>
        public static bool Highlight(string windowTitle, string text)
        {
            if (string.IsNullOrEmpty(text) || !IsOpen(windowTitle))
                return false;
            bool found;
            try
            {
                found = Highlighter.Highlight(windowTitle, text, HighlightSearchMode.Auto);
            }
            catch (System.Exception)
            {
                found = false;
            }
            if (!found)
            {
                Highlighter.Stop();
                return false;
            }
            if (s_StopAt <= 0)
                EditorApplication.update += StopLater;
            s_StopAt = EditorApplication.timeSinceStartup + HighlightSeconds;
            return true;
        }

        /// <summary>As <see cref="Highlight"/>, once a window just opened or rebuilt has drawn; <paramref name="fallback"/>
        /// runs when the control is not there.</summary>
        public static void HighlightSoon(string windowTitle, string text, System.Action fallback = null)
        {
            double at = EditorApplication.timeSinceStartup + 0.3;
            void Try()
            {
                if (EditorApplication.timeSinceStartup < at)
                    return;
                EditorApplication.update -= Try;
                if (!Highlight(windowTitle, text))
                    fallback?.Invoke();
            }
            EditorApplication.update += Try;
        }

        private static bool IsOpen(string windowTitle)
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window != null && window.titleContent != null && window.titleContent.text == windowTitle)
                    return true;
            }
            return false;
        }

        private static void StopLater()
        {
            if (EditorApplication.timeSinceStartup < s_StopAt)
                return;
            EditorApplication.update -= StopLater;
            s_StopAt = 0;
            Highlighter.Stop();
        }
    }
}
