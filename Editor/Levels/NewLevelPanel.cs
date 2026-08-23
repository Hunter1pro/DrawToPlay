using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE "NEW LEVEL" BOX on a level registry: a name, a group, a folder, the game's template,
    /// one button. Adding a row by hand is still there for a level that already exists as
    /// assets; this is for the one that does not yet.
    /// </summary>
    internal sealed class NewLevelPanel : VisualElement
    {
        private readonly LevelRegistry m_Levels;
        private readonly Action m_Changed;
        private readonly TextField m_Name;
        private readonly TextField m_Group;
        private readonly TextField m_Folder;
        private readonly DropdownField m_Template;
        private readonly Label m_Report;
        private readonly List<ILevelTemplate> m_Templates;

        public NewLevelPanel(LevelRegistry levels, Action changed)
        {
            m_Levels = levels;
            m_Changed = changed;
            m_Templates = LevelTemplates.All();

            style.marginTop = 4f;
            style.marginBottom = 8f;
            style.paddingLeft = 6f;
            style.paddingRight = 6f;
            style.paddingTop = 4f;
            style.paddingBottom = 6f;
            style.borderLeftWidth = 2f;
            style.borderLeftColor = new Color(0.4f, 0.7f, 0.5f, 0.6f);

            var title = new Label("New level — content, manifest, scene, row and build entry in one click");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            Add(title);

            m_Name = new TextField("Name") { value = "" };
            m_Name.tooltip = "The row's name — what a level.goto names, and the stem of every file.";
            Add(m_Name);

            m_Group = new TextField("Group") { value = FirstGroup(levels) };
            m_Group.tooltip = "Organisation only: the section this row sits in.";
            Add(m_Group);

            var folderRow = new VisualElement();
            folderRow.style.flexDirection = FlexDirection.Row;
            m_Folder = new TextField("In folder") { value = LevelFactory.DefaultFolder(levels) };
            m_Folder.style.flexGrow = 1f;
            m_Folder.tooltip = "Where the level's own folder is made — one folder per level, named "
                + "for it, holding its scene, content, manifest and whatever the template adds. "
                + "Defaults to the folder this registry's levels sit in.";
            folderRow.Add(m_Folder);
            var browse = new Button(Browse) { text = "…" };
            browse.style.width = 24f;
            folderRow.Add(browse);
            Add(folderRow);

            var names = new List<string>();
            for (int i = 0; i < m_Templates.Count; i++)
                names.Add(m_Templates[i].title);
            if (names.Count == 0)
                names.Add("(no template — a bare level with its host)");
            m_Template = new DropdownField("Template", names, 0);
            m_Template.tooltip = "What fills the scene: the game's recipe for a place. Found by type "
                + "— implement ILevelTemplate to offer one.";
            Add(m_Template);

            var create = new Button(Create) { text = "Create level" };
            create.style.alignSelf = Align.FlexStart;
            create.style.marginTop = 4f;
            Add(create);

            m_Report = new Label("");
            m_Report.style.whiteSpace = WhiteSpace.Normal;
            m_Report.style.opacity = 0.75f;
            m_Report.style.marginTop = 2f;
            Add(m_Report);
        }

        private static string FirstGroup(LevelRegistry levels)
        {
            for (int i = 0; levels != null && i < levels.entries.Count; i++)
            {
                if (levels.entries[i] != null && !string.IsNullOrEmpty(levels.entries[i].group))
                    return levels.entries[i].group;
            }
            return "";
        }

        private void Browse()
        {
            string picked = EditorUtility.OpenFolderPanel("Level folder", m_Folder.value, "");
            if (string.IsNullOrEmpty(picked))
                return;
            string project = System.IO.Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            picked = picked.Replace('\\', '/');
            if (project != null && picked.StartsWith(project, StringComparison.Ordinal))
                picked = picked.Substring(project.Length).TrimStart('/');
            m_Folder.value = picked;
        }

        private void Create()
        {
            ILevelTemplate template = m_Templates.Count > 0 && m_Template.index >= 0
                && m_Template.index < m_Templates.Count
                ? m_Templates[m_Template.index] : null;
            LevelDef made = LevelFactory.Create(m_Levels, m_Name.value, m_Group.value,
                m_Folder.value, template, out string report);
            m_Report.text = report;
            m_Report.style.color = made != null ? new Color(0.55f, 0.85f, 0.6f) : new Color(1f, 0.5f, 0.45f);
            if (made == null)
                return;
            m_Name.value = "";
            m_Changed?.Invoke();
            if (made.content != null)
                EditorGUIUtility.PingObject(made.content);
        }
    }
}
