using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// NEW LEVEL through the game's setup: a name, a place (the setup's word for it — "Episode"), one button. What
    /// the level catalogue's creator panel, the Level Manifest with no level open, and a mission node's "New level…"
    /// all show.
    /// </summary>
    internal sealed class NewLevelBox : VisualElement
    {
        public const string NameClass = "dtp-new-level-name";
        public const string PlaceClass = "dtp-new-level-place";

        private readonly ILevelSetup m_Setup;
        private readonly Action<LevelDef> m_Made;
        private readonly TextField m_Name;
        private readonly DropdownField m_Place;
        private readonly Label m_Report;

        public NewLevelBox(ILevelSetup setup, string place, Action<LevelDef> made)
        {
            m_Setup = setup;
            m_Made = made;

            m_Name = new TextField("Name") { value = "" };
            m_Name.AddToClassList(NameClass);
            m_Name.tooltip = "The level's name: its row, and the stem of every file made for it.";
            Add(m_Name);

            var places = new List<string>(setup.Places() ?? Array.Empty<string>());
            int index = Mathf.Max(0, string.IsNullOrEmpty(place) ? 0 : places.IndexOf(place));
            m_Place = new DropdownField(setup.placeLabel, places, places.Count > 0 ? index : -1);
            m_Place.AddToClassList(PlaceClass);
            m_Place.tooltip = "Where the level is made, and whose story it joins.";
            Add(m_Place);

            var create = new Button(Create) { text = "Create level" };
            create.style.alignSelf = Align.FlexStart;
            create.style.marginTop = 4f;
            create.tooltip = "Makes the level's scene and data, adds its row, and opens it. Its episode entry and its "
                + "tree are made when the first object is placed.";
            Add(create);

            m_Report = new Label("");
            m_Report.style.whiteSpace = WhiteSpace.Normal;
            m_Report.style.opacity = 0.75f;
            m_Report.style.marginTop = 2f;
            Add(m_Report);
        }

        private void Create()
        {
            LevelDef made = LevelSetups.CreateAndOpen(m_Name.value, m_Place.value, out string report);
            m_Report.text = report;
            m_Report.style.color = made != null ? new Color(0.55f, 0.85f, 0.6f) : new Color(1f, 0.5f, 0.45f);
            if (made == null)
                return;
            m_Name.value = "";
            m_Made?.Invoke(made);
        }
    }

    /// <summary>The <see cref="NewLevelBox"/> in a small window of its own, closed once the level is made.</summary>
    internal sealed class NewLevelPrompt : EditorWindow
    {
        [SerializeField] private string m_Place;

        internal static void Show(string place)
        {
            var window = CreateInstance<NewLevelPrompt>();
            window.titleContent = new GUIContent("New Level");
            window.m_Place = place;
            window.minSize = new Vector2(320f, 110f);
            window.maxSize = new Vector2(640f, 160f);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            ILevelSetup setup = LevelSetups.current;
            rootVisualElement.style.paddingLeft = 8f;
            rootVisualElement.style.paddingRight = 8f;
            rootVisualElement.style.paddingTop = 8f;
            if (setup == null)
            {
                rootVisualElement.Add(new Label("No level setup is registered."));
                return;
            }
            rootVisualElement.Add(new NewLevelBox(setup, m_Place, _ => Close()));
        }
    }
}
