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
    /// row from its registry, a key from the declared keys, a tag from the vocabularies; a parameter the
    /// step does not pick (derived) is shown read-only with where it comes from. Never a text box,
    /// except for words a person reads.
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

            // A derived value follows its source on every look — a row renamed, a level re-picked.
            if (StateTreeSchemaRules.ApplyDerived(tree, schema, node) > 0)
                m_Edited?.Invoke();

            BuildHeader(kind);
            BuildActions();
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
                + "hold; the dev states and every node's tasks, conditions and transitions "
                + "are in Dev mode."));

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

            var raw = new Button(() => m_ShowRaw?.Invoke(m_Node)) { text = "Dev view" };
            raw.tooltip = "Show this node's states, tasks and transitions in Dev mode.";
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
            id.tooltip = "The state id transitions name — renamed in Dev mode, where every "
                + "transition that targets it is rewired with it.";
            id.SetEnabled(false);
            m_Root.Add(id);

            if (!string.IsNullOrEmpty(kind.help))
                m_Root.Add(Note(kind.help));
        }

        /// <summary>The game's buttons on this node (open its level, play from it); none when no action offers one.</summary>
        private void BuildActions()
        {
            StateTreeMissionPath path = StateTreeMissionPath.Of(m_Tree, m_Schema, m_Node);
            List<(IStateTreeMissionAction action, StateTreeMissionOffer offer)> offered = StateTreeMissionActions.Offered(path);
            if (offered.Count == 0)
                return;
            var row = new VisualElement();
            row.AddToClassList("dtp-mission-actions");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 4f;
            foreach ((IStateTreeMissionAction action, StateTreeMissionOffer offer) in offered)
            {
                var button = new Button(() => action.Run(path)) { text = offer.label };
                button.tooltip = offer.blocked ?? offer.tooltip;
                button.SetEnabled(offer.blocked == null);
                row.Add(button);
            }
            m_Root.Add(row);
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
            List<StateTreeStepParameter> picked = parameters.FindAll(p => !p.isDerived);
            List<StateTreeStepParameter> derived = parameters.FindAll(p => p.isDerived);
            m_Root.Add(Section($"Parameters ({picked.Count})"));
            foreach (StateTreeStepParameter parameter in picked)
                m_Root.Add(BuildParameter(kind, run, parameter));
            if (derived.Count == 0)
                return;
            m_Root.Add(Section("Follows from the step"));
            foreach (StateTreeStepParameter parameter in derived)
                m_Root.Add(BuildDerived(kind, parameter));
        }

        /// <summary>A parameter the step does not pick: its value, read-only, and where it comes from.</summary>
        internal VisualElement BuildDerived(StateTreeSchemaKind kind, StateTreeStepParameter parameter)
        {
            string value = StateTreeSchemaRules.DerivedValue(m_Tree, m_Schema, m_Node, kind, parameter,
                out string why, out StateTreeRegistryEntry row);
            var box = new VisualElement();
            box.AddToClassList("dtp-derived");
            box.style.marginBottom = 2f;
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            var name = new Label(StateTreeSchemaRules.Pretty(parameter.declaration.name));
            name.style.minWidth = 120f;
            name.style.opacity = 0.8f;
            line.Add(name);
            var shown = new Label(value == null ? "— cannot be derived" : string.IsNullOrEmpty(value) ? "(none)" : value);
            shown.style.unityFontStyleAndWeight = FontStyle.Italic;
            shown.style.flexGrow = 1f;
            shown.style.whiteSpace = WhiteSpace.Normal;
            if (value == null)
                shown.style.color = new Color(1f, 0.45f, 0.4f);
            line.Add(shown);
            box.Add(line);
            string meaning = why;
            if (row != null && !string.IsNullOrEmpty(row.Describe()))
                meaning += " — " + row.Describe();
            if (parameter.isKey && !string.IsNullOrEmpty(value)
                && StateTreeKeyOffers.TryFind(StateTreeKeyOffers.For(m_Tree, m_Tree), value, out StateTreeKeyOffer offer)
                && !string.IsNullOrEmpty(offer.description))
                meaning += ". " + offer.description;
            var note = Note(meaning);
            note.style.marginLeft = 120f;
            note.style.fontSize = 10;
            box.Add(note);
            box.tooltip = parameter.declaration.name + " is derived — the step does not pick it; "
                + "Dev view shows the raw override it writes.";
            return box;
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
                    if (parameter.isEnum)
                        input = BuildEnum(run, parameter, label);
                    else if (parameter.isPlacement || parameter.entryType != null)
                        input = BuildPick(kind, run, parameter, label);
                    else if (parameter.isKey)
                        input = BuildKeyPick(kind, run, parameter, label);
                    else if (parameter.isTag)
                        input = BuildTagPick(run, parameter, label);
                    else if (parameter.stringKind == StateTreeStringKind.HumanText
                             || parameter.stringKind == StateTreeStringKind.Literal)
                        input = BuildText(run, parameter, label);
                    else
                        input = BuildNoPicker(parameter, label);
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

        /// <summary>An enum field: its values, kept as the picked value's name (the template's default when the step sets none).</summary>
        private VisualElement BuildEnum(RunSubTreeTask run, StateTreeStepParameter parameter, string label)
        {
            System.Type type = parameter.field.FieldType;
            string held = parameter.stringValue;
            System.Enum value = !string.IsNullOrEmpty(held) && System.Enum.IsDefined(type, held)
                ? (System.Enum)System.Enum.Parse(type, held)
                : (System.Enum)System.Enum.GetValues(type).GetValue(0);
            var field = new EnumField(label, value);
            field.RegisterValueChangedCallback(evt => Write(run, parameter.declaration, evt.newValue.ToString(), 0f, ""));
            return field;
        }

        /// <summary>Words a person reads (or a literal value) — the only text box a step may show.</summary>
        private VisualElement BuildText(RunSubTreeTask run, StateTreeStepParameter parameter, string label)
        {
            var text = new TextField(label) { value = parameter.stringValue, isDelayed = true };
            text.tooltip = parameter.stringKind == StateTreeStringKind.HumanText ? "Words the player reads." : "A literal value.";
            text.RegisterValueChangedCallback(evt => Write(run, parameter.declaration, evt.newValue, 0f, ""));
            return text;
        }

        /// <summary>A parameter whose bound field names something but has no picker: shown, never typed
        /// — the fix is a picker attribute on the field (or a derivation on the parameter).</summary>
        private VisualElement BuildNoPicker(StateTreeStepParameter parameter, string label)
        {
            var box = new VisualElement();
            var field = new StateTreePickField(label, parameter.stringValue, true,
                "No picker: " + (parameter.field == null ? "the template binds this parameter to nothing"
                    : parameter.field.DeclaringType?.Name + "." + parameter.field.Name + " is a free-text string"),
                null);
            field.button.SetEnabled(false);
            box.Add(field);
            box.Add(new HelpBox(parameter.field == null
                ? "The template binds this parameter to no field — it reaches only the blackboard. Bind it in Dev mode."
                : $"{parameter.field.DeclaringType?.Name}.{parameter.field.Name} is a free-text string, so there is "
                + "nothing to pick from. Give the field a picker ([PlacementId], [RowName], [KeyName], …) or derive "
                + "the parameter.", HelpBoxMessageType.Error));
            return box;
        }

        /// <summary>A key: the declared keys the hall and the template see; a new one is declared on the
        /// tree the step lives in.</summary>
        private VisualElement BuildKeyPick(StateTreeSchemaKind kind, RunSubTreeTask run, StateTreeStepParameter parameter,
            string label)
        {
            List<StateTreeKeyOffer> offers = StateTreeKeyOffers.For(m_Tree, m_Tree);
            foreach (StateTreeKeyOffer extra in StateTreeKeyOffers.For(kind.template, kind.template))
            {
                if (!StateTreeKeyOffers.TryFind(offers, extra.name, out _))
                    offers.Add(extra);
            }
            string current = parameter.stringValue;
            bool known = StateTreeKeyOffers.TryFind(offers, current, out StateTreeKeyOffer offer);
            return new StateTreePickField(label, current, !known && !string.IsNullOrEmpty(current),
                StateTreeKeyPick.Tooltip(known, offer),
                at => StateTreeKeyPick.Menu(at, offers, m_Tree, StateTreeKeyKind.String, current, true,
                    picked => Write(run, parameter.declaration, picked.name, 0f, "", picked.id)));
        }

        private VisualElement BuildTagPick(RunSubTreeTask run, StateTreeStepParameter parameter, string label)
        {
            var tags = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(m_Tree, tags);
            string current = parameter.stringValue;
            bool known = tags.Exists(t => t.name == current);
            return new StateTreePickField(label, current, !known && !string.IsNullOrEmpty(current), "A world tag.",
                at => StateTreePickItems.Tags(tags, m_Tree).Show(at, current, picked =>
                    Write(run, parameter.declaration, picked.value, 0f, (picked.payload as WorldTagDef)?.id ?? "")));
        }

        /// <summary>A row picker: the level's placements, or the rows of the bound field's registry —
        /// with ◎ to the row's asset (▶ opens it) or its catalog.</summary>
        private VisualElement BuildPick(StateTreeSchemaKind kind, RunSubTreeTask run,
            StateTreeStepParameter parameter, string label)
        {
            string shown = parameter.stringValue;
            bool missing = false;
            StateTreePickField field;
            if (parameter.isPlacement)
            {
                LevelObjectDef placement = StateTreeSchemaRules.Placement(m_Tree, m_Schema, m_Node, shown, out LevelObjectRegistry manifest);
                if (placement != null)
                    shown = placement.name;
                else
                    missing = !string.IsNullOrEmpty(shown);
                field = new StateTreePickField(label, shown, missing,
                    placement != null ? "A placement of " + manifest.name + " (" + placement.id + ")" : "A placement of the level's manifest.",
                    at => ShowPick(at, kind, run, parameter));
                field.WithPing(manifest, manifest != null ? "the manifest " + manifest.name : null);
            }
            else
            {
                StateTreeRegistryEntry entry = FindRow(kind, parameter, shown);
                missing = !string.IsNullOrEmpty(shown) && entry == null;
                List<StateTreeRegistryAsset> sources = StateTreeSchemaRules.RowSources(m_Tree, m_Schema, m_Node,
                    kind.template, parameter.entryType);
                field = new StateTreePickField(label, shown, missing,
                    entry != null ? entry.Describe() : "A " + parameter.entryType.Name.Replace("Def", "") + " row.",
                    at => ShowPick(at, kind, run, parameter));
                field.WithRowButtons(entry, entry != null ? StateTreeRowOffers.CatalogOf(sources, entry)
                    : sources.Count > 0 ? sources[0] : null);
            }
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

        /// <summary>The step's picker: a placement of its Level's manifest, or a row — this level's
        /// catalog first when an enclosing group names one.</summary>
        private void ShowPick(Rect at, StateTreeSchemaKind kind, RunSubTreeTask run, StateTreeStepParameter parameter)
        {
            StateTreePickMenu menu;
            if (parameter.isPlacement)
            {
                StateTreeSchemaRules.Placement(m_Tree, m_Schema, m_Node, "", out LevelObjectRegistry manifest);
                var rows = new List<LevelObjectDef>();
                for (int i = 0; manifest != null && i < manifest.Count; i++)
                {
                    if (manifest.EntryAt(i) is LevelObjectDef row)
                        rows.Add(row);
                }
                menu = StateTreePickItems.Placements(rows, m_Tree, manifest);
                if (manifest == null)
                    menu.empty = "No level manifest — give the Level its manifest";
            }
            else
            {
                List<StateTreeRegistryAsset> sources = StateTreeSchemaRules.RowSources(m_Tree, m_Schema, m_Node,
                    kind.template, parameter.entryType);
                menu = StateTreePickItems.Rows(sources, m_Tree, parameter.entryType,
                    StateTreeSchemaRules.EnclosingRegistry(m_Tree, m_Schema, m_Node, parameter.entryType));
                if (sources.Count == 0)
                    menu.empty = $"No {parameter.entryType.Name} registry in the template's Data";
            }
            menu.Show(at, parameter.stringValue, picked =>
                Write(run, parameter.declaration, picked.value, 0f,
                    parameter.isPlacement ? picked.value : (picked.payload as StateTreeRegistryEntry)?.id ?? ""));
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

        private void Write(RunSubTreeTask run, GraphTaskParameter declaration, string text, float number, string entryId,
            string keyId = "")
        {
            StateTreeSchemaRules.SetParameter(run, declaration, text, number, entryId, k_Undo, keyId);
            StateTreeSchemaRules.ApplyDerived(m_Tree, m_Schema, m_Node);
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

    }
}
