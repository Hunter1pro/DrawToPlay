using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// The picker a sub-tree's String parameter takes, from the template field it binds into — a placement
    /// from the level's manifest, a row from its catalog, a key, a tag, an enum. One builder for Mission
    /// mode's step and Dev mode's override row, so the two views cannot disagree about what a parameter is.
    /// </summary>
    internal sealed class StateTreeStepParameterInputs
    {
        /// <summary>Give the parameter a value: what runs, the row's id or placement id, the key's id.</summary>
        internal delegate void Writer(GraphTaskParameter declaration, string text, float number, string entryId,
            string keyId);

        private readonly StateTreeAsset m_Tree;
        private readonly StateTreeSchema m_Schema;
        private readonly StateTreeNodeAsset m_Node;
        private readonly StateTreeAsset m_Template;
        private readonly Writer m_Write;

        /// <param name="tree">The tree the calling state lives in — its keys, tags and context.</param>
        /// <param name="schema">Its schema, or null: only a group's context (a level's manifest) needs it.</param>
        /// <param name="node">The calling state.</param>
        /// <param name="template">The sub-tree it runs — its Data is where rows resolve.</param>
        internal StateTreeStepParameterInputs(StateTreeAsset tree, StateTreeSchema schema, StateTreeNodeAsset node,
            StateTreeAsset template, Writer write)
        {
            m_Tree = tree;
            m_Schema = schema;
            m_Node = node;
            m_Template = template;
            m_Write = write;
        }

        /// <summary>True when the bound field names something with a catalog to pick from.</summary>
        internal static bool HasPicker(StateTreeStepParameter parameter)
        {
            return parameter.isEnum || parameter.isPlacement || parameter.entryType != null || parameter.isKey
                || parameter.isTag;
        }

        /// <summary>Words a person reads, a literal — or a parameter that binds into nothing.</summary>
        internal static bool IsText(StateTreeStepParameter parameter)
        {
            return parameter.field == null || parameter.stringKind == StateTreeStringKind.HumanText
                || parameter.stringKind == StateTreeStringKind.Literal;
        }

        /// <summary>The picker for a String parameter, or null when its bound field has none.</summary>
        /// <param name="label">The field's label; null when the host draws its own.</param>
        internal VisualElement Picker(StateTreeStepParameter parameter, string label)
        {
            if (parameter.isEnum)
                return EnumPick(parameter, label);
            if (parameter.isPlacement || parameter.entryType != null)
                return Pick(parameter, label);
            if (parameter.isKey)
                return KeyPick(parameter, label);
            if (parameter.isTag)
                return TagPick(parameter, label);
            return null;
        }

        /// <summary>Words a person reads (or a literal value) — the only text box a step may show.</summary>
        internal VisualElement Text(StateTreeStepParameter parameter, string label)
        {
            var text = new TextField(label) { value = parameter.stringValue, isDelayed = true };
            text.tooltip = parameter.stringKind == StateTreeStringKind.HumanText ? "Words the player reads."
                : parameter.field == null ? "Bound to nothing in the template — it only reaches the blackboard."
                : "A literal value.";
            text.RegisterValueChangedCallback(evt => m_Write(parameter.declaration, evt.newValue, 0f, "", ""));
            return text;
        }

        /// <summary>A parameter whose bound field names something but has no picker: shown, never typed
        /// — the fix is a picker attribute on the field (or a derivation on the parameter).</summary>
        internal VisualElement NoPicker(StateTreeStepParameter parameter, string label)
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

        /// <summary>What a derived parameter holds for this state, and the sentence that says where it
        /// comes from (with the row's or key's meaning). Null value: it cannot be derived.</summary>
        internal string Derivation(RunSubTreeTask run, StateTreeStepParameter parameter, out string meaning)
        {
            string value = StateTreeSchemaRules.DerivedValue(m_Tree, m_Schema, m_Node, run, parameter,
                out string why, out StateTreeRegistryEntry row);
            meaning = why;
            if (row != null && !string.IsNullOrEmpty(row.Describe()))
                meaning += " — " + row.Describe();
            if (parameter.isKey && !string.IsNullOrEmpty(value)
                && StateTreeKeyOffers.TryFind(StateTreeKeyOffers.For(m_Tree, m_Tree), value, out StateTreeKeyOffer offer)
                && !string.IsNullOrEmpty(offer.description))
                meaning += ". " + offer.description;
            return value;
        }

        /// <summary>An enum field: its values, kept as the picked value's name (the template's default when
        /// the step sets none).</summary>
        private VisualElement EnumPick(StateTreeStepParameter parameter, string label)
        {
            Type type = parameter.field.FieldType;
            string held = parameter.stringValue;
            System.Enum value = !string.IsNullOrEmpty(held) && System.Enum.IsDefined(type, held)
                ? (System.Enum)System.Enum.Parse(type, held)
                : (System.Enum)System.Enum.GetValues(type).GetValue(0);
            var field = new EnumField(label, value);
            field.RegisterValueChangedCallback(evt => m_Write(parameter.declaration, evt.newValue.ToString(), 0f, "", ""));
            return field;
        }

        /// <summary>A key: the declared keys the calling tree and the template see; a new one is declared on
        /// the tree the step lives in.</summary>
        private VisualElement KeyPick(StateTreeStepParameter parameter, string label)
        {
            List<StateTreeKeyOffer> offers = StateTreeKeyOffers.For(m_Tree, m_Tree);
            foreach (StateTreeKeyOffer extra in StateTreeKeyOffers.For(m_Template, m_Template))
            {
                if (!StateTreeKeyOffers.TryFind(offers, extra.name, out _))
                    offers.Add(extra);
            }
            string current = parameter.stringValue;
            bool known = StateTreeKeyOffers.TryFind(offers, current, out StateTreeKeyOffer offer);
            return new StateTreePickField(label, current, !known && !string.IsNullOrEmpty(current),
                StateTreeKeyPick.Tooltip(known, offer),
                at => StateTreeKeyPick.Menu(at, offers, m_Tree, StateTreeKeyKind.String, current, true,
                    picked => m_Write(parameter.declaration, picked.name, 0f, "", picked.id)));
        }

        private VisualElement TagPick(StateTreeStepParameter parameter, string label)
        {
            var tags = new List<WorldTagDef>();
            StateTreeOffers.TagsFor(m_Tree, tags);
            string current = parameter.stringValue;
            bool known = tags.Exists(t => t.name == current);
            return new StateTreePickField(label, current, !known && !string.IsNullOrEmpty(current), "A world tag.",
                at => StateTreePickItems.Tags(tags, m_Tree).Show(at, current, picked =>
                    m_Write(parameter.declaration, picked.value, 0f, (picked.payload as WorldTagDef)?.id ?? "", "")));
        }

        /// <summary>A row picker: the level's placements, or the rows of the bound field's registry —
        /// with ◎ to the row's asset (▶ opens it) or its catalog.</summary>
        private VisualElement Pick(StateTreeStepParameter parameter, string label)
        {
            string shown = parameter.stringValue;
            StateTreePickField field;
            if (parameter.isPlacement)
            {
                LevelObjectDef placement = StateTreeSchemaRules.Placement(m_Tree, m_Schema, m_Node, shown,
                    out LevelObjectRegistry manifest);
                bool missing = placement == null && !string.IsNullOrEmpty(shown);
                if (placement != null)
                    shown = placement.name;
                field = new StateTreePickField(label, shown, missing,
                    placement != null ? "A placement of " + manifest.name + " (" + placement.id + ")"
                        : "A placement of the level's manifest.",
                    at => ShowPick(at, parameter));
                field.WithPing(manifest, manifest != null ? "the manifest " + manifest.name : null);
            }
            else
            {
                List<StateTreeRegistryAsset> sources = Sources(parameter);
                StateTreeRegistryEntry entry = FindRow(sources, shown);
                field = new StateTreePickField(label, shown, !string.IsNullOrEmpty(shown) && entry == null,
                    entry != null ? entry.Describe() : "A " + parameter.entryType.Name.Replace("Def", "") + " row.",
                    at => ShowPick(at, parameter));
                field.WithRowButtons(entry, entry != null ? StateTreeRowOffers.CatalogOf(sources, entry)
                    : sources.Count > 0 ? sources[0] : null);
            }
            return field;
        }

        private List<StateTreeRegistryAsset> Sources(StateTreeStepParameter parameter)
        {
            return StateTreeSchemaRules.RowSources(m_Tree, m_Schema, m_Node, m_Template, parameter.entryType);
        }

        private static StateTreeRegistryEntry FindRow(List<StateTreeRegistryAsset> sources, string name)
        {
            foreach (StateTreeRegistryAsset registry in sources)
            {
                StateTreeRegistryEntry entry = registry.FindByName(name);
                if (entry != null)
                    return entry;
            }
            return null;
        }

        /// <summary>The step's picker: a placement of its Level's manifest, or a row — this level's catalog
        /// first when an enclosing group names one.</summary>
        private void ShowPick(Rect at, StateTreeStepParameter parameter)
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
                else
                    menu.OfferNew(new List<StateTreeRegistryAsset> { manifest }, byId: true);
            }
            else
            {
                List<StateTreeRegistryAsset> sources = Sources(parameter);
                StateTreeRegistryAsset narrowTo = StateTreeSchemaRules.EnclosingRegistry(m_Tree, m_Schema, m_Node,
                    parameter.entryType);
                menu = StateTreePickItems.Rows(sources, m_Tree, parameter.entryType, narrowTo);
                if (sources.Count == 0)
                    menu.empty = $"No {parameter.entryType.Name} registry in the template's Data";
                menu.OfferNew(NewRowTargets(parameter.entryType, narrowTo));
            }
            menu.Show(at, parameter.stringValue, picked =>
                m_Write(parameter.declaration, picked.value, 0f,
                    parameter.isPlacement ? picked.value : (picked.payload as StateTreeRegistryEntry)?.id ?? "", ""));
        }

        /// <summary>Where "+ New" may make a step's row: the catalog an enclosing group names, else the
        /// template's own Data — never the project-wide fallback the picker lists when neither declares one.</summary>
        internal List<StateTreeRegistryAsset> NewRowTargets(Type entryType, StateTreeRegistryAsset narrowTo)
        {
            var targets = new List<StateTreeRegistryAsset>();
            if (narrowTo != null)
            {
                targets.Add(narrowTo);
                return targets;
            }
            for (int i = 0; m_Template != null && entryType != null && i < m_Template.registries.Count; i++)
            {
                StateTreeRegistryAsset registry = m_Template.registries[i];
                if (registry != null && entryType.IsAssignableFrom(registry.entryType) && !targets.Contains(registry))
                    targets.Add(registry);
            }
            return targets;
        }
    }
}
