using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PowerOfFire.DrawToPlay.Editor
{
    /// <summary>
    /// THE PLACEMENT'S OPTIONS, AS A PANEL (M34) — every attribute this kind declares, the value
    /// it would have, and a tick where this one differs.
    ///
    /// The same shape the graph parameters use, for the same reason: a checkbox per declared
    /// knob rather than a list you add rows to. "3 typed into an unticked row" and "3 typed into
    /// a ticked one" mean different things — follow the kind, or pin THIS one at 3 — and the
    /// difference has to survive somebody changing the prefab's seed later.
    ///
    /// THE UNTICKED VALUE IS THE BODY'S OWN SEED, so the panel shows what this placement will
    /// actually start at rather than an empty box — and a DASH where the body seeds nothing,
    /// because a confident 0 next to a number that comes from somewhere else is worse than
    /// saying nothing.
    ///
    /// It draws on the SET rather than the list because Unity gives a field attribute to a
    /// list's ELEMENTS: a per-row drawer can only decorate what somebody already added, and
    /// what an author needs is the row that is not there yet.
    ///
    /// Stored rows the def does not declare are listed after, as the warnings they are, with a
    /// way to delete them — a value that names nothing is refused at spawn anyway.
    /// </summary>
    [CustomPropertyDrawer(typeof(PlacementAttributesAttribute))]
    internal sealed class PlacementAttributesDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect area, SerializedProperty property, GUIContent label)
        {
            SerializedProperty rows = Rows(property);
            if (rows == null)
            {
                EditorGUI.PropertyField(area, property, label, true);
                return;
            }

            var kind = Kind(property);
            ServiceDef def = kind != null ? kind.service : null;
            List<ServiceAttribute> declared = def != null ? def.attributes : null;

            var line = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(line, new GUIContent("Options",
                def != null
                    ? "What '" + def.serviceName + "' says this kind has. Tick to give THIS one "
                        + "its own number."
                    : "This placement's kind has no def, so it declares no options."),
                EditorStyles.boldLabel);
            line.y += EditorGUIUtility.singleLineHeight + 2f;

            if (declared == null || declared.Count == 0)
            {
                EditorGUI.LabelField(line, kind == null
                    ? "pick a kind first"
                    : "this kind declares no attributes", EditorStyles.miniLabel);
                line.y += EditorGUIUtility.singleLineHeight + 2f;
                DrawStrays(ref line, rows, declared);
                return;
            }

            for (int i = 0; i < declared.Count; i++)
            {
                ServiceAttribute has = declared[i];
                string named = has != null ? has.Name : "";
                if (string.IsNullOrEmpty(named))
                    continue;

                int row = IndexOf(rows, named);
                bool overridden = row >= 0;

                var tickArea = new Rect(line.x, line.y, 18f, line.height);
                var nameArea = new Rect(tickArea.xMax + 2f, line.y,
                    Mathf.Max(80f, line.width * 0.45f), line.height);
                var valueArea = new Rect(nameArea.xMax + 6f, line.y,
                    Mathf.Max(60f, line.width - nameArea.width - 30f), line.height);

                bool nowOverridden = EditorGUI.Toggle(tickArea, overridden);
                EditorGUI.LabelField(nameArea, named);

                if (nowOverridden != overridden)
                {
                    if (nowOverridden)
                        Add(rows, named, Seeded(def, named, out _));
                    else
                        RemoveAt(rows, row);
                    property.serializedObject.ApplyModifiedProperties();
                    row = IndexOf(rows, named);
                    overridden = row >= 0;
                }

                if (overridden)
                {
                    SerializedProperty value = rows.GetArrayElementAtIndex(row)
                        .FindPropertyRelative("value");
                    value.floatValue = EditorGUI.FloatField(valueArea, value.floatValue);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        float seed = Seeded(def, named, out bool seeded);
                        if (seeded)
                            EditorGUI.FloatField(valueArea, seed);
                        else
                            EditorGUI.LabelField(valueArea, "— whatever the body starts at");
                    }
                }
                line.y += EditorGUIUtility.singleLineHeight + 2f;
            }

            DrawStrays(ref line, rows, declared);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty rows = Rows(property);
            if (rows == null)
                return EditorGUI.GetPropertyHeight(property, label, true);

            var kind = Kind(property);
            ServiceDef def = kind != null ? kind.service : null;
            int lines = 1;
            lines += def != null && def.attributes.Count > 0 ? def.attributes.Count : 1;
            lines += Strays(rows, def != null ? def.attributes : null).Count;
            return lines * (EditorGUIUtility.singleLineHeight + 2f) + 2f;
        }

        /// <summary>Rows nobody declares — a kind that lost an attribute leaves them behind, and
        /// a value that names nothing is refused at spawn, so they are shown to be deleted.</summary>
        private void DrawStrays(ref Rect line, SerializedProperty property,
            List<ServiceAttribute> declared)
        {
            List<int> strays = Strays(property, declared);
            for (int i = 0; i < strays.Count; i++)
            {
                SerializedProperty row = property.GetArrayElementAtIndex(strays[i]);
                var nameArea = new Rect(line.x, line.y, line.width - 26f, line.height);
                var dropArea = new Rect(nameArea.xMax + 2f, line.y, 22f, line.height);
                EditorGUI.LabelField(nameArea, "⚠ '"
                    + row.FindPropertyRelative("attribute").stringValue
                    + "' is not declared by this kind", EditorStyles.miniLabel);
                if (GUI.Button(dropArea, "✕"))
                {
                    RemoveAt(property, strays[i]);
                    property.serializedObject.ApplyModifiedProperties();
                    return;
                }
                line.y += EditorGUIUtility.singleLineHeight + 2f;
            }
        }

        internal static List<int> Strays(SerializedProperty property,
            List<ServiceAttribute> declared)
        {
            var strays = new List<int>();
            for (int i = 0; property.isArray && i < property.arraySize; i++)
            {
                string named = property.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("attribute").stringValue;
                var known = false;
                for (int d = 0; declared != null && d < declared.Count; d++)
                {
                    if (declared[d] != null && declared[d].Name == named)
                        known = true;
                }
                if (!known)
                    strays.Add(i);
            }
            return strays;
        }

        /// <summary>What this option is worth when nobody overrides it: the body's own seed.
        ///
        /// NOT ZERO when there is no seed — <paramref name="seeded"/> comes back false and the
        /// panel says "whatever the body starts at" instead of printing a number the object
        /// will never have. Half the M21 kinds take their health from the unit row rather than
        /// a serialized seed, and a confident 0 next to those would be the panel's first lie.
        /// </summary>
        internal static float Seeded(ServiceDef def, string attribute, out bool seeded)
        {
            seeded = false;
            GameObject prefab = def != null && def.body != null ? def.body.prefab : null;
            if (prefab == null)
                return 0f;
            var attributes = prefab.GetComponentInChildren<AttributeComponent>(true);
            for (int i = 0; attributes != null && i < attributes.seeds.Count; i++)
            {
                AttributeComponent.Seed seed = attributes.seeds[i];
                if (seed != null && seed.attribute.entryName == attribute)
                {
                    seeded = true;
                    return seed.baseValue;
                }
            }
            return 0f;
        }

        private static int IndexOf(SerializedProperty list, string attribute)
        {
            for (int i = 0; list.isArray && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("attribute").stringValue
                    == attribute)
                    return i;
            }
            return -1;
        }

        private static void Add(SerializedProperty list, string attribute, float value)
        {
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty row = list.GetArrayElementAtIndex(index);
            row.FindPropertyRelative("attribute").stringValue = attribute;
            row.FindPropertyRelative("value").floatValue = value;
        }

        private static void RemoveAt(SerializedProperty list, int index)
        {
            if (index >= 0 && index < list.arraySize)
                list.DeleteArrayElementAtIndex(index);
        }

        /// <summary>The rows this panel edits — the set's own list. The panel is drawn on the
        /// SET rather than on a bare list because Unity hands a field attribute to a list's
        /// ELEMENTS, and a per-element drawer cannot offer what nobody has overridden yet.</summary>
        private static SerializedProperty Rows(SerializedProperty property)
        {
            SerializedProperty rows = property.FindPropertyRelative("values");
            return rows != null && rows.isArray ? rows : null;
        }

        /// <summary>This placement's kind row, through the sibling field the attribute names.</summary>
        private LevelObjectKindDef Kind(SerializedProperty property)
        {
            var marked = fieldInfo != null
                ? System.Attribute.GetCustomAttribute(fieldInfo,
                    typeof(PlacementAttributesAttribute)) as PlacementAttributesAttribute
                : null;
            if (marked == null || string.IsNullOrEmpty(marked.kindField))
                return null;

            string path = property.propertyPath;
            int cut = path.LastIndexOf('.');
            if (cut < 0)
                return null;
            SerializedProperty kind = property.serializedObject.FindProperty(
                path.Substring(0, cut + 1) + marked.kindField);
            string id = kind?.FindPropertyRelative("entryId")?.stringValue;
            string named = kind?.FindPropertyRelative("entryName")?.stringValue;
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(named))
                return null;

            foreach (string guid in AssetDatabase.FindAssets(
                "t:" + nameof(LevelObjectKindRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<LevelObjectKindRegistry>(
                    AssetDatabase.GUIDToAssetPath(guid));
                var row = (string.IsNullOrEmpty(id)
                    ? registry?.FindByName(named)
                    : registry?.FindById(id)) as LevelObjectKindDef;
                if (row != null)
                    return row;
            }
            return null;
        }
    }
}
