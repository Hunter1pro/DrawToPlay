using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A program's PARAMETERS wherever the regular Inspector draws a
    /// <see cref="GraphTaskParameterSet"/> — registry dashboards above all, so a row that assigns
    /// a conversation can also tune it, the way a state that runs a task can.
    ///
    /// THE SAME MODEL AS THE STATE TREE, deliberately: the callee declares the knobs and their
    /// defaults (<see cref="GraphTaskAsset.parameters"/>, baked from the graph's variables) and the
    /// caller ticks the ones it wants its own value for. Hence a CHECKBOX per row rather than a
    /// plain field — "3" typed into an unticked row and "3" typed into a ticked one mean different
    /// things (follow the program vs pin this caller to 3), and the difference has to survive the
    /// program's author changing their mind about the default. An unticked row shows that default,
    /// disabled and dimmed, because that is what actually runs.
    ///
    /// THE SECTION IS BUILT FROM THE DECLARATION, never from the stored overrides, so a row exists
    /// for every knob whether or not this caller has touched it — a knob nobody knows about is a
    /// knob nobody turns. Stored rows that resolve to no declaration are listed after, as the
    /// warnings they are, with a way to delete them; the alternative is a value that silently does
    /// nothing.
    ///
    /// EVERY BINDING IS BY ID (<see cref="GraphTaskParameterOverride.Matches"/>), which is what
    /// lets the program's author rename a parameter without reaching into every caller. The name is
    /// what the row SHOWS, never what it matches on.
    ///
    /// WRITES GO THROUGH SerializedProperty, unlike the tree inspector's equivalent, because here
    /// the whole set lives inside one asset the Inspector already has a SerializedObject for — so
    /// undo, prefab overrides and multi-edit come for free instead of being re-implemented.
    /// </summary>
    [CustomPropertyDrawer(typeof(GraphTaskParameterSet))]
    internal sealed class GraphTaskParametersDrawer : PropertyDrawer
    {
        private const string k_ValuesProperty = "values";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.style.marginTop = 4f;

            var attribute = fieldInfo?.GetCustomAttribute<GraphTaskParametersAttribute>();
            if (attribute == null)
            {
                root.Add(new HelpBox("A GraphTaskParameterSet field needs "
                    + "[GraphTaskParameters(nameof(<the program field>))] so it knows which "
                    + "program's parameters to show.", HelpBoxMessageType.Error));
                return root;
            }

            SerializedProperty values = property.FindPropertyRelative(k_ValuesProperty);
            SerializedProperty programProperty = SiblingProperty(property, attribute.programField);
            if (values == null || programProperty == null)
            {
                root.Add(new HelpBox($"'{attribute.programField}' is not a serialized field beside "
                    + "this one, so its parameters cannot be found. Fix the name in "
                    + "[GraphTaskParameters].", HelpBoxMessageType.Error));
                return root;
            }

            // Rebuilt on every change of the program slot: a different program declares different
            // knobs, and rows for the old one would be worse than none.
            var body = new VisualElement();
            root.Add(body);
            Rebuild(body, property.Copy(), values.Copy(), programProperty.Copy());

            var watcher = new ObjectField { style = { display = DisplayStyle.None } };
            watcher.BindProperty(programProperty);
            watcher.RegisterValueChangedCallback(_ =>
                Rebuild(body, property.Copy(), values.Copy(), programProperty.Copy()));
            root.Add(watcher);

            return root;
        }

        /// <summary>Draw the section for whatever program the slot holds right now.</summary>
        /// <param name="body">Container to fill; cleared first.</param>
        /// <param name="setProperty">The set itself — the undo/apply target.</param>
        /// <param name="values">The stored override rows.</param>
        /// <param name="programProperty">The sibling program slot.</param>
        private static void Rebuild(VisualElement body, SerializedProperty setProperty,
            SerializedProperty values, SerializedProperty programProperty)
        {
            body.Clear();

            List<GraphTaskParameter> declared = DeclaredOf(programProperty.objectReferenceValue);
            if (declared == null)
            {
                // Silent: an empty program slot is a normal half-authored row, not a problem, and
                // a warning here would shout at every new entry.
                return;
            }
            int count = declared != null ? declared.Count : 0;
            List<int> stale = StaleRows(values, declared);
            if (count == 0 && stale.Count == 0)
                return;

            var title = new Label("Parameters (" + count + ")");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 4f;
            title.style.marginLeft = 8f;
            body.Add(title);

            if (count > 0)
            {
                var hint = new Label("Tick a parameter to give this row its own value. Unticked "
                    + "rows show what the program itself uses, and follow it when it changes.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.opacity = 0.75f;
                hint.style.marginLeft = 8f;
                hint.style.marginBottom = 2f;
                body.Add(hint);

                for (int i = 0; i < count; i++)
                {
                    GraphTaskParameter parameter = declared[i];
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;
                    body.Add(string.IsNullOrEmpty(parameter.id)
                        ? IdlessRow(parameter)
                        : Row(setProperty, values, parameter));
                }
            }

            for (int i = 0; i < stale.Count; i++)
                body.Add(StaleRow(setProperty, values, stale[i]));
        }

        /// <summary>
        /// One knob: the tick, the name, and the value field for its kind.
        /// </summary>
        /// <param name="setProperty">The undo/apply target.</param>
        /// <param name="values">The stored override rows.</param>
        /// <param name="parameter">The declaration this row stands for.</param>
        /// <returns>The row.</returns>
        private static VisualElement Row(SerializedProperty setProperty, SerializedProperty values,
            GraphTaskParameter parameter)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginLeft = 8f;

            int index = IndexOf(values, parameter);
            bool overridden = index >= 0 && Element(values, index)
                .FindPropertyRelative("enabled").boolValue;

            var toggle = new Toggle { value = overridden };
            toggle.style.marginRight = 2f;
            toggle.tooltip = "Ticked: this row's value. Unticked: the program's own default ("
                + DefaultLabel(parameter) + ").";
            row.Add(toggle);

            var label = new Label(ObjectNames.NicifyVariableName(parameter.name));
            label.style.width = 120f;
            label.style.flexShrink = 0f;
            label.tooltip = parameter.kind + " parameter of " + parameter.name + ".";
            row.Add(label);

            VisualElement field = ValueField(setProperty, values, parameter, () => IndexOf(values, parameter));
            field.style.flexGrow = 1f;
            field.SetEnabled(overridden);
            field.style.opacity = overridden ? 1f : 0.55f;
            row.Add(field);

            toggle.RegisterValueChangedCallback(changed =>
            {
                SetOverridden(setProperty, values, parameter, changed.newValue);
                field.SetEnabled(changed.newValue);
                field.style.opacity = changed.newValue ? 1f : 0.55f;
            });

            return row;
        }

        /// <summary>The editor for one kind, seeded from the stored row when there is one and from
        /// the program's default when there is not — so ticking a row starts from what was already
        /// running rather than from zero.</summary>
        private static VisualElement ValueField(SerializedProperty setProperty,
            SerializedProperty values, GraphTaskParameter parameter, Func<int> indexOf)
        {
            int index = indexOf();

            switch (parameter.kind)
            {
                case GraphTaskParameterKind.Bool:
                {
                    var toggle = new Toggle
                    {
                        value = index >= 0
                            ? Element(values, index).FindPropertyRelative("floatValue").floatValue != 0f
                            : parameter.floatValue != 0f
                    };
                    toggle.RegisterValueChangedCallback(changed => Write(setProperty, values,
                        parameter, indexOf, "floatValue", changed.newValue ? 1f : 0f));
                    return toggle;
                }

                case GraphTaskParameterKind.String:
                {
                    var text = new TextField { isDelayed = true };
                    text.style.flexGrow = 1f;
                    text.SetValueWithoutNotify(index >= 0
                        ? Element(values, index).FindPropertyRelative("stringValue").stringValue
                        : parameter.stringValue ?? string.Empty);
                    text.RegisterValueChangedCallback(changed => Write(setProperty, values,
                        parameter, indexOf, "stringValue", changed.newValue ?? string.Empty));

                    // A String parameter is almost always a NAME the program resolves — an item,
                    // a level, a tag. The rows that could legally be that name are exactly the
                    // ones this registry declared in Depends On, so they are one click away
                    // instead of something to remember and mistype. Still a text field, because
                    // a parameter that is not a registry name (a greeting, a key) must stay
                    // typeable — the picker fills it in, it does not replace it.
                    var pair = new VisualElement();
                    pair.style.flexDirection = FlexDirection.Row;
                    pair.style.flexGrow = 1f;
                    pair.Add(text);

                    var pick = new Button { text = "⛃" };
                    pick.style.width = 22f;
                    pick.style.flexShrink = 0f;
                    pick.tooltip = "Pick a row from the registries this one Depends On.";
                    pick.clicked += () => ShowEntryMenu(setProperty, pick,
                        picked => text.value = picked);
                    pair.Add(pick);

                    return pair;
                }

                default:
                {
                    var number = new FloatField { isDelayed = true };
                    number.SetValueWithoutNotify(index >= 0
                        ? Element(values, index).FindPropertyRelative("floatValue").floatValue
                        : parameter.floatValue);
                    number.RegisterValueChangedCallback(changed => Write(setProperty, values,
                        parameter, indexOf, "floatValue", changed.newValue));
                    return number;
                }
            }
        }

        /// <summary>
        /// The rows this registry can legally name, as a menu — every entry of every registry in
        /// its <see cref="StateTreeRegistryAsset.dependsOn"/> closure, submenued by entry class
        /// and by group path so a catalog of two hundred items is still navigable.
        ///
        /// THE SAME EDGE THE GRAPH USES. A dialog registry that declares "I depend on the item
        /// registry" gets item rows offered here AND its graphs checked against them
        /// (<c>EntryRefValidator</c>) — one declaration, both ends. A registry that declares
        /// nothing says so, rather than showing an empty menu that reads as "there is no data".
        /// </summary>
        /// <param name="setProperty">Anything from the registry's SerializedObject — the owning
        /// asset is what the closure starts from.</param>
        /// <param name="anchor">Element to drop the menu under.</param>
        /// <param name="picked">Called with the chosen row's name.</param>
        private static void ShowEntryMenu(SerializedProperty setProperty, VisualElement anchor,
            Action<string> picked)
        {
            var menu = new GenericMenu();
            var owner = setProperty.serializedObject.targetObject as StateTreeRegistryAsset;

            var reachable = new List<StateTreeRegistryAsset>();
            owner?.CollectWithDependencies(reachable);

            var offered = 0;
            for (int i = 0; i < reachable.Count; i++)
            {
                StateTreeRegistryAsset registry = reachable[i];
                // Its OWN rows are not offered: a parameter of a dialog naming another dialog is
                // not what Depends On is for, and it would bury the rows that are.
                if (registry == owner)
                    continue;

                for (int r = 0; r < registry.Count; r++)
                {
                    StateTreeRegistryEntry entry = registry.EntryAt(r);
                    if (entry == null || string.IsNullOrEmpty(entry.name))
                        continue;

                    string group = string.IsNullOrEmpty(entry.group)
                        ? string.Empty
                        : entry.group + "/";
                    string row = entry.name;
                    menu.AddItem(new GUIContent(registry.entryType.Name + "/" + group + row),
                        false, () => picked(row));
                    offered++;
                }
            }

            if (offered == 0)
            {
                menu.AddDisabledItem(new GUIContent(owner == null
                    ? "Not a registry — nothing to pick from"
                    : "Add a registry to this one's 'Depends On' list first"));
            }

            menu.DropDown(anchor.worldBound);
        }

        /// <summary>Write one field of this parameter's row, creating the row if the author edited
        /// a value before ticking the box — which is the natural gesture, and typing into a field
        /// that then throws the value away would be indefensible.</summary>
        private static void Write(SerializedProperty setProperty, SerializedProperty values,
            GraphTaskParameter parameter, Func<int> indexOf, string relative, object value)
        {
            setProperty.serializedObject.Update();
            int index = indexOf();
            if (index < 0)
                index = Append(values, parameter);

            SerializedProperty element = Element(values, index);
            switch (value)
            {
                case float number:
                    element.FindPropertyRelative(relative).floatValue = number;
                    break;
                case string text:
                    element.FindPropertyRelative(relative).stringValue = text;
                    break;
            }
            setProperty.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Tick or untick one knob. Unticking KEEPS the row and its value: the author may
        /// be comparing against the default, and losing what they typed for looking would be its
        /// own small betrayal. A row that is off does nothing at runtime
        /// (<see cref="GraphTaskParameterOverride.EnabledFor"/>).</summary>
        private static void SetOverridden(SerializedProperty setProperty, SerializedProperty values,
            GraphTaskParameter parameter, bool overridden)
        {
            setProperty.serializedObject.Update();
            int index = IndexOf(values, parameter);
            if (index < 0)
            {
                if (!overridden)
                    return;
                index = Append(values, parameter);
            }
            Element(values, index).FindPropertyRelative("enabled").boolValue = overridden;
            setProperty.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>A stored row bound to no declaration — the program's author renamed or deleted
        /// the parameter. Reported rather than swept up: deleting it here would be deciding for
        /// them, and leaving it silent is how a value that does nothing survives for months.</summary>
        private static VisualElement StaleRow(SerializedProperty setProperty,
            SerializedProperty values, int index)
        {
            SerializedProperty element = Element(values, index);
            string name = element.FindPropertyRelative("name").stringValue;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginLeft = 8f;

            var help = new HelpBox($"'{name}' is set here, but the program declares no parameter "
                + "by that name any more — it was renamed or removed. This value does nothing.",
                HelpBoxMessageType.Warning);
            help.style.flexGrow = 1f;
            row.Add(help);

            var remove = new Button(() =>
            {
                setProperty.serializedObject.Update();
                values.DeleteArrayElementAtIndex(index);
                setProperty.serializedObject.ApplyModifiedProperties();
            })
            { text = "✕" };
            remove.style.width = 22f;
            remove.style.flexShrink = 0f;
            remove.tooltip = "Forget this leftover value.";
            row.Add(remove);

            return row;
        }

        /// <summary>A declaration with no id cannot be bound to, and says so where its row would
        /// have been. Only a program baked before parameters had identity reaches this; minting an
        /// id here would not survive the next reimport, so the fix is named instead.</summary>
        private static VisualElement IdlessRow(GraphTaskParameter parameter)
        {
            var help = new HelpBox($"'{parameter.name}' was baked before parameters had ids, so it "
                + "cannot be overridden here. Open the graph and save it — the re-bake gives it "
                + $"one, and its default ({DefaultLabel(parameter)}) is what runs until then.",
                HelpBoxMessageType.Warning);
            help.style.marginLeft = 8f;
            return help;
        }

        /// <summary>Indices of stored rows that match no declaration.</summary>
        private static List<int> StaleRows(SerializedProperty values,
            List<GraphTaskParameter> declared)
        {
            var stale = new List<int>();
            for (int i = 0; i < values.arraySize; i++)
            {
                string id = Element(values, i).FindPropertyRelative("id").stringValue;
                if (string.IsNullOrEmpty(id))
                    continue;

                var found = false;
                for (int d = 0; declared != null && d < declared.Count && !found; d++)
                {
                    found = declared[d] != null
                        && string.Equals(declared[d].id, id, StringComparison.Ordinal);
                }
                if (!found)
                    stale.Add(i);
            }
            return stale;
        }

        /// <summary>The stored row for one declaration, by id, or -1.</summary>
        private static int IndexOf(SerializedProperty values, GraphTaskParameter parameter)
        {
            for (int i = 0; i < values.arraySize; i++)
            {
                if (string.Equals(Element(values, i).FindPropertyRelative("id").stringValue,
                    parameter.id, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        /// <summary>Add a row for one declaration, seeded from the program's default so ticking a
        /// box never changes the value by itself.</summary>
        private static int Append(SerializedProperty values, GraphTaskParameter parameter)
        {
            int index = values.arraySize;
            values.InsertArrayElementAtIndex(index);

            SerializedProperty element = Element(values, index);
            element.FindPropertyRelative("id").stringValue = parameter.id;
            element.FindPropertyRelative("name").stringValue = parameter.name;
            element.FindPropertyRelative("enabled").boolValue = false;
            element.FindPropertyRelative("floatValue").floatValue = parameter.floatValue;
            element.FindPropertyRelative("stringValue").stringValue =
                parameter.stringValue ?? string.Empty;
            SerializedProperty keyId = element.FindPropertyRelative("keyId");
            if (keyId != null)
                keyId.stringValue = string.Empty;
            SerializedProperty source = element.FindPropertyRelative("sourceParameterId");
            if (source != null)
                source.stringValue = string.Empty;
            return index;
        }

        private static SerializedProperty Element(SerializedProperty values, int index)
        {
            return values.GetArrayElementAtIndex(index);
        }

        /// <summary>
        /// The declarations of whatever the sibling slot holds — a graph program or a STATE TREE,
        /// because both declare the same parameter rows (the one-vocabulary rule on
        /// <see cref="StateTreeAsset.parameters"/>) and both are called with the same override
        /// rows. A level manifest row naming a tree gets the same knobs section a dialog row
        /// naming a program does. Null means the slot is empty or holds neither.
        /// </summary>
        private static List<GraphTaskParameter> DeclaredOf(UnityEngine.Object referenced)
        {
            switch (referenced)
            {
                case GraphTaskAsset program:
                    return program.parameters ?? s_NoParameters;
                case StateTreeAsset tree:
                    return tree.parameters ?? s_NoParameters;
                default:
                    return null;
            }
        }

        /// <summary>Stands in for a null declaration list so "empty slot" (draw nothing) and
        /// "asset with no parameters" (draw nothing but still check stale rows) stay distinct.</summary>
        private static readonly List<GraphTaskParameter> s_NoParameters =
            new List<GraphTaskParameter>();

        /// <summary>The sibling field named by the attribute — resolved through the PARENT of this
        /// property, so it works at any depth (a registry row is an array element of an asset).</summary>
        private static SerializedProperty SiblingProperty(SerializedProperty property, string name)
        {
            string path = property.propertyPath;
            int cut = path.LastIndexOf('.');
            if (cut < 0)
                return property.serializedObject.FindProperty(name);

            return property.serializedObject.FindProperty(path.Substring(0, cut + 1) + name);
        }

        /// <summary>The program's default, in the author's words, for the tooltips that explain
        /// what an unticked row is doing.</summary>
        private static string DefaultLabel(GraphTaskParameter parameter)
        {
            switch (parameter.kind)
            {
                case GraphTaskParameterKind.Bool:
                    return parameter.floatValue != 0f ? "on" : "off";
                case GraphTaskParameterKind.String:
                    return "'" + (parameter.stringValue ?? string.Empty) + "'";
                default:
                    return parameter.floatValue.ToString("0.###");
            }
        }
    }
}
