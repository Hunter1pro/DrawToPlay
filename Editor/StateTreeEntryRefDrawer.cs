using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// Every typed reference (<see cref="StateTreeEntryRef{TEntry}"/>) as a PICKER wherever the
    /// regular Inspector draws one — registry dashboards above all, which draw entries field by
    /// field and would otherwise show a raw id/name pair for a reference that is supposed to be
    /// a known-list choice.
    ///
    /// IT OPENS THE NODE PICKER, not a dropdown. A <c>DropdownField</c> is a flat alphabetical
    /// strip of names: fine for four items, unusable at a hundred, and it can never say what a row
    /// IS. A registry is exactly the thing that grows — items, levels, tags, doors — so the
    /// picker's search, collapsible categories (the row's own <c>group</c> path) and one-line
    /// description are worth more here than anywhere. It is the same window the task list uses
    /// (<see cref="StateTreeNodePicker"/>) through the rows-supplied seam
    /// (<see cref="StateTreePickerItem"/>), so choosing a task and choosing a row feel like one
    /// gesture rather than two conventions.
    ///
    /// The entry TYPE comes from the field's own generic argument, and the rows come from every
    /// registry asset in the project whose <see cref="StateTreeRegistryAsset.entryType"/>
    /// matches — so a new registry kind gets this picker with no editor work, exactly like the
    /// dashboard itself. Picking writes id AND name (the id is the reference, the name is the
    /// display cache the rename rule keeps fresh); a reference whose row is gone shows as
    /// itself rather than silently resetting.
    /// </summary>
    [CustomPropertyDrawer(typeof(StateTreeEntryRef<>))]
    internal sealed class StateTreeEntryRefDrawer : PropertyDrawer
    {
        private const string k_Unset = "(none)";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty idProperty = property.FindPropertyRelative("entryId");
            SerializedProperty nameProperty = property.FindPropertyRelative("entryName");
            Type entryType = EntryTypeOf(fieldInfo?.FieldType);
            if (idProperty == null || nameProperty == null || entryType == null)
                return new PropertyField(property);

            var owner = property.serializedObject.targetObject as StateTreeRegistryAsset;

            var row = new VisualElement();
            row.AddToClassList("unity-base-field");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = new Label(NiceLabel(property, entryType));
            label.AddToClassList("unity-base-field__label");
            row.Add(label);

            // The button IS the value: it reads as the chosen row, and pressing it opens the
            // picker. A separate "…" button beside a read-only field would spend a column on
            // something that has one action.
            var button = new Button { text = ButtonText(nameProperty.stringValue) };
            button.style.flexGrow = 1f;
            button.style.flexBasis = 0f;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.tooltip = "Pick a " + entryType.Name + " row. Search, or browse by the rows' "
                + "own group paths.";
            button.clicked += () =>
            {
                var rows = new List<SourcedRow>();
                CollectRows(entryType, rows, owner);

                StateTreeNodePicker.ShowItems(StateTreeNodePicker.ScreenRectOf(button),
                    ItemsFor(entryType, rows),
                    payload =>
                    {
                        // Re-fetched rather than captured: the picker outlives this callback's
                        // creation, and a SerializedProperty from a stale SerializedObject would
                        // write into nothing.
                        property.serializedObject.Update();
                        SerializedProperty liveId = property.serializedObject
                            .FindProperty(property.propertyPath).FindPropertyRelative("entryId");
                        SerializedProperty liveName = property.serializedObject
                            .FindProperty(property.propertyPath).FindPropertyRelative("entryName");

                        var picked = payload as StateTreeRegistryEntry;
                        liveName.stringValue = picked != null ? picked.name : string.Empty;
                        liveId.stringValue = picked != null ? picked.id : string.Empty;
                        property.serializedObject.ApplyModifiedProperties();
                        button.text = ButtonText(liveName.stringValue);
                    },
                    "Pick " + entryType.Name.Replace("Def", ""),
                    "Entry_" + entryType.Name);
            };
            row.Add(button);
            return row;
        }

        /// <summary>What the button reads when a row is chosen, and when none is. "(none)" is a
        /// real state — a reference nobody has filled in yet — and has to be distinguishable from
        /// a row that happens to be called nothing.</summary>
        private static string ButtonText(string entryName)
        {
            return string.IsNullOrEmpty(entryName) ? k_Unset : entryName;
        }

        /// <summary>
        /// The rows as picker items: the row's <c>group</c> becomes the category, and the first
        /// text field the entry class adds beyond the base three becomes the description.
        ///
        /// THE DESCRIPTION IS FOUND BY SHAPE, not named: an entry class is free to call its human
        /// text whatever it likes (<c>displayName</c>, <c>speaker</c>, <c>summary</c>), and the
        /// M13 rule is that declaring a registry kind costs an entry class and nothing else. A
        /// convention that required a specific field name would be a second thing to remember, and
        /// the row would silently lose its description when someone forgot it.
        /// </summary>
        /// <param name="entryType">The entry class being listed.</param>
        /// <param name="rows">The rows to offer, each with the registry it came from.</param>
        /// <returns>One item per row.</returns>
        private static List<StateTreePickerItem> ItemsFor(Type entryType, List<SourcedRow> rows)
        {
            System.Reflection.FieldInfo descriptionField = null;
            foreach (System.Reflection.FieldInfo field in entryType.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType != typeof(string))
                    continue;
                if (field.Name == "id" || field.Name == "name" || field.Name == "group")
                    continue;
                descriptionField = field;
                break;
            }

            var items = new List<StateTreePickerItem>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                StateTreeRegistryEntry entry = rows[i].entry;
                if (entry == null || string.IsNullOrEmpty(entry.name))
                    continue;

                items.Add(new StateTreePickerItem
                {
                    displayName = entry.name,
                    category = entry.group ?? string.Empty,
                    description = descriptionField?.GetValue(entry) as string ?? string.Empty,
                    // WHICH CATALOG, because two registries of the same kind routinely hold rows
                    // with the same name — three area manifests each with a 'medkit 1'. Without
                    // this the list shows the same word three times and the author has to guess.
                    identity = rows[i].registry != null
                        ? rows[i].registry.name
                        : entryType.Name,
                    // The ID, so starring a row survives renaming it — the same promise the
                    // reference itself makes.
                    persistKey = entry.id,
                    payload = entry
                });
            }
            return items;
        }

        private static string NiceLabel(SerializedProperty property, Type entryType)
        {
            string label = ObjectNames.NicifyVariableName(property.name);
            return label + "  (" + entryType.Name.Replace("Def", "") + ")";
        }

        /// <summary>The concrete entry type behind the field — the generic argument of
        /// <see cref="StateTreeEntryRef{TEntry}"/>, walked up through list/array element types
        /// so a List&lt;ref&gt; element draws like a lone one.</summary>
        private static Type EntryTypeOf(Type fieldType)
        {
            for (Type walk = fieldType; walk != null; walk = walk.BaseType)
            {
                if (walk.IsGenericType
                    && walk.GetGenericTypeDefinition() == typeof(StateTreeEntryRef<>))
                    return walk.GetGenericArguments()[0];
            }
            if (fieldType != null && fieldType.IsArray)
                return EntryTypeOf(fieldType.GetElementType());
            if (fieldType != null && fieldType.IsGenericType
                && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return EntryTypeOf(fieldType.GetGenericArguments()[0]);
            return null;
        }

        /// <summary>
        /// The rows this reference may choose from.
        ///
        /// NARROWED BY <see cref="StateTreeRegistryAsset.dependsOn"/> when the reference lives ON a
        /// registry row and that registry declares a dependency answering for the entry class —
        /// so a dialog registry that depends on the M21 item registry offers those four items and
        /// not every ItemDef in the project. Declaring the edge is what earns the narrowing.
        ///
        /// EVERYWHERE ELSE THE OLD RULE STANDS: every registry of the right entry class, project
        /// wide. A row whose registry declares nothing, and a reference on a task or a component,
        /// both keep the list they have always had — narrowing them would empty dropdowns that
        /// work today, which is not an upgrade anyone asked for.
        /// </summary>
        /// <param name="entryType">The entry class the reference wants.</param>
        /// <param name="into">Accumulator.</param>
        /// <param name="owner">The registry this reference lives on, or null when it does not.</param>
        private static void CollectRows(Type entryType, List<SourcedRow> into,
            StateTreeRegistryAsset owner)
        {
            if (owner != null && TryCollectDeclared(entryType, into, owner))
                return;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(StateTreeRegistryAsset));
            for (int i = 0; i < guids.Length; i++)
            {
                var registry = AssetDatabase.LoadAssetAtPath<StateTreeRegistryAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (registry == null || registry.entryType != entryType)
                    continue;
                Collect(registry, into);
            }
        }

        /// <summary>Rows from the owner's declared dependency closure. False — and nothing
        /// collected — when the closure answers for no such entry class, which is what sends the
        /// caller back to the project-wide list.</summary>
        private static bool TryCollectDeclared(Type entryType, List<SourcedRow> into,
            StateTreeRegistryAsset owner)
        {
            var reachable = new List<StateTreeRegistryAsset>();
            owner.CollectWithDependencies(reachable);

            var answered = false;
            for (int i = 0; i < reachable.Count; i++)
            {
                // The owner's own rows are not a dependency of itself.
                if (reachable[i] == owner || reachable[i].entryType != entryType)
                    continue;
                answered = true;
                Collect(reachable[i], into);
            }
            return answered;
        }

        private static void Collect(StateTreeRegistryAsset registry, List<SourcedRow> into)
        {
            for (int j = 0; j < registry.Count; j++)
            {
                StateTreeRegistryEntry row = registry.EntryAt(j);
                if (row != null)
                    into.Add(new SourcedRow { registry = registry, entry = row });
            }
        }

        /// <summary>A row and the registry it was found in — the pair the picker needs, because
        /// "which catalog" is not something a row carries and is the only thing that tells two
        /// identically-named rows apart.</summary>
        private struct SourcedRow
        {
            internal StateTreeRegistryAsset registry;
            internal StateTreeRegistryEntry entry;
        }
    }
}
