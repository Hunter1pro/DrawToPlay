using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The HIGH-LEVEL inspector of a schema's mode: a step shows its template's parameters and
    /// nothing of the states inside; a group shows its context; both show the facts a designer needs
    /// and cannot change (where a quit resumes, whether it saves, where it leads). A parameter takes
    /// its picker from the template field it is bound to — a placement from the level's manifest, a
    /// row from its registry, a key as a name.
    /// </summary>
    internal sealed class StateTreeMissionInspector
    {
        private const string k_Undo = "Edit Mission Node";

        private readonly VisualElement m_Root;
        private readonly Action m_Edited;
        private readonly Action m_Changed;
        private readonly Action<StateTreeNodeAsset> m_ShowRaw;
        private readonly Action<StateTreeNodeAsset> m_Palette;

        private StateTreeAsset m_Tree;
        private StateTreeSchema m_Schema;
        private StateTreeNodeAsset m_Node;
        private List<StateTreeSchemaProblem> m_Problems;

        /// <param name="edited">A value changed: save, refresh the rows and the gizmos.</param>
        /// <param name="changed">A step's shape changed: also re-validate and rebuild this inspector.</param>
        internal StateTreeMissionInspector(VisualElement root, Action edited, Action changed,
            Action<StateTreeNodeAsset> showRaw, Action<StateTreeNodeAsset> palette)
        {
            m_Root = root;
            m_Edited = edited;
            m_Changed = changed;
            m_ShowRaw = showRaw;
            m_Palette = palette;
        }

        internal void Rebuild(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            List<StateTreeSchemaProblem> problems)
        {
            m_Tree = tree;
            m_Schema = schema;
            m_Node = node;
            m_Problems = problems ?? new List<StateTreeSchemaProblem>();
            m_Root.Clear();

            StateTreeSchemaKind kind = StateTreeSchemaRules.KindOf(schema, node);
            if (tree == null || schema == null)
                return;
            if (kind == null)
            {
                BuildOverview();
                return;
            }

            BuildHeader(kind);
            BuildProblems(node);
            if (kind.isTemplate)
                BuildParameters(kind);
            else
                BuildContext(kind);
            BuildFacts(kind);
            BuildChildren(kind);
        }

        // --- sections --------------------------------------------------------------------

        private void BuildOverview()
        {
            var title = new Label(m_Schema.modeName + " — " + StateTreeEditorOps.TreeDisplayName(m_Tree));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.marginBottom = 6f;
            m_Root.Add(title);
            m_Root.Add(Note("Select a node to edit it. Add offers only the kinds the selected node may "
                + "hold; the programmer's states and every node's tasks, conditions and transitions "
                + "are in Programmer mode."));

            var rules = new Label("Rules");
            rules.style.unityFontStyleAndWeight = FontStyle.Bold;
            rules.style.marginTop = 8f;
            m_Root.Add(rules);
            m_Root.Add(Note("Top: " + Names(m_Schema.AllowedUnder(""))));
            foreach (StateTreeSchemaKind kind in m_Schema.kinds)
            {
                if (kind == null)
                    continue;
                List<StateTreeSchemaKind> under = m_Schema.AllowedUnder(kind.kind);
                m_Root.Add(Note($"{kind.icon} {kind.Label} → " + (under.Count == 0 ? "(leaf)" : Names(under))
                    + (kind.childLimit > 0 ? $"  (at most {kind.childLimit})" : "")));
            }

            if (m_Problems.Count > 0)
            {
                var heading = new Label($"Problems ({m_Problems.Count})");
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                heading.style.marginTop = 8f;
                m_Root.Add(heading);
                foreach (StateTreeSchemaProblem problem in m_Problems)
                {
                    string where = problem.node != null
                        ? (string.IsNullOrEmpty(problem.node.displayName) ? problem.node.nodeId : problem.node.displayName)
                        : "?";
                    m_Root.Add(new HelpBox(where + ": " + problem.message, HelpBoxMessageType.Error));
                }
            }
        }

        private void BuildHeader(StateTreeSchemaKind kind)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4f;

            var icon = new Label(string.IsNullOrEmpty(kind.icon) ? "●" : kind.icon);
            icon.style.fontSize = 18;
            icon.style.color = kind.color;
            icon.style.marginRight = 6f;
            header.Add(icon);

            var kindLabel = new Label(kind.Label);
            kindLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            kindLabel.style.fontSize = 14;
            kindLabel.style.flexGrow = 1f;
            header.Add(kindLabel);

            var raw = new Button(() => m_ShowRaw?.Invoke(m_Node)) { text = "Programmer view" };
            raw.tooltip = "Show this node's states, tasks and transitions in Programmer mode.";
            header.Add(raw);
            m_Root.Add(header);

            var name = new TextField("Name") { value = m_Node.displayName, isDelayed = true };
            name.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(m_Node, k_Undo);
                m_Node.displayName = evt.newValue;
                EditorUtility.SetDirty(m_Node);
                m_Changed?.Invoke();
            });
            m_Root.Add(name);

            var id = new TextField("State") { value = m_Node.nodeId, isReadOnly = true };
            id.tooltip = "The state id transitions name — renamed in Programmer mode, where every "
                + "transition that targets it is rewired with it.";
            id.SetEnabled(false);
            m_Root.Add(id);

            if (!string.IsNullOrEmpty(kind.help))
                m_Root.Add(Note(kind.help));
        }

        private void BuildProblems(StateTreeNodeAsset node)
        {
            foreach (StateTreeSchemaProblem problem in m_Problems)
            {
                if (problem.node == node)
                    m_Root.Add(new HelpBox(problem.message, HelpBoxMessageType.Error));
            }
        }

        private void BuildParameters(StateTreeSchemaKind kind)
        {
            RunSubTreeTask run = StateTreeSchemaRules.TemplateTask(kind, m_Node);
            m_Root.Add(Section("Template"));
            var template = new ObjectField { objectType = typeof(StateTreeAsset), value = kind.template };
            template.SetEnabled(false);
            m_Root.Add(template);
            if (run == null)
                return;

            List<StateTreeStepParameter> parameters = StateTreeSchemaRules.Parameters(kind, m_Node);
            m_Root.Add(Section($"Parameters ({parameters.Count})"));
            foreach (StateTreeStepParameter parameter in parameters)
                m_Root.Add(BuildParameter(kind, run, parameter));
        }

        internal VisualElement BuildParameter(StateTreeSchemaKind kind, RunSubTreeTask run,
            StateTreeStepParameter parameter)
        {
            GraphTaskParameter declaration = parameter.declaration;
            string label = StateTreeSchemaRules.Pretty(declaration.name);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 22f;
            row.tooltip = declaration.name + (parameter.field != null
                ? " → " + parameter.field.DeclaringType?.Name + "." + parameter.field.Name
                : " (bound to nothing in the template — it only reaches the blackboard)");

            VisualElement input;
            switch (declaration.kind)
            {
                case GraphTaskParameterKind.Bool:
                {
                    var toggle = new Toggle(label) { value = parameter.floatValue != 0f };
                    toggle.RegisterValueChangedCallback(evt => Write(run, declaration, "", evt.newValue ? 1f : 0f, ""));
                    input = toggle;
                    break;
                }
                case GraphTaskParameterKind.Float:
                {
                    var number = new FloatField(label) { value = parameter.floatValue, isDelayed = true };
                    number.RegisterValueChangedCallback(evt => Write(run, declaration, "", evt.newValue, ""));
                    input = number;
                    break;
                }
                default:
                    input = parameter.isPlacement || parameter.entryType != null
                        ? BuildPick(kind, run, parameter, label)
                        : BuildText(run, parameter, label);
                    break;
            }
            input.style.flexGrow = 1f;
            input.style.opacity = parameter.overridden ? 1f : 0.6f;
            row.Add(input);

            if (parameter.overridden)
            {
                var reset = new Button(() =>
                {
                    StateTreeSchemaRules.ResetParameter(run, declaration, k_Undo);
                    Changed();
                }) { text = "✕" };
                reset.tooltip = "Back to the template's default.";
                reset.style.width = 24f;
                row.Add(reset);
            }
            return row;
        }

        private VisualElement BuildText(RunSubTreeTask run, StateTreeStepParameter parameter, string label)
        {
            var text = new TextField(label) { value = parameter.stringValue, isDelayed = true };
            if (parameter.isKey)
                text.tooltip = "A blackboard key, by name.";
            text.RegisterValueChangedCallback(evt => Write(run, parameter.declaration, evt.newValue, 0f, ""));
            return text;
        }

        /// <summary>A row picker: the level's placements, or the rows of the bound field's registry.</summary>
        private VisualElement BuildPick(StateTreeSchemaKind kind, RunSubTreeTask run,
            StateTreeStepParameter parameter, string label)
        {
            var field = new VisualElement();
            field.style.flexDirection = FlexDirection.Row;
            field.style.alignItems = Align.Center;

            var name = new Label(label);
            name.AddToClassList(BaseField<string>.labelUssClassName);
            name.style.minWidth = 120f;
            field.Add(name);

            string shown = parameter.stringValue;
            bool missing = false;
            if (parameter.isPlacement)
            {
                LevelObjectDef placement = StateTreeSchemaRules.Placement(m_Tree, m_Schema, m_Node, shown, out _);
                if (placement != null)
                    shown = placement.name;
                else
                    missing = !string.IsNullOrEmpty(shown);
            }
            else
            {
                StateTreeRegistryEntry entry = FindRow(kind, parameter, shown);
                missing = !string.IsNullOrEmpty(shown) && entry == null;
            }

            var button = new Button { text = string.IsNullOrEmpty(shown) ? "(none) ▾" : shown + (missing ? "  (missing)" : "") + " ▾" };
            button.style.flexGrow = 1f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (missing)
                button.style.color = new Color(1f, 0.45f, 0.4f);
            button.clicked += () => ShowPick(kind, run, parameter);
            field.Add(button);
            return field;
        }

        private StateTreeRegistryEntry FindRow(StateTreeSchemaKind kind, StateTreeStepParameter parameter, string name)
        {
            foreach (StateTreeRegistryAsset registry in StateTreeSchemaRules.RowSources(m_Tree, m_Schema, m_Node,
                         kind.template, parameter.entryType))
            {
                StateTreeRegistryEntry entry = registry.FindByName(name);
                if (entry != null)
                    return entry;
            }
            return null;
        }

        private void ShowPick(StateTreeSchemaKind kind, RunSubTreeTask run, StateTreeStepParameter parameter)
        {
            var menu = new GenericMenu();
            if (parameter.isPlacement)
            {
                StateTreeSchemaRules.Placement(m_Tree, m_Schema, m_Node, "", out LevelObjectRegistry manifest);
                if (manifest == null)
                    menu.AddDisabledItem(new GUIContent("No level manifest — give the Level its manifest"));
                else
                {
                    var rows = new List<StateTreeRegistryEntry>();
                    for (int i = 0; i < manifest.Count; i++)
                    {
                        if (manifest.EntryAt(i) != null)
                            rows.Add(manifest.EntryAt(i));
                    }
                    rows.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    foreach (StateTreeRegistryEntry row in rows)
                    {
                        string id = row.id;
                        menu.AddItem(new GUIContent(Escape(row.name)), id == parameter.stringValue,
                            () => Write(run, parameter.declaration, id, 0f, id));
                    }
                }
            }
            else
            {
                List<StateTreeRegistryAsset> sources = StateTreeSchemaRules.RowSources(m_Tree, m_Schema, m_Node,
                    kind.template, parameter.entryType);
                if (sources.Count == 0)
                    menu.AddDisabledItem(new GUIContent($"No {parameter.entryType.Name} registry in the template's Data"));
                foreach (StateTreeRegistryAsset registry in sources)
                {
                    for (int i = 0; i < registry.Count; i++)
                    {
                        StateTreeRegistryEntry entry = registry.EntryAt(i);
                        if (entry == null || string.IsNullOrEmpty(entry.name))
                            continue;
                        string entryName = entry.name;
                        string entryId = entry.id;
                        string path = sources.Count > 1 ? registry.name + "/" + Escape(entryName) : Escape(entryName);
                        menu.AddItem(new GUIContent(path), entryName == parameter.stringValue,
                            () => Write(run, parameter.declaration, entryName, 0f, entryId));
                    }
                }
            }
            menu.ShowAsContext();
        }

        private void BuildContext(StateTreeSchemaKind kind)
        {
            StateTreeTaskAsset context = StateTreeSchemaRules.ContextTask(kind, m_Node);
            if (context == null)
                return;
            m_Root.Add(Section("Context"));
            var serialized = new SerializedObject(context);
            var fields = new VisualElement();
            SerializedProperty iterator = serialized.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.propertyPath == "m_Script" || iterator.propertyPath == "blocking"
                    || iterator.propertyPath == "returns")
                    continue;
                fields.Add(new PropertyField(iterator.Copy()));
            }
            fields.Bind(serialized);
            fields.RegisterCallback<SerializedPropertyChangeEvent>(_ => m_Edited?.Invoke());
            m_Root.Add(fields);
        }

        private void BuildFacts(StateTreeSchemaKind kind)
        {
            var facts = new List<string>();
            if (!string.IsNullOrEmpty(kind.resume))
                facts.Add("Resume: " + kind.resume);
            if (kind.isTemplate)
            {
                bool saves = kind.saves;
                RunSubTreeTask run = StateTreeSchemaRules.TemplateTask(kind, m_Node);
                foreach (StateTreeStepParameter parameter in StateTreeSchemaRules.Parameters(kind, m_Node))
                {
                    // A template that saves only when asked says so through a Bool 'save' parameter.
                    if (parameter.declaration.kind == GraphTaskParameterKind.Bool
                        && parameter.declaration.name.EndsWith("save", StringComparison.OrdinalIgnoreCase))
                        saves = parameter.floatValue != 0f;
                }
                facts.Add("Saves: " + (saves ? "yes" : "no"));
                if (run == null)
                    facts.Add("Runs: nothing (its template task is missing)");
            }
            var leads = new List<string>();
            foreach (StateTreeTransition transition in m_Node.transitions)
            {
                if (transition != null && !string.IsNullOrEmpty(transition.targetNodeId))
                    leads.Add((transition.checkWhileRunning ? "⚡" : "→ ") + transition.targetNodeId);
            }
            if (leads.Count > 0)
                facts.Add("Then: " + string.Join(", ", leads));
            else if (kind.isTemplate)
                facts.Add("Then: the next step");

            if (facts.Count == 0)
                return;
            m_Root.Add(Section("Facts"));
            foreach (string fact in facts)
                m_Root.Add(Note(fact));
        }

        private void BuildChildren(StateTreeSchemaKind kind)
        {
            List<StateTreeSchemaKind> allowed = m_Schema.AllowedUnder(kind.kind);
            if (allowed.Count == 0)
                return;
            m_Root.Add(Section("Holds"));
            m_Root.Add(Note(Names(allowed) + (kind.childLimit > 0 ? $"  — at most {kind.childLimit}" : "")));
            var add = new Button(() => m_Palette?.Invoke(m_Node)) { text = "Add ▾" };
            add.style.alignSelf = Align.FlexStart;
            m_Root.Add(add);
        }

        // --- plumbing --------------------------------------------------------------------

        private void Write(RunSubTreeTask run, GraphTaskParameter declaration, string text, float number, string entryId)
        {
            StateTreeSchemaRules.SetParameter(run, declaration, text, number, entryId, k_Undo);
            Changed();
        }

        private void Changed()
        {
            m_Changed?.Invoke();
        }

        private static Label Section(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8f;
            label.style.marginBottom = 2f;
            return label;
        }

        private static Label Note(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.8f;
            return label;
        }

        private static string Names(List<StateTreeSchemaKind> kinds)
        {
            var names = new List<string>();
            foreach (StateTreeSchemaKind kind in kinds)
                names.Add(kind.Label);
            return string.Join(", ", names);
        }

        private static string Escape(string text) => (text ?? "").Replace('/', '∕');
    }
}
