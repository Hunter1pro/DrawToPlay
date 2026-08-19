using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// A TAG, PICKED (M31) — the drawer for <see cref="WorldTagAttribute"/>.
    ///
    /// The offer is the asking asset's own declared vocabularies (a manifest's tag list, a
    /// registry's or def's declared catalogs), grouped by the row's group, so a tag is chosen
    /// from a known list instead of spelled from memory. That is the whole of the fix: this
    /// project matches tags by exact ordinal text, so one wrong capital is a quest that never
    /// completes and a raider nobody can find.
    ///
    /// PICKED IS LOCKED, and picked means "a declared row has this name". A tag no vocabulary
    /// holds stays typeable — a name being invented is not yet a contract — and the field says
    /// which state it is in rather than looking the same either way.
    ///
    /// THE ID MAKES A RENAME TRAVEL: a picked tag stores the row's id beside the name and
    /// re-reads the name through it, so renaming a vocabulary row renames every placement that
    /// carries it instead of orphaning them.
    /// </summary>
    [CustomPropertyDrawer(typeof(WorldTagAttribute))]
    internal sealed class WorldTagDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new PropertyField(property);

            var container = new VisualElement();
            Build(container, property.serializedObject, property.propertyPath,
                property.displayName);
            return container;
        }

        private void Build(VisualElement container, SerializedObject owner, string path,
            string label)
        {
            container.Clear();
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                return;

            WorldTagAttribute marked = Marked();
            var offers = new List<WorldTagDef>();
            bool declared = Offers(owner.targetObject, marked, offers);

            SerializedProperty id = IdOf(property, marked);
            WorldTagDef row = Row(offers, property.stringValue, id);

            // THE RENAME FOLLOWS THE WIRE: the row is the one place the name lives, so a picked
            // tag re-reads it rather than keeping the copy it was given.
            if (row != null && row.name != property.stringValue)
            {
                property.stringValue = row.name;
                owner.ApplyModifiedPropertiesWithoutUndo();
            }

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            container.Add(line);

            var text = new TextField(label);
            text.AddToClassList(TextField.alignedFieldUssClassName);
            text.style.flexGrow = 1f;
            text.BindProperty(property);
            text.SetEnabled(row == null);
            text.tooltip = row == null
                ? "No vocabulary here holds this tag, so it is still free text. ⛭ offers what "
                    + (declared ? "this asset declares." : "the project speaks.")
                : "Picked from '" + row.group + "' — the name follows the row. ⛭ to change it.";
            line.Add(text);

            var pick = new Button { text = "⛭" };
            pick.style.width = 26f;
            pick.style.flexShrink = 0f;
            pick.tooltip = offers.Count == 0
                ? "This asset declares no tag vocabulary yet."
                : "Pick a tag from what this asset declares.";
            pick.clicked += () => ShowMenu(pick, container, owner, path, label);
            line.Add(pick);
        }

        private void ShowMenu(VisualElement anchor, VisualElement container, SerializedObject owner,
            string path, string label)
        {
            WorldTagAttribute marked = Marked();
            var offers = new List<WorldTagDef>();
            bool declared = Offers(owner.targetObject, marked, offers);

            var menu = new GenericMenu();
            if (offers.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(owner.targetObject is LevelObjectRegistry
                    ? "this manifest lists no tag vocabulary — add one to its Tags"
                    : "nothing here declares a tag vocabulary"));
            }
            else if (!declared)
            {
                // SAID OUT LOUD: a component in a scene has no Depends On to read, so it is
                // offered the project's vocabularies rather than nothing. That is a weaker
                // guarantee than an asset's, and the menu should not pretend otherwise.
                menu.AddDisabledItem(new GUIContent("— project-wide (this object declares "
                    + "nothing) —"));
            }

            SerializedProperty live = owner.FindProperty(path);
            string current = live != null ? live.stringValue : "";
            for (int i = 0; i < offers.Count; i++)
            {
                WorldTagDef row = offers[i];
                string entry = string.IsNullOrEmpty(row.group)
                    ? row.name
                    : row.group + "/" + row.name;
                menu.AddItem(new GUIContent(entry), row.name == current,
                    () => Set(owner, path, container, label, row.name, row.id));
            }

            // THE WAY BACK OUT, because a tag can legitimately be spoken before its row exists —
            // a level being sketched, a name waiting for a vocabulary.
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Unpick — type it freely"), false,
                () => Set(owner, path, container, label, current, ""));
            menu.DropDown(anchor.worldBound);
        }

        private void Set(SerializedObject owner, string path, VisualElement container, string label,
            string name, string id)
        {
            owner.Update();
            SerializedProperty target = owner.FindProperty(path);
            if (target == null)
                return;
            target.stringValue = name ?? "";
            SerializedProperty idProperty = IdOf(target, Marked());
            if (idProperty != null)
                idProperty.stringValue = id ?? "";
            owner.ApplyModifiedProperties();
            Build(container, owner, path, label);
        }

        /// <summary>
        /// WHOSE VOCABULARY APPLIES — and the answer is not always the object being drawn.
        ///
        /// A registry, a def, a tree and a manifest declare their own. A TASK does not: it is a
        /// sub-asset of the tree that owns it, and the tree is what declared the Data — so the
        /// file's main asset is asked next, which is the same rule an author would use looking
        /// at it. A component in a prefab or a scene declares nothing at all and never can, so
        /// it is offered the project's vocabularies, and the menu says so rather than implying
        /// a neighbourhood it does not have.
        /// </summary>
        private static bool Offers(Object target, WorldTagAttribute marked, List<WorldTagDef> into)
        {
            string group = marked?.group ?? "";

            StateTreeOffers.TagsFor(target, into, group);
            if (into.Count > 0)
                return true;

            string path = target != null ? AssetDatabase.GetAssetPath(target) : "";
            if (!string.IsNullOrEmpty(path))
            {
                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main != null && main != target)
                {
                    StateTreeOffers.TagsFor(main, into, group);
                    if (into.Count > 0)
                        return true;
                }
            }

            // The fallback, for the objects that have no way to declare: every vocabulary the
            // project holds.
            foreach (string guid in AssetDatabase.FindAssets("t:WorldTagRegistry"))
            {
                var vocabulary = AssetDatabase.LoadAssetAtPath<WorldTagRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (vocabulary == null)
                    continue;
                for (int i = 0; i < vocabulary.Count; i++)
                {
                    if (vocabulary.EntryAt(i) is WorldTagDef row && !string.IsNullOrEmpty(row.name)
                        && (string.IsNullOrEmpty(group) || row.group == group)
                        && !into.Contains(row))
                        into.Add(row);
                }
            }
            return false;
        }

        /// <summary>The row this field is wired to: by id when it has one, else the row that
        /// happens to carry the same name — which is what makes an unpicked-but-known tag show
        /// as the link it effectively is.</summary>
        private static WorldTagDef Row(List<WorldTagDef> offers, string name,
            SerializedProperty id)
        {
            string wired = id != null ? id.stringValue : "";
            if (!string.IsNullOrEmpty(wired))
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    if (offers[i].id == wired)
                        return offers[i];
                }
            }
            if (string.IsNullOrEmpty(name))
                return null;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].name == name)
                    return offers[i];
            }
            return null;
        }

        private static SerializedProperty IdOf(SerializedProperty property,
            WorldTagAttribute marked)
        {
            if (marked == null || string.IsNullOrEmpty(marked.idField))
                return null;
            int dot = property.propertyPath.LastIndexOf('.');
            string parent = dot > 0 ? property.propertyPath.Substring(0, dot + 1) : "";
            return property.serializedObject.FindProperty(parent + marked.idField);
        }

        private WorldTagAttribute Marked()
        {
            return fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo, typeof(WorldTagAttribute))
                    as WorldTagAttribute
                : null;
        }
    }
}
