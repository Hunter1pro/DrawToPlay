using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>A sub-tree's state, picked — the drawer for <see cref="SubTreeStateAttribute"/>.</summary>
    [CustomPropertyDrawer(typeof(SubTreeStateAttribute))]
    internal sealed class SubTreeStateDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new Label(property.displayName + ": [SubTreeState] needs a string");
            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath, property.displayName);
            return container;
        }

        private static List<string> States(Object owner)
        {
            var ids = new List<string>();
            if (owner is RunSubTreeTask run && run.subTree != null)
            {
                foreach (StateTreeNodeAsset node in StateTreeEditorOps.CollectNodes(run.subTree))
                {
                    if (node != null && !string.IsNullOrEmpty(node.nodeId))
                        ids.Add(node.nodeId);
                }
            }
            return ids;
        }

        private static void Build(VisualElement container, SerializedObject owner, string path, string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;
            string current = property.stringValue;
            List<string> states = States(owner.targetObject);
            container.Add(new StateTreePickField(label, current, !string.IsNullOrEmpty(current) && !states.Contains(current),
                "A state of the sub-tree this task runs.", at =>
                {
                    var menu = new GenericMenu();
                    if (states.Count == 0)
                        menu.AddDisabledItem(new GUIContent("No sub-tree picked"));
                    foreach (string id in states)
                    {
                        string chosen = id;
                        menu.AddItem(new GUIContent(id.Replace('/', '∕')), id == current, () =>
                        {
                            owner.Update();
                            SerializedProperty live = owner.FindProperty(path);
                            if (live == null)
                                return;
                            live.stringValue = chosen;
                            owner.ApplyModifiedProperties();
                            Build(container, owner, path, label);
                        });
                    }
                    menu.DropDown(at);
                }));
        }
    }
}
