using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A preset's setting: one parameter of the tree its sibling field holds, picked by name.</summary>
    [CustomPropertyDrawer(typeof(PresetSettingsAttribute))]
    internal sealed class PresetSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var settings = (PresetSettingsAttribute)attribute;
            StateTreeAsset tree = Sibling(property, settings.treeField)?.objectReferenceValue as StateTreeAsset;
            var parameters = new List<GraphTaskParameter>();
            if (tree != null)
                parameters.AddRange(tree.parameters.FindAll(p => p != null && !string.IsNullOrEmpty(p.id)));
            string current = property.stringValue;
            GraphTaskParameter picked = parameters.Find(p => p.id == current);
            string label = property.displayName;
            return new StateTreePickField(label, picked != null ? picked.name : current,
                picked == null && !string.IsNullOrEmpty(current),
                tree != null ? "A parameter of " + tree.name + " each object sets for itself." : "Pick the tree first.",
                at =>
                {
                    var menu = new StateTreePickMenu { title = "Setting", empty = "The tree declares no parameters" };
                    foreach (GraphTaskParameter parameter in parameters)
                        menu.items.Add(new StateTreePickItem(parameter.id, parameter.name, parameter.kind.ToString()));
                    menu.Show(at, current, item =>
                    {
                        property.serializedObject.Update();
                        property.stringValue = item.value ?? "";
                        property.serializedObject.ApplyModifiedProperties();
                    });
                });
        }

        /// <summary>The field <paramref name="name"/> beside a list element or a plain field.</summary>
        internal static SerializedProperty Sibling(SerializedProperty property, string name)
        {
            string path = property.propertyPath;
            int array = path.LastIndexOf(".Array.data[", System.StringComparison.Ordinal);
            if (array >= 0)
                path = path.Substring(0, array);
            int dot = path.LastIndexOf('.');
            string owner = dot >= 0 ? path.Substring(0, dot + 1) : "";
            return property.serializedObject.FindProperty(owner + name);
        }
    }

    /// <summary>A mission step kind, picked from the template kinds of the project's tree schemas.</summary>
    [CustomPropertyDrawer(typeof(StepKindAttribute))]
    internal sealed class StepKindDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            string current = property.stringValue;
            StateTreeSchemaKind known = Find(current);
            return new StateTreePickField(property.displayName, known != null ? known.Label : current,
                known == null && !string.IsNullOrEmpty(current), "A step of a mission tree an object fits.",
                at => Menu().Show(at, current, item =>
                {
                    property.serializedObject.Update();
                    property.stringValue = item.value ?? "";
                    property.serializedObject.ApplyModifiedProperties();
                }));
        }

        /// <summary>Every template kind of every schema, grouped by the schema's mode.</summary>
        internal static StateTreePickMenu Menu()
        {
            var menu = new StateTreePickMenu { title = "Step", empty = "No tree schema declares a step" };
            var seen = new HashSet<string>();
            foreach (StateTreeSchema schema in StateTreeSchemas.All())
            {
                foreach (StateTreeSchemaKind kind in schema.kinds)
                {
                    if (kind != null && kind.isTemplate && seen.Add(kind.kind))
                        menu.items.Add(new StateTreePickItem(kind.kind, kind.Label, kind.kind, schema.modeName));
                }
            }
            return menu;
        }

        internal static StateTreeSchemaKind Find(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return null;
            foreach (StateTreeSchema schema in StateTreeSchemas.All())
            {
                StateTreeSchemaKind found = schema.Kind(kind);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
