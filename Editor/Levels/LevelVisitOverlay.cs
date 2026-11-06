using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
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

        public override void OnCreated()
        {
            s_Shown.Add(this);
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.projectChanged += Forget;
            Undo.undoRedoPerformed += Rebuild;
        }

        public override void OnWillBeDestroyed()
        {
            s_Shown.Remove(this);
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
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

        private void OnSceneChanged(Scene from, Scene to) => Forget();

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => Forget();

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
            string scene = SceneManager.GetActiveScene().path ?? "";
            if (m_LevelFor != scene)
            {
                // Once per scene: the search walks the project.
                m_LevelFor = scene;
                m_Level = LevelSetups.LevelOfScene(scene);
            }
            List<LevelVisitsEditor.Visit> visits = m_Level != null ? LevelVisitsEditor.Of(m_Level) : null;
            if (visits == null || visits.Count == 0)
            {
                m_Root.Add(new Label("Visit: —") { tooltip = "The open scene is no level in an episode.", style = { opacity = 0.6f } });
                return;
            }
            LevelVisitsEditor.Visit picked = LevelVisitsEditor.PickedOf(m_Level, visits);
            LevelContent level = m_Level;
            Button choose = null;
            choose = new Button(() => LevelManifestWindow.VisitMenu(level, visits, picked).DropDown(choose.worldBound))
            {
                text = "Visit: " + picked.label + "  ▾",
                tooltip = "The visit of " + level.name + " the scene shows and the Level Manifest edits."
            };
            choose.AddToClassList(ButtonClass);
            m_Root.Add(choose);
        }
    }
}
