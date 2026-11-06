using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// "Visit: Hall_2 ▾" in the Scene view: the visit of the open level the scene shows and the Level Manifest edits —
    /// the same pick (<see cref="LevelVisitsEditor"/>), swappable here. "—" when the open scene is no level in an episode.
    /// </summary>
    [Overlay(typeof(SceneView), k_Id, "Level Visit", defaultDisplay = true, defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top, defaultLayout = Layout.Panel)]
    internal sealed class LevelVisitOverlay : Overlay
    {
        private const string k_Id = "Scene View/Draw To Play Level Visit";
        public const string ButtonClass = "dtp-visit-overlay";

        private static readonly List<LevelVisitOverlay> s_Shown = new List<LevelVisitOverlay>();

        private VisualElement m_Root;
        private LevelContent m_Level;
        private string m_LevelFor;
        private string m_PickShown;
        private List<LevelVisitsEditor.Visit> m_Visits;

        /// <summary>The open scene's path, as the overlay sees it; a test points it at a scene path of its own.</summary>
        internal Func<string> openScene = () => SceneManager.GetActiveScene().path;

        /// <summary>For tests: what the overlay says, and the visits its menu offers.</summary>
        internal string text { get; private set; } = "";

        internal LevelContent level => m_Level;

        internal List<string> offered => m_Visits?.ConvertAll(v => v.label) ?? new List<string>();

        public override void OnCreated()
        {
            s_Shown.Add(this);
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.projectChanged += Forget;
            Undo.undoRedoPerformed += Rebuild;
        }

        public override void OnWillBeDestroyed()
        {
            s_Shown.Remove(this);
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.projectChanged -= Forget;
            Undo.undoRedoPerformed -= Rebuild;
        }

        public override VisualElement CreatePanelContent()
        {
            m_Root = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            Rebuild();
            return m_Root;
        }

        /// <summary>Every overlay shown follows a new pick.</summary>
        internal static void Refresh()
        {
            foreach (LevelVisitOverlay overlay in s_Shown)
                overlay.Rebuild();
        }

        // Checked on the Scene view's own layout rather than trusted to scene events: a scene loaded in Play mode, a
        // scene restored after it and a new scene saved under a level's path raise none of the edit-mode ones.
        private void OnSceneGui(SceneView view)
        {
            if (Event.current.type == EventType.Layout)
                Follow();
        }

        /// <summary>Rebuilt when the open scene or its level's pick is no longer the one shown — two string compares.</summary>
        internal void Follow()
        {
            if ((openScene() ?? "") != m_LevelFor || LevelVisitsEditor.Picked(m_Level) != m_PickShown)
                Rebuild();
        }

        private void Forget()
        {
            m_LevelFor = null;
            Rebuild();
        }

        private void Rebuild()
        {
            if (m_Root == null)
                return;
            m_Root.Clear();
            string scene = openScene() ?? "";
            if (m_LevelFor != scene)
            {
                // Once per scene: the search walks the project.
                m_LevelFor = scene;
                m_Level = LevelSetups.LevelOfScene(scene);
            }
            m_PickShown = LevelVisitsEditor.Picked(m_Level);
            m_Visits = m_Level != null ? LevelVisitsEditor.Of(m_Level) : null;
            if (m_Visits == null || m_Visits.Count == 0)
            {
                text = "Visit: —";
                m_Root.Add(new Label(text) { tooltip = "The open scene is no level in an episode.", style = { opacity = 0.6f } });
                return;
            }
            List<LevelVisitsEditor.Visit> visits = m_Visits;
            LevelVisitsEditor.Visit picked = LevelVisitsEditor.PickedOf(m_Level, visits);
            LevelContent level = m_Level;
            text = "Visit: " + picked.label;
            Button choose = null;
            choose = new Button(() => LevelManifestWindow.VisitMenu(level, visits, picked).DropDown(choose.worldBound))
            {
                text = text + "  ▾",
                tooltip = "The visit of " + level.name + " the scene shows and the Level Manifest edits."
            };
            choose.AddToClassList(ButtonClass);
            m_Root.Add(choose);
        }
    }
}
