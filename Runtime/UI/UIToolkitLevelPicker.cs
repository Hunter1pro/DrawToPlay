using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// The dev "go anywhere" overlay (M16): a dropdown over a <see cref="LevelRegistry"/>
    /// plus a Go button that WRITES A KEY on the Root scope — it never loads anything
    /// itself. The session tree owns transitions; a state watching
    /// <see cref="targetKey"/> (a <see cref="LoadLevelTask"/> with its dynamic name key
    /// wired to the same declaration) answers the request, so the dev tool and a gameplay
    /// portal are the same mechanism at different desks. Classes: <c>dtp-levelpicker</c>
    /// and children.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIToolkitLevelPicker : MonoBehaviour
    {
        public LevelRegistry registry;

        /// <summary>Root-scope key the Go button writes the chosen level's NAME to. A
        /// component field is authored text (the M14 boundary: components are the seam
        /// between tree-land and the scene).</summary>
        public string targetKey = "level:goto";

        private DropdownField m_Dropdown;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();

            var row = new VisualElement();
            row.AddToClassList("dtp-levelpicker");
            row.style.position = Position.Absolute;
            row.style.bottom = 10f;
            row.style.left = 10f;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            root.Add(row);

            var choices = new List<string>();
            for (int i = 0; registry != null && i < registry.entries.Count; i++)
            {
                LevelDef entry = registry.entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.name))
                    choices.Add(entry.name);
            }

            m_Dropdown = new DropdownField(choices, choices.Count > 0 ? 0 : -1);
            m_Dropdown.AddToClassList("dtp-levelpicker__choices");
            m_Dropdown.style.minWidth = 140f;
            row.Add(m_Dropdown);

            var go = new Button(RequestLevel) { text = "Go" };
            go.AddToClassList("dtp-levelpicker__go");
            go.tooltip = "Raise the goto key on the Root scope — the session tree decides "
                + "what that means.";
            row.Add(go);
        }

        private void RequestLevel()
        {
            if (string.IsNullOrEmpty(targetKey) || m_Dropdown == null
                || string.IsNullOrEmpty(m_Dropdown.value))
                return;

            StateTreeContextHost root = StateTreeContextHost.Resolve(gameObject,
                StateTreeContextKind.Root);
            if (root != null)
                root.Context.blackboard[targetKey] = m_Dropdown.value;
        }
    }
}
