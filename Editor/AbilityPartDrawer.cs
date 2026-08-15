using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// One <see cref="AbilityPartDef"/> row, drawn under its service's law (M23): the KIND is
    /// a dropdown of what the <see cref="ServiceDef.nestingRules"/> allow at this row's depth
    /// — an illegal child is not refused after the fact, it is UNPICKABLE, the same
    /// picked-not-typed rule every reference in this toolset follows. Data that arrived wrong
    /// anyway (a builder script, an old asset) shows an inline error naming the rule it
    /// breaks, mirroring <see cref="AbilityRules"/> exactly.
    ///
    /// Fields show by kind — an effect's magnitude beside an effect, a cue's name beside a
    /// cue — because a bag of every field for every kind is how the one-class model turns
    /// from a modeling choice into an authoring tax.
    /// </summary>
    [CustomPropertyDrawer(typeof(AbilityPartDef))]
    internal sealed class AbilityPartDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.style.marginLeft = 4f;
            Rebuild(root, property.Copy());
            return root;
        }

        private static void Rebuild(VisualElement root, SerializedProperty property)
        {
            root.Clear();

            SerializedProperty kindProp = property.FindPropertyRelative("kind");
            string parentKind = ParentKindOf(property);
            ServiceDef service = ServiceFor(property.serializedObject.targetObject);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            root.Add(header);

            if (service != null && !string.IsNullOrEmpty(parentKind))
            {
                IReadOnlyList<string> allowed = service.AllowedUnder(parentKind);
                var choices = new List<string>(allowed);
                string current = kindProp.stringValue;
                bool illegal = !string.IsNullOrEmpty(current) && !choices.Contains(current);
                if (illegal)
                    choices.Add(current);   // shown so the error names it; never OFFERED fresh
                if (choices.Count == 0)
                    choices.Add(current ?? "");

                var kindField = new DropdownField("Kind", choices,
                    Mathf.Max(0, choices.IndexOf(current)));
                kindField.style.flexGrow = 1f;
                kindField.RegisterValueChangedCallback(evt =>
                {
                    property.serializedObject.Update();
                    kindProp.stringValue = evt.newValue;
                    property.serializedObject.ApplyModifiedProperties();
                    Rebuild(root, property);
                });
                header.Add(kindField);

                if (illegal)
                {
                    root.Add(new HelpBox("A '" + current + "' cannot sit under '" + parentKind
                        + "' — the service's rules allow [" + string.Join(", ", allowed)
                        + "]. Re-pick the kind or move the part.", HelpBoxMessageType.Error));
                }
            }
            else
            {
                // No service claims this registry (or the part is orphaned): the field stays
                // typable rather than locked to an empty list, and validation elsewhere says
                // whether anyone cares.
                var kindField = new PropertyField(kindProp, "Kind");
                kindField.style.flexGrow = 1f;
                kindField.Bind(property.serializedObject);
                header.Add(kindField);
            }

            Add(root, property, "name");

            string kind = kindProp.stringValue;
            if (kind == AbilityPartDef.EffectKind)
            {
                Add(root, property, "attribute");
                Add(root, property, "magnitude");
                Add(root, property, "duration");
                SerializedProperty duration = property.FindPropertyRelative("duration");
                if (duration.enumValueIndex != (int)AbilityEffectDuration.Instant)
                {
                    Add(root, property, "seconds");
                    Add(root, property, "tickInterval");
                    Add(root, property, "maxStacks");
                    Add(root, property, "stacking");
                    Add(root, property, "grantedTags");
                }
            }
            else if (kind == AbilityPartDef.CueKind)
            {
                Add(root, property, "cueName");
            }

            // Children only where the rules allow any — a leaf kind gets no empty list
            // inviting rows the dropdown would then have to refuse.
            if (service == null || service.AllowedUnder(kind).Count > 0)
                Add(root, property, "children");
        }

        private static void Add(VisualElement root, SerializedProperty property, string field)
        {
            SerializedProperty child = property.FindPropertyRelative(field);
            if (child == null)
                return;
            var propertyField = new PropertyField(child);
            propertyField.Bind(property.serializedObject);
            root.Add(propertyField);
        }

        /// <summary>The kind of the part ABOVE this one, read off the property path: a part
        /// directly in a row's 'parts' list sits under the service's root kind; a part in
        /// another part's 'children' sits under that part's kind.</summary>
        private static string ParentKindOf(SerializedProperty property)
        {
            string path = property.propertyPath;
            int childrenAt = path.LastIndexOf(".children.Array.data[",
                System.StringComparison.Ordinal);
            int partsAt = path.LastIndexOf(".parts.Array.data[", System.StringComparison.Ordinal);
            if (childrenAt > partsAt && childrenAt >= 0)
            {
                SerializedProperty parent = property.serializedObject.FindProperty(
                    path.Substring(0, childrenAt) + ".kind");
                return parent != null ? parent.stringValue : "";
            }
            return partsAt >= 0 ? AbilityDef.RootKind : "";
        }

        /// <summary>The ServiceDef that claims this registry — how the drawer knows whose law
        /// applies. First match wins; two defs claiming one registry is its own wiring
        /// problem.</summary>
        private static ServiceDef ServiceFor(Object target)
        {
            var registry = target as StateTreeRegistryAsset;
            if (registry == null)
                return null;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(ServiceDef));
            for (int i = 0; i < guids.Length; i++)
            {
                var def = AssetDatabase.LoadAssetAtPath<ServiceDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (def != null && def.registry == registry)
                    return def;
            }
            return null;
        }
    }
}
